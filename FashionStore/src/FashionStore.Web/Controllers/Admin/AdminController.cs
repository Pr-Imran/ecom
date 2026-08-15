using System.ComponentModel.DataAnnotations;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IRoleSeeder _roleSeeder;
    private readonly IContentSeeder _contentSeeder;
    private readonly IAuditService _auditService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IRoleSeeder roleSeeder,
        IContentSeeder contentSeeder,
        IAuditService auditService,
        IWebHostEnvironment environment,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _roleSeeder = roleSeeder;
        _contentSeeder = contentSeeder;
        _auditService = auditService;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("seed-roles")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedRoles(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
                await _roleSeeder.SeedAsync(cancellationToken);
            return Ok(new { message = "Roles and permissions seeded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed roles");
            return StatusCode(500, new { error = "Failed to seed roles", details = ex.Message });
        }
    }

    [HttpPost("seed-content")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedContent(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            await _contentSeeder.SeedAsync(cancellationToken);
            return Ok(new { message = "Default content seeded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed default content");
            return StatusCode(500, new { error = "Failed to seed default content", details = ex.Message });
        }
    }

    [HttpPost("seed-superadmin")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedSuperAdmin([FromBody] SeedSuperAdminRequest request, CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var superAdminExists = await _roleManager.RoleExistsAsync("SuperAdmin");
            if (!superAdminExists)
            {
            await _roleSeeder.SeedAsync(cancellationToken);
            }

            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return BadRequest(new { error = "User with this email already exists" });
            }

            var superAdmin = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(superAdmin, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            }

            await _userManager.AddToRoleAsync(superAdmin, "SuperAdmin");

            _logger.LogInformation("SuperAdmin account created for {Email}", request.Email);

            return Ok(new
            {
                message = "SuperAdmin account created successfully",
                email = request.Email,
                note = "Please change this password immediately and disable this endpoint in production"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SuperAdmin account");
            return StatusCode(500, new { error = "Failed to create SuperAdmin account", details = ex.Message });
        }
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken = default)
    {
        var users = await _userManager.Users
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.IsActive,
                u.EmailConfirmed,
                u.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpPost("users/{userId}/deactivate")]
    public async Task<IActionResult> DeactivateUser(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound(new { error = "User not found." });
        }

        if (!user.IsActive)
        {
            return Ok(new { message = "User is already deactivated." });
        }

        user.IsActive = false;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // Rotate the security stamp so every existing session for the account is
        // invalidated immediately.
        await _userManager.UpdateSecurityStampAsync(user);

        await _auditService.RecordAsync(
            "User.Suspended",
            "ApplicationUser",
            user.Id,
            oldValue: "true",
            newValue: "false",
            cancellationToken: cancellationToken);

        _logger.LogInformation("User {UserId} suspended by {Actor}", user.Id, User.Identity?.Name);

        return Ok(new { message = "User deactivated and all existing sessions invalidated." });
    }

    [HttpPost("users/{userId}/activate")]
    public async Task<IActionResult> ActivateUser(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound(new { error = "User not found." });
        }

        if (user.IsActive)
        {
            return Ok(new { message = "User is already active." });
        }

        user.IsActive = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // Rotate the security stamp so the reactivated user signs in afresh.
        await _userManager.UpdateSecurityStampAsync(user);

        await _auditService.RecordAsync(
            "User.Activated",
            "ApplicationUser",
            user.Id,
            oldValue: "false",
            newValue: "true",
            cancellationToken: cancellationToken);

        _logger.LogInformation("User {UserId} activated by {Actor}", user.Id, User.Identity?.Name);

        return Ok(new { message = "User activated." });
    }
}

public class SeedSuperAdminRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
