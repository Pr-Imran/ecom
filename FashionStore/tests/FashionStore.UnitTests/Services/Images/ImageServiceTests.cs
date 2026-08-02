using FashionStore.Application.Common.Exceptions;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionStore.UnitTests.Services.Images;

public class ImageServiceTests
{
    private static readonly ImageSettings Settings = new()
    {
        MaxImageCountPerProduct = 20,
        MaxWidth = 12000,
        MaxHeight = 12000,
        WebpQuality = 80
    };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"images-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<IFileStorageService> CreateStorageMock()
    {
        var mock = new Mock<IFileStorageService>();
        mock.Setup(s => s.ResolveUrl(It.IsAny<string>()))
            .Returns((string path) => $"/uploads/{path}");
        mock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => new StoredFileResult(path, $"/uploads/{path}", 100));
        mock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Mock<IImageValidationService> CreateValidationMock(bool valid = true)
    {
        var mock = new Mock<IImageValidationService>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<UploadedFileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(valid
                ? ImageValidationResult.Valid("png", "image/png", 800, 1000)
                : ImageValidationResult.Invalid("Invalid file"));
        return mock;
    }

    private static Mock<IImageProcessingService> CreateProcessingMock()
    {
        return new Mock<IImageProcessingService>();
    }

    private static ImageProcessingDispatcher CreateDispatcher() => new();

    private static ImageService CreateService(
        AppDbContext context,
        Mock<IFileStorageService> storage,
        Mock<IImageValidationService> validation,
        Mock<IImageProcessingService> processing,
        IImageProcessingDispatcher? dispatcher = null)
    {
        return new ImageService(
            context,
            storage.Object,
            validation.Object,
            processing.Object,
            dispatcher ?? CreateDispatcher(),
            Settings,
            NullLogger<ImageService>.Instance);
    }

    private static async Task<Product> SeedProductAsync(AppDbContext context)
    {
        var category = new Category { Name = "Clothing", Slug = "clothing" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Dress",
            Slug = "test-dress",
            CategoryId = category.Id,
            BaseSku = "DRESS-1",
            BasePrice = 59.99m
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private static async Task<ProductVariant> SeedVariantAsync(AppDbContext context, Product product)
    {
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "DRESS-1-RED",
            Price = 59.99m
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        return variant;
    }

    private static UploadedFileInput CreateFile(string name = "dress.png", string contentType = "image/png", long length = 1000)
    {
        var bytes = new byte[length];
        return new UploadedFileInput(new MemoryStream(bytes), name, contentType, length);
    }

    [Fact]
    public async Task UploadProductImageAsync_FirstImage_BecomesMain()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var storage = CreateStorageMock();
        var service = CreateService(context, storage, CreateValidationMock(), CreateProcessingMock());

        var dto = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));

        Assert.True(dto.IsMain);
        Assert.Equal(0, dto.DisplayOrder);
    }

    [Fact]
    public async Task UploadProductImageAsync_SecondImage_IsNotMain()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var first = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        var second = await service.UploadProductImageAsync(product.Id, CreateFile("second.png"), new ProductImageUploadRequest(null, null, null));

        Assert.True(first.IsMain);
        Assert.False(second.IsMain);
        Assert.Equal(1, second.DisplayOrder);
    }

    [Fact]
    public async Task UploadProductImageAsync_WithIsMainTrue_ClearsPreviousMain()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        var promoted = await service.UploadProductImageAsync(product.Id, CreateFile("second.png"), new ProductImageUploadRequest(null, null, null, IsMain: true));

        var all = (await service.GetProductImagesAsync(product.Id)).ToList();
        Assert.Single(all, i => i.IsMain);
        Assert.True(promoted.IsMain);
    }

    [Fact]
    public async Task UploadProductImageAsync_WithInvalidFile_ThrowsImageValidationException()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(valid: false), CreateProcessingMock());

        await Assert.ThrowsAsync<ImageValidationException>(
            () => service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null)));
    }

    [Fact]
    public async Task UploadProductImageAsync_UsesUniqueRelativePaths_NeverOverwritesExistingFile()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var storage = CreateStorageMock();
        var savedPaths = new List<string>();
        storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string, CancellationToken>((path, _, _, _) => savedPaths.Add(path))
            .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => new StoredFileResult(path, $"/uploads/{path}", 100));

        var service = CreateService(context, storage, CreateValidationMock(), CreateProcessingMock());
        await service.UploadProductImageAsync(product.Id, CreateFile("same.png"), new ProductImageUploadRequest(null, null, null));
        await service.UploadProductImageAsync(product.Id, CreateFile("same.png"), new ProductImageUploadRequest(null, null, null));

        Assert.Equal(2, savedPaths.Count);
        Assert.Equal(2, savedPaths.Distinct().Count());
    }

    [Fact]
    public async Task SetMainImageAsync_ClearsOtherMainImages()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var first = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        var second = await service.UploadProductImageAsync(product.Id, CreateFile("second.png"), new ProductImageUploadRequest(null, null, null));

        await service.SetMainImageAsync(second.Id);

        var all = (await service.GetProductImagesAsync(product.Id)).ToList();
        Assert.Single(all, i => i.IsMain);
        Assert.Equal(second.Id, all.Single(i => i.IsMain).Id);
    }

    [Fact]
    public async Task ReorderProductImagesAsync_AppliesNewDisplayOrders()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var a = await service.UploadProductImageAsync(product.Id, CreateFile("a.png"), new ProductImageUploadRequest(null, null, null));
        var b = await service.UploadProductImageAsync(product.Id, CreateFile("b.png"), new ProductImageUploadRequest(null, null, null));
        var c = await service.UploadProductImageAsync(product.Id, CreateFile("c.png"), new ProductImageUploadRequest(null, null, null));

        await service.ReorderProductImagesAsync(product.Id, new[]
        {
            new ImageOrderItem(c.Id, 0),
            new ImageOrderItem(a.Id, 1),
            new ImageOrderItem(b.Id, 2)
        });

        var ordered = (await service.GetProductImagesAsync(product.Id)).OrderBy(i => i.DisplayOrder).ToList();
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, ordered.Select(i => i.Id));
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(i => i.DisplayOrder));
    }

    [Fact]
    public async Task DeleteImageAsync_DeletesStoredFileAndDerivatives()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var storage = CreateStorageMock();
        var processing = CreateProcessingMock();
        var service = CreateService(context, storage, CreateValidationMock(), processing);

        var dto = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));

        await service.DeleteImageAsync(dto.Id);

        var storedFileName = (await context.ProductImages.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == dto.Id))?.FileName;
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        processing.Verify(p => p.DeleteDerivativesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(await context.ProductImages.AnyAsync(i => i.Id == dto.Id));
    }

    [Fact]
    public async Task DeleteImageAsync_WithNoStoredRow_ReturnsFalse()
    {
        using var context = CreateContext();
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var result = await service.DeleteImageAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteMainImageAsync_PromotesNextImageToMain()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var main = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        var next = await service.UploadProductImageAsync(product.Id, CreateFile("next.png"), new ProductImageUploadRequest(null, null, null));

        await service.DeleteImageAsync(main.Id);

        var remaining = (await service.GetProductImagesAsync(product.Id)).ToList();
        Assert.Single(remaining, i => i.IsMain);
        Assert.Equal(next.Id, remaining.Single(i => i.IsMain).Id);
    }

    [Fact]
    public async Task DeleteMainImageAsync_WhenOnlyImage_LeavesNoMain()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());

        var main = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        await service.DeleteImageAsync(main.Id);

        var remaining = (await service.GetProductImagesAsync(product.Id)).ToList();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task AssignVariantAsync_WithVariantFromAnotherProduct_Throws()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var other = await SeedProductAsync(context);
        var variant = await SeedVariantAsync(context, other);

        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());
        var dto = await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AssignVariantAsync(dto.Id, variant.Id));
    }

    [Fact]
    public async Task UploadVariantImageAsync_AssignsImageToVariant()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var variant = await SeedVariantAsync(context, product);

        var service = CreateService(context, CreateStorageMock(), CreateValidationMock(), CreateProcessingMock());
        var dto = await service.UploadVariantImageAsync(variant.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));

        Assert.Equal(variant.Id, dto.VariantId);
        Assert.Equal(product.Id, dto.ProductId);
    }

    [Fact]
    public async Task UploadProductImageAsync_AtMaxCount_Throws()
    {
        using var context = CreateContext();
        var product = await SeedProductAsync(context);
        var limited = new ImageSettings { MaxImageCountPerProduct = 2 };
        var storage = CreateStorageMock();
        var validation = CreateValidationMock();
        var service = new ImageService(
            context,
            storage.Object,
            validation.Object,
            CreateProcessingMock().Object,
            CreateDispatcher(),
            limited,
            NullLogger<ImageService>.Instance);

        await service.UploadProductImageAsync(product.Id, CreateFile(), new ProductImageUploadRequest(null, null, null));
        await service.UploadProductImageAsync(product.Id, CreateFile("b.png"), new ProductImageUploadRequest(null, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadProductImageAsync(product.Id, CreateFile("c.png"), new ProductImageUploadRequest(null, null, null)));
    }
}
