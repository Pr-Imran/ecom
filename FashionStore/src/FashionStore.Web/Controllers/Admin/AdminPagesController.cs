using System.Security.Claims;
using FashionStore.Application.Authorization;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Invoices;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FashionStore.Web.Controllers.Admin;

[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminPagesController : Controller
{
    private readonly INavigationService _navigationService;
    private readonly IInvoiceService _invoiceService;
    private readonly IEmailAdminService _emailAdminService;
    private readonly IOptions<InvoiceSettings> _invoiceOptions;

    public AdminPagesController(
        INavigationService navigationService,
        IInvoiceService invoiceService,
        IEmailAdminService emailAdminService,
        IOptions<InvoiceSettings> invoiceOptions)
    {
        _navigationService = navigationService;
        _invoiceService = invoiceService;
        _emailAdminService = emailAdminService;
        _invoiceOptions = invoiceOptions;
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

    [HttpGet("/admin/variations")]
    public async Task<IActionResult> Variations(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Variations";
        return View();
    }

    [HttpGet("/admin/images")]
    public async Task<IActionResult> Images(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Product Images";
        return View();
    }

    [HttpGet("/admin/attributes")]
    public async Task<IActionResult> Attributes(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Product Attributes";
        return View();
    }

    [HttpGet("/admin/inventory")]
    public async Task<IActionResult> Inventory(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Inventory";
        return View();
    }

    [HttpGet("/admin/coupons")]
    public async Task<IActionResult> Coupons(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Coupons";
        return View();
    }

    [HttpGet("/admin/promotions")]
    public async Task<IActionResult> Promotions(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Promotions";
        return View();
    }

    [HttpGet("/admin/shipping")]
    public async Task<IActionResult> Shipping(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Shipping";
        return View();
    }

    [HttpGet("/admin/orders")]
    public async Task<IActionResult> Orders(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Orders";
        return View();
    }

    [HttpGet("/admin/orders/{id:guid}")]
    public async Task<IActionResult> OrderDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Order";
        ViewData["OrderId"] = id;
        return View();
    }

    [HttpGet("/admin/returns")]
    public async Task<IActionResult> Returns(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Returns";
        return View();
    }

    [HttpGet("/admin/returns/{id:guid}")]
    public async Task<IActionResult> ReturnDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Return";
        ViewData["ReturnId"] = id;
        return View();
    }

    [HttpGet("/admin/reviews")]
    public async Task<IActionResult> Reviews(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Reviews";
        return View();
    }

    [HttpGet("/admin/reviews/{id:guid}")]
    public async Task<IActionResult> ReviewDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Review";
        ViewData["ReviewId"] = id;
        return View();
    }

    [HttpGet("/admin/emails")]
    [Authorize(Policy = EmailPolicies.EmailsManage)]
    public async Task<IActionResult> Emails(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Emails";
        return View("~/Views/Admin/Emails.cshtml");
    }

    [HttpGet("/admin/emails/templates")]
    [Authorize(Policy = EmailPolicies.EmailsManage)]
    public async Task<IActionResult> EmailTemplates(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Email Templates";
        ViewData["EmailTemplatePreviews"] = await _emailAdminService.GetTemplatePreviewsAsync(cancellationToken);
        return View("~/Views/Admin/EmailTemplates.cshtml");
    }

    [HttpGet("/admin/content/pages")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentPages(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Pages";
        return View("~/Views/Admin/ContentPages.cshtml");
    }

    [HttpGet("/admin/content/banners")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentBanners(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Banners";
        return View("~/Views/Admin/ContentBanners.cshtml");
    }

    [HttpGet("/admin/content/homepage-sections")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentHomepageSections(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Homepage Sections";
        return View("~/Views/Admin/ContentHomepageSections.cshtml");
    }

    [HttpGet("/admin/content/navigation")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentNavigation(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Navigation";
        return View("~/Views/Admin/ContentNavigation.cshtml");
    }

    [HttpGet("/admin/content/faqs")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentFaqs(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "FAQs";
        return View("~/Views/Admin/ContentFaqs.cshtml");
    }

    [HttpGet("/admin/content/policy-documents")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> ContentPolicyDocuments(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Policy Documents";
        return View("~/Views/Admin/ContentPolicyDocuments.cshtml");
    }

    [HttpGet("/admin/settings")]
    [Authorize(Policy = SettingsPolicies.SettingsManage)]
    public async Task<IActionResult> WebsiteSettings(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Store Settings";
        ViewData["IsSuperAdmin"] = User.IsInRole("SuperAdmin");
        return View("~/Views/Admin/Settings.cshtml");
    }

    [HttpGet("/admin/orders/{id:guid}/invoice")]
    public async Task<IActionResult> OrderInvoice(Guid id, CancellationToken cancellationToken = default)    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var invoice = await _invoiceService.EnsureForOrderAsync(id, cancellationToken);
        var history = await _invoiceService.GetSendHistoryAsync(id, cancellationToken);

        ViewData["AdminNav"] = await _navigationService.GetAdminNavigationAsync(userId, cancellationToken);
        ViewData["PageTitle"] = "Invoice";
        ViewData["OrderId"] = id;

        return View("~/Views/Admin/OrderInvoice.cshtml", new InvoiceViewModel
        {
            Invoice = invoice,
            Branding = _invoiceOptions.Value,
            SendHistory = history,
            IsAdminView = true
        });
    }

    [HttpGet("/admin/orders/{id:guid}/invoice.pdf")]
    [Authorize(Policy = OrderPolicies.OrdersPrintInvoice)]
    public async Task<IActionResult> DownloadInvoicePdf(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.EnsureForOrderAsync(id, cancellationToken);
        var pdf = await _invoiceService.BuildPdfAsync(invoice, cancellationToken);
        var fileName = $"invoice-{invoice.InvoiceNumber}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    [HttpPost("/admin/orders/{id:guid}/invoice/regenerate")]
    [Authorize(Policy = OrderPolicies.OrdersPrintInvoice)]
    public async Task<IActionResult> RegenerateInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        await _invoiceService.RegenerateAsync(id, cancellationToken);
        return RedirectToAction(nameof(OrderInvoice), new { id });
    }

    [HttpPost("/admin/orders/{id:guid}/invoice/email")]
    [Authorize(Policy = OrderPolicies.OrdersPrintInvoice)]
    public async Task<IActionResult> EmailInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _invoiceService.EmailPdfAsync(id, actor, cancellationToken);
        TempData["InvoiceEmailMessage"] = result.Success
            ? $"Invoice emailed to {result.SentTo}."
            : $"The invoice could not be emailed: {result.ErrorMessage}";
        return RedirectToAction(nameof(OrderInvoice), new { id });
    }
}
