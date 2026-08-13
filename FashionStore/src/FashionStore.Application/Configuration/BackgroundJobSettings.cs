namespace FashionStore.Application.Configuration;

public sealed class BackgroundJobSettings
{
    public const string SectionName = "BackgroundJobs";
    public bool Enabled { get; init; } = true;
    public string ServerName { get; init; } = "FashionStore-Server";
    public int WorkerCount { get; init; } = 5;
    public int RetentionDays { get; init; } = 7;
    public string[] Queues { get; init; } = ["default", "email", "invoice", "cleanup"];

    /// <summary>Cron for the outbox delivery job. Defaults to every minute.</summary>
    public string EmailQueueCron { get; init; } = "*/1 * * * *";

    /// <summary>Cron for cancelling placed orders that were never paid. Defaults to every 5 minutes.</summary>
    public string ExpireUnpaidOrdersCron { get; init; } = "*/5 * * * *";

    /// <summary>Cron for applying the promotion schedule. Defaults to every 10 minutes.</summary>
    public string ScheduledPromotionsCron { get; init; } = "*/10 * * * *";

    /// <summary>Cron for the low-stock admin digest. Defaults to once a day at 08:00 UTC.</summary>
    public string LowStockAlertCron { get; init; } = "0 8 * * *";

    /// <summary>Cron for removing stale temporary uploads. Defaults to once a day at 03:00 UTC.</summary>
    public string CleanupTemporaryUploadsCron { get; init; } = "0 3 * * *";
}
