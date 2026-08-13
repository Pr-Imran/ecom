namespace FashionStore.Application.Email;

public sealed record EmailLogItem(
    Guid Id,
    string ToEmail,
    string? RecipientName,
    string Subject,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? SentAtUtc,
    string? LastError,
    DateTime CreatedAtUtc);

public sealed record EmailLogPage(
    IReadOnlyList<EmailLogItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

public sealed record EmailTemplatePreview(string Name, string Subject, string Html);

public sealed record ResendEmailResult(bool Success, string? Error);

/// <summary>Admin read/re-send operations over the email log.</summary>
public interface IEmailAdminService
{
    Task<EmailLogPage> GetLogAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<EmailLogItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Re-queues a previously queued/sent/failed email for delivery.</summary>
    Task<ResendEmailResult> ResendAsync(Guid id, string? initiatedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailTemplatePreview>> GetTemplatePreviewsAsync(CancellationToken cancellationToken = default);
}
