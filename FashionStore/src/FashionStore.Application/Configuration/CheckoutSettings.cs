namespace FashionStore.Application.Configuration;

/// <summary>
/// Order-level checkout rules. The browser never supplies these values; the
/// checkout engine reads them from configuration and enforces them server-side.
/// </summary>
public sealed class CheckoutSettings
{
    public const string SectionName = "Checkout";

    /// <summary>The lowest order total (after discounts, before shipping) accepted.</summary>
    public decimal? MinOrderAmount { get; init; }

    /// <summary>The highest order total (after discounts, before shipping) accepted.</summary>
    public decimal? MaxOrderAmount { get; init; }

    /// <summary>When true the customer must accept the terms before an order can be placed.</summary>
    public bool RequiresTermsAcceptance { get; init; } = true;

    /// <summary>When true anonymous visitors can check out without an account.</summary>
    public bool GuestCheckoutEnabled { get; init; } = true;

    /// <summary>When true a guest must supply a phone number for delivery contact.</summary>
    public bool RequireGuestPhone { get; init; } = true;

    /// <summary>
    /// Secret used to sign the checkout continuation token. Leave empty to use a
    /// per-process random key (fine for single-instance development; production
    /// should set a stable value so tokens survive restarts).
    /// </summary>
    public string ContinuationTokenSecret { get; init; } = string.Empty;
}
