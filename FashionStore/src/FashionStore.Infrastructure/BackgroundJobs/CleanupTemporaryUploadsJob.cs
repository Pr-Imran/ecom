using FashionStore.Application.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Removes files staged under the storage <c>tmp</c> folder (used by future upload
/// flows before a file is committed) once they are older than the retention window.
/// </summary>
public sealed class CleanupTemporaryUploadsJob
{
    private const int DefaultRetentionHours = 24;

    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageSettings _settings;
    private readonly ILogger<CleanupTemporaryUploadsJob> _logger;

    public CleanupTemporaryUploadsJob(
        IWebHostEnvironment environment,
        FileStorageSettings settings,
        ILogger<CleanupTemporaryUploadsJob> logger)
    {
        _environment = environment;
        _settings = settings;
        _logger = logger;
    }

    /// <returns>The number of temporary files removed.</returns>
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var basePath = string.IsNullOrWhiteSpace(_settings.BasePath)
            ? Path.Combine(_environment.ContentRootPath, "uploads")
            : Path.IsPathRooted(_settings.BasePath)
                ? _settings.BasePath
                : Path.Combine(_environment.ContentRootPath, _settings.BasePath);

        var tempDir = Path.Combine(basePath, "tmp");
        if (!Directory.Exists(tempDir))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow.AddHours(-DefaultRetentionHours);
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    File.Delete(file);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clean temporary file {File}", file);
            }
        }

        if (count > 0)
        {
            _logger.LogInformation("Cleaned {Count} temporary upload(s) under {TempDir}", count, tempDir);
        }

        return Task.FromResult(count);
    }
}
