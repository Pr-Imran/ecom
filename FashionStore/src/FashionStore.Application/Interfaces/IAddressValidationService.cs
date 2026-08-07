using FashionStore.Application.DTOs.Account;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Country-specific address validation. The design is extensible: a validator
/// targets a set of ISO country codes (or all countries as a fallback) and is
/// resolved through a registry, so new country rules can be added without
/// touching the address service. All validation happens on the server; the client
/// never decides which fields are required.
/// </summary>
public interface IAddressValidationService
{
    /// <summary>
    /// Validates an address for its country. Returns a list of human-readable
    /// field errors (empty when the address is valid).
    /// </summary>
    IReadOnlyList<string> Validate(SaveAddressRequest request);
}

/// <summary>
/// A single country-specific address rule. Implementations declare which country
/// codes they apply to and validate the required fields for those countries.
/// </summary>
public interface ICountryAddressValidator
{
    /// <summary>
    /// Returns true when this validator applies to the given ISO alpha-2 country
    /// code (case-insensitive).
    /// </summary>
    bool AppliesTo(string countryCode);

    /// <summary>
    /// Returns the required field errors for an address (empty when valid).
    /// </summary>
    IReadOnlyList<string> Validate(SaveAddressRequest request);
}
