using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class CheckoutCalculationServiceTests
{
    private const string UserId = "user-1";
    private const string Currency = "USD";

    private static CartItemDto Item(
        decimal unitPrice = 50m,
        int quantity = 1,
        bool available = true,
        string? reason = null) =>
        new(
            CartItemId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            VariantId: Guid.NewGuid(),
            ProductName: "Classic Tee",
            Slug: "classic-tee",
            BrandName: null,
            ImageUrl: "/img/tee.jpg",
            ImageCardUrl: null,
            ImageAltText: null,
            Sku: "TEE-001",
            ColourName: "Black",
            SizeName: "M",
            UnitPrice: unitPrice,
            CompareAtPrice: null,
            DiscountPercent: null,
            Quantity: quantity,
            LineTotal: unitPrice * quantity,
            AvailableStock: 10,
            IsAvailable: available,
            IsInStock: available,
            IsActive: available,
            UnavailableReason: reason);

    private static CartPricingResult Pricing(
        decimal subtotal = 50m,
        decimal promotions = 0m,
        decimal coupon = 0m,
        bool couponApplied = false,
        bool freeShipping = false,
        decimal? lineTotal = null) =>
        new(
            Subtotal: subtotal,
            PromotionsDiscount: promotions,
            CouponDiscount: coupon,
            ShippingDiscount: 0m,
            Total: Math.Max(0m, subtotal - promotions - coupon),
            IsFreeShipping: freeShipping,
            CouponApplied: couponApplied,
            AppliedCouponCode: couponApplied ? "SAVE10" : null,
            CouponMessage: null,
            Breakdown: Array.Empty<DiscountBreakdownItem>(),
            Lines: new[]
            {
                new CartLinePricing(
                    Guid.Empty,
                    subtotal,
                    promotions,
                    coupon,
                    lineTotal ?? Math.Max(0m, subtotal - promotions - coupon))
            });

    private static ShippingQuoteDto Quote(
        Guid? methodId = null,
        decimal price = 7m,
        bool available = true,
        bool supportsCod = true) =>
        new(
            MethodId: methodId ?? Guid.NewGuid(),
            Code: "STANDARD",
            Name: "Standard Delivery",
            Description: null,
            Type: ShippingMethodType.Standard,
            Price: price,
            IsFree: price == 0m,
            IsAvailable: available,
            UnavailableReason: available ? null : "Not available",
            EstimatedMinDays: 3,
            EstimatedMaxDays: 5,
            SupportsCashOnDelivery: supportsCod,
            FreeShippingThreshold: null,
            RemainingForFreeShipping: null,
            PickupInstructions: null);

    private static CheckoutAddressInput Address(
        string country = "US",
        string? region = "NY",
        string? postal = "10001") =>
        new(
            SavedAddressId: null,
            RecipientName: "Jane Doe",
            Phone: "555-0100",
            AddressLine1: "1 Main Street",
            AddressLine2: null,
            Area: null,
            City: "New York",
            Region: region,
            PostalCode: postal!,
            CountryCode: country,
            DeliveryInstructions: null);

    private static CheckoutCalculationInput ValidInput(
        CartItemDto item,
        string? userId = UserId,
        string paymentMethod = "card",
        Guid? shippingMethodId = null,
        CheckoutAddressInput? address = null,
        string? guestEmail = null,
        string? guestPhone = null,
        bool terms = true,
        string? coupon = null,
        string? token = null) =>
        new(
            UserId: userId,
            Items: new[] { item },
            CouponCode: coupon,
            GuestEmail: guestEmail,
            GuestPhone: guestPhone,
            ShippingAddress: address ?? Address(),
            BillingAddress: null,
            BillingSameAsShipping: true,
            ShippingMethodId: shippingMethodId,
            PaymentMethodCode: paymentMethod,
            TermsAccepted: terms,
            ContinuationToken: token);

    private sealed class Fixture
    {
        public Mock<IDiscountService> Discount { get; } = new();
        public Mock<IShippingCalculationService> Shipping { get; } = new();
        public Mock<IAddressValidationService> AddressValidation { get; } = new();

        public CheckoutSettings Checkout { get; set; } = new();
        public TaxSettings Tax { get; set; } = new();
        public StoreSettings Store { get; set; } = new();

        public Fixture()
        {
            Discount
                .Setup(d => d.CalculateAsync(
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyList<CartItemDto>>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Pricing(subtotal: 50m));

            AddressValidation
                .Setup(a => a.Validate(It.IsAny<SaveAddressRequest>()))
                .Returns(Array.Empty<string>());

            Shipping
                .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { Quote() }));
        }

        public CheckoutCalculationService CreateService()
        {
            return new CheckoutCalculationService(
                Discount.Object,
                Shipping.Object,
                AddressValidation.Object,
                Options.Create(Checkout),
                Options.Create(Tax),
                Options.Create(Store),
                NullLogger<CheckoutCalculationService>.Instance);
        }
    }

    private static async Task<CheckoutCalculationResult> RunValid(
        Fixture fixture,
        CartItemDto item,
        Guid? methodId = null,
        string paymentMethod = "card",
        string? coupon = null,
        string? token = null)
    {
        var pricing = string.IsNullOrEmpty(coupon)
            ? Pricing(subtotal: item.LineTotal)
            : Pricing(subtotal: item.LineTotal, coupon: 10m, couponApplied: true, lineTotal: item.LineTotal - 10m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pricing);

        var quote = Quote(methodId);
        var effectiveMethod = methodId ?? quote.MethodId;
        fixture.Shipping
            .Setup(s => s.QuoteAsync(
                It.IsAny<ShippingCalculationInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        return await service.CalculateAsync(
            ValidInput(item, shippingMethodId: effectiveMethod, paymentMethod: paymentMethod, coupon: coupon, token: token),
            CancellationToken.None);
    }

    [Fact]
    public async Task Calculate_EmptyCart_ReturnsCartErrorAndZeroTotals()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(ValidInput(Item()).WithItems(), CancellationToken.None);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cart", error.Field);
        Assert.Equal("empty", error.Code);
        Assert.Equal(0m, result.Totals.GrandTotal);
    }

    [Fact]
    public async Task Calculate_UnavailableItem_ReportsUnavailableReason()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(available: false, reason: "Out of stock")),
            CancellationToken.None);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cart", error.Field);
        Assert.Equal("unavailable-item", error.Code);
        Assert.Contains("Out of stock", error.Message);
    }

    [Fact]
    public async Task Calculate_MissingShippingAddress_AddsError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var item = Item();

        var result = await service.CalculateAsync(
            ValidInput(item).WithShippingAddress(null),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "shippingAddress" && e.Code == "required");
    }

    [Fact]
    public async Task Calculate_UnknownCountry_AddsAddressError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), address: Address(country: "ZZ")),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "shippingAddress" && e.Code == "country-unknown");
    }

    [Fact]
    public async Task Calculate_GuestWithoutEmail_AddsEmailError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), userId: null, guestPhone: "555-0100"),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "guestEmail" && e.Code == "required");
    }

    [Fact]
    public async Task Calculate_GuestInvalidEmail_AddsEmailError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), userId: null, guestEmail: "not-an-email", guestPhone: "555-0100"),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "guestEmail" && e.Code == "invalid");
    }

    [Fact]
    public async Task Calculate_GuestPhoneRequired_WhenConfigured()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), userId: null, guestEmail: "guest@example.com"),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "guestPhone" && e.Code == "required");
    }

    [Fact]
    public async Task Calculate_UnsupportedDestination_AddsShippingAddressError()
    {
        var fixture = new Fixture();
        var item = Item();
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: item.LineTotal));
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(false, "We do not currently deliver to this destination.", Array.Empty<ShippingQuoteDto>()));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(ValidInput(item), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "shippingAddress" && e.Code == "destination-not-supported");
    }

    [Fact]
    public async Task Calculate_MissingShippingMethod_AddsError()
    {
        var fixture = new Fixture();
        var item = Item();
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: item.LineTotal));
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { Quote() }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(item, shippingMethodId: null),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "shippingMethod" && e.Code == "required");
    }

    [Fact]
    public async Task Calculate_UnavailableShippingMethod_AddsError()
    {
        var fixture = new Fixture();
        var item = Item();
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: item.LineTotal));

        var quote = Quote(available: false);
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(item, shippingMethodId: quote.MethodId),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "shippingMethod" && e.Code == "unavailable");
    }

    [Fact]
    public async Task Calculate_CodOnNonCodShipping_AddsPaymentError()
    {
        var fixture = new Fixture();
        var item = Item();
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: item.LineTotal));

        var quote = Quote(supportsCod: false);
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(item, paymentMethod: "cod", shippingMethodId: quote.MethodId),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "paymentMethod" && e.Code == "cod-unavailable");
    }

    [Fact]
    public async Task Calculate_UnknownPaymentMethod_AddsError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), paymentMethod: "bitcoin"),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "paymentMethod" && e.Code == "invalid");
    }

    [Fact]
    public async Task Calculate_TermsNotAccepted_AddsError()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CalculateAsync(
            ValidInput(Item(), terms: false),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "terms" && e.Code == "not-accepted");
    }

    [Fact]
    public async Task Calculate_BelowMinimumOrder_AddsError()
    {
        var fixture = new Fixture { Checkout = new CheckoutSettings { MinOrderAmount = 100m } };
        var item = Item(unitPrice: 50m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: 50m));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(ValidInput(item), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "order" && e.Code == "below-minimum");
    }

    [Fact]
    public async Task Calculate_AboveMaximumOrder_AddsError()
    {
        var fixture = new Fixture { Checkout = new CheckoutSettings { MaxOrderAmount = 100m } };
        var item = Item(unitPrice: 150m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: 150m));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(ValidInput(item), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "order" && e.Code == "above-maximum");
    }

    [Fact]
    public async Task Calculate_ValidOrder_ComputesTotalsAndToken()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        var result = await RunValid(fixture, item);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(100m, result.Totals.Subtotal);
        Assert.Equal(7m, result.Totals.Shipping);
        Assert.Equal(0m, result.Totals.Tax);
        Assert.Equal(107m, result.Totals.GrandTotal);
        Assert.Equal(Currency, result.Totals.Currency);
        Assert.False(string.IsNullOrEmpty(result.ContinuationToken));
        Assert.Single(result.Lines);
    }

    [Fact]
    public async Task Calculate_TaxAppliedPerCountryOverride()
    {
        var fixture = new Fixture { Tax = new TaxSettings { CountryRates = new Dictionary<string, decimal> { ["US"] = 8.25m } } };
        var item = Item(unitPrice: 100m);
        var result = await RunValid(fixture, item);

        Assert.True(result.IsValid);
        Assert.Equal(8.25m, result.Tax.RatePercent);
        Assert.Equal(107m, result.Tax.TaxableAmount);
        Assert.Equal(8.83m, result.Tax.TaxAmount);
        Assert.Equal(115.83m, result.Totals.GrandTotal);
    }

    [Fact]
    public async Task Calculate_TaxFallsBackToDefaultRate()
    {
        var fixture = new Fixture { Tax = new TaxSettings { DefaultRatePercent = 5m } };
        var item = Item(unitPrice: 100m);
        var result = await RunValid(fixture, item);

        Assert.True(result.IsValid);
        Assert.Equal(5m, result.Tax.RatePercent);
        Assert.Equal(5.35m, result.Tax.TaxAmount);
    }

    [Fact]
    public async Task Calculate_CouponApplied_IncludedInBreakdown()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        var result = await RunValid(fixture, item, coupon: "SAVE10");

        Assert.True(result.IsValid);
        Assert.Equal(10m, result.Totals.CouponDiscount);
        Assert.Equal(97m, result.Totals.GrandTotal);
    }

    [Fact]
    public async Task Calculate_RejectedCoupon_AddsWarning()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                "BAD",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: 100m, couponApplied: false, coupon: 0m, lineTotal: 100m));

        var quote = Quote();
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(item, coupon: "BAD", shippingMethodId: quote.MethodId),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("coupon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Calculate_FreeShippingMakesShippingZero()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: 100m, freeShipping: true, lineTotal: 100m));

        var quote = Quote(price: 0m);
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(item, shippingMethodId: quote.MethodId),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.True(result.Totals.IsFreeShipping);
        Assert.Equal(0m, result.Totals.Shipping);
        Assert.Equal(100m, result.Totals.GrandTotal);
    }

    [Fact]
    public async Task Calculate_StableToken_NoPriceChangeWarning()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        var first = await RunValid(fixture, item);

        var token = first.ContinuationToken;
        var methodId = first.SelectedShipping!.MethodId;
        var second = await RunValid(fixture, item, methodId: methodId);

        Assert.Equal(token, second.ContinuationToken);
        Assert.False(second.PricesChanged);
        Assert.DoesNotContain(second.Warnings, w => w.Contains("changed"));
    }

    [Fact]
    public async Task Calculate_PriceChange_FlagsPricesChanged()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        var first = await RunValid(fixture, item);
        var token = first.ContinuationToken;
        var methodId = first.SelectedShipping!.MethodId;

        var changed = Item(unitPrice: 120m);
        fixture.Discount
            .Setup(d => d.CalculateAsync(
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CartItemDto>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pricing(subtotal: 120m, lineTotal: 120m));

        var quote = Quote(methodId);
        fixture.Shipping
            .Setup(s => s.QuoteAsync(It.IsAny<ShippingCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingQuoteResultDto(true, null, new[] { quote }));

        var service = fixture.CreateService();
        var result = await service.CalculateAsync(
            ValidInput(changed, shippingMethodId: methodId, token: token),
            CancellationToken.None);

        Assert.True(result.PricesChanged);
        Assert.Contains(result.Warnings, w => w.Contains("changed"));
    }

    [Fact]
    public async Task Calculate_CodOnCodShipping_Succeeds()
    {
        var fixture = new Fixture();
        var item = Item(unitPrice: 100m);
        var result = await RunValid(fixture, item, paymentMethod: "cod");

        Assert.True(result.IsValid);
        Assert.NotNull(result.SelectedShipping);
    }
}

internal static class CheckoutInputTestExtensions
{
    public static CheckoutCalculationInput WithItems(this CheckoutCalculationInput input, params CartItemDto[] items) =>
        input with { Items = items };

    public static CheckoutCalculationInput WithShippingAddress(this CheckoutCalculationInput input, CheckoutAddressInput? address) =>
        input with { ShippingAddress = address };
}
