using FashionStore.Application.DTOs.Products;

namespace FashionStore.Web.Models;

/// <summary>
/// Cookie-backed temporary wishlist for anonymous visitors. Stores at most
/// <see cref="MaxItems"/> product/variant references so a session can accumulate a
/// wishlist before sign-in. The controller merges these entries into the customer's
/// persisted wishlist after login and clears the cookie.
/// </summary>
public static class AnonymousWishlistCookie
{
    public const string CookieName = "fashionstore_wishlist";
    private const int MaxItems = 30;
    private const string EntrySeparator = ",";
    private const string ValueSeparator = ":";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static IReadOnlyList<WishlistMutationRequest> Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<WishlistMutationRequest>();
        }

        var entries = new List<WishlistMutationRequest>();
        foreach (var part in raw.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = part;
            Guid? variantId = null;

            var separatorIndex = part.IndexOf(ValueSeparator, StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                value = part[..separatorIndex];
                if (Guid.TryParse(part[(separatorIndex + 1)..], out var parsedVariant))
                {
                    variantId = parsedVariant;
                }
            }

            if (Guid.TryParse(value, out var productId) &&
                !entries.Any(e => e.ProductId == productId && e.VariantId == variantId))
            {
                entries.Add(new WishlistMutationRequest(productId, variantId));
            }
        }

        return entries;
    }

    public static int Append(HttpContext httpContext, Guid productId, Guid? variantId)
    {
        var entries = Read(httpContext)
            .Where(e => !(e.ProductId == productId && e.VariantId == variantId))
            .ToList();

        entries.Insert(0, new WishlistMutationRequest(productId, variantId));
        entries = entries.Take(MaxItems).ToList();

        var raw = string.Join(EntrySeparator, entries.Select(e =>
            e.VariantId.HasValue ? $"{e.ProductId}{ValueSeparator}{e.VariantId.Value}" : e.ProductId.ToString()));

        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, raw, options);
        return entries.Count;
    }

    public static int Remove(HttpContext httpContext, Guid productId, Guid? variantId)
    {
        var entries = Read(httpContext)
            .Where(e => !(e.ProductId == productId && e.VariantId == variantId))
            .ToList();

        if (entries.Count == 0)
        {
            Clear(httpContext);
            return 0;
        }

        var raw = string.Join(EntrySeparator, entries.Select(e =>
            e.VariantId.HasValue ? $"{e.ProductId}{ValueSeparator}{e.VariantId.Value}" : e.ProductId.ToString()));

        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, raw, options);
        return entries.Count;
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName);
    }

    public static int GetCount(HttpContext httpContext)
    {
        return Read(httpContext).Count;
    }
}
