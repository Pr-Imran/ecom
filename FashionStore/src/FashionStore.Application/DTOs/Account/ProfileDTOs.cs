namespace FashionStore.Application.DTOs.Account;

/// <summary>
/// Customer profile as exposed to the account area. Email is the identity key and
/// cannot be changed by the profile editor; the remaining fields are customer
/// managed. Notification preferences are exposed as a set of preference codes.
/// </summary>
public sealed record CustomerProfileDto(
    string UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    string? ProfileImageUrl,
    DateTime? DateOfBirth,
    bool MarketingOptIn,
    IReadOnlyList<string> NotificationPreferences,
    bool IsActive,
    DateTime? DeactivationRequestedAtUtc,
    string? DeactivationReason,
    DateTime CreatedAtUtc
);

/// <summary>
/// Editable profile fields. Email is deliberately not part of this request; it is
/// the identity key and is changed through a separate verified flow.
/// </summary>
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    DateTime? DateOfBirth
);

/// <summary>
/// Marketing and notification preference update. <see cref="NotificationPreferences"/>
/// contains allowed preference codes; unknown codes are ignored.
/// </summary>
public sealed record UpdatePreferencesRequest(
    bool MarketingOptIn,
    IReadOnlyList<string> NotificationPreferences
);

/// <summary>
/// Optional reason submitted with an account deactivation request. The request is
/// recorded (preparation) and remains pending for administrator action.
/// </summary>
public sealed record DeactivationRequest(string? Reason);

/// <summary>
/// Result of a profile mutation carrying the refreshed profile on success.
/// </summary>
public sealed record ProfileMutationResult(
    bool Success,
    string? ErrorMessage,
    CustomerProfileDto? Profile = null
);
