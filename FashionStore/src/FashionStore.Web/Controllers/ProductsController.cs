using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

[Route("products")]
public class ProductsController : Controller
{
    private readonly ICatalogService _catalogService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        ICatalogService catalogService,
        ILogger<ProductsController> logger)
    {
        _catalogService = catalogService;
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
}
