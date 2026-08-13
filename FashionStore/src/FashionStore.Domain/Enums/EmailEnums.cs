namespace FashionStore.Domain.Enums;

/// <summary>
/// Lifecycle of a queued email. Emails are created as <see cref="Pending"/> inside
/// the same transaction as the business change that caused them (outbox pattern),
/// picked up by the background sender, and only ever sent after that transaction
/// commits. Failed sends are retried with a backoff delay until
/// <c>MaxAttempts</c> is reached.
/// </summary>
public enum EmailStatus
{
    /// <summary>Waiting to be picked up by the background sender.</summary>
    Pending = 0,

    /// <summary>Currently being sent by a worker.</summary>
    Processing = 1,

    /// <summary>Delivered successfully by the active email provider.</summary>
    Sent = 2,

    /// <summary>All retry attempts were exhausted.</summary>
    Failed = 3,

    /// <summary>Manually cancelled by an administrator.</summary>
    Cancelled = 4
}
