using System.Security.Claims;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Auth;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FashionStore.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IWishlistService _wishlistService;
    private readonly ICartService _cartService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IAuthService authService,
        IWishlistService wishlistService,
        ICartService cartService,
        IProfileService profileService,
        INavigationService navigationService,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _wishlistService = wishlistService;
        _cartService = cartService;
        _profileService = profileService;
        _navigationService = navigationService;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest model, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ViewData["ReturnUrl"] = returnUrl;

        try
        {
            var loginResponse = await _authService.LoginAsync(model, cancellationToken);

            if (!loginResponse.RequiresTwoFactor)
            {
                await MergeAnonymousWishlistAsync(loginResponse.UserId, cancellationToken);
                await MergeAnonymousCartAsync(loginResponse.UserId, cancellationToken);
            }

            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
        catch (SecurityException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {Email}", model.EmailOrUserName);
            ModelState.AddModelError(string.Empty, "An error occurred. Please try again.");
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterRequest model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result = await _authService.RegisterAsync(model, cancellationToken);

            TempData["SuccessMessage"] = result.RequiresEmailConfirmation
                ? "Registration successful. Please check your email to confirm your account."
                : "Registration successful. You can now log in.";

            return RedirectToAction("Login");
        }
        catch (IdentityException ex)
        {
            foreach (var error in ex.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", model.Email);
            ModelState.AddModelError(string.Empty, "An error occurred. Please try again.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                await _authService.LogoutAsync(userId, CancellationToken.None);
            }
        }

        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(string userId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            ViewData["Success"] = false;
            ViewData["Message"] = "Invalid confirmation link.";
            return View("EmailConfirmationResult");
        }

        var result = await _authService.ConfirmEmailAsync(new ConfirmEmailRequest(userId, token), cancellationToken);
        
        ViewData["Success"] = result;
        ViewData["Message"] = result 
            ? "Your email has been confirmed. You can now sign in." 
            : "Confirmation failed. The link may have expired. Please request a new confirmation email.";
        
        return View("EmailConfirmationResult");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("passwordreset")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest model, CancellationToken cancellationToken = default)
    {
        if (ModelState.IsValid)
        {
            await _authService.ForgotPasswordAsync(model, cancellationToken);
            TempData["SuccessMessage"] = "If your email is registered, you will receive password reset instructions.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? token = null, string? email = null)
    {
        if (string.IsNullOrEmpty(token))
        {
            return View("Error");
        }

        var model = new ResetPasswordRequest { Token = token, Email = email };
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("passwordreset")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.ResetPasswordAsync(model, cancellationToken);

        if (result)
        {
            TempData["SuccessMessage"] = "Password reset successfully. Please log in.";
            return RedirectToAction("Login");
        }

        ModelState.AddModelError(string.Empty, "Password reset failed. Token may have expired.");
        return View(model);
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var result = await _authService.ChangePasswordAsync(userId, model, cancellationToken);

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction("Index", "Home");
        }

        foreach (var error in result.Errors ?? Enumerable.Empty<string>())
        {
            ModelState.AddModelError(string.Empty, error);
        }

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
    [HttpGet]
    [AllowAnonymous]
    public IActionResult LockedOut()
    {
        return View();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return RedirectToAction("Login");
        }

        await PopulateAccountNavAsync(userId, cancellationToken);

        ViewData["Title"] = "My Account";
        return View(profile);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return RedirectToAction("Login");
        }

        await PopulateAccountNavAsync(userId, cancellationToken);

        var model = new UpdateProfileRequest(
            profile.FirstName,
            profile.LastName,
            profile.DisplayName,
            profile.PhoneNumber,
            profile.DateOfBirth);

        ViewData["Title"] = "Profile";
        ViewData["AvatarUrl"] = profile.ProfileImageUrl;
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(UpdateProfileRequest model, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAccountNavAsync(userId, cancellationToken);
            ViewData["Title"] = "Profile";
            return View(model);
        }

        var result = await _profileService.UpdateProfileAsync(userId, model, cancellationToken);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Profile updated.";
            return RedirectToAction(nameof(Profile));
        }

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to save your profile.");
        await PopulateAccountNavAsync(userId, cancellationToken);
        ViewData["Title"] = "Profile";
        return View(model);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return RedirectToAction("Login");
        }

        await PopulateAccountNavAsync(userId, cancellationToken);

        var model = new UpdatePreferencesRequest(
            profile.MarketingOptIn,
            profile.NotificationPreferences);

        ViewData["Title"] = "Preferences";
        ViewData["Profile"] = profile;
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(UpdatePreferencesRequest model, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAccountNavAsync(userId, cancellationToken);
            ViewData["Title"] = "Preferences";
            return View(model);
        }

        var result = await _profileService.UpdatePreferencesAsync(userId, model, cancellationToken);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Preferences updated.";
            return RedirectToAction(nameof(Settings));
        }

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to save your preferences.");
        await PopulateAccountNavAsync(userId, cancellationToken);
        ViewData["Title"] = "Preferences";
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDeactivation([FromForm] string? reason = null, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var result = await _profileService.RequestDeactivationAsync(userId, new DeactivationRequest(reason), cancellationToken);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Your deactivation request has been recorded." : (result.ErrorMessage ?? "Unable to record your request.");

        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "No image was provided.";
            return RedirectToAction(nameof(Profile));
        }

        await using var stream = file.OpenReadStream();
        var input = new UploadedFileInput(stream, file.FileName, file.ContentType, file.Length);
        var result = await _profileService.UploadProfileImageAsync(userId, input, cancellationToken);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Profile image updated." : (result.ErrorMessage ?? "Unable to update your profile image.");

        return RedirectToAction(nameof(Profile));
    }

    private async Task PopulateAccountNavAsync(string userId, CancellationToken cancellationToken)
    {
        ViewData["AccountNav"] = await _navigationService.GetAccountNavigationAsync(userId, cancellationToken);
    }

    private async Task MergeAnonymousWishlistAsync(string userId, CancellationToken cancellationToken)
    {
        var anonymousEntries = AnonymousWishlistCookie.Read(HttpContext);
        if (anonymousEntries.Count == 0)
        {
            return;
        }

        try
        {
            var merged = await _wishlistService.MergeAsync(userId, anonymousEntries, cancellationToken);
            if (merged > 0)
            {
                _logger.LogInformation("Merged {Count} anonymous wishlist entries for user {UserId}", merged, userId);
            }
            AnonymousWishlistCookie.Clear(HttpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge anonymous wishlist for user {UserId}", userId);
        }
    }

    private async Task MergeAnonymousCartAsync(string userId, CancellationToken cancellationToken)
    {
        var anonymousEntries = AnonymousCartCookie.Read(HttpContext);
        if (anonymousEntries.Count == 0)
        {
            return;
        }

        try
        {
            var merged = await _cartService.MergeAsync(userId, anonymousEntries, cancellationToken);
            if (merged > 0)
            {
                _logger.LogInformation("Merged {Count} anonymous cart entries for user {UserId}", merged, userId);
            }
            AnonymousCartCookie.Clear(HttpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge anonymous cart for user {UserId}", userId);
        }
    }
}
