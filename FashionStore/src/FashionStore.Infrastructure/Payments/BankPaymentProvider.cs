using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Bank payment provider-ready implementation. Initiation produces a bank transfer
/// reference and the payment details shown on the confirmation screen. A live
/// integration would verify the incoming transfer via a signed webhook from the
/// bank.
/// </summary>
public sealed class BankPaymentProvider : PaymentProviderBase, IPaymentProvider
{
    public BankPaymentProvider(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public string ProviderCode => "bank";
    public string DisplayName => "Bank Transfer";
    public bool SupportsHostedCheckout => false;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        var reference = $"{request.ProviderCode.ToUpperInvariant()}-{request.OrderNumber}";
        return Task.FromResult(new PaymentInitiationResult(
            true,
            $"bank_{Guid.NewGuid():N}",
            null,
            reference,
            $"Transfer {request.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {request.Currency} using reference {reference}. Your order is confirmed once the transfer is received.",
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
