namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the content administration surface (pages, banners,
/// homepage sections, navigation, FAQs and policy documents). Creating, editing
/// and deleting content all map to the single <c>Content.Manage</c> capability.
/// </summary>
public static class ContentPolicies
{
    public const string ContentManage = "Content.Manage";
}
