using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Infrastructure.Services.Images;
using FashionStore.Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FashionStore.UnitTests.Services.Images;

public class ImageProcessingServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalFileStorageService _storage;
    private readonly ImageProcessingService _service;

    public ImageProcessingServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fashionstore-process-{Guid.NewGuid():N}");
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_tempRoot);
        _storage = new LocalFileStorageService(
            environment.Object,
            new FileStorageSettings { BasePath = "uploads", PublicUrlBase = "/uploads" },
            NullLogger<LocalFileStorageService>.Instance);
        _service = new ImageProcessingService(
            _storage,
            new ImageSettings { WebpQuality = 80 },
            NullLogger<ImageProcessingService>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static byte[] CreatePng(int width = 800, int height = 1000)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(x => x.BackgroundColor(Color.White));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task ProcessDerivativesAsync_GeneratesAllFourDerivativeFiles()
    {
        var originalPath = "products/abcde0123456789abcdefabcdef0123.png";
        using var content = new MemoryStream(CreatePng());
        await _storage.SaveAsync(originalPath, content, "image/png");

        await _service.ProcessDerivativesAsync(originalPath);

        foreach (var spec in ImageDerivatives.All.Values)
        {
            var derivativePath = ImageDerivatives.BuildDerivativePath(originalPath, spec);
            Assert.True(await _storage.ExistsAsync(derivativePath), $"Expected derivative {derivativePath} to exist");
        }

        Assert.True(await _storage.ExistsAsync(originalPath));
    }

    [Fact]
    public async Task ProcessDerivativesAsync_ThumbnailUsesCropMode()
    {
        var originalPath = "products/wide.png";
        using var content = new MemoryStream(CreatePng(800, 400));
        await _storage.SaveAsync(originalPath, content, "image/png");

        await _service.ProcessDerivativesAsync(originalPath);

        var thumbPath = ImageDerivatives.BuildDerivativePath(originalPath, ImageDerivatives.All[ImageDerivativeKind.Thumbnail]);
        await using var thumbStream = await _storage.OpenReadAsync(thumbPath);
        using var image = await Image.LoadAsync(thumbStream);
        Assert.Equal(96, image.Width);
        Assert.Equal(96, image.Height);
    }

    [Fact]
    public async Task DeleteDerivativesAsync_RemovesDerivativeFiles()
    {
        var originalPath = "products/delete.png";
        using var content = new MemoryStream(CreatePng());
        await _storage.SaveAsync(originalPath, content, "image/png");
        await _service.ProcessDerivativesAsync(originalPath);

        await _service.DeleteDerivativesAsync(originalPath);

        foreach (var spec in ImageDerivatives.All.Values)
        {
            var derivativePath = ImageDerivatives.BuildDerivativePath(originalPath, spec);
            Assert.False(await _storage.ExistsAsync(derivativePath), $"Expected derivative {derivativePath} to be deleted");
        }

        Assert.True(await _storage.ExistsAsync(originalPath));
    }

    [Fact]
    public async Task DeleteDerivativesAsync_WithMissingOriginals_DoesNotThrow()
    {
        await _service.DeleteDerivativesAsync("products/never-uploaded.png");
    }
}
