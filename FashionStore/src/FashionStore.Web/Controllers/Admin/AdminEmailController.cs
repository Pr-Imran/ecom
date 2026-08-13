using System.Security.Claims;
using FashionStore.Application.Authorization;
using FashionStore.Application.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative email log API. Lists queued/sent/failed messages with search and
/// status filters, shows a single message, and re-queues messages for delivery.
/// Every action is guarded by the <c>Emails.Manage</c> permission.
/// </summary>
[ApiController]
[Route("api/admin/emails")]
public class AdminEmailController : ControllerBase
{
    private readonly IEmailAdminService _emailAdminService;
    private readonly ILogger<AdminEmailController> _logger;

    public AdminEmailController(
        IEmailAdminService emailAdminService,
        ILogger<AdminEmailController> logger)
    {
        _emailAdminService = emailAdminService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = EmailPolicies.EmailsManage)]
    public async Task<IActionResult> GetLog(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _emailAdminService.GetLogAsync(search, status, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = EmailPolicies.EmailsManage)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _emailAdminService.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:guid}/resend")]
    [Authorize(Policy = EmailPolicies.EmailsManage)]
    public async Task<IActionResult> Resend(Guid id, CancellationToken cancellationToken = default)
    {
        var initiatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _emailAdminService.ResendAsync(id, initiatedBy, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new { success = true });
    }
}
