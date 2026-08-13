using System.Security.Claims;
using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Content;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative content management API. Every mutation is guarded by the
/// <c>Content.Manage</c> policy and flows through
/// <see cref="IContentManagementService"/>, which sanitizes rich content, validates
/// URLs and invalidates the content caches after each change.
/// </summary>
[ApiController]
[Route("api/admin/content")]
public class AdminContentController : ControllerBase
{
    private readonly IContentManagementService _content;
    private readonly IImageValidationService _imageValidation;
    private readonly IFileStorageService _storage;
    private readonly ILogger<AdminContentController> _logger;

    public AdminContentController(
        IContentManagementService content,
        IImageValidationService imageValidation,
        IFileStorageService storage,
        ILogger<AdminContentController> logger)
    {
        _content = content;
        _imageValidation = imageValidation;
        _storage = storage;
        _logger = logger;
    }

    // ---- Pages ----

    [HttpGet("pages")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetPages([FromQuery] ContentPageQuery query, CancellationToken cancellationToken = default)
    {
        var result = await _content.GetPagesAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("pages/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetPage(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _content.GetPageAsync(id, cancellationToken);
        return page is null ? NotFound() : Ok(page);
    }

    [HttpPost("pages")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> CreatePage([FromBody] ContentPageRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A page payload is required." });
        }

        var result = await _content.CreatePageAsync(request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("pages/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> UpdatePage(Guid id, [FromBody] ContentPageRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A page payload is required." });
        }

        var result = await _content.UpdatePageAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("pages/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> DeletePage(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _content.DeletePageAsync(id, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- Banners ----

    [HttpGet("banners")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetBanners(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetBannersAsync(cancellationToken));
    }

    [HttpPost("banners")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> CreateBanner([FromBody] BannerRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A banner payload is required." });
        }

        var result = await _content.CreateBannerAsync(request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("banners/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> UpdateBanner(Guid id, [FromBody] BannerRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A banner payload is required." });
        }

        var result = await _content.UpdateBannerAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("banners/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> DeleteBanner(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _content.DeleteBannerAsync(id, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- Homepage sections ----

    [HttpGet("homepage-sections")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetHomepageSections(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetHomepageSectionsAsync(cancellationToken));
    }

    [HttpPost("homepage-sections")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> CreateHomepageSection([FromBody] HomepageSectionRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A section payload is required." });
        }

        var result = await _content.CreateHomepageSectionAsync(request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("homepage-sections/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> UpdateHomepageSection(Guid id, [FromBody] HomepageSectionRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A section payload is required." });
        }

        var result = await _content.UpdateHomepageSectionAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("homepage-sections/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> DeleteHomepageSection(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _content.DeleteHomepageSectionAsync(id, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- Navigation ----

    [HttpGet("navigation")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetNavigationMenus(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetNavigationMenusAsync(cancellationToken));
    }

    [HttpPut("navigation/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> SaveNavigationMenu(Guid id, [FromBody] NavigationMenuRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A navigation payload is required." });
        }

        var result = await _content.SaveNavigationMenuAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- FAQs ----

    [HttpGet("faqs")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetFaqItems(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetFaqItemsAsync(cancellationToken));
    }

    [HttpPost("faqs")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> CreateFaqItem([FromBody] FaqItemRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A FAQ payload is required." });
        }

        var result = await _content.CreateFaqItemAsync(request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("faqs/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> UpdateFaqItem(Guid id, [FromBody] FaqItemRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A FAQ payload is required." });
        }

        var result = await _content.UpdateFaqItemAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("faqs/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> DeleteFaqItem(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _content.DeleteFaqItemAsync(id, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- Blog posts ----

    [HttpGet("blog-posts")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetBlogPosts(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetBlogPostsAsync(cancellationToken));
    }

    // ---- Policy documents ----

    [HttpGet("policy-documents")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> GetPolicyDocuments(CancellationToken cancellationToken = default)
    {
        return Ok(await _content.GetPolicyDocumentsAsync(cancellationToken));
    }

    [HttpPut("policy-documents/{id:guid}")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    public async Task<IActionResult> UpdatePolicyDocument(Guid id, [FromBody] PolicyDocumentRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A policy document payload is required." });
        }

        var result = await _content.UpdatePolicyDocumentAsync(id, request, ActorId(), cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ---- Media ----

    /// <summary>
    /// Uploads an image for use in banners, pages or branding. The file is
    /// validated against the shared allow-list (extension, content type, size and
    /// signature) before being stored, so uploaded content never bypasses the
    /// upload restrictions.
    /// </summary>
    [HttpPost("media")]
    [Authorize(Policy = ContentPolicies.ContentManage)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadMedia(IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { success = false, error = "A file is required." });
        }

        var input = new UploadedFileInput(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);
        var validation = await _imageValidation.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { success = false, error = string.Join(" ", validation.Errors) });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var relativePath = $"content/{Guid.NewGuid():N}{extension}";
        var stored = await _storage.SaveAsync(relativePath, input.Content, validation.NormalizedContentType!, cancellationToken);

        _logger.LogInformation("Content media uploaded as {RelativePath} ({SizeBytes} bytes)", stored.RelativePath, stored.SizeBytes);
        return Ok(new { success = true, url = stored.Url });
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
}
