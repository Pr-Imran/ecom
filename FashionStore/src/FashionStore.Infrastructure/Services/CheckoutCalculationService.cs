using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// The central server-side checkout engine. Every price, discount, shipping charge,
/// tax figure and total is recomputed here through the pricing and shipping engines
/// on every call; the browser only supplies the free-form destination, method ids,
/// guest contact details and the terms flag. The service validates the cart,
/// addresses, shipping method, payment method, order limits, guest contact data and
/// terms, applies tax for the destination country and returns a deterministic result
/// carrying a signed continuation token so the UI can detect stale quoted totals.
/// </summary>
public sealed class CheckoutCalculationService : ICheckoutCalculationService
{
    private const int CurrencyScale = 2;
    private const decimal Hundred = 100m;

    // Per-process fallback key used when no token secret is configured. Single
    // instances stay consistent across requests; multi-instance or restart-persistent
    // setups must configure Checkout:ContinuationTokenSecret.
    private static readonly byte[] FallbackTokenKey = RandomNumberGenerator.GetBytes(32);

    private readonly IDiscountService _discountService;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly IAddressValidationService _addressValidationService;
    private readonly IOptions<CheckoutSettings> _checkoutOptions;
    private readonly IOptions<TaxSettings> _taxOptions;
    private readonly IOptions<StoreSettings> _storeOptions;
    private readonly ILogger<CheckoutCalculationService> _logger;

    public CheckoutCalculationService(
        IDiscountService discountService,
        IShippingCalculationService shippingCalculationService,
        IAddressValidationService addressValidationService,
        IOptions<CheckoutSettings> checkoutOptions,
        IOptions<TaxSettings> taxOptions,
        IOptions<StoreSettings> storeOptions,
        ILogger<CheckoutCalculationService> logger)
    {
        _discountService = discountService;
        _shippingCalculationService = shippingCalculationService;
        _addressValidationService = addressValidationService;
        _checkoutOptions = checkoutOptions;
        _taxOptions = taxOptions;
        _storeOptions = storeOptions;
        _logger = logger;
    }

    public async Task<CheckoutCalculationResult> CalculateAsync(
        CheckoutCalculationInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<CheckoutValidationError>();
        var warnings = new List<string>();
        var currency = _storeOptions.Value.CurrencyCode;

        var availableItems = input.Items.Where(i => i.IsAvailable).ToList();
        ValidateCart(input.Items, errors);

        if (availableItems.Count == 0)
        {
            return BuildResult(
                errors,
                warnings,
                new List<CheckoutLineItemDto>(),
                Array.Empty<ShippingQuoteDto>(),
                null,
                ZeroTotals(currency),
                ZeroTax(currency),
                Array.Empty<DiscountBreakdownItem>(),
                input,
                CalculateToken(input, null, 0m, 0m, 0m, 0m, currency));
        }

        var pricing = await _discountService.CalculateAsync(
            input.UserId,
            input.Items,
            input.CouponCode,
            cancellationToken);

        var goodsTotal = Math.Max(0m, pricing.Total);
        var subtotal = pricing.Subtotal;

        if (!string.IsNullOrEmpty(input.CouponCode) && !pricing.CouponApplied)
        {
            warnings.Add(pricing.CouponMessage ?? "The coupon could not be applied.");
        }

        ValidateGuestContact(input, errors);

        var shippingAddress = input.ShippingAddress;
        var billingAddress = input.BillingSameAsShipping ? input.ShippingAddress : input.BillingAddress;

        if (shippingAddress is null)
        {
            errors.Add(new CheckoutValidationError(
                "shippingAddress", "required", "Enter a shipping address."));
        }
        else
        {
            ValidateAddress(shippingAddress, "shippingAddress", errors);
        }

        if (billingAddress is null)
        {
            errors.Add(new CheckoutValidationError(
                "billingAddress", "required", "Enter a billing address."));
        }
        else if (!ReferenceEquals(billingAddress, input.ShippingAddress))
        {
            ValidateAddress(billingAddress, "billingAddress", errors);
        }

        ShippingQuoteResultDto? quote = null;
        CheckoutSelectedShippingDto? selectedShipping = null;
        IReadOnlyList<ShippingQuoteDto> shippingOptions = Array.Empty<ShippingQuoteDto>();
        decimal shipping = 0m;

        if (input.ShippingAddress is not null &&
            !errors.Any(e => e.Field == "shippingAddress"))
        {
            quote = await _shippingCalculationService.QuoteAsync(
                new ShippingCalculationInput(
                    input.ShippingAddress.CountryCode,
                    input.ShippingAddress.City,
                    input.ShippingAddress.Region,
                    input.ShippingAddress.PostalCode,
                    subtotal,
                    availableItems
                        .Select(i => new ShippingLineInput(i.ProductId, i.VariantId, i.Quantity))
                        .ToList(),
                    pricing.IsFreeShipping),
                cancellationToken);

            shippingOptions = quote.Quotes;

            if (!quote.IsSupported)
            {
                errors.Add(new CheckoutValidationError(
                    "shippingAddress",
                    "destination-not-supported",
                    quote.UnsupportedReason ?? "We do not currently deliver to this destination."));
            }
            else
            {
                selectedShipping = ResolveShipping(input.ShippingMethodId, quote, errors);
                shipping = selectedShipping?.Price ?? 0m;
            }
        }

        ValidateOrderLimits(goodsTotal, errors);
        ValidatePaymentMethod(input.PaymentMethodCode, selectedShipping, errors);
        ValidateTerms(input.TermsAccepted, errors);

        var tax = ComputeTax(shippingAddress?.CountryCode, goodsTotal, shipping, currency);
        var grandTotal = Round(goodsTotal + shipping + tax.TaxAmount);
        var amountPayable = grandTotal;

        var lines = BuildLines(input.Items, pricing, shipping, tax.TaxAmount);
        var token = CalculateToken(input, quote, subtotal, shipping, tax.TaxAmount, grandTotal, currency);

        var pricesChanged = input.ContinuationToken is not null &&
                            !string.Equals(input.ContinuationToken, token, StringComparison.Ordinal);

        if (pricesChanged)
        {
            warnings.Add("Prices or totals have changed since you reviewed your order. Please review again.");
        }

        var totals = new CheckoutTotalsDto(
            Round(subtotal),
            Round(pricing.PromotionsDiscount),
            Round(pricing.CouponDiscount),
            Round(shipping),
            Round(tax.TaxAmount),
            grandTotal,
            amountPayable,
            currency,
            pricing.IsFreeShipping);

        return BuildResult(
            errors,
            warnings,
            lines,
            shippingOptions,
            selectedShipping,
            totals,
            tax,
            pricing.Breakdown,
            input,
            token,
            pricesChanged);
    }

    // ---- Validation ----

    private static void ValidateCart(IReadOnlyList<CartItemDto> items, List<CheckoutValidationError> errors)
    {
        if (items.Count == 0)
        {
            errors.Add(new CheckoutValidationError("cart", "empty", "Your cart is empty."));
            return;
        }

        if (items.Any(i => !i.IsAvailable))
        {
            var first = items.First(i => !i.IsAvailable);
            errors.Add(new CheckoutValidationError(
                "cart",
                "unavailable-item",
                first.UnavailableReason ?? $"{first.ProductName} is no longer available."));
        }
    }

    private void ValidateGuestContact(CheckoutCalculationInput input, List<CheckoutValidationError> errors)
    {
        if (!string.IsNullOrEmpty(input.UserId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input.GuestEmail))
        {
            errors.Add(new CheckoutValidationError("guestEmail", "required", "Enter your email address."));
        }
        else if (!IsValidEmail(input.GuestEmail))
        {
            errors.Add(new CheckoutValidationError("guestEmail", "invalid", "Enter a valid email address."));
        }

        if (_checkoutOptions.Value.RequireGuestPhone && string.IsNullOrWhiteSpace(input.GuestPhone))
        {
            errors.Add(new CheckoutValidationError("guestPhone", "required", "Enter a phone number for delivery contact."));
        }
    }

    private void ValidateAddress(CheckoutAddressInput address, string field, List<CheckoutValidationError> errors)
    {
        if (!CountryCatalog.IsKnown(address.CountryCode))
        {
            errors.Add(new CheckoutValidationError(field, "country-unknown", $"We do not recognize the country '{address.CountryCode}'."));
            return;
        }

        var mapped = new SaveAddressRequest(
            "Checkout",
            address.RecipientName,
            address.Phone,
            address.AddressLine1,
            address.AddressLine2,
            address.Area,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            address.DeliveryInstructions);

        var fieldErrors = _addressValidationService.Validate(mapped);
        if (fieldErrors.Count == 0)
        {
            return;
        }

        foreach (var message in fieldErrors)
        {
            errors.Add(new CheckoutValidationError(field, "invalid-address", message));
        }
    }

    private void ValidateOrderLimits(decimal goodsTotal, List<CheckoutValidationError> errors)
    {
        var settings = _checkoutOptions.Value;

        if (settings.MinOrderAmount.HasValue && goodsTotal < settings.MinOrderAmount.Value)
        {
            errors.Add(new CheckoutValidationError(
                "order",
                "below-minimum",
                $"The minimum order amount is {settings.MinOrderAmount.Value:N2}. Your current total is {goodsTotal:N2}."));
        }

        if (settings.MaxOrderAmount.HasValue && goodsTotal > settings.MaxOrderAmount.Value)
        {
            errors.Add(new CheckoutValidationError(
                "order",
                "above-maximum",
                $"The maximum order amount is {settings.MaxOrderAmount.Value:N2}."));
        }
    }

    private static void ValidatePaymentMethod(
        string? paymentMethodCode,
        CheckoutSelectedShippingDto? selectedShipping,
        List<CheckoutValidationError> errors)
    {
        var method = PaymentMethodCatalog.Find(paymentMethodCode);
        if (method is null)
        {
            errors.Add(new CheckoutValidationError("paymentMethod", "invalid", "Select a payment method."));
            return;
        }

        if (method.RequiresCodShipping && (selectedShipping is null || !selectedShipping.SupportsCashOnDelivery))
        {
            errors.Add(new CheckoutValidationError(
                "paymentMethod",
                "cod-unavailable",
                "Cash on delivery is not available for the selected delivery method."));
        }
    }

    private static void ValidateTerms(bool termsAccepted, List<CheckoutValidationError> errors)
    {
        if (!termsAccepted)
        {
            errors.Add(new CheckoutValidationError("terms", "not-accepted", "You must accept the terms and conditions."));
        }
    }

    // ---- Calculation ----

    private static CheckoutSelectedShippingDto? ResolveShipping(
        Guid? shippingMethodId,
        ShippingQuoteResultDto quote,
        List<CheckoutValidationError> errors)
    {
        if (!shippingMethodId.HasValue)
        {
            errors.Add(new CheckoutValidationError("shippingMethod", "required", "Select a delivery method."));
            return null;
        }

        var selected = quote.Quotes.FirstOrDefault(q => q.MethodId == shippingMethodId.Value);
        if (selected is null)
        {
            errors.Add(new CheckoutValidationError("shippingMethod", "invalid", "The selected delivery method is not available."));
            return null;
        }

        if (!selected.IsAvailable)
        {
            errors.Add(new CheckoutValidationError(
                "shippingMethod",
                "unavailable",
                selected.UnavailableReason ?? "The selected delivery method is not available."));
            return null;
        }

        return new CheckoutSelectedShippingDto(
            selected.MethodId,
            selected.Code,
            selected.Name,
            selected.Price,
            selected.IsFree,
            selected.EstimatedMinDays,
            selected.EstimatedMaxDays,
            selected.SupportsCashOnDelivery,
            selected.PickupInstructions);
    }

    private CheckoutTaxBreakdownDto ComputeTax(string? countryCode, decimal goodsTotal, decimal shipping, string currency)
    {
        var ratePercent = _taxOptions.Value.RateFor(countryCode);
        var taxable = Round(goodsTotal + shipping);
        var tax = Round(taxable * ratePercent / Hundred);

        return new CheckoutTaxBreakdownDto(ratePercent, taxable, tax, currency);
    }

    private static List<CheckoutLineItemDto> BuildLines(
        IReadOnlyList<CartItemDto> items,
        CartPricingResult pricing,
        decimal shipping,
        decimal taxAmount)
    {
        var perLine = pricing.Lines.ToDictionary(l => l.VariantId, l => l);

        var lines = new List<CheckoutLineItemDto>(items.Count);
        decimal taxAllocated = 0m;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!item.IsAvailable || item.LineTotal <= 0)
            {
                continue;
            }

            perLine.TryGetValue(item.VariantId, out var linePricing);

            var lineTotal = linePricing?.LineTotal ?? Round(item.UnitPrice * item.Quantity);
            var lineTax = index == items.Count - 1
                ? Round(taxAmount - taxAllocated)
                : Round(taxAmount * (lineTotal / Math.Max(0.01m, pricing.Total)));
            taxAllocated = Round(taxAllocated + lineTax);

            lines.Add(new CheckoutLineItemDto(
                item.ProductId,
                item.VariantId,
                item.ProductName,
                item.Slug,
                item.Sku,
                item.ColourName,
                item.SizeName,
                item.ImageUrl,
                item.UnitPrice,
                item.CompareAtPrice,
                item.Quantity,
                linePricing?.LineSubtotal ?? Round(item.UnitPrice * item.Quantity),
                linePricing?.PromotionDiscount ?? 0m,
                linePricing?.CouponDiscount ?? 0m,
                lineTax,
                lineTotal));
        }

        return lines;
    }

    private string CalculateToken(
        CheckoutCalculationInput input,
        ShippingQuoteResultDto? quote,
        decimal subtotal,
        decimal shipping,
        decimal tax,
        decimal grandTotal,
        string currency)
    {
        var builder = new StringBuilder();
        builder.Append(input.UserId ?? input.GuestEmail ?? string.Empty).Append('|');
        builder.Append(currency).Append('|');
        builder.Append(input.PaymentMethodCode ?? string.Empty).Append('|');
        builder.Append(input.ShippingMethodId?.ToString() ?? string.Empty).Append('|');
        builder.Append(input.ShippingAddress?.CountryCode ?? string.Empty).Append('|');
        builder.Append(input.ShippingAddress?.City ?? string.Empty).Append('|');
        builder.Append(input.ShippingAddress?.PostalCode ?? string.Empty).Append('|');
        builder.Append(subtotal.ToString("F2", CultureInfo.InvariantCulture)).Append('|');
        builder.Append(shipping.ToString("F2", CultureInfo.InvariantCulture)).Append('|');
        builder.Append(tax.ToString("F2", CultureInfo.InvariantCulture)).Append('|');
        builder.Append(grandTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('|');

        foreach (var item in input.Items.Where(i => i.IsAvailable).OrderBy(i => i.VariantId))
        {
            builder.Append(item.VariantId).Append(':')
                   .Append(item.Quantity).Append(':')
                   .Append(item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)).Append(';');
        }

        var payload = builder.ToString();
        var key = _checkoutOptions.Value.ContinuationTokenSecret.Length > 0
            ? Encoding.UTF8.GetBytes(_checkoutOptions.Value.ContinuationTokenSecret)
            : FallbackTokenKey;

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private CheckoutCalculationResult BuildResult(
        IReadOnlyList<CheckoutValidationError> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<CheckoutLineItemDto> lines,
        IReadOnlyList<ShippingQuoteDto> shippingOptions,
        CheckoutSelectedShippingDto? selectedShipping,
        CheckoutTotalsDto totals,
        CheckoutTaxBreakdownDto tax,
        IReadOnlyList<DiscountBreakdownItem> discounts,
        CheckoutCalculationInput input,
        string token,
        bool pricesChanged = false)
    {
        _logger.LogInformation(
            "Checkout calculated for {Context} with {ErrorCount} validation issues",
            string.IsNullOrEmpty(input.UserId) ? "guest" : "customer",
            errors.Count);

        return new CheckoutCalculationResult(
            errors.Count == 0,
            errors,
            warnings,
            lines,
            shippingOptions,
            selectedShipping,
            totals,
            tax,
            discounts,
            token,
            pricesChanged);
    }

    private static CheckoutTotalsDto ZeroTotals(string currency) =>
        new(0m, 0m, 0m, 0m, 0m, 0m, 0m, currency, false);

    private static CheckoutTaxBreakdownDto ZeroTax(string currency) =>
        new(0m, 0m, 0m, currency);

    private static decimal Round(decimal value) => Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);

    private static bool IsValidEmail(string email) =>
        email.Length <= 254 &&
        email.Contains('@') &&
        email.IndexOf('@') > 0 &&
        email.IndexOf('@') < email.Length - 1;
}
