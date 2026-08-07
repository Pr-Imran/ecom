using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Images;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Customer profile management for the account area. Every operation is scoped to
/// the customer id resolved from the authenticated principal; callers must never
/// trust a client-supplied owner id. Email is the identity key and is never
/// changed through these operations.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Loads the customer profile. Returns null when the user does not exist.
    /// </summary>
    Task<CustomerProfileDto?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editable profile fields. Validates display fields, enforces a
    /// consistent display name fallback and refreshes the profile on success.
    /// </summary>
    Task<ProfileMutationResult> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates marketing and notification preferences. Unknown notification codes
    /// are ignored so the client can never enable a channel the server does not
    /// understand.
    /// </summary>
    Task<ProfileMutationResult> UpdatePreferencesAsync(
        string userId,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an account deactivation request (preparation only). The customer is
    /// not deactivated immediately; the request is flagged for administrator
    /// action and remains visible on the profile.
    /// </summary>
    Task<ProfileMutationResult> RequestDeactivationAsync(
        string userId,
        DeactivationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new profile image for the customer. The image is validated and
    /// persisted through the storage abstraction; the previous image is removed.
    /// Returns the updated profile.
    /// </summary>
    Task<ProfileMutationResult> UploadProfileImageAsync(
        string userId,
        UploadedFileInput file,
        CancellationToken cancellationToken = default);
}
