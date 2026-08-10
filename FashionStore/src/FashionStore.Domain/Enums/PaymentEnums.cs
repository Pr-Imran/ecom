namespace FashionStore.Domain.Enums;

/// <summary>
/// The lifecycle of a <c>Payment</c> record. A payment is created at order
/// placement, moves to <see cref="Initiated"/> once handed to a provider, and
/// settles into <see cref="Paid"/>, <see cref="Failed"/>, <see cref="Cancelled"/>
/// or <see cref="Expired"/>. Refund activity moves a settled payment to
/// <see cref="PartiallyRefunded"/> or <see cref="Refunded"/>. A payment is never
/// marked paid purely from a browser redirect; only a verified provider callback
/// or webhook may settle it.
/// </summary>
public enum PaymentState
{
    Pending = 0,
    Initiated = 1,
    Authorised = 2,
    Paid = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6,
    PartiallyRefunded = 7,
    Refunded = 8
}

/// <summary>
/// The state of a single attempt to collect a payment for a payment record.
/// Multiple attempts may exist for one payment (for example when a card provider
/// retries or a customer retries an MFS payment).
/// </summary>
public enum PaymentAttemptStatus
{
    Pending = 0,
    Initiated = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Expired = 5
}

/// <summary>
/// The kind of action recorded against a payment in a <c>PaymentTransaction</c>.
/// Every action is immutable and auditable, mirroring the money movement for the
/// order.
/// </summary>
public enum PaymentTransactionType
{
    Initiate = 0,
    Authorise = 1,
    Capture = 2,
    Webhook = 3,
    Callback = 4,
    StatusCheck = 5,
    Release = 6,
    Cancel = 7,
    Expire = 8,
    Refund = 9
}

/// <summary>
/// The outcome of processing a provider webhook. The status is persisted on the
/// <c>PaymentWebhookLog</c> so a full audit trail of every incoming callback exists,
/// including the ones that were refused.
/// </summary>
public enum PaymentWebhookStatus
{
    Received = 0,
    Verified = 1,
    Processed = 2,
    Duplicate = 3,
    InvalidSignature = 4,
    InvalidTimestamp = 5,
    Replayed = 6,
    UnknownTransaction = 7,
    AmountMismatch = 8,
    CurrencyMismatch = 9,
    InvalidOrderState = 10,
    ProviderDisabled = 11,
    Failed = 12
}
