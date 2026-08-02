using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Infrastructure.Services.Images;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FashionStore.UnitTests.Services.Images;

public class ImageValidationServiceTests
{
    private static readonly FileStorageSettings StorageSettings = new()
    {
        BasePath = "uploads",
        PublicUrlBase = "/uploads",
        MaxFileSizeBytes = 1024 * 1024,
        AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".avif"]
    };

    private static readonly ImageSettings ImageSettings = new()
    {
        MaxWidth = 12000,
        MaxHeight = 12000
    };

    private static ImageValidationService CreateService(FileStorageSettings? storage = null, ImageSettings? images = null)
    {
        return new ImageValidationService(
            storage ?? StorageSettings,
            images ?? ImageSettings,
            NullLogger<ImageValidationService>.Instance);
    }

    private static UploadedFileInput ToInput(byte[] bytes, string fileName = "photo.png", string contentType = "image/png")
        => new(new MemoryStream(bytes), fileName, contentType, bytes.Length);

    private static byte[] CreatePng(int width = 100, int height = 120)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(x => x.BackgroundColor(Color.White));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task ValidateAsync_WithNullFile_IsInvalid()
    {
        var service = CreateService();
        var result = await service.ValidateAsync(new UploadedFileInput(new MemoryStream(), "photo.png", "image/png", 0));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDisallowedExtension_IsInvalid()
    {
        var service = CreateService();
        var result = await service.ValidateAsync(ToInput(CreatePng(), "photo.exe", "image/png"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("extension", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WithDisallowedContentType_IsInvalid()
    {
        var service = CreateService();
        var result = await service.ValidateAsync(ToInput(CreatePng(), "photo.png", "text/plain"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("content type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WithOversizedFile_IsInvalid()
    {
        var smallLimit = new FileStorageSettings
        {
            BasePath = "uploads",
            PublicUrlBase = "/uploads",
            MaxFileSizeBytes = 64,
            AllowedExtensions = [".png"]
        };
        var service = CreateService(storage: smallLimit);
        var result = await service.ValidateAsync(ToInput(CreatePng(), "photo.png", "image/png"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WithNonImageContent_IsInvalid()
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes("definitely not an image payload");
        var service = CreateService();
        var result = await service.ValidateAsync(ToInput(textBytes, "photo.jpg", "image/jpeg"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("signature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WithOversizedDimensions_IsInvalid()
    {
        var smallImages = new ImageSettings { MaxWidth = 50, MaxHeight = 50 };
        var service = CreateService(images: smallImages);
        var result = await service.ValidateAsync(ToInput(CreatePng(200, 200), "photo.png", "image/png"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dimensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WithValidPng_IsValid()
    {
        var service = CreateService();
        var result = await service.ValidateAsync(ToInput(CreatePng(640, 480), "photo.png", "image/png"));
        Assert.True(result.IsValid);
        Assert.Equal("png", result.DetectedFormat);
        Assert.Equal("image/png", result.NormalizedContentType);
        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
    }

    [Fact]
    public async Task ValidateAsync_RewindsStream_SoContentCanBeSavedAfterwards()
    {
        var service = CreateService();
        var bytes = CreatePng(64, 64);
        var stream = new MemoryStream(bytes);
        stream.Position = bytes.Length;

        var result = await service.ValidateAsync(new UploadedFileInput(stream, "photo.png", "image/png", bytes.Length));
        Assert.True(result.IsValid);
        Assert.Equal(0, stream.Position);
    }
}
