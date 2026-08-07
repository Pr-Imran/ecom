namespace FashionStore.Application.DTOs.Account;

/// <summary>
/// A customer address as exposed to the account area. Every entry is scoped to the
/// owning customer; the server always enforces ownership on read and write.
/// </summary>
public sealed record AddressDto(
    Guid Id,
    string Label,
    string RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions,
    bool IsDefaultShipping,
    bool IsDefaultBilling,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

/// <summary>
/// Client-supplied address mutation. Country-specific required fields are
/// validated on the server through an extensible validator registry; the client
/// never decides which fields are mandatory.
/// </summary>
public sealed record SaveAddressRequest(
    string Label,
    string RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions,
    bool IsDefaultShipping = false,
    bool IsDefaultBilling = false
);

/// <summary>
/// Result of an address mutation carrying the persisted address on success.
/// </summary>
public sealed record AddressMutationResult(
    bool Success,
    string? ErrorMessage,
    AddressDto? Address = null
);

/// <summary>
/// View model for the address book page: the customer's addresses, whether a
/// default shipping and billing address exist, and the selectable countries.
/// </summary>
public sealed record AddressBookViewData(
    IReadOnlyList<AddressDto> Addresses,
    bool HasDefaultShipping,
    bool HasDefaultBilling,
    IReadOnlyList<CountryOption> Countries
);

/// <summary>
/// A selectable country rendered in the address form.
/// </summary>
public sealed record CountryOption(string Code, string Name);
