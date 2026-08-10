using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Generic online payment provider placeholder. Demonstrates the hosted-checkout
/// contract (redirect + signed webhook) without binding to any specific gateway;
/// swapping in a real provider keeps the same interface.
/// </summary>
public sealed class OnlinePaymentProvider : HostedCheckoutProviderBase, IPaymentProvider
{
    public OnlinePaymentProvider(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public override string ProviderCode => "online";
    public override string DisplayName => "Online Payment";
}
