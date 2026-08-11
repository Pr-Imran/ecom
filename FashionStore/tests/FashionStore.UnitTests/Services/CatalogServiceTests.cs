using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionStore.UnitTests.Services;

public class CatalogServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-catalog-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static CatalogService CreateService(AppDbContext context)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ResolveUrl(It.IsAny<string>()))
            .Returns((string path) => $"/uploads/{path}");
        return new CatalogService(context, storage.Object, NullLogger<CatalogService>.Instance);
    }

    private static async Task<CatalogSeed> SeedAsync(AppDbContext context)
    {
        var now = DateTime.UtcNow;

        var clothing = new Category { Name = "Clothing", Slug = "clothing", IsActive = true };
        var knitwear = new Category { Name = "Knitwear", Slug = "knitwear", IsActive = true, ParentCategory = clothing };
        var footwear = new Category { Name = "Footwear", Slug = "footwear", IsActive = true };

        var everlane = new Brand { Name = "Everlane", Slug = "everlane", IsActive = true };
        var nike = new Brand { Name = "Nike", Slug = "nike", IsActive = true };

        var autumn = new Collection { Name = "Autumn Edit", Slug = "autumn-edit", IsActive = true };

        var cashmereTag = new ProductTag { Name = "Cashmere", Slug = "cashmere" };
        var saleTag = new ProductTag { Name = "Clearance", Slug = "clearance" };

        var colour = new ProductAttribute { Name = "Colour", Slug = "colour" };
        var heatherGrey = new ProductAttributeValue { Name = "Heather Grey", Slug = "heather-grey", HexColour = "#999999", ProductAttribute = colour };
        var black = new ProductAttributeValue { Name = "Black", Slug = "black", HexColour = "#000000", ProductAttribute = colour };

        var size = new ProductAttribute { Name = "Size", Slug = "size" };
        var sizeS = new ProductAttributeValue { Name = "S", Slug = "s", ProductAttribute = size };
        var sizeM = new ProductAttributeValue { Name = "M", Slug = "m", ProductAttribute = size };
        var sizeL = new ProductAttributeValue { Name = "L", Slug = "l", ProductAttribute = size };

        context.Categories.AddRange(clothing, knitwear, footwear);
        context.Brands.AddRange(everlane, nike);
        context.Collections.Add(autumn);
        context.ProductTags.AddRange(cashmereTag, saleTag);
        context.ProductAttributes.AddRange(colour, size);
        context.ProductAttributeValues.AddRange(heatherGrey, black, sizeS, sizeM, sizeL);
        await context.SaveChangesAsync();

        var sweater = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            CategoryId = clothing.Id,
            BrandId = everlane.Id,
            CollectionId = autumn.Id,
            Material = "Cashmere",
            Gender = "Women",
            BaseSku = "SW-1001",
            BasePrice = 128.00m,
            CompareAtPrice = 160.00m,
            SearchKeywords = "cosy jumper knitwear",
            IsActive = true,
            IsNewArrival = true,
            IsFeatured = true,
            IsBestSeller = true,
            DisplayOrder = 1,
            PublishedAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-10),
            ProductTagMappings = new List<ProductTagMapping> { new() { ProductTag = cashmereTag } }
        };

        var beanie = new Product
        {
            Name = "Wool Knit Beanie",
            Slug = "wool-knit-beanie",
            CategoryId = knitwear.Id,
            BrandId = everlane.Id,
            CollectionId = autumn.Id,
            Material = "Wool",
            Gender = "Unisex",
            BaseSku = "BE-2002",
            BasePrice = 40.00m,
            IsActive = true,
            DisplayOrder = 2,
            PublishedAtUtc = now.AddDays(-2),
            CreatedAtUtc = now.AddDays(-40)
        };

        var shoe = new Product
        {
            Name = "Trail Running Shoe",
            Slug = "trail-running-shoe",
            CategoryId = footwear.Id,
            BrandId = nike.Id,
            Material = "Synthetic",
            Gender = "Men",
            BaseSku = "SH-3003",
            BasePrice = 150.00m,
            IsActive = true,
            IsBestSeller = true,
            DisplayOrder = 3,
            PublishedAtUtc = now.AddDays(-3),
            CreatedAtUtc = now.AddDays(-5)
        };

        var unpublished = new Product
        {
            Name = "Unpublished Blazer",
            Slug = "unpublished-blazer",
            CategoryId = clothing.Id,
            BrandId = everlane.Id,
            BaseSku = "BL-4004",
            BasePrice = 99.00m,
            IsActive = true,
            DisplayOrder = 4,
            PublishedAtUtc = null,
            CreatedAtUtc = now
        };

        var inactive = new Product
        {
            Name = "Retired Scarf",
            Slug = "retired-scarf",
            CategoryId = clothing.Id,
            BrandId = everlane.Id,
            BaseSku = "SC-5005",
            BasePrice = 25.00m,
            IsActive = false,
            DisplayOrder = 5,
            PublishedAtUtc = now.AddDays(-1),
            CreatedAtUtc = now
        };

        context.Products.AddRange(sweater, beanie, shoe, unpublished, inactive);
        await context.SaveChangesAsync();

        var sweaterGreyM = new ProductVariant
        {
            ProductId = sweater.Id,
            Sku = "SW-1001-GREY-M",
            Price = 128.00m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 10,
            ReservedStock = 0,
            VariantAttributeValues = new List<ProductVariantAttributeValue>
            {
                new() { AttributeValue = heatherGrey },
                new() { AttributeValue = sizeM }
            }
        };

        var beanieBlack = new ProductVariant
        {
            ProductId = beanie.Id,
            Sku = "BE-2002-BLK-OS",
            Price = 40.00m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 3,
            ReservedStock = 0,
            VariantAttributeValues = new List<ProductVariantAttributeValue>
            {
                new() { AttributeValue = black },
                new() { AttributeValue = sizeS }
            }
        };

        var shoeBlack9 = new ProductVariant
        {
            ProductId = shoe.Id,
            Sku = "SH-3003-BLK-09",
            Price = 150.00m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 0,
            ReservedStock = 0,
            VariantAttributeValues = new List<ProductVariantAttributeValue>
            {
                new() { AttributeValue = black },
                new() { AttributeValue = sizeL }
            }
        };

        context.ProductVariants.AddRange(sweaterGreyM, beanieBlack, shoeBlack9);
        await context.SaveChangesAsync();

        context.ProductReviews.AddRange(
            new ProductReview { ProductId = sweater.Id, Rating = 5, Status = ReviewStatus.Approved },
            new ProductReview { ProductId = sweater.Id, Rating = 4, Status = ReviewStatus.Approved },
            new ProductReview { ProductId = sweater.Id, Rating = 1, Status = ReviewStatus.Pending },
            new ProductReview { ProductId = beanie.Id, Rating = 2, Status = ReviewStatus.Approved },
            new ProductReview { ProductId = shoe.Id, Rating = 5, Status = ReviewStatus.Approved });

        context.ProductImages.Add(new ProductImage
        {
            ProductId = sweater.Id,
            FileName = "sweater.jpg",
            IsMain = true,
            DisplayOrder = 0,
            ImageFormat = "jpeg",
            ContentType = "image/jpeg"
        });

        await context.SaveChangesAsync();

        return new CatalogSeed(
            clothing, knitwear, footwear,
            everlane, nike,
            autumn,
            cashmereTag,
            heatherGrey, black,
            sweater, beanie, shoe,
            sweaterGreyM, beanieBlack, shoeBlack9);
    }

    [Fact]
    public async Task GetProductsAsync_BaseQuery_OnlyReturnsActivePublishedProducts()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery(), CancellationToken.None);

        Assert.Equal(3, data.Results.TotalCount);
        Assert.DoesNotContain(data.Results.Items, i => i.Name == "Unpublished Blazer");
        Assert.DoesNotContain(data.Results.Items, i => i.Name == "Retired Scarf");
    }

    [Fact]
    public async Task GetProductsAsync_SearchByName_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Q = "Sweater" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("Cashmere Crew Neck Sweater", data.Results.Items[0].Name);
    }

    [Theory]
    [InlineData("SW-1001")]
    [InlineData("SW-1001-GREY-M")]
    [InlineData("Everlane")]
    [InlineData("Clothing")]
    [InlineData("Cashmere")]
    [InlineData("cosy")]
    public async Task GetProductsAsync_SearchAcrossFields_FindsSweater(string term)
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Q = term }, CancellationToken.None);

        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
    }

    [Fact]
    public async Task GetProductsAsync_CategoryFilter_IncludesSubcategoryProducts()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Category = "clothing" }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "wool-knit-beanie");
    }

    [Fact]
    public async Task GetProductsAsync_SubcategoryFilter_ReturnsOnlyOwnProducts()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Category = "knitwear" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("wool-knit-beanie", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_BrandFilter_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Brand = "nike" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("trail-running-shoe", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_CollectionFilter_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Collection = "autumn-edit" }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "wool-knit-beanie");
    }

    [Fact]
    public async Task GetProductsAsync_ColourFilter_MatchesVariantAttribute()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Colour = new[] { "heather-grey" } }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SizeFilter_MatchesVariantAttribute()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Size = new[] { "m" } }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_MaterialFilter_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Material = new[] { "Cashmere" } }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_TagFilter_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Tag = new[] { "cashmere" } }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_GenderFilter_CaseInsensitive()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Gender = "WOMEN" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_PriceRange_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { MinPrice = 100m, MaxPrice = 200m }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "trail-running-shoe");
    }

    [Fact]
    public async Task GetProductsAsync_SwappedPriceRange_DoesNotCrashAndFilters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { MinPrice = 200m, MaxPrice = 100m }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "trail-running-shoe");
    }

    [Fact]
    public async Task GetProductsAsync_InStock_FiltersOutOutOfStock()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { InStock = true }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.DoesNotContain(data.Results.Items, i => i.Slug == "trail-running-shoe");
    }

    [Fact]
    public async Task GetProductsAsync_OnSale_Filters()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { OnSale = true }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_MinRating_OnlyApprovedReviewsCount()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { MinRating = 4 }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "trail-running-shoe");
    }

    [Fact]
    public async Task GetProductsAsync_ListingTypeNew_FiltersNewArrivals()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { ListingType = "new" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_ListingTypeBest_FiltersBestSellers()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { ListingType = "best" }, CancellationToken.None);

        Assert.Equal(2, data.Results.TotalCount);
        Assert.Contains(data.Results.Items, i => i.Slug == "cashmere-crew-neck-sweater");
        Assert.Contains(data.Results.Items, i => i.Slug == "trail-running-shoe");
    }

    [Fact]
    public async Task GetProductsAsync_ListingTypeSale_FiltersDiscounted()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { ListingType = "sale" }, CancellationToken.None);

        Assert.Equal(1, data.Results.TotalCount);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SortPriceAscending_OrdersByPrice()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "price-asc" }, CancellationToken.None);

        Assert.Equal(3, data.Results.Items.Count);
        Assert.Equal(40m, data.Results.Items[0].Price);
        Assert.Equal(128m, data.Results.Items[1].Price);
        Assert.Equal(150m, data.Results.Items[2].Price);
    }

    [Fact]
    public async Task GetProductsAsync_SortPriceDescending_OrdersByPrice()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "price-desc" }, CancellationToken.None);

        Assert.Equal("trail-running-shoe", data.Results.Items[0].Slug);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[1].Slug);
        Assert.Equal("wool-knit-beanie", data.Results.Items[2].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SortNewest_OrdersByCreatedAtDescending()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "newest" }, CancellationToken.None);

        Assert.Equal("trail-running-shoe", data.Results.Items[0].Slug);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[1].Slug);
        Assert.Equal("wool-knit-beanie", data.Results.Items[2].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SortOldest_OrdersByCreatedAtAscending()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "oldest" }, CancellationToken.None);

        Assert.Equal("wool-knit-beanie", data.Results.Items[0].Slug);
        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[1].Slug);
        Assert.Equal("trail-running-shoe", data.Results.Items[2].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SortBestSelling_OrdersBestSellersFirst()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "best-selling" }, CancellationToken.None);

        Assert.Contains("cashmere-crew-neck-sweater", new[] { data.Results.Items[0].Slug, data.Results.Items[1].Slug });
        Assert.Contains("trail-running-shoe", new[] { data.Results.Items[0].Slug, data.Results.Items[1].Slug });
        Assert.Equal("wool-knit-beanie", data.Results.Items[2].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_SortHighestRated_OrdersByAverageRating()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "rating" }, CancellationToken.None);

        Assert.Equal(5.0, data.Results.Items[0].AverageRating);
        Assert.Equal(4.5, data.Results.Items[1].AverageRating);
        Assert.Equal(2.0, data.Results.Items[2].AverageRating);
    }

    [Fact]
    public async Task GetProductsAsync_SortDiscount_OrdersDiscountedFirst()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "discount" }, CancellationToken.None);

        Assert.Equal("cashmere-crew-neck-sweater", data.Results.Items[0].Slug);
    }

    [Fact]
    public async Task GetProductsAsync_InvalidSort_FallsBackToRelevance()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Sort = "gibberish" }, CancellationToken.None);

        Assert.Equal(3, data.Results.TotalCount);
        Assert.Equal(1, data.Results.Page);
    }

    [Fact]
    public async Task GetProductsAsync_NegativePage_ClampsToOne()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Page = -5 }, CancellationToken.None);

        Assert.Equal(1, data.Results.Page);
        Assert.Equal(3, data.Results.Items.Count);
    }

    [Fact]
    public async Task GetProductsAsync_PageSizeClampedToMax48()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { PageSize = 9999 }, CancellationToken.None);

        Assert.Equal(48, data.Results.PageSize);
    }

    [Fact]
    public async Task GetProductsAsync_PageBeyondLast_ClampsToLastPage()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { PageSize = 1, Page = 99 }, CancellationToken.None);

        Assert.Equal(3, data.Results.TotalPages);
        Assert.Equal(3, data.Results.Page);
    }

    [Fact]
    public async Task GetProductsAsync_InvalidRating_Ignored()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { MinRating = 99 }, CancellationToken.None);

        Assert.Equal(3, data.Results.TotalCount);
    }

    [Fact]
    public async Task GetProductsAsync_EmptyState_ReturnsEmptyResult()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Brand = "does-not-exist" }, CancellationToken.None);

        Assert.Empty(data.Results.Items);
        Assert.Equal(0, data.Results.TotalCount);
        Assert.Equal(0, data.Results.TotalPages);
        Assert.Null(data.MinAvailablePrice);
        Assert.Null(data.MaxAvailablePrice);
    }

    [Fact]
    public async Task GetProductsAsync_DiscountAndRatingComputedOnItems()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery(), CancellationToken.None);
        var sweater = data.Results.Items.Single(i => i.Slug == "cashmere-crew-neck-sweater");

        Assert.Equal(20, sweater.DiscountPercent);
        Assert.Equal(4.5, sweater.AverageRating);
        Assert.Equal(2, sweater.ReviewCount);
        Assert.True(sweater.IsNew);
        Assert.True(sweater.IsInStock);
        Assert.False(sweater.IsLowStock);
        Assert.Single(sweater.Colours);
        Assert.Equal("Heather Grey", sweater.Colours[0].Name);
        Assert.NotNull(sweater.ImageUrl);
    }

    [Fact]
    public async Task GetProductsAsync_ColourFacet_PresentAndSelected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        var data = await service.GetProductsAsync(new ProductListQuery { Colour = new[] { "black" } }, CancellationToken.None);

        var colourFacet = data.Facets.Single(f => f.Key == "colour");
        Assert.Contains(colourFacet.Values, v => v.Value == "black" && v.Selected);
    }

    [Fact]
    public async Task ResolveEntityNameAsync_KnownEntities_ReturnsNames()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var seed = await SeedAsync(context);

        Assert.Equal("Clothing", await service.ResolveEntityNameAsync(CatalogEntityKind.Category, "clothing", CancellationToken.None));
        Assert.Equal("Everlane", await service.ResolveEntityNameAsync(CatalogEntityKind.Brand, "everlane", CancellationToken.None));
        Assert.Equal("Autumn Edit", await service.ResolveEntityNameAsync(CatalogEntityKind.Collection, "autumn-edit", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveEntityNameAsync_UnknownSlug_ReturnsNull()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        Assert.Null(await service.ResolveEntityNameAsync(CatalogEntityKind.Category, "missing", CancellationToken.None));
        Assert.Null(await service.ResolveEntityNameAsync(CatalogEntityKind.Brand, "missing", CancellationToken.None));
        Assert.Null(await service.ResolveEntityNameAsync(CatalogEntityKind.Collection, "missing", CancellationToken.None));
        Assert.Null(await service.ResolveEntityNameAsync(CatalogEntityKind.Brand, "", CancellationToken.None));
    }

    private sealed record CatalogSeed(
        Category Clothing,
        Category Knitwear,
        Category Footwear,
        Brand Everlane,
        Brand Nike,
        Collection Autumn,
        ProductTag CashmereTag,
        ProductAttributeValue HeatherGrey,
        ProductAttributeValue Black,
        Product Sweater,
        Product Beanie,
        Product Shoe,
        ProductVariant SweaterGreyM,
        ProductVariant BeanieBlack,
        ProductVariant ShoeBlack9);
}
