using System.Collections.Concurrent;
using System.Net;

[assembly: AssemblyFixture(typeof(Meziantou.Framework.SyntaxHighlighting.Tests.HighlighterPreviewFixture))]

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class HighlighterPreviewFixture : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TestCase>> _cases = new(StringComparer.Ordinal);

    public static HighlighterPreviewFixture? Current { get; private set; }

    public HighlighterPreviewFixture()
    {
        Current = this;
    }

    public void Add(string language, string testName, string code, string highlighted)
    {
        var queue = _cases.GetOrAdd(language, _ => new ConcurrentQueue<TestCase>());
        queue.Enqueue(new TestCase(testName, code, highlighted));
    }

    public void Dispose()
    {
        var outputDir = AppContext.BaseDirectory;
        foreach (var language in _cases.Keys.Order(StringComparer.Ordinal))
        {
            var cases = _cases[language]
                .OrderBy(c => c.TestName, StringComparer.Ordinal)
                .ToList();

            var path = Path.Combine(outputDir, language + ".html");
            File.WriteAllText(path, RenderHtml(language, cases));
        }
    }

    private static string RenderHtml(string language, List<TestCase> cases)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html><head>\n<meta charset=\"utf-8\">\n<title>");
        sb.Append(WebUtility.HtmlEncode(language));
        sb.Append("</title>\n<style>\n");
        sb.Append(LayoutCss);
        sb.Append('\n');
        sb.Append(HljsDefaultCss);
        sb.Append("\n</style>\n</head><body>\n<h1>");
        sb.Append(WebUtility.HtmlEncode(language));
        sb.Append("</h1>\n");

        foreach (var c in cases)
        {
            sb.Append("<div class=\"case\">\n  <h2>");
            sb.Append(WebUtility.HtmlEncode(c.TestName));
            sb.Append("</h2>\n  <div class=\"cols\">\n");
            sb.Append("    <div><h3>Original</h3><pre class=\"raw\"><code>");
            sb.Append(WebUtility.HtmlEncode(c.Code));
            sb.Append("</code></pre></div>\n");
            sb.Append("    <div><h3>Highlighted</h3><pre><code class=\"hljs\">");
            sb.Append(c.Highlighted);
            sb.Append("</code></pre></div>\n");
            sb.Append("  </div>\n</div>\n");
        }

        sb.Append("</body></html>\n");
        return sb.ToString();
    }

    private sealed record TestCase(string TestName, string Code, string Highlighted);

    private const string LayoutCss = """
        body { font-family: system-ui, sans-serif; margin: 2rem; max-width: 1100px; }
        h1 { margin-bottom: 0.5rem; }
        .case { margin: 2rem 0; padding-top: 1rem; border-top: 1px solid #d0d7de; }
        .case h2 { font-size: 1.05rem; margin: 0 0 0.5rem 0; font-family: monospace; }
        .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
        .cols h3 { font-size: 0.85rem; text-transform: uppercase; color: #57606a; margin: 0 0 0.25rem 0; }
        pre { padding: 1em; border-radius: 4px; overflow-x: auto; margin: 0; }
        pre.raw { background: #f6f8fa; border: 1px solid #d0d7de; }
        """;

    private const string HljsDefaultCss = """
        pre code.hljs { display: block; overflow-x: auto; padding: 1em }
        code.hljs { padding: 3px 5px }
        .hljs { background: #F3F3F3; color: #444 }
        .hljs-comment { color: #697070 }
        .hljs-punctuation, .hljs-tag { color: #444a }
        .hljs-tag .hljs-attr, .hljs-tag .hljs-name { color: #444 }
        .hljs-attribute, .hljs-doctag, .hljs-keyword, .hljs-meta .hljs-keyword, .hljs-name, .hljs-selector-tag { font-weight: bold }
        .hljs-deletion, .hljs-number, .hljs-quote, .hljs-selector-class, .hljs-selector-id, .hljs-string, .hljs-template-tag, .hljs-type { color: #880000 }
        .hljs-section, .hljs-title { color: #880000; font-weight: bold }
        .hljs-link, .hljs-operator, .hljs-regexp, .hljs-selector-attr, .hljs-selector-pseudo, .hljs-symbol, .hljs-template-variable, .hljs-variable { color: #ab5656 }
        .hljs-literal { color: #695 }
        .hljs-addition, .hljs-built_in, .hljs-bullet, .hljs-code { color: #397300 }
        .hljs-meta { color: #1f7199 }
        .hljs-meta .hljs-string { color: #38a }
        .hljs-emphasis { font-style: italic }
        .hljs-strong { font-weight: bold }
        """;
}
