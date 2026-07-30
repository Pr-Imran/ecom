namespace FashionStore.Application.DTOs.Products;

public sealed record CreateProductRequest(
    string Name,
    string ShortDescription,
    string FullDescription,
    Guid CategoryId,
    Guid? BrandId,
    Guid? CollectionId,
    string ProductType,
    string? Material,
    string? Fabric,
    string? CareInstructions,
    string? Gender,
    string? CountryOfOrigin,
    string BaseSku,
    string? Barcode,
    decimal BasePrice,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    string TaxCategory,
    decimal? Weight,
    bool IsActive,
    bool IsFeatured,
    bool IsNewArrival,
    bool IsBestSeller,
    bool AllowReviews,
    DateTime? PublishedAtUtc,
    string? SeoTitle,
    string? SeoDescription,
    string? SearchKeywords,
    List<Guid>? TagIds
);

public sealed record UpdateProductRequest(
    Guid Id,
    string Name,
    string ShortDescription,
    string FullDescription,
    Guid CategoryId,
    Guid? BrandId,
    Guid? CollectionId,
    string ProductType,
    string? Material,
    string? Fabric,
    string? CareInstructions,
    string? Gender,
    string? CountryOfOrigin,
    string BaseSku,
    string? Barcode,
    decimal BasePrice,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    string TaxCategory,
    decimal? Weight,
    bool IsActive,
    bool IsFeatured,
    bool IsNewArrival,
    bool IsBestSeller,
    bool AllowReviews,
    DateTime? PublishedAtUtc,
    string? SeoTitle,
    string? SeoDescription,
    string? SearchKeywords,
    List<Guid>? TagIds
);

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    Guid CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    Guid? CollectionId,
    string? CollectionName,
    string ProductType,
    string? Material,
    string? Fabric,
    string? CareInstructions,
    string? Gender,
    string? CountryOfOrigin,
    string BaseSku,
    string? Barcode,
    decimal BasePrice,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    string TaxCategory,
    decimal? Weight,
    bool IsActive,
    bool IsFeatured,
    bool IsNewArrival,
    bool IsBestSeller,
    bool AllowReviews,
    DateTime? PublishedAtUtc,
    string? SeoTitle,
    string? SeoDescription,
    string? SearchKeywords,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    List<string> Tags
);

public sealed record ProductListDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string BaseSku,
    decimal BasePrice,
    bool IsActive,
    bool IsFeatured,
    DateTime? PublishedAtUtc,
    string? CategoryName,
    string? BrandName,
    DateTime CreatedAtUtc
);

public sealed record DuplicateProductRequest(
    Guid SourceProductId,
    string NewName,
    string? NewSku
);

public sealed record ProductSearchRequest(
    string? SearchTerm,
    Guid? CategoryId,
    Guid? BrandId,
    bool? IsActive,
    bool? IsFeatured,
    string? SortBy,
    bool SortDescending,
    int Page = 1,
    int PageSize = 20
);

public sealed record ProductSearchResult(
    IEnumerable<ProductListDto> Products,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);
