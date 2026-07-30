using System.ComponentModel.DataAnnotations;
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
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IRoleSeeder roleSeeder,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _roleSeeder = roleSeeder;
        _logger = logger;
    }

    [HttpPost("seed-roles")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedRoles(CancellationToken cancellationToken = default)
    {
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

    [HttpPost("seed-superadmin")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedSuperAdmin([FromBody] SeedSuperAdminRequest request, CancellationToken cancellationToken = default)
    {
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
