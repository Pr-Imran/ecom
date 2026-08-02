using FashionStore.Application.Configuration;
using Microsoft.Extensions.Logging;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace FashionStore.Infrastructure.Services.Images;

public sealed class ImageValidationService : IImageValidationService
{
    private static readonly Dictionary<string, string> AllowedFormats = new()
    {
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
        ["avif"] = "image/avif"
    };

    private readonly FileStorageSettings _settings;
    private readonly ImageSettings _imageSettings;
    private readonly ILogger<ImageValidationService> _logger;

    public ImageValidationService(FileStorageSettings settings, ImageSettings imageSettings, ILogger<ImageValidationService> logger)
    {
        _settings = settings;
        _imageSettings = imageSettings;
        _logger = logger;
    }

    public async Task<ImageValidationResult> ValidateAsync(UploadedFileInput file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length <= 0)
        {
            return ImageValidationResult.Invalid("No file provided");
        }

        var errors = new List<string>();

        var extension = Path.GetExtension(file.OriginalFileName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !_settings.AllowedExtensions.Contains(extension))
        {
            errors.Add($"File extension '{extension}' is not allowed");
        }

        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
        if (!AllowedFormats.ContainsValue(contentType))
        {
            errors.Add($"Content type '{contentType}' is not allowed");
        }

        if (file.Length > _settings.MaxFileSizeBytes)
        {
            errors.Add($"File exceeds the maximum allowed size of {_settings.MaxFileSizeBytes} bytes");
        }

        string? detectedFormat = null;
        string? normalizedContentType = null;
        var width = 0;
        var height = 0;

        Rewind(file);

        try
        {
            var format = await Image.DetectFormatAsync(file.Content, cancellationToken);
            var imageInfo = await Image.IdentifyAsync(file.Content, cancellationToken);

            if (imageInfo == null)
            {
                errors.Add("File content is not a recognised image");
            }
            else
            {
                detectedFormat = format?.Name?.ToLowerInvariant();
                width = imageInfo.Width;
                height = imageInfo.Height;

                if (detectedFormat == null || !AllowedFormats.TryGetValue(detectedFormat, out var allowedContentType))
                {
                    errors.Add("File signature does not match an allowed image format");
                }
                else
                {
                    normalizedContentType = allowedContentType;
                }

                if (width <= 0 || height <= 0)
                {
                    errors.Add("Image has invalid dimensions");
                }
                else if (width > _imageSettings.MaxWidth || height > _imageSettings.MaxHeight)
                {
                    errors.Add($"Image dimensions {width}x{height} exceed the maximum allowed size");
                }
            }
        }
        catch (ImageFormatException)
        {
            errors.Add("File signature does not match an allowed image format");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to identify uploaded image {FileName}", file.OriginalFileName);
            errors.Add("File content is not a valid image");
        }
        finally
        {
            Rewind(file);
        }

        if (errors.Count > 0)
        {
            return ImageValidationResult.Invalid(errors.ToArray());
        }

        return ImageValidationResult.Valid(detectedFormat!, normalizedContentType!, width, height);
    }

    private static void Rewind(UploadedFileInput file)
    {
        if (file.Content.CanSeek)
        {
            file.Content.Position = 0;
        }
    }
}
