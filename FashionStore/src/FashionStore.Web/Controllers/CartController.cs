using System.Security.Claims;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Storefront shopping cart. Authenticated customers get a persisted, server-scoped
/// cart keyed to their account; anonymous visitors use a temporary cookie cart that
/// is merged into the account after sign-in. Every mutation is scoped to the current
/// principal, so one customer can never read or mutate another customer's cart, and
/// all pricing and stock values are recomputed on the server.
/// </summary>
[Route("cart")]
public class CartController : Controller
{
    private readonly ICartService _cartService;
    private readonly IAddToCartService _addToCartService;
    private readonly IDiscountService _discountService;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly ILogger<CartController> _logger;

    public CartController(
        ICartService cartService,
        IAddToCartService addToCartService,
        IDiscountService discountService,
        IShippingCalculationService shippingCalculationService,
        ILogger<CartController> logger)
    {
        _cartService = cartService;
        _addToCartService = addToCartService;
        _discountService = discountService;
        _shippingCalculationService = shippingCalculationService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            var anonymous = AnonymousCartCookie.Read(HttpContext);
            var data = await _cartService.ResolveAnonymousAsync(
                anonymous,
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
            return View(data);
        }

        var cart = await _cartService.GetCartAsync(userId, cancellationToken);
        return View(cart);
    }

    [HttpPost("add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([FromBody] CartMutationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty || request.VariantId == Guid.Empty || request.Quantity < 1)
        {
            return BadRequest(new { success = false, message = "Invalid cart request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var validation = await _addToCartService.ValidateAsync(
                new AddToCartRequest(request.ProductId, request.VariantId, request.Quantity),
                cancellationToken);

            if (!validation.Success)
            {
                return Ok(new { success = false, message = validation.ErrorMessage });
            }

            var count = AnonymousCartCookie.Add(HttpContext, request.ProductId, request.VariantId, request.Quantity);
            return Ok(new { success = true, count });
        }

        var result = await _cartService.AddAsync(userId, request.ProductId, request.VariantId, request.Quantity, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity([FromBody] CartMutationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty || request.VariantId == Guid.Empty || request.Quantity < 1)
        {
            return BadRequest(new { success = false, message = "Invalid cart request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var validation = await _addToCartService.ValidateAsync(
                new AddToCartRequest(request.ProductId, request.VariantId, request.Quantity),
                cancellationToken);

            if (!validation.Success)
            {
                return Ok(new { success = false, message = validation.ErrorMessage });
            }

            var count = AnonymousCartCookie.UpdateQuantity(HttpContext, request.ProductId, request.VariantId, request.Quantity);
            return Ok(new { success = true, count });
        }

        var result = await _cartService.UpdateQuantityAsync(userId, request.ProductId, request.VariantId, request.Quantity, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost("remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromBody] CartMutationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty || request.VariantId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Invalid cart request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var count = AnonymousCartCookie.Remove(HttpContext, request.ProductId, request.VariantId);
            return Ok(new { success = true, count });
        }

        var result = await _cartService.RemoveAsync(userId, request.ProductId, request.VariantId, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost("clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            AnonymousCartCookie.Clear(HttpContext);
            return Ok(new { success = true, count = 0 });
        }

        var result = await _cartService.ClearAsync(userId, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Ok(new { count = AnonymousCartCookie.GetCount(HttpContext) });
        }

        var count = await _cartService.GetCountAsync(userId, cancellationToken);
        return Ok(new { count });
    }

    [HttpPost("coupon")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon([FromBody] CouponRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { success = false, message = "Enter a coupon code." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var data = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                null,
                cancellationToken);

            var result = await _discountService.ValidateCouponAsync(null, data.Items, request.Code, cancellationToken);
            if (result.Success)
            {
                AnonymousCouponCookie.Set(HttpContext, result.AppliedCouponCode ?? request.Code.Trim());
            }

            return Ok(result);
        }

        var applied = await _cartService.ApplyCouponAsync(userId, request.Code, cancellationToken);
        return Ok(applied);
    }

    [HttpPost("coupon/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCoupon(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            AnonymousCouponCookie.Clear(HttpContext);

            var data = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                null,
                cancellationToken);

            var pricing = data.Pricing;
            return Ok(new CouponApplyResult(
                true,
                "Coupon removed",
                false,
                null,
                pricing?.PromotionsDiscount ?? 0m,
                0m,
                pricing?.Total ?? data.Subtotal,
                pricing?.IsFreeShipping ?? false,
                pricing?.Breakdown ?? Array.Empty<FashionStore.Application.DTOs.Promotions.DiscountBreakdownItem>()));
        }

        var removed = await _cartService.RemoveCouponAsync(userId, cancellationToken);
        return Ok(removed);
    }

    [HttpGet("mini")]
    public async Task<IActionResult> MiniCart(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData data;
        if (string.IsNullOrEmpty(userId))
        {
            data = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
        }
        else
        {
            data = await _cartService.GetCartAsync(userId, cancellationToken);
        }

        return PartialView("Partials/_MiniCart", data);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData data;
        if (string.IsNullOrEmpty(userId))
        {
            data = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
        }
        else
        {
            data = await _cartService.GetCartAsync(userId, cancellationToken);
        }

        return PartialView("Partials/_OrderSummary", data);
    }

    /// <summary>
    /// Quotes the shipping methods for the current cart. The destination is taken
    /// from the request but the lines and subtotal are resolved from the server-side
    /// cart so the browser can never influence the calculated shipping cost.
    /// </summary>
    [HttpPost("shipping/quote")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShippingQuote([FromBody] ShippingQuoteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CountryCode))
        {
            return BadRequest(new { success = false, message = "Enter a delivery country." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData data;
        if (string.IsNullOrEmpty(userId))
        {
            data = await _cartService.ResolveAnonymousAsync(
                AnonymousCartCookie.Read(HttpContext),
                AnonymousCouponCookie.Read(HttpContext),
                cancellationToken);
        }
        else
        {
            data = await _cartService.GetCartAsync(userId, cancellationToken);
        }

        var lines = data.Items
            .Where(i => i.IsAvailable)
            .Select(i => new ShippingLineInput(i.ProductId, i.VariantId, i.Quantity))
            .ToList();

        var input = new ShippingCalculationInput(
            request.CountryCode,
            request.City,
            request.Region,
            request.PostalCode,
            data.Pricing?.Subtotal ?? data.Subtotal,
            lines,
            data.Pricing?.IsFreeShipping ?? false);

        var result = await _shippingCalculationService.QuoteAsync(input, cancellationToken);
        return Ok(result);
    }
}
