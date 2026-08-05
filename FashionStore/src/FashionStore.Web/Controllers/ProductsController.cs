using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

[Route("products")]
public class ProductsController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly IProductDetailsService _productDetailsService;
    private readonly IAddToCartService _addToCartService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        ICatalogService catalogService,
        IProductDetailsService productDetailsService,
        IAddToCartService addToCartService,
        ILogger<ProductsController> logger)
    {
        _catalogService = catalogService;
        _productDetailsService = productDetailsService;
        _addToCartService = addToCartService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(ProductListQuery query, CancellationToken cancellationToken)
    {
        query.ListingType = "all";
        query.ListingTitle = "All Products";
        query.ListingSubtitle = "Browse the full collection";
        query.ListingLink = "/products";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View(data);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(ProductListQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Q))
        {
            return RedirectToAction(nameof(Index));
        }

        query.ListingType = "search";
        query.ListingTitle = "Search Results";
        query.ListingSubtitle = $"Matching \"{query.Q.Trim()}\"";
        query.ListingLink = "/products/search";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("sale")]
    public async Task<IActionResult> Sale(ProductListQuery query, CancellationToken cancellationToken)
    {
        query.ListingType = "sale";
        query.OnSale = true;
        query.ListingTitle = "On Sale";
        query.ListingSubtitle = "Limited-time deals while stock lasts";
        query.ListingLink = "/products/sale";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(ProductListQuery query, CancellationToken cancellationToken)
    {
        query.ListingType = "new";
        query.ListingTitle = "New Arrivals";
        query.ListingSubtitle = "Fresh from the runway";
        query.ListingLink = "/products/new";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("best")]
    public async Task<IActionResult> Best(ProductListQuery query, CancellationToken cancellationToken)
    {
        query.ListingType = "best";
        query.ListingTitle = "Best Sellers";
        query.ListingSubtitle = "Loved by thousands of shoppers";
        query.ListingLink = "/products/best";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("/categories")]
    public IActionResult Categories()
    {
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/collections")]
    public IActionResult Collections()
    {
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/categories/{slug}")]
    public async Task<IActionResult> Category(string slug, ProductListQuery query, CancellationToken cancellationToken)
    {
        var name = await _catalogService.ResolveEntityNameAsync(CatalogEntityKind.Category, slug, cancellationToken);
        if (name is null)
        {
            return NotFound();
        }

        query.Category = slug;
        query.ListingType = "category";
        query.ListingTitle = name;
        query.ListingSubtitle = "Shop the category";
        query.ListingLink = $"/categories/{slug}";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("/brands/{slug}")]
    public async Task<IActionResult> Brand(string slug, ProductListQuery query, CancellationToken cancellationToken)
    {
        var name = await _catalogService.ResolveEntityNameAsync(CatalogEntityKind.Brand, slug, cancellationToken);
        if (name is null)
        {
            return NotFound();
        }

        query.Brand = slug;
        query.ListingType = "brand";
        query.ListingTitle = name;
        query.ListingSubtitle = "Shop the brand";
        query.ListingLink = $"/brands/{slug}";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("/collections/{slug}")]
    public async Task<IActionResult> Collection(string slug, ProductListQuery query, CancellationToken cancellationToken)
    {
        var name = await _catalogService.ResolveEntityNameAsync(CatalogEntityKind.Collection, slug, cancellationToken);
        if (name is null)
        {
            return NotFound();
        }

        query.Collection = slug;
        query.ListingType = "collection";
        query.ListingTitle = name;
        query.ListingSubtitle = "Shop the collection";
        query.ListingLink = $"/collections/{slug}";

        var data = await _catalogService.GetProductsAsync(query, cancellationToken);
        return View("Index", data);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        var recentlyViewedIds = RecentlyViewedCookie.Read(HttpContext);
        var data = await _productDetailsService.GetDetailsAsync(slug, recentlyViewedIds, cancellationToken);

        if (data is null)
        {
            return NotFound();
        }

        RecentlyViewedCookie.Append(HttpContext, data.Id);

        ViewData["CanonicalUrl"] = $"{Request.Scheme}://{Request.Host}{Request.Path}";
        return View(data);
    }

    [HttpPost("add-to-cart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty || request.VariantId == Guid.Empty)
        {
            return BadRequest(new { success = false, error = "Invalid add-to-cart request." });
        }

        var result = await _addToCartService.ValidateAsync(request, cancellationToken);
        return Ok(new { success = result.Success, error = result.ErrorMessage, item = result.Item });
    }
}
