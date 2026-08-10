using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Resolves payment providers by their stable provider code. Only enabled
/// providers are returned; unknown or disabled codes resolve to null so the
/// payment flow can reject them cleanly. Providers are configured through
/// <c>PaymentSettings</c> rather than being hardcoded, so enabling a gateway is a
/// configuration change.
/// </summary>
public sealed class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;

    public PaymentProviderFactory(IOptions<PaymentSettings> options)
    {
        var settings = options.Value;
        var providers = new List<IPaymentProvider>();

        var cod = settings.GetProvider("cod");
        var card = settings.GetProvider("card");
        var online = settings.GetProvider("online");
        var mfs = settings.GetProvider("mfs");
        var bank = settings.GetProvider("bank");

        if (cod is not null)
        {
            providers.Add(new CodPaymentProvider(cod));
        }

        if (card is not null)
        {
            providers.Add(new CardPaymentProvider(card));
        }

        if (online is not null)
        {
            providers.Add(new OnlinePaymentProvider(online));
        }

        if (mfs is not null)
        {
            providers.Add(new MfsPaymentProvider(mfs));
        }

        if (bank is not null)
        {
            providers.Add(new BankPaymentProvider(bank));
        }

        _providers = providers.ToDictionary(
            p => p.ProviderCode,
            p => (IPaymentProvider)p,
            StringComparer.OrdinalIgnoreCase);
    }

    public IPaymentProvider? GetProvider(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return null;
        }

        return _providers.TryGetValue(providerCode, out var provider)
            ? provider
            : null;
    }
}
