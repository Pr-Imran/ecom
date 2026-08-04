using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FashionStore.Web.Models;
using FashionStore.Application.DTOs.Home;
using FashionStore.Application.Interfaces;

namespace FashionStore.Web.Controllers;

public class HomeController : Controller
{
    private readonly IHomePageService _homePageService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IHomePageService homePageService,
        ILogger<HomeController> logger)
    {
        _homePageService = homePageService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await _homePageService.GetHomePageAsync(cancellationToken);
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Components()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return View(new ErrorViewModel
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            ErrorCode = "ERROR",
            Message = "An error occurred while processing your request."
        });
    }
}
