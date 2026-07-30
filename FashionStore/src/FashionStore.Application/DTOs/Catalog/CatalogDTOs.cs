namespace FashionStore.Application.DTOs.Catalog;

public sealed record CreateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    string? ImageUrl,
    string? IconUrl,
    string? SeoTitle,
    string? SeoDescription,
    int DisplayOrder = 0,
    bool IsActive = true,
    bool ShowInMainMenu = false
);

public sealed record UpdateCategoryRequest(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    int DisplayOrder,
    string? ImageUrl,
    string? IconUrl,
    bool IsActive,
    bool ShowInMainMenu,
    string? SeoTitle,
    string? SeoDescription
);

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    int DisplayOrder,
    string? Description,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    int ChildrenCount,
    string? ImageUrl,
    string? IconUrl,
    bool IsActive,
    bool ShowInMainMenu,
    string? SeoTitle,
    string? SeoDescription,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public sealed record CategoryHierarchyDto(
    Guid Id,
    string Name,
    string Slug,
    string? IconUrl,
    int DisplayOrder,
    IEnumerable<CategoryHierarchyDto> Children
);

public sealed record CreateBrandRequest(
    string Name,
    string? Description,
    string? LogoUrl,
    string? WebsiteUrl,
    string? SeoTitle,
    string? SeoDescription,
    int DisplayOrder = 0,
    bool IsActive = true
);

public sealed record UpdateBrandRequest(
    Guid Id,
    string Name,
    string? Description,
    string? LogoUrl,
    string? WebsiteUrl,
    int DisplayOrder,
    bool IsActive,
    string? SeoTitle,
    string? SeoDescription
);

public sealed record BrandDto(
    Guid Id,
    string Name,
    string Slug,
    int DisplayOrder,
    string? Description,
    string? LogoUrl,
    string? WebsiteUrl,
    bool IsActive,
    string? SeoTitle,
    string? SeoDescription,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    int ProductCount
);

public sealed record CreateCollectionRequest(
    string Name,
    string? Description,
    string? BannerImageUrl,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    string? SeoTitle,
    string? SeoDescription,
    int DisplayOrder = 0,
    bool IsActive = true
);

public sealed record UpdateCollectionRequest(
    Guid Id,
    string Name,
    string? Description,
    string? BannerImageUrl,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    int DisplayOrder,
    bool IsActive,
    string? SeoTitle,
    string? SeoDescription
);

public sealed record CollectionDto(
    Guid Id,
    string Name,
    string Slug,
    int DisplayOrder,
    string? Description,
    string? BannerImageUrl,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    bool IsActive,
    string? SeoTitle,
    string? SeoDescription,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    int ProductCount
);
