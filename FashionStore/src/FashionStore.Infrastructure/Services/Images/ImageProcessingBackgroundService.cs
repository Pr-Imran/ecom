using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using Microsoft.Extensions.Logging;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FashionStore.Infrastructure.Services.Images;

public sealed class ImageProcessingBackgroundService : BackgroundService
{
    private readonly IImageProcessingDispatcher _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImageProcessingBackgroundService> _logger;

    public ImageProcessingBackgroundService(
        IImageProcessingDispatcher queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ImageProcessingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ImageProcessingJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processing = scope.ServiceProvider.GetRequiredService<IImageProcessingService>();
                await processing.ProcessDerivativesAsync(job.OriginalRelativePath, stoppingToken);

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var image = await context.ProductImages.FindAsync(new object[] { job.ImageId }, stoppingToken);
                if (image != null)
                {
                    image.ProcessingStatus = "Completed";
                    image.UpdatedAtUtc = DateTime.UtcNow;
                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Background workers must isolate failures from every queued item.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Failed to process image derivatives for {ImageId} ({Path})", job.ImageId, job.OriginalRelativePath);
                await MarkFailedAsync(job.ImageId);
            }
        }
    }

    private async Task MarkFailedAsync(Guid imageId)
    {
        if (imageId == Guid.Empty)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var image = await context.ProductImages.FindAsync(new object[] { imageId });
            if (image != null)
            {
                image.ProcessingStatus = "Failed";
                image.UpdatedAtUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
        }
#pragma warning disable CA1031 // Status marking must never take down the worker.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to mark image {ImageId} as failed", imageId);
        }
    }
}
