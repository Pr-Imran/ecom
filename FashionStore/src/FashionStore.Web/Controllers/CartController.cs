using System.Security.Claims;
using FashionStore.Application.DTOs.Products;
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
    private readonly ILogger<CartController> _logger;

    public CartController(
        ICartService cartService,
        IAddToCartService addToCartService,
        ILogger<CartController> logger)
    {
        _cartService = cartService;
        _addToCartService = addToCartService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            var anonymous = AnonymousCartCookie.Read(HttpContext);
            var data = await _cartService.ResolveAnonymousAsync(anonymous, cancellationToken);
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

    [HttpGet("mini")]
    public async Task<IActionResult> MiniCart(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        CartViewData data;
        if (string.IsNullOrEmpty(userId))
        {
            data = await _cartService.ResolveAnonymousAsync(AnonymousCartCookie.Read(HttpContext), cancellationToken);
        }
        else
        {
            data = await _cartService.GetCartAsync(userId, cancellationToken);
        }

        return PartialView("Partials/_MiniCart", data);
    }
}
