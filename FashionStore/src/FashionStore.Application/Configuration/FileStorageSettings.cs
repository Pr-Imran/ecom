namespace FashionStore.Application.Configuration;

public sealed class FileStorageSettings
{
    public const string SectionName = "FileStorage";
    public string Provider { get; init; } = "Local";
    public string BasePath { get; init; } = string.Empty;
    public string PublicUrlBase { get; init; } = string.Empty;
    public long MaxFileSizeBytes { get; init; } = 10_485_760;
    public string[] AllowedExtensions { get; init; } = [".jpg", ".jpeg", ".png", ".webp"];
}
