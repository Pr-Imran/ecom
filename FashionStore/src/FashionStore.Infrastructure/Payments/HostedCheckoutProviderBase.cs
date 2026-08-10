using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Shared behavior for hosted-checkout providers. Initiation produces a provider
/// transaction id and hands the customer off to a checkout page; the placeholder
/// uses the storefront's mock hosted-checkout page which simulates a gateway and
/// fires a signed webhook back into the webhook endpoint. Live integrations would
/// point <see cref="RedirectUrl"/> at the real gateway.
/// </summary>
public abstract class HostedCheckoutProviderBase : PaymentProviderBase
{
    protected HostedCheckoutProviderBase(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public abstract string ProviderCode { get; }
    public abstract string DisplayName { get; }
    public bool SupportsHostedCheckout => true;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        var transactionId = $"{ProviderCode}_{Guid.NewGuid():N}";
        var returnUrl = Escape(request.ReturnUrl);
        var cancelUrl = Escape(request.CancelUrl);

        var redirectUrl =
            $"/payments/mock-hosted-checkout?providerCode={ProviderCode}" +
            $"&orderNumber={Escape(request.OrderNumber)}" +
            $"&amount={request.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&currency={Escape(request.Currency)}" +
            $"&returnUrl={returnUrl}" +
            $"&cancelUrl={cancelUrl}";

        return Task.FromResult(new PaymentInitiationResult(
            true,
            transactionId,
            redirectUrl,
            null,
            null,
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

    private static string Escape(string? value) =>
        Uri.EscapeDataString(value ?? string.Empty);
}
