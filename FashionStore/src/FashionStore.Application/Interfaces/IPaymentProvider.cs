using FashionStore.Application.DTOs.Payments;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Abstraction over a payment gateway. Providers are stateless and configured
/// through <c>PaymentProviderSettings</c>; the storefront only ever talks to a
/// provider through this interface so a new gateway is a configuration change
/// rather than a code change. Providers never receive raw card information: card
/// entry is the responsibility of the provider's hosted checkout page.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Stable provider code ("cod", "card", "mfs", "bank").</summary>
    string ProviderCode { get; }

    string DisplayName { get; }

    /// <summary>Whether this provider redirects the customer to a hosted checkout page.</summary>
    bool SupportsHostedCheckout { get; }

    /// <summary>
    /// Starts a payment for an order. For hosted-checkout providers the result
    /// carries the URL to redirect the browser to; for reference-based providers
    /// (MFS, bank) it carries the reference and instructions shown on the
    /// confirmation screen.
    /// </summary>
    Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider for the current state of a previously initiated payment.
    /// Placeholder integrations return the known local state; live integrations
    /// would query the gateway. The result state is validated against the stored
    /// amount/currency before it is applied.
    /// </summary>
    Task<PaymentStatusCheckResult> CheckStatusAsync(
        PaymentStatusCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the signature of an incoming webhook against this provider's
    /// configured secret.
    /// </summary>
    bool VerifyWebhookSignature(string rawPayload, string? signature);

    /// <summary>
    /// Parses a provider webhook body into the normalized
    /// <see cref="PaymentWebhookEvent"/>, or returns null when the payload is not a
    /// recognized/parseable event.
    /// </summary>
    PaymentWebhookEvent? TryParseWebhook(string rawPayload);
}
