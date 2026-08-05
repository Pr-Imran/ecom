namespace FashionStore.Web.Models;

/// <summary>
/// Lightweight, cookie-backed recently-viewed history used by the product details
/// page. Stores at most <see cref="MaxItems"/> product ids ordered most-recent first.
/// A dedicated tracking implementation replaces this in the wishlist phase; this
/// helper keeps the details page functional without a database write per view.
/// </summary>
public static class RecentlyViewedCookie
{
    public const string CookieName = "fashionstore_recently_viewed";
    private const int MaxItems = 12;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static IReadOnlyList<Guid> Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<Guid>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Guid.TryParse(part, out var id) ? (Guid?)id : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }

    public static void Append(HttpContext httpContext, Guid productId)
    {
        var ids = new List<Guid> { productId };
        ids.AddRange(Read(httpContext).Where(id => id != productId));
        ids = ids.Take(MaxItems).ToList();

        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, string.Join(",", ids), options);
    }
}
