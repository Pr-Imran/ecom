using System.Security.Claims;
using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative website settings API. Reads are guarded by the
/// <c>Settings.Manage</c> policy and served from the settings cache; writes are
/// audited and protected settings (currency, timezone, maintenance mode) are
/// rejected for non-SuperAdmin callers by the settings service.
/// </summary>
[ApiController]
[Route("api/admin/settings")]
public class AdminSettingsController : ControllerBase
{
    private readonly IWebsiteSettingsService _settings;

    public AdminSettingsController(IWebsiteSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    [Authorize(Policy = SettingsPolicies.SettingsManage)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken = default)
    {
        var snapshot = await _settings.GetSettingsAsync(cancellationToken);
        return Ok(snapshot);
    }

    [HttpPut]
    [Authorize(Policy = SettingsPolicies.SettingsManage)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateWebsiteSettingsRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A settings payload is required." });
        }

        var isSuperAdmin = User.IsInRole("SuperAdmin");
        var result = await _settings.UpdateSettingsAsync(request, ActorId(), isSuperAdmin, cancellationToken);

        return result.Success
            ? Ok(new { success = true })
            : BadRequest(new { success = false, error = result.ErrorMessage });
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
}
