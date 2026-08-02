using FashionStore.Application.Configuration;
using FashionStore.Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionStore.UnitTests.Services.Images;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IWebHostEnvironment> _environment;
    private readonly FileStorageSettings _settings;
    private readonly LocalFileStorageService _service;

    public LocalFileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fashionstore-storage-{Guid.NewGuid():N}");
        _settings = new FileStorageSettings
        {
            BasePath = "uploads",
            PublicUrlBase = "/uploads",
            MaxFileSizeBytes = 1024 * 1024
        };
        _environment = new Mock<IWebHostEnvironment>();
        _environment.SetupGet(e => e.ContentRootPath).Returns(_tempRoot);
        _service = new LocalFileStorageService(_environment.Object, _settings, NullLogger<LocalFileStorageService>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WritesFileToContentRoot()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));

        var result = await _service.SaveAsync("products/abc/file.png", content, "image/png");

        var fullPath = Path.Combine(_tempRoot, "uploads", "products", "abc", "file.png");
        Assert.True(File.Exists(fullPath));
        Assert.Equal("/uploads/products/abc/file.png", result.Url);
        Assert.Equal("products/abc/file.png", result.RelativePath);
    }

    [Fact]
    public async Task SaveAsync_WithSamePath_OverwritesButServiceUsesGuidPaths()
    {
        using var first = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("one"));
        using var second = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("two"));
        await _service.SaveAsync("same.bin", first, "application/octet-stream");
        await _service.SaveAsync("same.bin", second, "application/octet-stream");

        var fullPath = Path.Combine(_tempRoot, "uploads", "same.bin");
        var bytes = await File.ReadAllBytesAsync(fullPath);
        Assert.Equal("two", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueAfterSave()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));
        await _service.SaveAsync("exists.bin", content, "application/octet-stream");

        Assert.True(await _service.ExistsAsync("exists.bin"));
        Assert.False(await _service.ExistsAsync("missing.bin"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndReturnsTrue()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));
        await _service.SaveAsync("delete.bin", content, "application/octet-stream");

        var deleted = await _service.DeleteAsync("delete.bin");

        Assert.True(deleted);
        Assert.False(File.Exists(Path.Combine(_tempRoot, "uploads", "delete.bin")));
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_ReturnsFalse()
    {
        Assert.False(await _service.DeleteAsync("never-existed.bin"));
    }

    [Fact]
    public async Task SaveAsync_PathTraversal_Throws()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveAsync("../../../escape.bin", content, "application/octet-stream"));
    }

    [Fact]
    public async Task SaveAsync_PathTraversal_DoesNotWriteOutsideRoot()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));
        try
        {
            await _service.SaveAsync("../../escape.bin", content, "application/octet-stream");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.False(File.Exists(Path.Combine(_tempRoot, "escape.bin")));
        Assert.False(File.Exists(Path.GetTempPath().TrimEnd('/') + "escape.bin"));
    }

    [Fact]
    public void ResolveUrl_WithRelativePath_ReturnsPublicUrl()
    {
        Assert.Equal("/uploads/products/abc/file.png", _service.ResolveUrl("products/abc/file.png"));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsContent()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("readable"));
        await _service.SaveAsync("read.bin", content, "application/octet-stream");

        await using var stream = await _service.OpenReadAsync("read.bin");
        using var reader = new StreamReader(stream);
        Assert.Equal("readable", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.OpenReadAsync("missing.bin"));
    }
}
