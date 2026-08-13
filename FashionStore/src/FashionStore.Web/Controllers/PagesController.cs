using FashionStore.Application.DTOs.Content;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Public storefront pages driven by content management: About, Contact, Size
/// Guide, the legal policy documents and custom pages. System pages are looked up
/// by their stable slug; custom pages resolve through the generic route. Only
/// published content renders — drafts and archived records return 404.
/// </summary>
public class PagesController : Controller
{
    private readonly IContentManagementService _content;
    private readonly IWebsiteSettingsService _settings;
    private readonly ILogger<PagesController> _logger;

    public PagesController(
        IContentManagementService content,
        IWebsiteSettingsService settings,
        ILogger<PagesController> logger)
    {
        _content = content;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>About page (system content page slug "about").</summary>
    [HttpGet("/about")]
    public async Task<IActionResult> About(CancellationToken cancellationToken)
    {
        return await RenderSystemPageAsync("about", cancellationToken);
    }

    /// <summary>Contact page (system content page slug "contact").</summary>
    [HttpGet("/contact")]
    public async Task<IActionResult> Contact(CancellationToken cancellationToken)
    {
        var page = await _content.GetPageBySlugAsync("contact", cancellationToken);
        if (page is null || !IsVisible(page))
        {
            return NotFound();
        }

        var settings = await _settings.GetSettingsAsync(cancellationToken);
        return View("Contact", new ContactPageViewModel(page, settings.Contact));
    }

    /// <summary>Size guide (system content page slug "size-guide").</summary>
    [HttpGet("/size-guide")]
    public async Task<IActionResult> SizeGuide(CancellationToken cancellationToken)
    {
        return await RenderSystemPageAsync("size-guide", cancellationToken);
    }

    /// <summary>Delivery policy (policy document code "delivery-policy").</summary>
    [HttpGet("/delivery-policy")]
    public async Task<IActionResult> DeliveryPolicy(CancellationToken cancellationToken)
    {
        return await RenderPolicyAsync("delivery-policy", cancellationToken);
    }

    /// <summary>Return policy (policy document code "return-policy").</summary>
    [HttpGet("/return-policy")]
    public async Task<IActionResult> ReturnPolicy(CancellationToken cancellationToken)
    {
        return await RenderPolicyAsync("return-policy", cancellationToken);
    }

    /// <summary>Privacy policy (policy document code "privacy-policy").</summary>
    [HttpGet("/privacy-policy")]
    public async Task<IActionResult> PrivacyPolicy(CancellationToken cancellationToken)
    {
        return await RenderPolicyAsync("privacy-policy", cancellationToken);
    }

    /// <summary>Terms of service (policy document code "terms").</summary>
    [HttpGet("/terms")]
    public async Task<IActionResult> Terms(CancellationToken cancellationToken)
    {
        return await RenderPolicyAsync("terms", cancellationToken);
    }

    /// <summary>FAQ page listing active FAQs grouped by category.</summary>
    [HttpGet("/faq")]
    public async Task<IActionResult> Faq(CancellationToken cancellationToken)
    {
        var items = await _content.GetFaqItemsAsync(cancellationToken);
        var active = items.Where(i => i.IsActive).ToList();

        var grouped = active
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "General" : i.Category!)
            .OrderBy(g => g.Key)
            .Select(g => new FaqGroupViewModel(g.Key, g.Select(i => new FaqItemViewModel(i.Question, i.Answer))));

        return View(grouped);
    }

    /// <summary>Custom pages resolved by slug.</summary>
    [HttpGet("/pages/{slug}")]
    public async Task<IActionResult> CustomPage(string slug, CancellationToken cancellationToken)
    {
        var page = await _content.GetPageBySlugAsync(slug, cancellationToken);
        if (page is null || !IsVisible(page))
        {
            return NotFound();
        }

        ViewData["Title"] = page.Title;
        ViewData["MetaDescription"] = page.MetaDescription;
        return page.Template == ContentPageTemplate.FullWidth
            ? View("FullWidthPage", page)
            : View("Page", page);
    }

    private async Task<IActionResult> RenderSystemPageAsync(string slug, CancellationToken cancellationToken)
    {
        var page = await _content.GetPageBySlugAsync(slug, cancellationToken);
        if (page is null || !IsVisible(page))
        {
            return NotFound();
        }

        ViewData["Title"] = page.Title;
        ViewData["MetaDescription"] = page.MetaDescription;
        return View("Page", page);
    }

    private async Task<IActionResult> RenderPolicyAsync(string code, CancellationToken cancellationToken)
    {
        var document = await _content.GetPolicyDocumentByCodeAsync(code, cancellationToken);
        if (document is null || document.Status != ContentStatus.Published)
        {
            return NotFound();
        }

        ViewData["Title"] = document.Title;
        return View("PolicyDocument", document);
    }

    private static bool IsVisible(ContentPageDto page)
        => page.Status == ContentStatus.Published &&
           page.PublishedAtUtc.HasValue &&
           page.PublishedAtUtc.Value <= DateTime.UtcNow;
}

public sealed record ContactPageViewModel(ContentPageDto Page, ContactSection Contact);

public sealed record FaqGroupViewModel(string Category, IEnumerable<FaqItemViewModel> Items);

public sealed record FaqItemViewModel(string Question, string? Answer);
