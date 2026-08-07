using FashionStore.Application.DTOs.Account;

namespace FashionStore.Application.Common;

/// <summary>
/// Catalog of countries selectable in the address book. Codes are ISO 3166-1
/// alpha-2. The list is intentionally the common storefront subset; new entries
/// are additive and safe to extend.
/// </summary>
public static class CountryCatalog
{
    public static IReadOnlyList<CountryOption> All { get; } = new[]
    {
        new CountryOption("US", "United States"),
        new CountryOption("GB", "United Kingdom"),
        new CountryOption("CA", "Canada"),
        new CountryOption("AU", "Australia"),
        new CountryOption("DE", "Germany"),
        new CountryOption("FR", "France"),
        new CountryOption("IT", "Italy"),
        new CountryOption("ES", "Spain"),
        new CountryOption("NL", "Netherlands"),
        new CountryOption("BE", "Belgium"),
        new CountryOption("PT", "Portugal"),
        new CountryOption("IE", "Ireland"),
        new CountryOption("CH", "Switzerland"),
        new CountryOption("AT", "Austria"),
        new CountryOption("SE", "Sweden"),
        new CountryOption("NO", "Norway"),
        new CountryOption("DK", "Denmark"),
        new CountryOption("FI", "Finland"),
        new CountryOption("PL", "Poland"),
        new CountryOption("CZ", "Czechia"),
        new CountryOption("JP", "Japan"),
        new CountryOption("KR", "South Korea"),
        new CountryOption("CN", "China"),
        new CountryOption("SG", "Singapore"),
        new CountryOption("AE", "United Arab Emirates"),
        new CountryOption("SA", "Saudi Arabia"),
        new CountryOption("IN", "India"),
        new CountryOption("BR", "Brazil"),
        new CountryOption("MX", "Mexico"),
        new CountryOption("ZA", "South Africa")
    }.AsReadOnly();

    public static bool IsKnown(string countryCode)
    {
        return !string.IsNullOrWhiteSpace(countryCode) &&
               All.Any(c => string.Equals(c.Code, countryCode, StringComparison.OrdinalIgnoreCase));
    }
}
