using System.Text;
using System.Text.RegularExpressions;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Review content screening. All review text passes through here on submission:
/// HTML is stripped down to safe plain text (raw unsafe HTML is never stored), and a
/// lightweight spam/unsafe heuristic flags suspicious content so moderators can
/// review it first. Flagging is advisory — it does not block submission, it marks the
/// review for the moderation queue.
/// </summary>
public static partial class ReviewContentModerator
{
    private static readonly HashSet<string> BlockedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://", "https://", "www.",
        "buy now", "click here", "free gift", "earn money", "cash out",
        "viagra", "casino", "lottery", "bitcoin", "crypto", "free hosting",
        "make money fast", "increase followers", "followers", "subscribers",
        "nude", "escort", "porn",
        "password:", "credit card number", "ssn", "bank account"
    };

    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex EmailRegex = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PhoneRegex = new(
        @"(\+?\d[\d\s\-().]{6,}\d)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex TagRegex = new(
        @"<\/?[^>]+>",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ScriptRegex = new(
        @"<\s*script[\s\S]*?>[\s\S]*?<\s*/\s*script\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex RepeatedCharRegex = new(
        @"(.)\1{5,}",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Strips HTML/script content, decodes entities and collapses whitespace into
    /// plain text. Review bodies are always rendered with Razor escaping, so the
    /// result can never execute as markup.
    /// </summary>
    public static string SanitizeToPlainText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var cleaned = content;
        cleaned = ScriptRegex.Replace(cleaned, string.Empty);
        cleaned = TagRegex.Replace(cleaned, " ");
        cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
        cleaned = Regex.Replace(cleaned, @"[ \t]{2,}", " ", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        return cleaned.Trim();
    }

    /// <summary>
    /// True when the content looks like spam or unsafe content. The heuristic checks
    /// blocked marketing terms, links, email/phone harvesting, excessive repetition
    /// and shouty text.
    /// </summary>
    public static bool IsFlagged(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var text = content;
        var normalized = text.ToLowerInvariant();

        if (BlockedTerms.Any(normalized.Contains))
        {
            return true;
        }

        var urls = UrlRegex.Matches(text).Count;
        if (urls > 1)
        {
            return true;
        }

        if (EmailRegex.IsMatch(text) || PhoneRegex.IsMatch(text))
        {
            return true;
        }

        if (RepeatedCharRegex.IsMatch(text))
        {
            return true;
        }

        var letters = text.Count(char.IsLetter);
        var upper = text.Count(char.IsUpper);
        if (letters >= 20 && upper / (double)letters >= 0.6)
        {
            return true;
        }

        return false;
    }
}
