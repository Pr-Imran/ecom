using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administration of permanent slug redirects. Redirects keep deep links and
/// search engine equity intact when a product, category, brand, collection or
/// page slug is renamed: the public catalogue/content controllers issue a 301 to
/// the new slug whenever a redirect matches.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
[Route("admin/redirects")]
public class AdminRedirectsController : Controller
{
    private readonly ISlugRedirectService _redirectService;
    private readonly INavigationService _navigationService;

    public AdminRedirectsController(
        ISlugRedirectService redirectService,
        INavigationService navigationService)
    {
        _redirectService = redirectService;
        _navigationService = navigationService;
    }

    [HttpGet("")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "URL Redirects";

        var redirects = await _redirectService.GetAllAsync(cancellationToken);
        return View("~/Views/Admin/Redirects.cshtml", redirects);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> Create(SlugRedirectForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || form is null)
        {
            return await IndexWithErrorsAsync("Please correct the redirect details.", cancellationToken);
        }

        var result = await _redirectService.AddOrUpdateAsync(new SlugRedirectRequest(
            form.EntityType,
            form.OldSlug ?? string.Empty,
            form.NewSlug ?? string.Empty), cancellationToken);

        if (result.IsFailure)
        {
            return await IndexWithErrorsAsync(result.ErrorMessage ?? "Could not save the redirect.", cancellationToken);
        }

        TempData["AdminNotice"] = $"Redirect '{form.OldSlug}' saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _redirectService.RemoveAsync(id, cancellationToken);
        TempData["AdminNotice"] = "Redirect deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> IndexWithErrorsAsync(string error, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "URL Redirects";
        ViewData["AdminError"] = error;

        var redirects = await _redirectService.GetAllAsync(cancellationToken);
        return View("~/Views/Admin/Redirects.cshtml", redirects);
    }
}

public sealed class SlugRedirectForm
{
    public SlugEntityType EntityType { get; set; }
    public string? OldSlug { get; set; }
    public string? NewSlug { get; set; }
}
