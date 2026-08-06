using FashionStore.Application.DTOs.Products;

namespace FashionStore.Web.Models;

/// <summary>
/// Cookie-backed temporary cart for anonymous visitors. Stores at most
/// <see cref="MaxItems"/> product/variant references with quantities so a session
/// can accumulate a cart before sign-in. The controller merges these entries into
/// the customer's persisted cart after login and clears the cookie. Only
/// identifiers and quantities are stored; pricing and stock are always recomputed
/// server-side.
/// </summary>
public static class AnonymousCartCookie
{
    public const string CookieName = "fashionstore_cart";
    private const int MaxItems = 30;
    private const int MaxQuantity = 99;
    private const string EntrySeparator = ",";
    private const string ValueSeparator = ":";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static IReadOnlyList<AnonymousCartEntry> Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<AnonymousCartEntry>();
        }

        var entries = new List<AnonymousCartEntry>();
        foreach (var part in raw.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = part.Split(ValueSeparator, StringSplitOptions.TrimEntries);
            if (segments.Length != 3)
            {
                continue;
            }

            if (!Guid.TryParse(segments[0], out var productId) ||
                !Guid.TryParse(segments[1], out var variantId) ||
                !int.TryParse(segments[2], out var quantity) ||
                quantity < 1 ||
                entries.Any(e => e.ProductId == productId && e.VariantId == variantId))
            {
                continue;
            }

            entries.Add(new AnonymousCartEntry(productId, variantId, Math.Min(quantity, MaxQuantity)));
        }

        return entries;
    }

    public static int Add(HttpContext httpContext, Guid productId, Guid variantId, int quantity)
    {
        var entries = Read(httpContext).ToList();
        var existing = entries.FirstOrDefault(e => e.ProductId == productId && e.VariantId == variantId);

        if (existing is not null)
        {
            var combined = Math.Min(existing.Quantity + quantity, MaxQuantity);
            entries.Remove(existing);
            entries.Insert(0, new AnonymousCartEntry(productId, variantId, combined));
        }
        else
        {
            entries.Insert(0, new AnonymousCartEntry(productId, variantId, Math.Min(quantity, MaxQuantity)));
        }

        entries = entries.Take(MaxItems).ToList();
        Write(httpContext, entries);
        return GetCount(entries);
    }

    public static int UpdateQuantity(HttpContext httpContext, Guid productId, Guid variantId, int quantity)
    {
        if (quantity < 1)
        {
            return Remove(httpContext, productId, variantId);
        }

        var entries = Read(httpContext).ToList();
        var existing = entries.FirstOrDefault(e => e.ProductId == productId && e.VariantId == variantId);

        if (existing is not null)
        {
            entries.Remove(existing);
            entries.Insert(0, new AnonymousCartEntry(productId, variantId, Math.Min(quantity, MaxQuantity)));
            Write(httpContext, entries);
        }

        return GetCount(entries);
    }

    public static int Remove(HttpContext httpContext, Guid productId, Guid variantId)
    {
        var entries = Read(httpContext)
            .Where(e => !(e.ProductId == productId && e.VariantId == variantId))
            .ToList();

        if (entries.Count == 0)
        {
            Clear(httpContext);
            return 0;
        }

        Write(httpContext, entries);
        return GetCount(entries);
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName);
    }

    public static int GetCount(HttpContext httpContext)
    {
        return GetCount(Read(httpContext));
    }

    private static int GetCount(IReadOnlyCollection<AnonymousCartEntry> entries)
    {
        return entries.Sum(e => e.Quantity);
    }

    private static void Write(HttpContext httpContext, IReadOnlyList<AnonymousCartEntry> entries)
    {
        var raw = string.Join(EntrySeparator, entries.Select(e =>
            $"{e.ProductId}{ValueSeparator}{e.VariantId}{ValueSeparator}{e.Quantity}"));

        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, raw, options);
    }
}
