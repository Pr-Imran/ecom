namespace FashionStore.Application.Configuration;

public sealed class ImageSettings
{
    public const string SectionName = "Image";

    public int MaxImageCountPerProduct { get; init; } = 20;

    public int MaxWidth { get; init; } = 12000;

    public int MaxHeight { get; init; } = 12000;

    public int WebpQuality { get; init; } = 80;

    public int JpegQuality { get; init; } = 82;

    public bool PreserveOriginal { get; init; } = true;

    public string FallbackImagePath { get; init; } = "/images/placeholder.svg";
}
