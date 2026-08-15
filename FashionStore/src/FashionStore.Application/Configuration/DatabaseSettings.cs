namespace FashionStore.Application.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>
    /// Database provider: <c>SqlServer</c> (default) or <c>PostgreSql</c>.
    /// The selected provider configures both EF Core and Hangfire storage.
    /// </summary>
    public string Provider { get; init; } = "SqlServer";

    public string ConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int MaxRetryCount { get; init; } = 3;
    public int MaxRetryDelaySeconds { get; init; } = 30;
}
