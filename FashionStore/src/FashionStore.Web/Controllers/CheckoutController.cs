using System.Security.Claims;
using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Storefront checkout. The page is a multi-step app-like flow (contact, address,
/// delivery, payment, review) driven entirely by server-computed values: the cart is
/// resolved server-side, prices/discounts/shipping/tax/totals are recomputed by the
/// checkout engine on every call, and the browser only ever submits free-form
/// destination fields, method ids, guest contact details and the terms flag. No final
/// order is created yet; the calculate endpoint returns a normalized result plus a
/// signed continuation token so stale totals can be detected.
/// </summary>
[Route("checkout")]
public class CheckoutController : Controller
{
    private readonly ICartService _cartService;
    private readonly ICheckoutCalculationService _checkoutCalculationService;
    private readonly IAddressService _addressService;
    private readonly IProfileService _profileService;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ICartService cartService,
        ICheckoutCalculationService checkoutCalculationService,
        IAddressService addressService,
        IProfileService profileService,
        ILogger<CheckoutController> logger)
    {
        _cartService = cartService;
        _checkoutCalculationService = checkoutCalculationService;
        _addressService = addressService;
        _profileService = profileService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData cart;
        if (string.IsNullOrEmpty(userId))
        {
            cart = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
        }
        else
        {
            cart = await _cartService.GetCartAsync(userId, cancellationToken);
        }

        if (cart.ItemCount == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        IReadOnlyList<AddressDto> savedAddresses = Array.Empty<AddressDto>();
        string? email = null;
        string? phone = null;

        if (!string.IsNullOrEmpty(userId))
        {
            var addressBook = await _addressService.GetAddressBookAsync(userId, cancellationToken);
            savedAddresses = addressBook.Addresses;

            var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
            email = profile?.Email;
            phone = profile?.PhoneNumber;
        }

        var data = new CheckoutViewData(
            cart.Items,
            cart.Pricing?.Subtotal ?? cart.Subtotal,
            Format(cart.Pricing?.Subtotal ?? cart.Subtotal),
            !string.IsNullOrEmpty(userId),
            email,
            phone,
            savedAddresses,
            CountryCatalog.All,
            PaymentMethodCatalog.All);

        return View(data);
    }

    /// <summary>
    /// Runs the central server-side checkout calculation. The cart is resolved from
    /// the server-side store; the browser only supplies the free-form destination,
    /// method ids, guest contact details and the terms flag. A previous continuation
    /// token is echoed back so the engine can flag when the quoted totals are stale.
    /// </summary>
    [HttpPost("calculate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate([FromBody] CheckoutCalculateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Invalid checkout request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData cart;
        string? couponCode;
        if (string.IsNullOrEmpty(userId))
        {
            cart = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
            couponCode = AnonymousCouponCookie.Read(HttpContext);
        }
        else
        {
            cart = await _cartService.GetCartAsync(userId, cancellationToken);
            couponCode = cart.Pricing?.AppliedCouponCode;
        }

        var items = cart.Items;
        if (cart.ItemCount == 0)
        {
            return Ok(new CheckoutCalculationResult(
                false,
                new[] { new CheckoutValidationError("cart", "empty", "Your cart is empty.") },
                Array.Empty<string>(),
                Array.Empty<CheckoutLineItemDto>(),
                Array.Empty<ShippingQuoteDto>(),
                null,
                ZeroTotals(),
                ZeroTax(),
                Array.Empty<DiscountBreakdownItem>(),
                string.Empty,
                false));
        }

        var shippingAddress = await ResolveAddressAsync(userId, request.ShippingAddress, cancellationToken);
        var billingAddress = await ResolveAddressAsync(userId, request.BillingAddress, cancellationToken);

        var input = new CheckoutCalculationInput(
            userId,
            items,
            couponCode,
            request.GuestEmail,
            request.GuestPhone,
            shippingAddress,
            billingAddress,
            request.BillingSameAsShipping,
            request.ShippingMethodId,
            request.PaymentMethodCode,
            request.TermsAccepted,
            request.ContinuationToken);

        var result = await _checkoutCalculationService.CalculateAsync(input, cancellationToken);
        return Ok(result);
    }

    private static string Format(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Resolves an address submitted by the browser. When the customer selected a
    /// saved address card the id is carried in <see cref="CheckoutAddressInput.SavedAddressId"/>
    /// and the full address is loaded server-side (never trusted from the browser);
    /// otherwise the free-form fields are used as submitted and validated by the engine.
    /// </summary>
    private async Task<CheckoutAddressInput?> ResolveAddressAsync(
        string? userId,
        CheckoutAddressInput? input,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(input.SavedAddressId) ||
            string.IsNullOrEmpty(userId) ||
            !Guid.TryParse(input.SavedAddressId, out var addressId))
        {
            return input;
        }

        var saved = await _addressService.GetByIdAsync(userId, addressId, cancellationToken);
        if (saved is null)
        {
            return input with { SavedAddressId = null };
        }

        return new CheckoutAddressInput(
            input.SavedAddressId,
            saved.RecipientName,
            saved.Phone,
            saved.AddressLine1,
            saved.AddressLine2,
            saved.Area,
            saved.City,
            saved.Region,
            saved.PostalCode,
            saved.CountryCode,
            saved.DeliveryInstructions);
    }

    private static CheckoutTotalsDto ZeroTotals() => new(0m, 0m, 0m, 0m, 0m, 0m, 0m, "USD", false);

    private static CheckoutTaxBreakdownDto ZeroTax() => new(0m, 0m, 0m, "USD");
}

/// <summary>
/// Browser-supplied checkout calculation request. Only free-form destination fields,
/// method ids, guest contact details and the terms flag are accepted; the cart lines
/// and every price are resolved and recomputed server-side.
/// </summary>
public sealed record CheckoutCalculateRequest(
    string? GuestEmail,
    string? GuestPhone,
    CheckoutAddressInput? ShippingAddress,
    CheckoutAddressInput? BillingAddress,
    bool BillingSameAsShipping,
    Guid? ShippingMethodId,
    string? PaymentMethodCode,
    bool TermsAccepted,
    string? ContinuationToken);
