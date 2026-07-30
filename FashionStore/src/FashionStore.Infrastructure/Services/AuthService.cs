using System.Net;
using System.Security.Claims;
using System.Text;
using FashionStore.Application.DTOs.Auth;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AppDbContext _context;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailService _emailService;
    private readonly IDataProtector _dataProtector;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        AppDbContext context,
        ILogger<AuthService> logger,
        IEmailService emailService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
        _logger = logger;
        _emailService = emailService;
        _dataProtector = dataProtectionProvider.CreateProtector("Auth");
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            DisplayName = !string.IsNullOrEmpty(request.FirstName) || !string.IsNullOrEmpty(request.LastName)
                ? $"{request.FirstName} {request.LastName}".Trim()
                : null,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "Customer");
            _logger.LogInformation("User {Email} registered successfully", user.Email);

            var requiresConfirmation = _signInManager.UserManager.Options.SignIn.RequireConfirmedEmail;

            if (requiresConfirmation)
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                await _emailService.SendConfirmationEmailAsync(user.Email!, user.Id, token, cancellationToken);
            }
            else
            {
                await _emailService.SendWelcomeEmailAsync(user.Email!, user.DisplayName ?? user.Email!, cancellationToken);
            }

            return new RegisterResponse(user.Id, user.Email!, requiresConfirmation);
        }

        var errors = result.Errors.Select(e => e.Description).ToList();
        throw new IdentityException(errors);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.EmailOrUserName)
                   ?? await _userManager.FindByNameAsync(request.EmailOrUserName);

        if (user == null)
        {
            _logger.LogWarning("Login attempt with non-existent email/username: {Email}", request.EmailOrUserName);
            throw new SecurityException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for inactive user: {Email}", user.Email);

            throw new SecurityException("Account deactivated. Please contact support.");
        }

        if (user.LockoutEnd > DateTime.UtcNow)
        {
            _logger.LogWarning("Login attempt for locked out user: {Email}", user.Email);
            throw new SecurityException("Account temporarily locked. Please try again later.");
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            return new LoginResponse(user.Id, user.Email!, user.DisplayName, new[] { "Customer" }, true, false);
        }

        if (result.Succeeded)
        {
            user.LastLoginAtUtc = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            await _userManager.UpdateAsync(user);
            await AuditAsync(user.Id, "User.Login", null, null);

            var roles = await _userManager.GetRolesAsync(user);
            _logger.LogInformation("User {Email} logged in successfully", user.Email);

            return new LoginResponse(user.Id, user.Email!, user.DisplayName, roles.ToArray(), false, false);
        }

        user.FailedLoginAttempts++;
        await _userManager.UpdateAsync(user);

        throw new SecurityException("Invalid email or password.");
    }

    public async Task LogoutAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _signInManager.SignOutAsync();
        await AuditAsync(userId, "User.Logout", null, null);
        _logger.LogInformation("User {UserId} logged out", userId);
    }

    public async Task<bool> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return false;

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded;
    }

    public async Task<bool> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            await Task.Delay(100, cancellationToken);
            return true;
        }

        _logger.LogInformation("Password reset requested for {Email}", request.Email);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _emailService.SendPasswordResetEmailAsync(request.Email, token, cancellationToken);

        return true;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.NewPassword))
            return false;

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null) return false;

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        return result.Succeeded;
    }

    public async Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return new AuthResult(false, Errors: new[] { "User not found" });

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (result.Succeeded)
        {
            await AuditAsync(userId, "User.ChangePassword", null, null);
            return new AuthResult(true, userId, "Password changed successfully");
        }

        return new AuthResult(
            false,
            userId,
            "Unable to change password",
            result.Errors.Select(e => e.Description).ToList());
    }

    public async Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task<string[]> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return Array.Empty<string>();

        var claims = await _userManager.GetClaimsAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await _roleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims)
                {
                    claims.Add(claim);
                }
            }
        }

        return claims.Where(c => c.Type == "permission").Select(c => c.Value).Distinct().ToArray();
    }

    public async Task<bool> IsUserInRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user != null && await _userManager.IsInRoleAsync(user, role);
    }

    public async Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken cancellationToken = default)
    {
        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(permission);
    }

    private async Task AuditAsync(string userId, string action, string? entity, object? data = null)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            EntityType = entity ?? "Unknown",
            IpAddress = "",
            UserAgent = "",
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync();
    }
}

public class IdentityException : Exception
{
    public ICollection<string> Errors { get; }

    public IdentityException(ICollection<string> errors)
        : base(string.Join("; ", errors))
    {
        Errors = errors;
    }
}

public class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
}
