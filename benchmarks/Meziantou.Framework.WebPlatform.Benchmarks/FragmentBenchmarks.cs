using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Parses HTML fragments in the context of a body element, as innerHTML and the HTML sanitizers do, then appends them to the
/// body and serializes it.
/// </summary>
[MemoryDiagnoser]
public class FragmentBenchmarks
{
    private readonly HtmlDocumentParser _parser = new(new HtmlParserOptions { MaxElementCount = int.MaxValue });
    private string _html = "";
    private Element _body = null!;

    [Params("Empty", "Short", "Comment", "Hostile", "NullCharacters")]
    public string Fragment { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _body = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>").Body!;
        _html = Fragment switch
        {
            "Empty" => "",

            // A one-line comment
            "Short" => "<p>Hello <b>world</b>, see <a href=\"https://example.com/page?id=1\">this page</a>.</p>",

            // A comment written in a rich text editor
            "Comment" => """
                <p>Thanks for the article! A few remarks:</p>
                <ul>
                  <li><strong>First</strong>, the <code>Span&lt;T&gt;</code> example does not compile on <em>.NET 8</em>.</li>
                  <li>Second, see <a href="https://learn.microsoft.com/en-us/dotnet/api/system.span-1" target="_blank" rel="noopener">the docs</a>.</li>
                  <li>Third, <span class="highlight" style="color: red">this</span> is wrong.</li>
                </ul>
                <blockquote><p>Premature optimization is the root of all evil.</p></blockquote>
                <p><img src="https://example.com/images/screenshot.png" alt="Screenshot" width="640" height="480"></p>
                <pre><code class="language-csharp">var x = 1;
                Console.WriteLine(x);
                </code></pre>
                <p>Regards,<br>John</p>
                """,

            // Markup that a sanitizer must clean: event handlers, scripts, unsafe URLs, unknown elements
            "Hostile" => CreateHostileFragment(),

            // A text split into many character tokens: each NULL character is ignored
            "NullCharacters" => string.Concat(Enumerable.Repeat("a\0", 10_000)),
            _ => throw new InvalidOperationException(),
        };
    }

    [Benchmark]
    public DocumentFragment ParseFragment() => _parser.ParseFragment(_html, _body);

    [Benchmark]
    public string ParseAppendAndSerialize()
    {
        var body = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>").Body!;
        body.Append(_parser.ParseFragment(_html, body));
        return body.GetHtml();
    }

    private static string CreateHostileFragment()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            sb.Append("<div id=\"d").Append(i).Append("\" class=\"c\" onclick=\"alert(1)\" style=\"x\" data-x=\"1\" aria-label=\"label\">");
            sb.Append("<a href=\"javascript:alert(1)\" onmouseover=\"x()\">link</a> <img src=\"data:image/png;base64,iVBORw0KGgo=\" onerror=\"x()\" srcset=\"a.png 1x, javascript:x 2x\">");
            sb.Append("<script>alert(1)</script><custom-tag foo=bar>text<iframe src=x></iframe></custom-tag>");
            sb.Append("<svg><circle r=1></circle></svg><form><input type=text></form>");
            sb.Append("<p>Some &amp; text &#13; with <font color=red>font</font></p></div>");
        }

        return sb.ToString();
    }
}
