namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the administrative review moderation endpoints. Moderation
/// (approve / reject / hide / delete / notes) is a single capability so every action
/// maps to the existing <c>Reviews.Manage</c> permission.
/// </summary>
public static class ReviewPolicies
{
    public const string ReviewsManage = "Reviews.Manage";
}
