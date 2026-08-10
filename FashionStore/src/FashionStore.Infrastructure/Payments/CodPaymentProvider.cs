using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Cash-on-delivery provider. No gateway is contacted: the order is reserved for
/// delivery and the money is collected from the customer when the order arrives.
/// Stock is held for the long COD reservation window instead of being released by
/// a payment webhook.
/// </summary>
public sealed class CodPaymentProvider : PaymentProviderBase, IPaymentProvider
{
    public CodPaymentProvider(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public string ProviderCode => "cod";
    public string DisplayName => "Cash on Delivery";
    public bool SupportsHostedCheckout => false;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentInitiationResult(
            true,
            null,
            null,
            null,
            "Pay in cash when your order is delivered.",
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
