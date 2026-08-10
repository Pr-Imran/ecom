using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Card provider-ready implementation. The placeholder hands off to a hosted
/// checkout page and is charged by a signed webhook; a live integration would
/// replace the mock redirect with the card gateway's hosted checkout.
/// </summary>
public sealed class CardPaymentProvider : HostedCheckoutProviderBase, IPaymentProvider
{
    public CardPaymentProvider(PaymentProviderSettings settings)
        : base(settings)
    {
    }

    public override string ProviderCode => "card";
    public override string DisplayName => "Card Payment";
}
