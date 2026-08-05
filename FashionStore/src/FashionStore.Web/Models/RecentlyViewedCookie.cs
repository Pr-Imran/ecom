namespace FashionStore.Web.Models;

/// <summary>
/// Cookie-backed recently-viewed history. Stores at most <see cref="MaxItems"/>
/// product ids ordered most-recent first with a per-entry last-viewed timestamp.
/// Entries older than <see cref="ExpirationDays"/> are dropped on read so the list
/// never grows unbounded. Being cookie based, no database write occurs on product
/// views, which keeps repeated page refreshes cheap and privacy-friendly.
/// </summary>
public static class RecentlyViewedCookie
{
    public const string CookieName = "fashionstore_recently_viewed";
    private const int MaxItems = 12;
    private const int ExpirationDays = 30;
    private const string EntrySeparator = ",";
    private const string ValueSeparator = ":";

    public static IReadOnlyList<Guid> Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<Guid>();
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-ExpirationDays);
        var ids = new List<Guid>();

        foreach (var part in raw.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = part;
            var timestamp = DateTimeOffset.MinValue;

            var separatorIndex = part.IndexOf(ValueSeparator, StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                value = part[..separatorIndex];
                if (long.TryParse(part[(separatorIndex + 1)..], out var unixSeconds))
                {
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                }
            }

            if (timestamp < cutoff)
            {
                continue;
            }

            if (Guid.TryParse(value, out var id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static void Append(HttpContext httpContext, Guid productId)
    {
        var existing = Read(httpContext).Where(id => id != productId).ToList();
        var ids = new List<Guid> { productId };
        ids.AddRange(existing);
        ids = ids.Take(MaxItems).ToList();

        var now = DateTimeOffset.UtcNow;
        var entries = ids.Select(id => $"{id}{ValueSeparator}{now.ToUnixTimeSeconds()}");
        var raw = string.Join(EntrySeparator, entries);

        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(ExpirationDays),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, raw, options);
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName);
    }
}
