using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Content;

/// <summary>A content page as shown to administrators and the storefront.</summary>
public sealed record ContentPageDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? BodyHtml,
    ContentPageTemplate Template,
    ContentStatus Status,
    bool IsSystem,
    DateTime? PublishedAtUtc,
    string? MetaTitle,
    string? MetaDescription,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record ContentPageListItemDto(
    Guid Id,
    string Title,
    string Slug,
    ContentStatus Status,
    bool IsSystem,
    DateTime? PublishedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>Payload for creating or updating a content page.</summary>
public sealed record ContentPageRequest(
    string Title,
    string Slug,
    string? Summary,
    string? BodyHtml,
    ContentPageTemplate Template,
    ContentStatus Status,
    string? MetaTitle,
    string? MetaDescription);

/// <summary>A promotional banner as shown to administrators and the storefront.</summary>
public sealed record BannerDto(
    Guid Id,
    string Name,
    string? Title,
    string? Subtitle,
    string? ImageUrl,
    string? LinkUrl,
    string? LinkText,
    string Style,
    BannerPlacement Placement,
    int DisplayOrder,
    ContentStatus Status,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc);

public sealed record BannerRequest(
    string Name,
    string? Title,
    string? Subtitle,
    string? ImageUrl,
    string? LinkUrl,
    string? LinkText,
    string Style,
    BannerPlacement Placement,
    int DisplayOrder,
    ContentStatus Status,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc);

/// <summary>A homepage section as shown to administrators and the storefront.</summary>
public sealed record HomepageSectionDto(
    Guid Id,
    string SectionType,
    string Title,
    string? Subtitle,
    string? ContentJson,
    string? Html,
    int DisplayOrder,
    ContentStatus Status,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc);

public sealed record HomepageSectionRequest(
    string SectionType,
    string Title,
    string? Subtitle,
    string? ContentJson,
    string? Html,
    int DisplayOrder,
    ContentStatus Status,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc);

/// <summary>A navigation menu including its (flat) item list.</summary>
public sealed record NavigationMenuDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    bool IsActive,
    IReadOnlyList<NavigationItemDto> Items);

/// <summary>A navigation item with its resolved children.</summary>
public sealed record NavigationItemDto(
    Guid Id,
    Guid? ParentId,
    string Label,
    string Url,
    string? Target,
    int DisplayOrder,
    bool IsActive);

public sealed record NavigationMenuRequest(
    string Name,
    string Code,
    string? Description,
    bool IsActive,
    IReadOnlyList<NavigationItemRequest>? Items);

public sealed record NavigationItemRequest(
    Guid? Id,
    Guid? ParentId,
    string Label,
    string Url,
    string? Target,
    int DisplayOrder,
    bool IsActive);

/// <summary>A frequently asked question.</summary>
public sealed record FaqItemDto(
    Guid Id,
    string Question,
    string? Answer,
    string? Category,
    int DisplayOrder,
    bool IsActive,
    DateTime? UpdatedAtUtc);

public sealed record FaqItemRequest(
    string Question,
    string? Answer,
    string? Category,
    int DisplayOrder,
    bool IsActive);

/// <summary>A blog post (administration only for now).</summary>
public sealed record BlogPostDto(
    Guid Id,
    string Title,
    string Slug,
    string? Excerpt,
    string? ContentHtml,
    string? CoverImageUrl,
    ContentStatus Status,
    DateTime? PublishedAtUtc,
    string? AuthorName);

/// <summary>A legal / informational policy document.</summary>
public sealed record PolicyDocumentDto(
    Guid Id,
    string Code,
    string Title,
    string? Summary,
    string? BodyHtml,
    ContentStatus Status,
    DateTime? PublishedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record PolicyDocumentRequest(
    string Title,
    string? Summary,
    string? BodyHtml,
    ContentStatus Status);

/// <summary>Result of a content mutation (create / update / reorder).</summary>
public sealed record ContentMutationResult(
    bool Success,
    Guid Id,
    string? ErrorMessage = null);

/// <summary>Page list query options.</summary>
public sealed record ContentPageQuery(int Page = 1, int PageSize = 50, string? Search = null);

/// <summary>Paged page list result.</summary>
public sealed record ContentPageListResult(
    IReadOnlyList<ContentPageListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
