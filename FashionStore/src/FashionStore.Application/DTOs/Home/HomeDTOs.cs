using FashionStore.Application.DTOs.Navigation;

namespace FashionStore.Application.DTOs.Home;

/// <summary>
/// Aggregated view model for the public storefront homepage. Section content is
/// composed from strongly typed configuration (announcements, hero, promo
/// banners, benefits, lookbook) and from live catalogue data (categories,
/// products, collections, brands).
/// </summary>
public sealed record HomePageData(
    IReadOnlyList<Announcement> Announcements,
    HeroBannerDto Hero,
    IReadOnlyList<PromoBannerDto> PromoBanners,
    IReadOnlyList<HomeCategoryDto> Categories,
    IReadOnlyList<HomeProductCardDto> NewArrivals,
    IReadOnlyList<HomeProductCardDto> FeaturedProducts,
    IReadOnlyList<HomeProductCardDto> BestSellers,
    IReadOnlyList<HomeCollectionDto> Collections,
    IReadOnlyList<HomeProductCardDto> SaleProducts,
    IReadOnlyList<HomeBrandDto> Brands,
    IReadOnlyList<BenefitDto> Benefits,
    LookbookDto? Lookbook,
    bool ShowNewsletter
);

public sealed record HeroBannerDto(
    string Title,
    string Subtitle,
    string? ImageUrl,
    string? CtaText,
    string? CtaUrl
);

public sealed record PromoBannerDto(
    string Title,
    string? Subtitle,
    string? ImageUrl,
    string? LinkText,
    string? LinkUrl,
    string Style = "primary"
);

public sealed record HomeCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? ImageUrl,
    string? IconUrl,
    int ProductCount
);

/// <summary>
/// Compact storefront product card model. Prices are server-computed from the
/// product or its default variation and are never trusted from the client.
/// </summary>
public sealed record HomeProductCardDto(
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
    IReadOnlyList<HomeColourDto> Colours
);

public sealed record HomeColourDto(
    string Name,
    string? HexColour
);

public sealed record HomeCollectionDto(
    Guid Id,
    string Name,
    string Slug,
    string? BannerImageUrl,
    string? Description,
    DateTime? EndAtUtc
);

public sealed record HomeBrandDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl
);

public sealed record BenefitDto(
    string Icon,
    string Title,
    string Description
);

public sealed record LookbookDto(
    string Title,
    string Subtitle,
    string? ImageUrl,
    string? LinkText,
    string? LinkUrl
);
