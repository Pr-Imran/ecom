namespace FashionStore.Application.Configuration;

/// <summary>
/// Tax rules used by the checkout engine. A default rate applies everywhere; an
/// optional per-country override (ISO 3166-1 alpha-2 code key) can set a rate for
/// a specific destination. Tax is always computed server-side on the post-discount
/// goods total plus shipping; the browser never supplies a tax figure.
/// </summary>
public sealed class TaxSettings
{
    public const string SectionName = "Tax";

    /// <summary>Default tax rate as a percentage (for example 8.25 for 8.25%).</summary>
    public decimal DefaultRatePercent { get; init; }

    /// <summary>Per-country tax rate overrides keyed by ISO 3166-1 alpha-2 code.</summary>
    public IReadOnlyDictionary<string, decimal> CountryRates { get; init; } =
        new Dictionary<string, decimal>();

    /// <summary>
    /// Resolves the effective tax rate percentage for a destination country,
    /// falling back to <see cref="DefaultRatePercent"/> when no override exists.
    /// </summary>
    public decimal RateFor(string? countryCode)
    {
        if (!string.IsNullOrWhiteSpace(countryCode) &&
            CountryRates.TryGetValue(countryCode.Trim().ToUpperInvariant(), out var rate))
        {
            return rate;
        }

        return DefaultRatePercent;
    }
}
