using FashionStore.Application.DTOs.Home;

namespace FashionStore.Application.DTOs.Catalog;

public enum ProductSortOrder
{
    Relevance = 0,
    Newest,
    Oldest,
    PriceLowHigh,
    PriceHighLow,
    Popularity,
    BestSelling,
    HighestRated,
    Discount,
    Featured
}

/// <summary>
/// Catalogue listing sort options with their query-string values and labels.
/// </summary>
public static class ProductSortOptions
{
    public static IReadOnlyList<(string Value, string Label)> All { get; } = new (string, string)[]
    {
        ("relevance", "Relevance"),
        ("featured", "Featured"),
        ("newest", "Newest"),
        ("oldest", "Oldest"),
        ("price-asc", "Price: Low to High"),
        ("price-desc", "Price: High to Low"),
        ("popularity", "Popularity"),
        ("best-selling", "Best Selling"),
        ("rating", "Highest Rated"),
        ("discount", "Discount")
    };

    public static ProductSortOrder Parse(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "featured" => ProductSortOrder.Featured,
            "newest" or "new" => ProductSortOrder.Newest,
            "oldest" => ProductSortOrder.Oldest,
            "price-asc" or "price-low-to-high" or "price-ascending" => ProductSortOrder.PriceLowHigh,
            "price-desc" or "price-high-to-low" or "price-descending" => ProductSortOrder.PriceHighLow,
            "popularity" => ProductSortOrder.Popularity,
            "best-selling" or "bestselling" or "best-sellers" => ProductSortOrder.BestSelling,
            "rating" or "highest-rated" or "highest-rating" => ProductSortOrder.HighestRated,
            "discount" => ProductSortOrder.Discount,
            _ => ProductSortOrder.Relevance
        };
    }

    public static string ToUrlValue(ProductSortOrder order) => order switch
    {
        ProductSortOrder.Featured => "featured",
        ProductSortOrder.Newest => "newest",
        ProductSortOrder.Oldest => "oldest",
        ProductSortOrder.PriceLowHigh => "price-asc",
        ProductSortOrder.PriceHighLow => "price-desc",
        ProductSortOrder.Popularity => "popularity",
        ProductSortOrder.BestSelling => "best-selling",
        ProductSortOrder.HighestRated => "rating",
        ProductSortOrder.Discount => "discount",
        _ => "relevance"
    };
}

public enum CatalogEntityKind
{
    Category,
    Brand,
    Collection
}

/// <summary>
/// Mutable query-string model bound from the storefront catalogue URLs.
/// </summary>
public class ProductListQuery
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Collection { get; set; }

    public string[] Colour { get; set; } = Array.Empty<string>();
    public string[] Size { get; set; } = Array.Empty<string>();
    public string[] Material { get; set; } = Array.Empty<string>();
    public string[] Tag { get; set; } = Array.Empty<string>();
    public string? Gender { get; set; }

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStock { get; set; }
    public bool OnSale { get; set; }
    public int? MinRating { get; set; }

    public string? Sort { get; set; } = "relevance";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public string View { get; set; } = "grid";

    /// <summary>Identifies the listing context (all, category, brand, collection, search, sale, new, best).</summary>
    public string? ListingType { get; set; }
    public string? ListingTitle { get; set; }
    public string? ListingSubtitle { get; set; }
    public string? ListingLink { get; set; }
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Storefront product card. Prices, stock and rating are server-computed and never
/// trusted from the client.
/// </summary>
public sealed record ProductListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? BrandName,
    string? ImageUrl,
    string? ImageCardUrl,
    string? ImageAltText,
    decimal Price,
    decimal? CompareAtPrice,
    int? DiscountPercent,
    bool IsNew,
    bool IsInStock,
    bool IsLowStock,
    IReadOnlyList<HomeColourDto> Colours,
    double AverageRating,
    int ReviewCount);

public sealed record FacetValueDto(
    string Value,
    string Label,
    int Count,
    bool Selected);

public sealed record FacetGroupDto(
    string Key,
    string Label,
    bool MultiSelect,
    IReadOnlyList<FacetValueDto> Values);

/// <summary>
/// Aggregated result for a catalogue listing page: the paged products, the available
/// facets derived from the filtered result set, and listing context.
/// </summary>
public sealed record CatalogPageData(
    PagedResult<ProductListItemDto> Results,
    IReadOnlyList<FacetGroupDto> Facets,
    ProductListQuery Query,
    decimal? MinAvailablePrice,
    decimal? MaxAvailablePrice,
    string? ListingTitle,
    string? ListingSubtitle,
    string? ListingLink);
