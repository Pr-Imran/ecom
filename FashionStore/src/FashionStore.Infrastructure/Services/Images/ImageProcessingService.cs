using FashionStore.Application.Configuration;
using Microsoft.Extensions.Logging;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FashionStore.Infrastructure.Services.Images;

public sealed class ImageProcessingService : IImageProcessingService
{
    private readonly IFileStorageService _storage;
    private readonly ImageSettings _settings;
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(IFileStorageService storage, ImageSettings settings, ILogger<ImageProcessingService> logger)
    {
        _storage = storage;
        _settings = settings;
        _logger = logger;
    }

    public async Task ProcessDerivativesAsync(string originalRelativePath, CancellationToken cancellationToken = default)
    {
        await using var source = await _storage.OpenReadAsync(originalRelativePath, cancellationToken);

        using var image = await Image.LoadAsync(source, cancellationToken);
        image.Mutate(x => x.AutoOrient());

        foreach (var spec in ImageDerivatives.All.Values)
        {
            var derivativePath = ImageDerivatives.BuildDerivativePath(originalRelativePath, spec);

            using var resized = image.Clone(ctx =>
            {
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(spec.MaxWidth, spec.MaxHeight),
                    Mode = spec.ResizeMode == ImageResizeMode.Crop ? ResizeMode.Crop : ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                });
            });

            await using var output = new MemoryStream();
            await resized.SaveAsWebpAsync(output, new WebpEncoder { Quality = _settings.WebpQuality }, cancellationToken);
            output.Position = 0;

            await _storage.SaveAsync(derivativePath, output, "image/webp", cancellationToken);
        }

        _logger.LogInformation("Generated image derivatives for {Path}", originalRelativePath);
    }

    public async Task DeleteDerivativesAsync(string originalRelativePath, CancellationToken cancellationToken = default)
    {
        foreach (var spec in ImageDerivatives.All.Values)
        {
            var derivativePath = ImageDerivatives.BuildDerivativePath(originalRelativePath, spec);
            await _storage.DeleteAsync(derivativePath, cancellationToken);
        }
    }
}
