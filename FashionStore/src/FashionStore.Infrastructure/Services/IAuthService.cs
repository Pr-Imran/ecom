using FashionStore.Application.DTOs.Auth;
using FashionStore.Infrastructure.Data;

namespace FashionStore.Infrastructure.Services;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);
    Task<bool> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<string[]> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsUserInRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken cancellationToken = default);
}
