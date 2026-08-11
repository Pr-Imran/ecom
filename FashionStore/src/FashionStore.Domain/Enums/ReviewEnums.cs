namespace FashionStore.Domain.Enums;

/// <summary>
/// The moderation lifecycle of a product review. Reviews are submitted as Pending,
/// then approved (visible on the product) or rejected by a moderator. Hidden
/// suppresses an approved review without deleting it (for example for re-review after
/// a customer dispute). Only Approved reviews count towards the product rating.
/// </summary>
public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Hidden = 3
}
