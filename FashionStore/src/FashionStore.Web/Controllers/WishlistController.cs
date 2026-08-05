using System.Security.Claims;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Storefront wishlist. Authenticated customers get a persisted, server-scoped
/// wishlist keyed to their account; anonymous visitors use a temporary cookie list
/// that is merged into the account after sign-in. Every mutation is scoped to the
/// current principal, so one customer can never read or mutate another customer's
/// entries.
/// </summary>
public class WishlistController : Controller
{
    private readonly IWishlistService _wishlistService;
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<WishlistController> _logger;

    public WishlistController(
        IWishlistService wishlistService,
        IAntiforgery antiforgery,
        ILogger<WishlistController> logger)
    {
        _wishlistService = wishlistService;
        _antiforgery = antiforgery;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var recentlyViewedIds = RecentlyViewedCookie.Read(HttpContext);

        if (string.IsNullOrEmpty(userId))
        {
            var anonymous = AnonymousWishlistCookie.Read(HttpContext);
            var data = await _wishlistService.ResolveAnonymousAsync(anonymous, cancellationToken);
            return View(data);
        }

        var wishlist = await _wishlistService.GetWishlistAsync(userId, recentlyViewedIds, cancellationToken);
        return View(wishlist);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([FromBody] WishlistMutationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Invalid wishlist request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var count = AnonymousWishlistCookie.Append(HttpContext, request.ProductId, request.VariantId);
            return Ok(new { success = true, count });
        }

        var result = await _wishlistService.AddAsync(userId, request.ProductId, request.VariantId, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromBody] WishlistMutationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Invalid wishlist request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            var count = AnonymousWishlistCookie.Remove(HttpContext, request.ProductId, request.VariantId);
            return Ok(new { success = true, count });
        }

        var result = await _wishlistService.RemoveByProductAsync(userId, request.ProductId, request.VariantId, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost("wishlist/remove-by-id")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveById([FromBody] RemoveWishlistItemRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.WishlistItemId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Invalid wishlist request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, message = "Sign in to manage your wishlist." });
        }

        var result = await _wishlistService.RemoveAsync(userId, request.WishlistItemId, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpPost("wishlist/move-to-cart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveToCart([FromBody] MoveToCartRequestDto? request, CancellationToken cancellationToken)
    {
        if (request is null || request.WishlistItemId == Guid.Empty || request.Quantity < 1)
        {
            return BadRequest(new { success = false, message = "Invalid move-to-cart request." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, message = "Sign in to move items to your cart." });
        }

        var result = await _wishlistService.MoveToCartAsync(userId, request.WishlistItemId, request.Quantity, cancellationToken);
        return Ok(new { success = result.Success, message = result.ErrorMessage, count = result.ItemCount });
    }

    [HttpGet]
    public async Task<IActionResult> Count(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Ok(new { count = AnonymousWishlistCookie.GetCount(HttpContext) });
        }

        var count = await _wishlistService.GetCountAsync(userId, cancellationToken);
        return Ok(new { count });
    }
}
