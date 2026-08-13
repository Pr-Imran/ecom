using System.Text.RegularExpressions;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Sanitizes rich HTML before it is stored for content entities (pages, policy
/// documents, homepage sections and blog posts). Uses an allow-list approach:
/// only a fixed set of safe tags and attributes survive; scripts, iframes,
/// style/script blocks, event handler attributes, javascript: URLs and
/// data: URLs are stripped. Output can therefore be rendered with
/// <c>@Html.Raw</c> on the storefront without executing markup.
/// </summary>
public static partial class RichContentSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "dl", "dt", "dd",
        "a", "img", "figure", "figcaption", "hr",
        "blockquote", "pre", "code", "table", "thead", "tbody", "tfoot",
        "tr", "th", "td", "caption", "span", "div", "small", "mark"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "target", "rel", "class", "style", "width", "height", "align", "colspan", "rowspan"
    };

    /// <summary>
    /// Tags whose content is dropped entirely (script/style/iframe bodies carry
    /// executable content and must never survive even inside an allowed tag).
    /// </summary>
    private static readonly HashSet<string> DroppedContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "svg", "math", "form", "input", "button", "textarea", "select", "link", "meta", "base", "head", "title"
    };

    private static readonly Regex TagRegex = new(
        @"<\s*\/?([a-zA-Z][a-zA-Z0-9]*)([^>]*)>",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex AttrRegex = new(
        @"([a-zA-Z-]+)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]*)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex UnsafeUrlRegex = new(
        @"(?i)^\s*(javascript|vbscript|data):",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Sanitizes <paramref name="html"/>, returning markup composed only of
    /// allow-listed tags and attributes. When the result is empty the original
    /// content is returned as escaped plain text so a value is always persisted.
    /// </summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Drop executable content blocks entirely before tag processing.
        var working = DroppedContentTags.Aggregate(html, (current, tag) =>
            Regex.Replace(
                current,
                $@"<\s*{Regex.Escape(tag)}[\s\S]*?<\s*/\s*{Regex.Escape(tag)}\s*>",
                string.Empty,
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200)));

        working = TagRegex.Replace(working, match =>
        {
            var isClosing = match.Value.TrimStart().StartsWith("</", StringComparison.OrdinalIgnoreCase);
            var tagName = match.Groups[1].Value;

            if (!AllowedTags.Contains(tagName))
            {
                // Unknown tag: keep text content only.
                return string.Empty;
            }

            if (isClosing)
            {
                return $"</{tagName.ToLowerInvariant()}>";
            }

            var sanitizedAttrs = SanitizeAttributes(match.Groups[2].Value, tagName);
            return $"<{tagName.ToLowerInvariant()}{sanitizedAttrs}>";
        });

        // Collapse accidental blank blocks left behind by removed tags.
        working = Regex.Replace(working, @"\n{3,}", "\n\n", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        if (string.IsNullOrWhiteSpace(working))
        {
            return System.Net.WebUtility.HtmlEncode(html);
        }

        return working.Trim();
    }

    private static string SanitizeAttributes(string attributeBlock, string tagName)
    {
        var result = string.Empty;
        var matches = AttrRegex.Matches(attributeBlock);

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim('"', '\'');

            if (!AllowedAttributes.Contains(name))
            {
                continue;
            }

            // Only allow http(s), mailto, tel and relative URLs.
            if (name.Equals("href", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                if (UnsafeUrlRegex.IsMatch(value) || value.Contains('<') || value.Contains('>'))
                {
                    continue;
                }
            }

            // Links open in a new tab must carry rel="noopener" for safety.
            if (name.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                value.Equals("_blank", StringComparison.OrdinalIgnoreCase) &&
                !ContainsAttribute(attributeBlock, "rel"))
            {
                result += " rel=\"noopener noreferrer\"";
            }

            var encodedValue = System.Net.WebUtility.HtmlEncode(value);
            result += $" {name.ToLowerInvariant()}=\"{encodedValue}\"";
        }

        return result;
    }

    private static bool ContainsAttribute(string block, string attribute)
    {
        return AttrRegex.IsMatch(block) &&
               AttrRegex.Matches(block).Any(m =>
                   m.Groups[1].Value.Equals(attribute, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the value is a safe URL for a link or image source.</summary>
    public static bool IsSafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('/') || trimmed.StartsWith('#'))
        {
            return true;
        }

        if (UnsafeUrlRegex.IsMatch(trimmed))
        {
            return false;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "mailto" || uri.Scheme == "tel");
    }

    /// <summary>Strips all tags and returns plain text (used for summaries).</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = DroppedContentTags.Aggregate(html, (current, tag) =>
            Regex.Replace(current, $@"<\s*{Regex.Escape(tag)}[\s\S]*?<\s*/\s*{Regex.Escape(tag)}\s*>", string.Empty, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)));
        text = TagRegex.Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]{2,}", " ", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(text, @"\n{3,}", "\n\n", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        return text.Trim();
    }
}
