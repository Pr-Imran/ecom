using FashionStore.Application.DTOs.Catalog;
using System.Globalization;
using System.Text;

namespace FashionStore.Web.Models;

/// <summary>
/// Builds shareable, canonical-safe catalogue URLs from the current listing state.
/// Every mutation clones the query model, applies a single change and resets the
/// page to 1 so filters/sorting always start at the first result set.
/// </summary>
public static class CatalogUrlBuilder
{
    /// <summary>
    /// Full href for the current listing state (base path + query string).
    /// </summary>
    public static string Href(ProductListQuery query)
    {
        return Base(query) + ToQueryString(query);
    }

    /// <summary>
    /// Href after applying <paramref name="mutate"/> to a clone of the query.
    /// </summary>
    public static string Href(ProductListQuery query, Action<ProductListQuery> mutate)
    {
        var clone = Clone(query);
        mutate(clone);
        return Base(clone) + ToQueryString(clone);
    }

    /// <summary>
    /// Href for a pagination page keeping every filter and the current sort.
    /// </summary>
    public static string Page(ProductListQuery query, int page)
    {
        return Href(query, q => q.Page = Math.Max(1, page));
    }

    /// <summary>
    /// Href for toggling a single-select facet value. Selecting an already-selected
    /// value clears it (used for radio-style facets and the "clear" chip).
    /// </summary>
    public static string ToggleSingle(ProductListQuery query, string key, string? value)
    {
        if (key == "rating")
        {
            return Href(query, q =>
            {
                q.MinRating = int.TryParse(value, out var rating) && q.MinRating != rating ? rating : (int?)null;
                q.Page = 1;
            });
        }

        return Href(query, q =>
        {
            var current = GetSingle(q, key);
            SetSingle(q, key, string.Equals(current, value, StringComparison.OrdinalIgnoreCase) ? null : value);
            q.Page = 1;
        });
    }

    /// <summary>
    /// Href for toggling a boolean availability/sale switch on the query string.
    /// </summary>
    public static string ToggleBool(ProductListQuery query, string key)
    {
        return Href(query, q =>
        {
            switch (key)
            {
                case "instock":
                    q.InStock = !q.InStock;
                    break;
                case "onsale":
                    q.OnSale = !q.OnSale;
                    break;
            }

            q.Page = 1;
        });
    }

    /// <summary>
    /// Href for toggling a multi-select facet value (add or remove).
    /// </summary>
    public static string ToggleMulti(ProductListQuery query, string key, string value)
    {
        return Href(query, q =>
        {
            var values = GetMulti(q, key).ToList();
            var index = values.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                values.RemoveAt(index);
            }
            else
            {
                values.Add(value);
            }

            SetMulti(q, key, values);
            q.Page = 1;
        });
    }

    /// <summary>
    /// Href that removes one applied filter entirely (e.g. a selected chip).
    /// </summary>
    public static string Remove(ProductListQuery query, string key)
    {
        return Href(query, q =>
        {
            switch (key)
            {
                case "q": q.Q = null; break;
                case "category": q.Category = null; break;
                case "brand": q.Brand = null; break;
                case "collection": q.Collection = null; break;
                case "gender": q.Gender = null; break;
                case "minprice": q.MinPrice = null; break;
                case "maxprice": q.MaxPrice = null; break;
                case "instock": q.InStock = false; break;
                case "onsale": q.OnSale = false; break;
                case "minrating": q.MinRating = null; break;
                case "colour":
                case "size":
                case "material":
                case "tag": SetMulti(q, key, Array.Empty<string>()); break;
            }

            q.Page = 1;
        });
    }

    /// <summary>
    /// Href that clears every filter while keeping the listing context and search term.
    /// </summary>
    public static string ClearFilters(ProductListQuery query)
    {
        return Href(query, q =>
        {
            q.Category = null;
            q.Brand = null;
            q.Collection = null;
            q.Colour = Array.Empty<string>();
            q.Size = Array.Empty<string>();
            q.Material = Array.Empty<string>();
            q.Tag = Array.Empty<string>();
            q.Gender = null;
            q.MinPrice = null;
            q.MaxPrice = null;
            q.InStock = false;
            q.OnSale = false;
            q.MinRating = null;
            q.Page = 1;
        });
    }

    /// <summary>
    /// Href for changing sort. Relevance is omitted from the query string as the default.
    /// </summary>
    public static string Sort(ProductListQuery query, string value)
    {
        return Href(query, q =>
        {
            q.Sort = value;
            q.Page = 1;
        });
    }

    /// <summary>
    /// Href for switching the grid/list view mode.
    /// </summary>
    public static string View(ProductListQuery query, string view)
    {
        return Href(query, q => q.View = view);
    }

    /// <summary>
    /// The filter key imposed by the current listing context (category/brand/collection
    /// routes), or null when no context filter is active. Context filters are part of the
    /// page identity and are not shown as removable chips.
    /// </summary>
    public static string? ContextFilterKey(ProductListQuery query)
    {
        return query.ListingType switch
        {
            "category" => "category",
            "brand" => "brand",
            "collection" => "collection",
            _ => null
        };
    }

    /// <summary>
    /// True when any user-applied filter (excluding the listing context) is active.
    /// </summary>
    public static bool HasAppliedFilters(ProductListQuery query)
    {
        var contextKey = ContextFilterKey(query);
        bool IsContext(string? key) => contextKey != null && key == contextKey;

        return !string.IsNullOrWhiteSpace(query.Q)
            || (!IsContext("category") && !string.IsNullOrEmpty(query.Category))
            || (!IsContext("brand") && !string.IsNullOrEmpty(query.Brand))
            || (!IsContext("collection") && !string.IsNullOrEmpty(query.Collection))
            || !string.IsNullOrEmpty(query.Gender)
            || query.Colour.Length > 0
            || query.Size.Length > 0
            || query.Material.Length > 0
            || query.Tag.Length > 0
            || query.MinPrice.HasValue
            || query.MaxPrice.HasValue
            || query.InStock
            || query.OnSale
            || query.MinRating.HasValue;
    }

    /// <summary>
    /// Href for the price range form target that preserves all other filters.
    /// </summary>
    public static string PriceRange(ProductListQuery query)
    {
        return Base(query);
    }

    /// <summary>
    /// Canonical self-link for the listing: base path plus the current filters and
    /// sort with pagination and view mode stripped so search engines see one URL per
    /// filter combination.
    /// </summary>
    public static string Canonical(ProductListQuery query)
    {
        return Href(query, q =>
        {
            q.Page = 1;
            q.View = "grid";
        });
    }

    /// <summary>
    /// Base URL fragment for the pagination component: everything up to and including
    /// "page=" so the page number can be appended.
    /// </summary>
    public static string PageBase(ProductListQuery query)
    {
        var clone = Clone(query);
        clone.Page = 0;
        var qs = ToQueryString(clone);
        return Base(clone) + qs + (qs.Length > 0 ? "&page=" : "?page=");
    }

    private static string Base(ProductListQuery query)
    {
        return query.ListingLink ?? "/products";
    }

    private static ProductListQuery Clone(ProductListQuery query)
    {
        return new ProductListQuery
        {
            Q = query.Q,
            Category = query.Category,
            Brand = query.Brand,
            Collection = query.Collection,
            Colour = query.Colour.ToArray(),
            Size = query.Size.ToArray(),
            Material = query.Material.ToArray(),
            Tag = query.Tag.ToArray(),
            Gender = query.Gender,
            MinPrice = query.MinPrice,
            MaxPrice = query.MaxPrice,
            InStock = query.InStock,
            OnSale = query.OnSale,
            MinRating = query.MinRating,
            Sort = query.Sort,
            Page = query.Page,
            PageSize = query.PageSize,
            View = query.View,
            ListingType = query.ListingType,
            ListingTitle = query.ListingTitle,
            ListingSubtitle = query.ListingSubtitle,
            ListingLink = query.ListingLink
        };
    }

    private static string ToQueryString(ProductListQuery query)
    {
        var sb = new StringBuilder();
        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(key))
              .Append('=')
              .Append(Uri.EscapeDataString(value));
        }

        Add("q", query.Q);
        Add("category", query.Category);
        Add("brand", query.Brand);
        Add("collection", query.Collection);
        foreach (var value in Clean(query.Colour))
        {
            Add("colour", value);
        }

        foreach (var value in Clean(query.Size))
        {
            Add("size", value);
        }

        foreach (var value in Clean(query.Material))
        {
            Add("material", value);
        }

        foreach (var value in Clean(query.Tag))
        {
            Add("tag", value);
        }

        Add("gender", query.Gender);
        if (query.MinPrice.HasValue)
        {
            Add("minprice", query.MinPrice.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (query.MaxPrice.HasValue)
        {
            Add("maxprice", query.MaxPrice.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (query.InStock)
        {
            Add("instock", "true");
        }

        if (query.OnSale)
        {
            Add("onsale", "true");
        }

        if (query.MinRating.HasValue)
        {
            Add("minrating", query.MinRating.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(query.Sort, "relevance", StringComparison.OrdinalIgnoreCase))
        {
            Add("sort", query.Sort);
        }

        if (query.Page > 1)
        {
            Add("page", query.Page.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(query.View, "grid", StringComparison.OrdinalIgnoreCase))
        {
            Add("view", query.View);
        }

        return sb.Length > 0 ? "?" + sb : string.Empty;
    }

    private static string? GetSingle(ProductListQuery query, string key) => key switch
    {
        "q" => query.Q,
        "category" => query.Category,
        "brand" => query.Brand,
        "collection" => query.Collection,
        "gender" => query.Gender,
        _ => null
    };

    private static void SetSingle(ProductListQuery query, string key, string? value)
    {
        switch (key)
        {
            case "q": query.Q = value; break;
            case "category": query.Category = value; break;
            case "brand": query.Brand = value; break;
            case "collection": query.Collection = value; break;
            case "gender": query.Gender = value; break;
        }
    }

    private static string[] GetMulti(ProductListQuery query, string key) => key switch
    {
        "colour" => query.Colour,
        "size" => query.Size,
        "material" => query.Material,
        "tag" => query.Tag,
        _ => Array.Empty<string>()
    };

    private static void SetMulti(ProductListQuery query, string key, IEnumerable<string> values)
    {
        var cleaned = Clean(values);
        switch (key)
        {
            case "colour": query.Colour = cleaned; break;
            case "size": query.Size = cleaned; break;
            case "material": query.Material = cleaned; break;
            case "tag": query.Tag = cleaned; break;
        }
    }

    private static string[] Clean(IEnumerable<string>? values)
    {
        return values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }
}
