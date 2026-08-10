using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Mobile financial service (MFS) provider-ready implementation. Initiation creates
/// a wallet payment reference and returns the instructions shown on the
/// confirmation screen (for example "confirm the request in your wallet app").
/// A live integration would push the payment request to the customer's wallet and
/// await the signed webhook.
/// </summary>
public sealed class MfsPaymentProvider : PaymentProviderBase, IPaymentProvider
{
    public MfsPaymentProvider(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public string ProviderCode => "mfs";
    public string DisplayName => "Mobile Wallet";
    public bool SupportsHostedCheckout => false;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        var reference = $"{request.ProviderCode.ToUpperInvariant()}-{request.OrderNumber}";
        return Task.FromResult(new PaymentInitiationResult(
            true,
            $"mfs_{Guid.NewGuid():N}",
            null,
            reference,
            "A payment request has been sent to your mobile wallet. Approve it in your wallet app to complete your order.",
            null,
            null));
    }

    public Task<PaymentStatusCheckResult> CheckStatusAsync(
        PaymentStatusCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentStatusCheckResult(
            true,
            request.CurrentState,
            request.ProviderTransactionId,
            null,
            null));
    }
}
