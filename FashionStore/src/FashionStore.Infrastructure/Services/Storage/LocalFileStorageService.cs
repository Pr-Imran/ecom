using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageSettings _settings;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IWebHostEnvironment environment,
        FileStorageSettings settings,
        ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _settings = settings;
        _logger = logger;
    }

    public string ProviderName => "Local";

    public async Task<StoredFileResult> SaveAsync(
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = FileStoragePath.Normalize(relativePath);
        var fullPath = ResolveFullPath(normalizedPath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await content.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        return new StoredFileResult(normalizedPath, ResolveUrl(normalizedPath), fileStream.Length);
    }

    public Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(FileStoragePath.Normalize(relativePath));
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        _logger.LogInformation("Deleted stored file {Path}", relativePath);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(ResolveFullPath(FileStoragePath.Normalize(relativePath))));
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(FileStoragePath.Normalize(relativePath));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Stored file not found: {relativePath}", fullPath);
        }

        return Task.FromResult<Stream>(new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true));
    }

    public string ResolveUrl(string relativePath)
    {
        var normalized = FileStoragePath.Normalize(relativePath);
        var baseUrl = _settings.PublicUrlBase.TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl)
            ? $"/{normalized}"
            : $"{baseUrl}/{normalized}";
    }

    private string ResolveFullPath(string relativePath)
    {
        var basePath = Path.IsPathRooted(_settings.BasePath)
            ? _settings.BasePath
            : Path.Combine(_environment.ContentRootPath, _settings.BasePath);

        basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(basePath);

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var combined = segments.Aggregate(basePath, Path.Combine);
        var fullPath = Path.GetFullPath(combined);

        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage path '{relativePath}' escapes the configured base path");
        }

        return fullPath;
    }
}
