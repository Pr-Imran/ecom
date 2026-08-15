using System.Security.Claims;
using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Payments;
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
    private readonly IOrderService _orderService;
    private readonly ICustomerOrderService _customerOrderService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ICartService cartService,
        ICheckoutCalculationService checkoutCalculationService,
        IAddressService addressService,
        IProfileService profileService,
        IOrderService orderService,
        ICustomerOrderService customerOrderService,
        IPaymentService paymentService,
        ILogger<CheckoutController> logger)
    {
        _cartService = cartService;
        _checkoutCalculationService = checkoutCalculationService;
        _addressService = addressService;
        _profileService = profileService;
        _orderService = orderService;
        _customerOrderService = customerOrderService;
        _paymentService = paymentService;
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

    /// <summary>
    /// Places the order. The same server-side cart resolution and checkout
    /// calculation are re-run here: prices are recomputed, the quoted totals must
    /// still match the signed continuation token, stock is verified, and the order,
    /// its snapshots, the stock reservations and the coupon usage are committed in a
    /// single transaction. The idempotency key makes a repeated attempt (double
    /// click, refresh, retry) return the already-created order instead of a duplicate.
    /// </summary>
    [HttpPost("place")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Place([FromBody] PlaceOrderRequest? request, CancellationToken cancellationToken)
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

        if (cart.ItemCount == 0)
        {
            return Ok(new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                new[] { new CheckoutValidationError("cart", "empty", "Your cart is empty.") }));
        }

        var shippingAddress = await ResolveAddressAsync(userId, request.ShippingAddress, cancellationToken);
        var billingAddress = await ResolveAddressAsync(userId, request.BillingAddress, cancellationToken);

        var input = new CheckoutCalculationInput(
            userId,
            cart.Items,
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

        var result = await _orderService.PlaceOrderAsync(
            input,
            request.IdempotencyKey,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Mobile confirmation screen shown after a successful placement. The order is
    /// rendered from the immutable snapshots stored at placement time, so it stays
    /// correct even if products or addresses change later. The current public
    /// payment status is attached so the screen can render pending, paid and failed
    /// payment states.
    /// </summary>
    [HttpGet("confirmation/{publicOrderNumber}")]
    public async Task<IActionResult> Confirmation(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var order = await _orderService.GetByPublicOrderNumberAsync(publicOrderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        // Signed-in customers can only view their own orders. Guests have no
        // server-side identity, so they must present the signed ticket issued for
        // this order number at placement; the number alone is never sufficient.
        string? guestToken = null;
        if (!string.IsNullOrEmpty(userId))
        {
            if (!string.Equals(order.UserId, userId, StringComparison.Ordinal))
            {
                return NotFound();
            }
        }
        else if (string.IsNullOrWhiteSpace(t) ||
                 _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is null)
        {
            return NotFound();
        }
        else
        {
            guestToken = t;
        }

        var payment = await _paymentService.GetStatusByOrderNumberAsync(publicOrderNumber, cancellationToken);

        return View(new ConfirmationViewData(order, payment, guestToken));
    }

    /// <summary>
    /// Initiates the payment for a placed order (idempotently). Hosted providers
    /// return a redirect URL the browser follows to the gateway page; reference
    /// providers (MFS, bank) return the reference and instructions shown on the
    /// confirmation screen.
    /// </summary>
    [HttpPost("pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay([FromBody] InitiatePaymentRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            return BadRequest(new { success = false, message = "Invalid payment request." });
        }

        var order = await _orderService.GetByPublicOrderNumberAsync(request.OrderNumber, cancellationToken);
        if (order is null)
        {
            return NotFound(new { success = false, message = "Order not found." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            if (!string.Equals(order.UserId, userId, StringComparison.Ordinal))
            {
                return NotFound(new { success = false, message = "Order not found." });
            }
        }
        else if (string.IsNullOrWhiteSpace(request.GuestAccessToken) ||
                 _customerOrderService.ValidateGuestToken(request.GuestAccessToken, request.OrderNumber) is null)
        {
            return NotFound(new { success = false, message = "Order not found." });
        }

        var isGuest = string.IsNullOrEmpty(userId);
        var returnUrl = Url.Action(
            "Confirmation",
            "Checkout",
            new { publicOrderNumber = request.OrderNumber, t = isGuest ? request.GuestAccessToken : null },
            Request.Scheme);

        try
        {
            var placement = await _paymentService.InitiateForOrderAsync(
                order.OrderId,
                returnUrl,
                returnUrl,
                cancellationToken);

            return Ok(new { success = true, payment = placement });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Payment initiation rejected for order {OrderNumber}", request.OrderNumber);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Public payment status polled by the confirmation screen. This is a read-only
    /// status view; it never settles a payment by itself. Guests must present the
    /// signed ticket for the order number, matching the confirmation screen.
    /// </summary>
    [HttpGet("confirmation/{publicOrderNumber}/payment-status")]
    public async Task<IActionResult> PaymentStatus(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetByPublicOrderNumberAsync(publicOrderNumber, cancellationToken);
        if (order is null)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authorized = !string.IsNullOrEmpty(userId)
            ? string.Equals(order.UserId, userId, StringComparison.Ordinal)
            : !string.IsNullOrWhiteSpace(t) && _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is not null;

        if (!authorized)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        var status = await _paymentService.GetStatusByOrderNumberAsync(publicOrderNumber, cancellationToken);
        if (status is null)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        return Ok(new { success = true, payment = status });
    }

    /// <summary>
    /// Browser return from a hosted checkout. This is a callback, not proof of
    /// payment: the payment service asks the provider for the current status and
    /// only applies a provider-confirmed state. The order is never marked paid
    /// purely because the browser arrived here.
    /// </summary>
    [HttpPost("confirmation/{publicOrderNumber}/callback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BrowserCallback(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetByPublicOrderNumberAsync(publicOrderNumber, cancellationToken);
        if (order is null)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authorized = !string.IsNullOrEmpty(userId)
            ? string.Equals(order.UserId, userId, StringComparison.Ordinal)
            : !string.IsNullOrWhiteSpace(t) && _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is not null;

        if (!authorized)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        var status = await _paymentService.HandleBrowserCallbackAsync(publicOrderNumber, cancellationToken);
        if (status is null)
        {
            return NotFound(new { success = false, message = "Payment not found." });
        }

        return Ok(new { success = true, payment = status });
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

/// <summary>
/// Body for initiating a payment for a placed order. Signed-in customers resolve
/// the order by their identity; guests must also present the signed ticket issued
/// for the order number.
/// </summary>
public sealed record InitiatePaymentRequest(string OrderNumber, string? GuestAccessToken = null);
