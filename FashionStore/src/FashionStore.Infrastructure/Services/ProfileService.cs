using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Customer profile implementation. All operations are scoped to the customer id
/// supplied by the caller (resolved from the authenticated principal). Email is
/// the identity key and is never modified through this service. Notification
/// preference codes are whitelisted so the client can only enable known channels.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private static readonly string[] AllowedNotificationCodes = { "order_updates", "offers" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IImageValidationService _imageValidation;
    private readonly FileStorageSettings _storageSettings;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        IFileStorageService storage,
        IImageValidationService imageValidation,
        FileStorageSettings storageSettings,
        ILogger<ProfileService> logger)
    {
        _userManager = userManager;
        _context = context;
        _storage = storage;
        _imageValidation = imageValidation;
        _storageSettings = storageSettings;
        _logger = logger;
    }

    public async Task<CustomerProfileDto?> GetProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user == null ? null : ToDto(user);
    }

    public async Task<ProfileMutationResult> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new ProfileMutationResult(false, "Account not found.");
        }

        if (request.DateOfBirth.HasValue && request.DateOfBirth > DateTime.UtcNow)
        {
            return new ProfileMutationResult(false, "Date of birth cannot be in the future.");
        }

        user.FirstName = TrimToNull(request.FirstName);
        user.LastName = TrimToNull(request.LastName);
        user.DisplayName = TrimToNull(request.DisplayName);
        user.PhoneNumber = TrimToNull(request.PhoneNumber);
        user.DateOfBirth = request.DateOfBirth;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return new ProfileMutationResult(false, FirstError(result));
        }

        return new ProfileMutationResult(true, null, ToDto(user));
    }

    public async Task<ProfileMutationResult> UpdatePreferencesAsync(
        string userId,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new ProfileMutationResult(false, "Account not found.");
        }

        var codes = (request.NotificationPreferences ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Where(c => AllowedNotificationCodes.Contains(c))
            .ToArray();

        user.MarketingOptIn = request.MarketingOptIn;
        user.NotificationPreferences = codes.Length > 0 ? string.Join(',', codes) : null;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return new ProfileMutationResult(false, FirstError(result));
        }

        return new ProfileMutationResult(true, null, ToDto(user));
    }

    public async Task<ProfileMutationResult> RequestDeactivationAsync(
        string userId,
        DeactivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new ProfileMutationResult(false, "Account not found.");
        }

        user.DeactivationRequestedAtUtc = DateTime.UtcNow;
        user.DeactivationReason = TrimToNull(request.Reason);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return new ProfileMutationResult(false, FirstError(result));
        }

        _logger.LogInformation("Deactivation requested for user {UserId}", userId);
        return new ProfileMutationResult(true, null, ToDto(user));
    }

    public async Task<ProfileMutationResult> UploadProfileImageAsync(
        string userId,
        UploadedFileInput file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length <= 0)
        {
            return new ProfileMutationResult(false, "No image was provided.");
        }

        var validation = await _imageValidation.ValidateAsync(file, cancellationToken);
        if (!validation.IsValid)
        {
            return new ProfileMutationResult(false, string.Join(" ", validation.Errors));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new ProfileMutationResult(false, "Account not found.");
        }

        var extension = Path.GetExtension(file.OriginalFileName)?.ToLowerInvariant() ?? ".jpg";
        var relativePath = FileStoragePath.Combine($"profiles/{userId:N}", $"avatar{extension}");

        var oldUrl = user.ProfileImageUrl;

        var stored = await _storage.SaveAsync(relativePath, file.Content, file.ContentType, cancellationToken);

        user.ProfileImageUrl = stored.Url;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await _storage.DeleteAsync(stored.RelativePath, cancellationToken);
            return new ProfileMutationResult(false, FirstError(result));
        }

        if (!string.IsNullOrWhiteSpace(oldUrl) && !string.Equals(oldUrl, stored.Url, StringComparison.Ordinal))
        {
            try
            {
                await DeleteStoredFileByUrlAsync(oldUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove previous profile image {Url} for user {UserId}", oldUrl, userId);
            }
        }

        return new ProfileMutationResult(true, null, ToDto(user));
    }

    private static CustomerProfileDto ToDto(ApplicationUser user)
    {
        var preferences = string.IsNullOrWhiteSpace(user.NotificationPreferences)
            ? Array.Empty<string>()
            : user.NotificationPreferences.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new CustomerProfileDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.PhoneNumber,
            user.ProfileImageUrl,
            user.DateOfBirth,
            user.MarketingOptIn,
            preferences,
            user.IsActive,
            user.DeactivationRequestedAtUtc,
            user.DeactivationReason,
            user.CreatedAtUtc);
    }

    private async Task DeleteStoredFileByUrlAsync(string url, CancellationToken cancellationToken)
    {
        var baseUrl = _storageSettings.PublicUrlBase.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) || !url.StartsWith(baseUrl + "/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = url[(baseUrl.Length + 1)..].TrimStart('/');
        await _storage.DeleteAsync(relative, cancellationToken);
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FirstError(IdentityResult result)
    {
        return result.Errors.Select(e => e.Description).FirstOrDefault() ?? "Unable to save your changes.";
    }
}
