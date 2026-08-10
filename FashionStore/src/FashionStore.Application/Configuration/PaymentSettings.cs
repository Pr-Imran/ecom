namespace FashionStore.Application.Configuration;

/// <summary>
/// Payment architecture settings. Each enabled provider is described by a
/// <see cref="PaymentProviderSettings"/> entry keyed by its provider code ("cod",
/// "card", "mfs", "bank", ...). Webhook security is configured centrally
/// (timestamp tolerance) while each provider carries its own webhook secret so a
/// leaked secret only affects a single provider.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Payments";

    /// <summary>How old a webhook timestamp may be before it is rejected, in seconds.</summary>
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    /// <summary>Storefront redirect URL returned by a hosted-checkout provider.</summary>
    public string ReturnUrl { get; init; } = string.Empty;

    /// <summary>Storefront redirect URL shown when the customer cancels a hosted checkout.</summary>
    public string CancelUrl { get; init; } = string.Empty;

    /// <summary>Per-provider configuration, keyed by provider code.</summary>
    public IReadOnlyList<PaymentProviderSettings> Providers { get; init; } = Array.Empty<PaymentProviderSettings>();

    /// <summary>Resolves provider configuration by code, or null when unknown/disabled.</summary>
    public PaymentProviderSettings? GetProvider(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return null;
        }

        return Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
            p.IsEnabled);
    }
}

/// <summary>
/// Configuration for a single payment provider. <see cref="WebhookSecret"/> is the
/// shared secret used to verify incoming webhook signatures; it must be provided by
/// the operator, never hardcoded in source.
/// </summary>
public sealed class PaymentProviderSettings
{
    /// <summary>Stable provider code ("cod", "card", "mfs", "bank").</summary>
    public string ProviderCode { get; init; } = string.Empty;

    /// <summary>Display name used in administrative screens.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    /// <summary>Shared secret for verifying webhook signatures (operator-supplied).</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>Whether the provider hands the customer off to a hosted checkout page.</summary>
    public bool SupportsHostedCheckout { get; init; }
}
