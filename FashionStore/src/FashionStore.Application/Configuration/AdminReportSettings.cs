namespace FashionStore.Application.Configuration;

/// <summary>
/// Configuration for the administration reporting surface. The export directory
/// is resolved relative to the application content root when it is relative;
/// export jobs write prepared CSV files there and the download endpoint streams
/// them back to the admin.
/// </summary>
public sealed class AdminReportSettings
{
    public const string SectionName = "AdminReport";

    /// <summary>Directory where prepared export files are written (relative paths resolve against the content root).</summary>
    public string ExportDirectory { get; init; } = "exports";

    /// <summary>Maximum number of rows written per batch while a background export is generated.</summary>
    public int ExportBatchSize { get; init; } = 500;

    /// <summary>Maximum allowed report date range in days.</summary>
    public int MaxDateRangeDays { get; init; } = 366;
}
