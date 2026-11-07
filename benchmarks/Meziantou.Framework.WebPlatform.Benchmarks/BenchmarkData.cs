using System.Net.Http;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Test documents. Large documents are downloaded once and cached in the artifacts folder.</summary>
internal static class BenchmarkData
{
    // BENCHMARK_DATA_DIRECTORY shares the documents between several checkouts (e.g. the base and the head of a pull request)
    private static readonly string CacheDirectory = Environment.GetEnvironmentVariable("BENCHMARK_DATA_DIRECTORY") is { Length: > 0 } directory
        ? directory
        : Path.Combine(FindRoot(), "artifacts", "benchmark-data");

    public static string Get(string name) => name switch
    {
        "html-spec" => Download("html-spec.html", "https://html.spec.whatwg.org/"),
        "wikipedia" => Download("wikipedia.html", "https://en.wikipedia.org/wiki/HTML"),

        // The style sheets linked by the Wikipedia article
        "wikipedia-css" => Download("wikipedia-1.css", "https://en.wikipedia.org/w/load.php?lang=en&modules=ext.cite.parsoid.styles%7Cext.cite.styles%7Cext.pygments%7Cext.uls.interlanguage%7Cext.visualEditor.desktopArticleTarget.noscript%7Cext.wikimediamessages.styles%7Cjquery.makeCollapsible.styles%7Cmediawiki.skinning.content.parsoid%7Cskins.vector.icons%2Cstyles%7Cskins.vector.search.codex.styles%7Cwikibase.client.init&only=styles&skin=vector-2022")
            + "\n" + Download("wikipedia-2.css", "https://en.wikipedia.org/w/load.php?lang=en&modules=site.styles&only=styles&skin=vector-2022"),
        "bootstrap-css" => Download("bootstrap.css", "https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.3.3/css/bootstrap.css"),

        // The documents whose linked style sheets are replaced by a style element, so no fetch is needed
        "wikipedia-styled" => InlineStyleSheets(Get("wikipedia"), Get("wikipedia-css")),
        "small-bootstrap" => InlineStyleSheets(Get("small"), Get("bootstrap-css")),
        "small" => """
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Small page</title><link rel="stylesheet" href="style.css"></head>
            <body>
            <header><nav><ul><li><a href="/">Home</a><li><a href="/about">About</a><li><a href="/blog">Blog</a></ul></nav></header>
            <main><article><h1>Title</h1><p>Some <b>bold</b> and <i>italic</i> text &amp; entities &copy; &#x263A;.</p>
            <table><tr><td>1<td>2<tr><td>3<td>4</table>
            <form><input name=q><select><option>a<option selected>b</select><button>Go</button></form>
            </article></main>
            <footer><p>Footer</p></footer>
            </body>
            </html>
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string InlineStyleSheets(string html, string css)
    {
        const string Link = "<link rel=\"stylesheet\"";
        var start = html.IndexOf(Link, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("The document has no linked style sheet");

        var result = new StringBuilder(html.Length + css.Length);
        result.Append(html, 0, start).Append("<style>").Append(css).Append("</style>");
        var index = start;
        while (index >= 0)
        {
            var end = html.IndexOf('>', index, StringComparison.Ordinal) + 1;
            index = html.IndexOf(Link, end, StringComparison.Ordinal);
            result.Append(html, end, (index < 0 ? html.Length : index) - end);
        }

        return result.ToString();
    }

    private static string Download(string fileName, string url)
    {
        var path = Path.Combine(CacheDirectory, fileName);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(CacheDirectory);
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Meziantou.Framework.WebPlatform.Benchmarks/1.0");
            File.WriteAllText(path, client.GetStringAsync(new Uri(url)).GetAwaiter().GetResult());
        }

        return File.ReadAllText(path);
    }

    private static string FindRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? Directory.GetCurrentDirectory();
    }
}
