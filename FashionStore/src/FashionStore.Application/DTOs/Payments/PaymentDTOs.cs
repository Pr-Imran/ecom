using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Payments;

/// <summary>
/// Server-side initiation request for a payment. The amount and currency always
/// come from the stored order (never the browser); the provider resolves the
/// redirect/instruction flow.
/// </summary>
public sealed record PaymentInitiationRequest(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    string ProviderCode,
    string PaymentMethodCode,
    decimal Amount,
    string Currency,
    string? CustomerEmail,
    string? ReturnUrl,
    string? CancelUrl,
    string IdempotencyKey);

/// <summary>
/// Outcome of asking a provider to initiate a payment. For hosted-checkout
/// providers <see cref="RedirectUrl"/> sends the customer to the provider page; for
/// reference-based methods (MFS, bank) <see cref="HostedCheckoutReference"/> and
/// <see cref="Instructions"/> tell the customer what to do next.
/// </summary>
public sealed record PaymentInitiationResult(
    bool Success,
    string? ProviderTransactionId,
    string? RedirectUrl,
    string? HostedCheckoutReference,
    string? Instructions,
    string? FailureCode,
    string? FailureReason);

/// <summary>
/// A normalized, provider-agnostic webhook event. Each provider parses its own raw
/// payload into this shape so the payment service can verify amount/currency,
/// locate the payment and apply the transition without knowing gateway details.
/// </summary>
public sealed record PaymentWebhookEvent(
    string EventId,
    string EventType,
    string? ProviderTransactionId,
    string? OrderNumber,
    decimal Amount,
    string Currency,
    DateTimeOffset Timestamp,
    string? FailureReason);

/// <summary>Outcome of verifying and applying a provider webhook.</summary>
public sealed record PaymentWebhookHandlingResult(
    bool Success,
    PaymentWebhookStatus Status,
    string? ProviderEventId,
    string? FailureReason);

/// <summary>
/// Server-side status check request. <see cref="ExpectedAmount"/> and
/// <see cref="ExpectedCurrency"/> are the stored payment values the provider result
/// is validated against. <see cref="CurrentState"/> is the locally recorded state;
/// placeholder integrations echo it because there is no live gateway to query.
/// </summary>
public sealed record PaymentStatusCheckRequest(
    Guid PaymentId,
    string ProviderCode,
    string? ProviderTransactionId,
    decimal ExpectedAmount,
    string ExpectedCurrency,
    PaymentState CurrentState);

/// <summary>Result of a provider status check.</summary>
public sealed record PaymentStatusCheckResult(
    bool Success,
    PaymentState State,
    string? ProviderTransactionId,
    string? FailureCode,
    string? FailureReason);

/// <summary>
/// Refund request. Refunds are only allowed against paid payments and are applied
/// incrementally so partial refunds never exceed the captured amount.
/// </summary>
public sealed record PaymentRefundRequest(
    Guid PaymentId,
    string ProviderCode,
    string ProviderTransactionId,
    decimal Amount,
    string Currency,
    string? InitiatedBy);

/// <summary>Outcome of a refund request.</summary>
public sealed record PaymentRefundResult(
    bool Success,
    string? ProviderRefundId,
    string? FailureCode,
    string? FailureReason);

/// <summary>
/// Public payment status shown to the customer on the confirmation screen and
/// polled from the storefront.
/// </summary>
public sealed record PaymentStatusDto(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    string ProviderCode,
    string PaymentMethodCode,
    PaymentState State,
    string? ProviderTransactionId,
    string? HostedCheckoutReference,
    string? Instructions,
    decimal Amount,
    string Currency,
    bool OrderPaid,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? FailedAtUtc,
    string? FailureReason);

/// <summary>
/// Payment info returned to the browser after order placement. <see cref="RedirectUrl"/>
/// is present when the selected provider hands the customer off to a hosted
/// checkout page; otherwise the confirmation screen renders the reference and
/// instructions and polls the payment status.
/// </summary>
public sealed record PaymentPlacementInfo(
    bool PaymentRequired,
    string? RedirectUrl,
    string? HostedCheckoutReference,
    string? Instructions,
    string State,
    string ProviderCode);
