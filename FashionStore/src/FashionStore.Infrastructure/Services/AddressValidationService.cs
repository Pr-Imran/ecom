using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Address validation implementation. Validation is delegated to a registry of
/// country-specific validators: the validator matching the address country runs
/// first, followed by the generic fallback, so country rules can be added without
/// modifying the address service. All required-field decisions live on the server.
/// </summary>
public sealed class AddressValidationService : IAddressValidationService
{
    private readonly IEnumerable<ICountryAddressValidator> _validators;

    public AddressValidationService(IEnumerable<ICountryAddressValidator> validators)
    {
        _validators = validators;
    }

    public IReadOnlyList<string> Validate(SaveAddressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CountryCode))
        {
            return new[] { "Country is required." };
        }

        if (!CountryCatalog.IsKnown(request.CountryCode))
        {
            return new[] { "Country is not supported." };
        }

        var errors = new List<string>();

        foreach (var validator in _validators)
        {
            if (validator.AppliesTo(request.CountryCode))
            {
                errors.AddRange(validator.Validate(request));
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Generic required fields that apply to every address regardless of country.
/// </summary>
public sealed class DefaultCountryAddressValidator : ICountryAddressValidator
{
    public bool AppliesTo(string countryCode) => true;

    public IReadOnlyList<string> Validate(SaveAddressRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RecipientName))
        {
            errors.Add("Recipient name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AddressLine1))
        {
            errors.Add("Address line 1 is required.");
        }

        if (string.IsNullOrWhiteSpace(request.City))
        {
            errors.Add("City is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PostalCode))
        {
            errors.Add("Postal code is required.");
        }

        return errors;
    }
}

/// <summary>
/// United States: requires a state/region and a five-digit (or ZIP+4) postal code.
/// </summary>
public sealed class UnitedStatesAddressValidator : ICountryAddressValidator
{
    public bool AppliesTo(string countryCode) =>
        string.Equals(countryCode, "US", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Validate(SaveAddressRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Region))
        {
            errors.Add("State is required for addresses in the United States.");
        }

        if (!string.IsNullOrWhiteSpace(request.PostalCode) &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.PostalCode.Trim(), @"^\d{5}(-\d{4})?$"))
        {
            errors.Add("Postal code must be a valid US ZIP code (e.g. 10001 or 10001-1234).");
        }

        return errors;
    }
}

/// <summary>
/// United Kingdom: requires a postcode in the recognised alphanumeric format.
/// </summary>
public sealed class UnitedKingdomAddressValidator : ICountryAddressValidator
{
    public bool AppliesTo(string countryCode) =>
        string.Equals(countryCode, "GB", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Validate(SaveAddressRequest request)
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.PostalCode) &&
            !System.Text.RegularExpressions.Regex.IsMatch(
                request.PostalCode.Trim().ToUpperInvariant(),
                @"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$"))
        {
            errors.Add("Postcode must be a valid UK postcode (e.g. SW1A 1AA).");
        }

        return errors;
    }
}
