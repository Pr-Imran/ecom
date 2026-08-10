namespace FashionStore.Application.Interfaces;

/// <summary>
/// Resolves a payment provider by its stable provider code. The factory is the
/// single place that maps codes like "cod", "card", "mfs" or "bank" to their
/// concrete provider implementations.
/// </summary>
public interface IPaymentProviderFactory
{
    /// <summary>Returns the provider for a code, or null when unknown or disabled.</summary>
    IPaymentProvider? GetProvider(string? providerCode);
}
