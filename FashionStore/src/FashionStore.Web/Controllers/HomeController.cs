using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FashionStore.Web.Models;

namespace FashionStore.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
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
