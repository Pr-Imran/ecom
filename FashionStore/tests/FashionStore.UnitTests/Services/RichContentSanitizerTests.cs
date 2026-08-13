using FashionStore.Infrastructure.Services;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class RichContentSanitizerTests
{
    [Fact]
    public void Sanitize_StripsScriptTags()
    {
        var input = "<p>Hello</p><script>alert('xss')</script><p>World</p>";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void Sanitize_StripsInlineEventHandlers()
    {
        var input = "<img src=\"/x.png\" onerror=\"alert(1)\" alt=\"x\">";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_BlocksJavaScriptUrlScheme()
    {
        var input = "<a href=\"javascript:alert(1)\">Click</a>";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Click", result);
    }

    [Fact]
    public void Sanitize_BlocksStyleAndIframeElements()
    {
        var input = "<style>body{display:none}</style><iframe src=\"https://evil.example\"></iframe><p>ok</p>";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.DoesNotContain("style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok", result);
    }

    [Fact]
    public void Sanitize_PreservesSafeMarkup()
    {
        var input = "<h2>Heading</h2><p><strong>Bold</strong> and <em>italic</em></p><ul><li>One</li></ul>";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.Contains("<h2>Heading</h2>", result);
        Assert.Contains("<strong>Bold</strong>", result);
        Assert.Contains("<li>One</li>", result);
    }

    [Fact]
    public void Sanitize_ForcesNoopenerOnBlankTargetLinks()
    {
        var input = "<a href=\"https://example.com\" target=\"_blank\">Link</a>";

        var result = RichContentSanitizer.Sanitize(input);

        Assert.Contains("rel=\"noopener noreferrer\"", result);
    }

    [Fact]
    public void Sanitize_NullOrEmptyIsHandled()
    {
        Assert.Equal(string.Empty, RichContentSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, RichContentSanitizer.Sanitize(string.Empty));
    }

    [Theory]
    [InlineData("https://example.com/x.png", true)]
    [InlineData("https://cdn.example.com/logo.webp", true)]
    [InlineData("/local/path.jpg", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    public void IsSafeUrl_ValidatesSchemes(string url, bool expected)
    {
        Assert.Equal(expected, RichContentSanitizer.IsSafeUrl(url));
    }

    [Fact]
    public void ToPlainText_RemovesMarkup()
    {
        var input = "<p>Hello <strong>world</strong></p>";

        var result = RichContentSanitizer.ToPlainText(input);

        Assert.Equal("Hello world", result);
    }
}
