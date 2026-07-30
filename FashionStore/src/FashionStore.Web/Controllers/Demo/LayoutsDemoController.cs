using FashionStore.Application.DTOs.Navigation;
using FashionStore.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Demo;

public class LayoutsDemoController : Controller
{
    private readonly INavigationService _navigationService;

    public LayoutsDemoController(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;

        ViewData["Header"] = new HeaderViewModel(
            User?.Identity?.IsAuthenticated == true ? User.FindFirst("name")?.Value : null,
            User?.Identity?.IsAuthenticated == true ? User.FindFirst("email")?.Value : null,
            0,
            "$0.00",
            User?.IsInRole("SuperAdmin") == true || User?.IsInRole("Admin") == true,
            new[] { new CategoryItem("New Arrivals", "new"), new CategoryItem("Sale", "sale") },
            _navigationService.GetActiveAnnouncements()
        );

        ViewData["MobileNav"] = await _navigationService.GetMobileNavigationAsync(userId, cancellationToken);
        ViewData["PublicNav"] = await _navigationService.GetPublicNavigationAsync(cancellationToken);

        return View();
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;

        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Layouts Demo";
        ViewData["UserDisplayName"] = User.FindFirst("name")?.Value ?? "Admin";
        ViewData["UserEmail"] = User.FindFirst("email")?.Value ?? "admin@example.com";
        ViewData["UserInitial"] = (User.FindFirst("name")?.Value ?? "A").FirstOrDefault().ToString().ToUpper();

        ViewBag.Breadcrumbs = _navigationService.GenerateBreadcrumbs(
            new[] { ("Dashboard", "/admin") },
            "Layouts Demo"
        );

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Account(CancellationToken cancellationToken = default)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;

        ViewData["UserDisplayName"] = User?.FindFirst("name")?.Value ?? "Customer";
        ViewData["UserEmail"] = User?.FindFirst("email")?.Value ?? "customer@example.com";
        ViewData["UserInitial"] = (User?.FindFirst("name")?.Value ?? "C").FirstOrDefault().ToString().ToUpper();
        ViewData["AccountNav"] = userId != null ? await _navigationService.GetAccountNavigationAsync(userId, cancellationToken) : Enumerable.Empty<NavigationItem>();
        ViewData["MobileNav"] = await _navigationService.GetMobileNavigationAsync(userId, cancellationToken);
        ViewData["Breadcrumbs"] = _navigationService.GenerateBreadcrumbs(new[] { ("Account", "/account") }, "Overview");

        return View();
    }

    [HttpGet]
    public IActionResult StatesDemo()
    {
        return View();
    }
}
