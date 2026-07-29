namespace FashionStore.Application.Configuration;

public sealed class BackgroundJobSettings
{
    public const string SectionName = "BackgroundJobs";
    public string ServerName { get; init; } = "FashionStore-Server";
    public int WorkerCount { get; init; } = 5;
    public int RetentionDays { get; init; } = 7;
    public string[] Queues { get; init; } = ["default", "email", "invoice", "cleanup"];
}
