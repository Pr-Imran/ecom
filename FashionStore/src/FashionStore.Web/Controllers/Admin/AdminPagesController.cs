using FashionStore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminPagesController : Controller
{
    private readonly INavigationService _navigationService;

    public AdminPagesController(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [HttpGet("/admin")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;

        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Dashboard";
        ViewData["UserDisplayName"] = User.FindFirst("name")?.Value ?? "Admin";
        ViewData["UserEmail"] = User.FindFirst("email")?.Value ?? "admin@example.com";

        return View();
    }

    [HttpGet("/admin/categories")]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        return View();
    }

    [HttpGet("/admin/brands")]
    public async Task<IActionResult> Brands(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        return View();
    }

    [HttpGet("/admin/collections")]
    public async Task<IActionResult> Collections(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        return View();
    }

    [HttpGet("/admin/products")]
    public async Task<IActionResult> Products(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        return View();
    }
}
