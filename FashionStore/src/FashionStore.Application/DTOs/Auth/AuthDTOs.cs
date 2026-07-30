namespace FashionStore.Application.DTOs.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? FirstName = null,
    string? LastName = null,
    string? PhoneNumber = null,
    bool AcceptTerms = false
);

public sealed record RegisterResponse(
    string UserId,
    string Email,
    bool RequiresEmailConfirmation
);

public sealed record LoginRequest(
    string EmailOrUserName,
    string Password,
    bool RememberMe = false
);

public sealed record LoginResponse(
    string UserId,
    string Email,
    string? DisplayName,
    string[] Roles,
    bool RequiresTwoFactor,
    bool RequiresEmailConfirmation
);

public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string? Email = null, string? Token = null, string? NewPassword = null);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ConfirmEmailRequest(string UserId, string Token);

public sealed record AuthResult(
    bool Success,
    string? UserId = null,
    string? Message = null,
    ICollection<string>? Errors = null,
    bool IsLockedOut = false,
    bool IsNotActive = false,
    bool RequiresEmailConfirmation = false
);
