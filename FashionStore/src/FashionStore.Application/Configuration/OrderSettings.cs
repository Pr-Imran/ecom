namespace FashionStore.Application.Configuration;

/// <summary>
/// Order-level rules applied at placement time. The browser never supplies these
/// values; the order service reads them from configuration and enforces them
/// server-side. The stock reservation policy differs by payment method: cash on
/// delivery reserves stock for a long window (goods are set aside for the delivery
/// attempt) while online payments reserve for a short window that is released when
/// the payment expires or fails.
/// </summary>
public sealed class OrderSettings
{
    public const string SectionName = "Order";

    /// <summary>Prefix used when generating public order numbers.</summary>
    public string OrderNumberPrefix { get; init; } = "ORD";

    /// <summary>How long a cash-on-delivery stock reservation stays active, in minutes.</summary>
    public int CodReservationMinutes { get; init; } = 4320;

    /// <summary>How long an online-payment stock reservation stays active, in minutes.</summary>
    public int OnlineReservationMinutes { get; init; } = 30;

    /// <summary>Lifetime of an idempotency record before it can be purged.</summary>
    public int IdempotencyRetentionDays { get; init; } = 30;

    /// <summary>
    /// HMAC secret used to sign guest order access tickets. Override in production
    /// configuration; the default is a development placeholder.
    /// </summary>
    public string GuestAccessTokenSecret { get; init; } = "dev-guest-order-access-secret";

    /// <summary>How long a guest order access ticket stays valid, in minutes.</summary>
    public int GuestAccessTokenMinutes { get; init; } = 60;
}
