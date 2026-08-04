namespace FashionStore.Application.Configuration;

/// <summary>
/// Strongly typed configuration for the public storefront homepage.
///
/// Section content (hero, promotional banners, benefits, lookbook) is driven
/// from configuration so it can later be edited through the admin content
/// management without changing code. Database-driven sections (categories,
/// products, collections, brands) are controlled by the enable flags and
/// counts below.
/// </summary>
public sealed class HomePageSettings
{
    public const string SectionName = "HomePage";

    public bool EnableAnnouncementBar { get; init; } = true;
    public bool EnableCategories { get; init; } = true;
    public bool EnableNewArrivals { get; init; } = true;
    public bool EnableFeaturedProducts { get; init; } = true;
    public bool EnableBestSellers { get; init; } = true;
    public bool EnableCollections { get; init; } = true;
    public bool EnableSaleProducts { get; init; } = true;
    public bool EnableBrands { get; init; } = true;
    public bool EnableBenefits { get; init; } = true;
    public bool EnableNewsletter { get; init; } = true;
    public bool EnableLookbook { get; init; } = true;

    public int CategoryCount { get; init; } = 10;
    public int NewArrivalsCount { get; init; } = 8;
    public int FeaturedProductsCount { get; init; } = 8;
    public int BestSellersCount { get; init; } = 8;
    public int SaleProductsCount { get; init; } = 8;
    public int CollectionCount { get; init; } = 4;
    public int BrandCount { get; init; } = 12;
    public int BenefitCount { get; init; } = 4;

    public HeroSectionSettings Hero { get; init; } = new();
    public IReadOnlyList<PromoBannerSectionSettings> PromoBanners { get; init; } = new List<PromoBannerSectionSettings>();
    public IReadOnlyList<BenefitSectionSettings> Benefits { get; init; } = new List<BenefitSectionSettings>();
    public LookbookSectionSettings Lookbook { get; init; } = new();
}

public sealed class HeroSectionSettings
{
    public string Title { get; init; } = "New Season, New You";
    public string Subtitle { get; init; } = "Discover this season's must-have styles for every occasion.";
    public string? ImageUrl { get; init; }
    public string? CtaText { get; init; } = "Shop Now";
    public string? CtaUrl { get; init; } = "/products";
}

public sealed class PromoBannerSectionSettings
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? ImageUrl { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
    public string Style { get; init; } = "primary";
}

public sealed class BenefitSectionSettings
{
    public string Icon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class LookbookSectionSettings
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
}
