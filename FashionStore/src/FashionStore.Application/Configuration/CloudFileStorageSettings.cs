namespace FashionStore.Application.Configuration;

public sealed class CloudFileStorageSettings
{
    public const string SectionName = "CloudFileStorage";

    public string Provider { get; init; } = "AzureBlob";

    public string ContainerName { get; init; } = "fashionstore-assets";

    public string ConnectionString { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public string AccountKey { get; init; } = string.Empty;

    public string BucketName { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public int RetryCount { get; init; } = 3;
}
