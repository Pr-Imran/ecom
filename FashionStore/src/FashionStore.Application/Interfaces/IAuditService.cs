namespace FashionStore.Application.Interfaces;

/// <summary>
/// Writes immutable audit records for sensitive administrative actions (admin
/// login, permission changes, product delete/deactivate, price changes, stock
/// changes, order status changes, refunds, settings changes, account suspension).
/// The acting user is resolved from the current HTTP context when available,
/// otherwise from the supplied actor id. IP address and user agent are captured
/// from the current request when present.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? oldValue = null,
        string? newValue = null,
        string? actorId = null,
        CancellationToken cancellationToken = default);
}
