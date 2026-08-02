namespace FashionStore.Application.DTOs.Images;

public enum ImageResizeMode
{
    Max,
    Crop
}

public enum ImageDerivativeKind
{
    Thumbnail,
    ProductCard,
    ProductDetail,
    Gallery
}

public sealed record ImageDerivativeSpec(
    ImageDerivativeKind Kind,
    string Suffix,
    int MaxWidth,
    int MaxHeight,
    ImageResizeMode ResizeMode
);

public static class ImageDerivatives
{
    public const string WebpExtension = ".webp";

    public static readonly IReadOnlyDictionary<ImageDerivativeKind, ImageDerivativeSpec> All =
        new Dictionary<ImageDerivativeKind, ImageDerivativeSpec>
        {
            [ImageDerivativeKind.Thumbnail] = new(ImageDerivativeKind.Thumbnail, "_thumb", 96, 96, ImageResizeMode.Crop),
            [ImageDerivativeKind.ProductCard] = new(ImageDerivativeKind.ProductCard, "_card", 400, 500, ImageResizeMode.Max),
            [ImageDerivativeKind.ProductDetail] = new(ImageDerivativeKind.ProductDetail, "_detail", 800, 1000, ImageResizeMode.Max),
            [ImageDerivativeKind.Gallery] = new(ImageDerivativeKind.Gallery, "_gallery", 1200, 1500, ImageResizeMode.Max)
        };

    public static string BuildDerivativePath(string originalRelativePath, ImageDerivativeSpec spec)
    {
        var lastSlash = originalRelativePath.LastIndexOf('/');
        var directory = lastSlash >= 0 ? originalRelativePath[..lastSlash] : string.Empty;
        var fileName = lastSlash >= 0 ? originalRelativePath[(lastSlash + 1)..] : originalRelativePath;
        var name = fileName[..fileName.LastIndexOf('.')];

        return string.IsNullOrEmpty(directory)
            ? $"{name}{spec.Suffix}{WebpExtension}"
            : $"{directory}/{name}{spec.Suffix}{WebpExtension}";
    }

    public static string GetOriginalDirectory(string originalRelativePath)
    {
        var lastSlash = originalRelativePath.LastIndexOf('/');
        return lastSlash >= 0 ? originalRelativePath[..lastSlash] : string.Empty;
    }
}
