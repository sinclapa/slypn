using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

public class HtmlSanitizerTests
{
    private readonly IHtmlSanitizer _sut = new HtmlSanitizer();

    [Theory]
    [InlineData("<p>Plain paragraph</p>")]
    [InlineData("<p><strong>bold</strong> and <em>italic</em></p>")]
    [InlineData("<h1>Heading</h1><p>Body</p>")]
    [InlineData("<ul><li>one</li><li>two</li></ul>")]
    [InlineData("<blockquote><p>quoted</p></blockquote>")]
    [InlineData("<a href=\"https://www.parkinsons.org.uk/\" target=\"_blank\" rel=\"noopener\">PUK</a>")]
    [InlineData("<a href=\"mailto:hello@example.com\">email</a>")]
    [InlineData("<img src=\"https://example.com/x.png\" alt=\"x\" />")]
    public void Sanitize_AllowsTipTapToolbarOutput(string html)
    {
        var output = _sut.Sanitize(html);
        Assert.False(string.IsNullOrWhiteSpace(output),
            $"Expected '{html}' to survive sanitisation, got empty.");
        // The sanitiser may normalise whitespace / quote style — just ensure the
        // core marker survives, not byte-equality.
    }

    [Theory]
    // Inline scripts of every shape — must be removed entirely.
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<scr<script>ipt>alert(1)</scr</script>ipt>")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("<math><mtext><script>alert(1)</script></mtext></math>")]
    [InlineData("<iframe src=\"https://evil.example.com\"></iframe>")]
    [InlineData("<object data=\"javascript:alert(1)\"></object>")]
    [InlineData("<embed src=\"javascript:alert(1)\" />")]
    [InlineData("<form action=\"https://evil.example.com\"><input/></form>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example.com\" />")]
    [InlineData("<style>body { background: url('javascript:alert(1)') }</style>")]
    public void Sanitize_StripsScriptingAndDangerousTags(string html)
    {
        var output = _sut.Sanitize(html);
        Assert.DoesNotContain("alert",       output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script",     output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe",     output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg",        output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object",     output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<embed",      output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form",       output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<meta",       output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Inline event handlers anywhere — must not survive.
    [InlineData("<p onclick=\"alert(1)\">click</p>")]
    [InlineData("<a href=\"#\" onmouseover=\"alert(1)\">x</a>")]
    [InlineData("<img src=\"https://example.com/x.png\" onerror=\"alert(1)\" />")]
    [InlineData("<div onload=\"alert(1)\">x</div>")]
    public void Sanitize_StripsInlineEventHandlers(string html)
    {
        var output = _sut.Sanitize(html);
        Assert.DoesNotContain("onclick",     output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror",     output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload",      output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert",       output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // javascript: / data: / vbscript: URLs in href or src — must be dropped.
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">case</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">vb</a>")]
    [InlineData("<img src=\"javascript:alert(1)\" />")]
    [InlineData("<img src=\"data:text/html;base64,PHN2Zw==\" />")]
    public void Sanitize_StripsDangerousUriSchemes(string html)
    {
        var output = _sut.Sanitize(html);
        Assert.DoesNotContain("javascript:", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:",   output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:",       output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert",       output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_NullAndEmpty_AreSafe()
    {
        Assert.Equal(string.Empty, _sut.Sanitize(null));
        Assert.Equal(string.Empty, _sut.Sanitize(""));
        Assert.Equal(string.Empty, _sut.Sanitize("   "));
    }
}
