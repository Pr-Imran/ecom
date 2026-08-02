namespace FashionStore.Application.DTOs.Images;

public sealed record ProductImageUploadRequest(
    string? AltText,
    string? Caption,
    Guid? VariantId,
    bool IsMain = false
);

public sealed record ImageOrderItem(
    Guid ImageId,
    int DisplayOrder
);

public sealed record ProductImageDto(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string FileName,
    string Url,
    string? ThumbnailUrl,
    string? ProductCardUrl,
    string? ProductDetailUrl,
    string? GalleryUrl,
    string? AltText,
    string? Caption,
    bool IsMain,
    int DisplayOrder,
    string ImageFormat,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    string ProcessingStatus,
    DateTime CreatedAtUtc
);

public sealed record ImageValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string? DetectedFormat,
    string? NormalizedContentType,
    int Width,
    int Height
)
{
    public static ImageValidationResult Valid(string format, string contentType, int width, int height)
        => new(true, Array.Empty<string>(), format, contentType, width, height);

    public static ImageValidationResult Invalid(params string[] errors)
        => new(false, errors, null, null, 0, 0);
}

public sealed record StorefrontImageModel(
    string Url,
    string? ThumbnailUrl,
    string? ProductCardUrl,
    string? ProductDetailUrl,
    string? GalleryUrl,
    string? AltText,
    int Width,
    int Height,
    string CssClass,
    int? DisplayWidth,
    int? DisplayHeight,
    bool LazyLoad = true
);
