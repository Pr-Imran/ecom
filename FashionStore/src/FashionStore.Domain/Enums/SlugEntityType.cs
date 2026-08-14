namespace FashionStore.Domain.Enums;

/// <summary>
/// The kind of entity a <c>SlugRedirect</c> belongs to. Used to scope slug
/// redirect lookups so a product slug never collides with a category slug.
/// </summary>
public enum SlugEntityType
{
    Product = 1,
    Category = 2,
    Brand = 3,
    Collection = 4,
    Page = 5
}
