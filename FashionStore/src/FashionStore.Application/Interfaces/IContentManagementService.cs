using FashionStore.Application.DTOs.Content;
using FashionStore.Domain.Enums;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Manages storefront content: pages, banners, homepage sections, navigation
/// menus, FAQs, blog posts and policy documents. Write paths sanitize rich
/// content, validate URLs and invalidate the content caches; read paths for the
/// storefront return only published, in-schedule records.
/// </summary>
public interface IContentManagementService
{
    // Pages
    Task<ContentPageListResult> GetPagesAsync(ContentPageQuery query, CancellationToken cancellationToken = default);
    Task<ContentPageDto?> GetPageAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContentPageDto?> GetPageBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> CreatePageAsync(ContentPageRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> UpdatePageAsync(Guid id, ContentPageRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> DeletePageAsync(Guid id, string actorId, CancellationToken cancellationToken = default);

    // Banners
    Task<IReadOnlyList<BannerDto>> GetBannersAsync(CancellationToken cancellationToken = default);
    Task<ContentMutationResult> CreateBannerAsync(BannerRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> UpdateBannerAsync(Guid id, BannerRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> DeleteBannerAsync(Guid id, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Published, in-schedule banners for the given placement (storefront read).</summary>
    Task<IReadOnlyList<BannerDto>> GetActiveBannersAsync(BannerPlacement placement, CancellationToken cancellationToken = default);

    // Homepage sections
    Task<IReadOnlyList<HomepageSectionDto>> GetHomepageSectionsAsync(CancellationToken cancellationToken = default);
    Task<ContentMutationResult> CreateHomepageSectionAsync(HomepageSectionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> UpdateHomepageSectionAsync(Guid id, HomepageSectionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> DeleteHomepageSectionAsync(Guid id, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Published, in-schedule homepage sections ordered by display order (storefront read).</summary>
    Task<IReadOnlyList<HomepageSectionDto>> GetActiveHomepageSectionsAsync(CancellationToken cancellationToken = default);

    // Navigation
    Task<IReadOnlyList<NavigationMenuDto>> GetNavigationMenusAsync(CancellationToken cancellationToken = default);
    Task<NavigationMenuDto?> GetNavigationMenuByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> SaveNavigationMenuAsync(Guid id, NavigationMenuRequest request, string actorId, CancellationToken cancellationToken = default);

    // FAQs
    Task<IReadOnlyList<FaqItemDto>> GetFaqItemsAsync(CancellationToken cancellationToken = default);
    Task<ContentMutationResult> CreateFaqItemAsync(FaqItemRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> UpdateFaqItemAsync(Guid id, FaqItemRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> DeleteFaqItemAsync(Guid id, string actorId, CancellationToken cancellationToken = default);

    // Blog posts (preparation)
    Task<IReadOnlyList<BlogPostDto>> GetBlogPostsAsync(CancellationToken cancellationToken = default);

    // Policy documents
    Task<IReadOnlyList<PolicyDocumentDto>> GetPolicyDocumentsAsync(CancellationToken cancellationToken = default);
    Task<PolicyDocumentDto?> GetPolicyDocumentByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ContentMutationResult> UpdatePolicyDocumentAsync(Guid id, PolicyDocumentRequest request, string actorId, CancellationToken cancellationToken = default);

    // Cache
    Task InvalidateContentCacheAsync(CancellationToken cancellationToken = default);
}
