using FashionStore.Application.DTOs.Payments;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Orchestrates the payment lifecycle for an order: initiation (hosted-checkout
/// redirect or reference/instructions), browser callback handling, verified
/// webhook processing, status checks and refunds. The service is the only component
/// that writes payment state and transitions order payment status; it never trusts
/// a browser redirect to mark an order paid. Stock reservations are released when
/// an online payment fails or expires.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Creates (or reuses, idempotently) the payment record for an order and asks
    /// the selected provider to initiate it. Returns the redirect URL when the
    /// provider uses a hosted checkout, otherwise the reference/instructions to
    /// display on the confirmation screen.
    /// </summary>
    Task<PaymentPlacementInfo> InitiateForOrderAsync(
        Guid orderId,
        string? returnUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the public payment status for an order by its public number.</summary>
    Task<PaymentStatusDto?> GetStatusByOrderNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles the browser returning from a hosted checkout (the "return" URL).
    /// This is a callback, not proof of payment: the service asks the provider for
    /// the current status and applies it only when the provider confirms it. The
    /// order is never marked paid purely because the browser arrived here.
    /// </summary>
    Task<PaymentStatusDto?> HandleBrowserCallbackAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a verified provider webhook. Verifies signature, timestamp and
    /// replay, then applies the amount/currency-validated state transition and
    /// releases stock when an online payment fails or expires.
    /// </summary>
    Task<PaymentWebhookHandlingResult> HandleWebhookAsync(
        string providerCode,
        string rawPayload,
        string? signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds an amount against a paid payment, recording the refund and updating
    /// the payment and order refund totals.
    /// </summary>
    Task<PaymentRefundResult> RefundAsync(
        Guid paymentId,
        decimal amount,
        string? initiatedBy,
        CancellationToken cancellationToken = default);
}
