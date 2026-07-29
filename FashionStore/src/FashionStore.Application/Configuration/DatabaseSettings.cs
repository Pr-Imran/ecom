namespace FashionStore.Application.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";
    public string ConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int MaxRetryCount { get; init; } = 3;
    public int MaxRetryDelaySeconds { get; init; } = 30;
}
