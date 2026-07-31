namespace FashionStore.Application.DTOs.Products;

public sealed record ProductAttributeDto(
    Guid Id,
    string Name,
    string Slug,
    string DisplayType,
    bool IsVariationAttribute,
    bool IsActive,
    int DisplayOrder,
    string? Description,
    List<ProductAttributeValueDto> Values,
    DateTime CreatedAtUtc
);

public sealed record ProductAttributeValueDto(
    Guid Id,
    Guid ProductAttributeId,
    string Name,
    string Slug,
    string? DisplayValue,
    string? HexColour,
    string? ImageUrl,
    bool IsActive,
    int DisplayOrder
);

public sealed record CreateProductAttributeRequest(
    string Name,
    string DisplayType,
    bool IsVariationAttribute,
    int DisplayOrder,
    string? Description
);

public sealed record UpdateProductAttributeRequest(
    Guid Id,
    string Name,
    string DisplayType,
    bool IsVariationAttribute,
    int DisplayOrder,
    string? Description
);

public sealed record CreateProductAttributeValueRequest(
    Guid ProductAttributeId,
    string Name,
    string? DisplayValue,
    string? HexColour,
    string? ImageUrl,
    int DisplayOrder
);

public sealed record UpdateProductAttributeValueRequest(
    Guid Id,
    string Name,
    string? DisplayValue,
    string? HexColour,
    string? ImageUrl,
    int DisplayOrder
);

public sealed record ProductVariantDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    decimal? Weight,
    bool IsDefault,
    bool IsActive,
    int? StockQuantity,
    int? ReservedStock,
    int? LowStockThreshold,
    string? ImageUrl,
    string? Notes,
    Dictionary<string, string> AttributeValues,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public sealed record CreateProductVariantRequest(
    Guid ProductId,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    decimal? Weight,
    bool IsDefault,
    bool IsActive,
    int? StockQuantity,
    string? ImageUrl,
    string? Notes,
    List<Guid> AttributeValueIds
);

public sealed record UpdateProductVariantRequest(
    Guid Id,
    Guid ProductId,
    string Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? CostPrice,
    decimal? Weight,
    bool IsDefault,
    bool IsActive,
    int? StockQuantity,
    string? ImageUrl,
    string? Notes,
    List<Guid> AttributeValueIds
);

public sealed record GenerateVariantsRequest(
    Guid ProductId,
    List<Guid> AttributeValueIds,
    string SkuPattern,
    decimal BasePrice,
    bool GenerateAllCombinations
);

public sealed record VariantCombinationDto(
    List<Guid> AttributeValueIds,
    Dictionary<string, string> DisplayValues,
    string SuggestedSku,
    string? Sku,
    decimal? Price,
    decimal? CompareAtPrice,
    int? StockQuantity,
    bool? IsActive,
    bool? IsDefault,
    bool Exists,
    Guid? ExistingVariantId
);

public sealed record BulkUpdateVariantsRequest(
    List<Guid> VariantIds,
    decimal? PriceAdjustment,
    bool PriceAdjustmentIsPercentage,
    int? StockAdjustment,
    bool? IsActive,
    decimal? NewPrice,
    int? NewStock
);

public sealed record StorefrontVariantDto(
    Guid Id,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    bool IsInStock,
    int? StockQuantity,
    string? ImageUrl,
    Dictionary<string, string> AttributeValues,
    Dictionary<string, string?> AttributeValueIds,
    bool IsDefault
);

public sealed record StorefrontProductVariationsDto(
    Guid ProductId,
    List<StorefrontVariationOptionDto> AvailableOptions,
    List<StorefrontVariantDto> Variants,
    List<VariantCombinationAvailabilityDto> AvailableCombinations
);

public sealed record StorefrontVariationOptionDto(
    string AttributeName,
    string AttributeSlug,
    List<StorefrontVariationOptionValueDto> Values
);

public sealed record StorefrontVariationOptionValueDto(
    Guid Id,
    string Name,
    string? DisplayValue,
    string? HexColour,
    string? ImageUrl,
    bool IsAvailable
);

public sealed record VariantCombinationAvailabilityDto(
    List<string> AttributeSlugs,
    List<string> ValueSlugs,
    Guid? VariantId,
    bool IsAvailable,
    bool IsInStock,
    decimal? Price,
    string? ImageUrl
);
