using FashionStore.Application.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services.Storage;

/// <summary>
/// Verifies that the configured local file-storage directory exists and is
/// writable. Used by the readiness health check so an orchestrator or load
/// balancer can stop routing traffic before a full upload volume breaks product
/// images and review attachments. Non-local providers report healthy, since the
/// remote endpoint is exercised by the cloud storage provider itself.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageSettings _settings;
    private readonly ILogger<StorageHealthCheck> _logger;

    public StorageHealthCheck(
        IWebHostEnvironment environment,
        FileStorageSettings settings,
        ILogger<StorageHealthCheck> logger)
    {
        _environment = environment;
        _settings = settings;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_settings.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(HealthCheckResult.Healthy("Non-local storage provider configured."));
        }

        if (string.IsNullOrWhiteSpace(_settings.BasePath))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("FileStorage:BasePath is not configured."));
        }

        try
        {
            var root = Path.Combine(_environment.ContentRootPath, _settings.BasePath);
            Directory.CreateDirectory(root);

            var probePath = Path.Combine(root, $".health-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);

            return Task.FromResult(HealthCheckResult.Healthy("Local storage is writable."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local storage health check failed at {Path}", _settings.BasePath);
            return Task.FromResult(HealthCheckResult.Unhealthy("Local storage is not writable.", ex));
        }
    }
}
