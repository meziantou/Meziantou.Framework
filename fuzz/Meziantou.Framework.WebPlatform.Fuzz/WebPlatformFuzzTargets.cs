using System.Xml;
using Meziantou.Framework.Fuzzing;
using CssApi = Meziantou.Framework.WebPlatform.Css.Om.Css;
using DomUrl = Meziantou.Framework.WebPlatform.Dom.Url;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Meziantou.Framework.WebPlatform.Unicode;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.XPath;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The fuzzing targets. A target processes any input without throwing, except for the exceptions that the API documents
/// for invalid input (e.g. a <see cref="DomException"/> for an invalid selector): any other exception, a crash (stack
/// overflow, out of memory) or a hang is a bug.
/// </summary>
public static class WebPlatformFuzzTargets
{
    private const string QueryDocumentMarkup = """
        <!DOCTYPE html>
        <html lang="en" dir="ltr"><head><title>Document</title><style>p { color: red }</style></head>
        <body class="a b" id="body">
        <div id="main" data-x="1" data-y="a b c"><p class="a">Text <b>bold</b> <i lang="fr">it</i><!-- comment --></p>
        <ul><li>1<li class="odd">2<li><a href="https://example.com/a?b#c" hreflang="en">3</a></ul>
        <table><caption>c</caption><tr><th scope="col">h<td>d</table>
        <form name="f"><input name="q" required value="x"><input type="checkbox" checked><select><option>a<option selected disabled>b</select>
        <textarea placeholder="p"></textarea><button type="submit" disabled>Go</button><fieldset disabled><input></fieldset></form>
        <svg viewBox="0 0 10 10"><g><circle r="1"/><text xml:space="preserve">t</text></g><foreignObject><p>f</p></foreignObject></svg>
        <math><mi>x</mi><annotation-xml encoding="text/html"><b>a</b></annotation-xml></math>
        <template><p>in template</p></template><details open><summary>s</summary>d</details><dialog open>d</dialog>
        <div hidden></div><span lang="en-US"></span><a name="anchor"></a><img alt="" src="x.png"><video controls></video>
        </div>
        </body></html>
        """;

    // The cost of an XPath expression is polynomial in the size of the document, with the nesting of the predicates as the
    // exponent (e.g. //*[//*[//*]]), as in every XPath 1.0 engine: the document of the XPath target is small, so that the
    // fuzzer explores the expressions instead of reporting their inherent cost as timeouts
    private const string XPathDocumentMarkup = """
        <!DOCTYPE html><html lang=en><head><title>Document</title></head><body id=body class="a b"><!--c-->
        <div id=main data-x=1><p class=a>Text <b>1</b></p><svg><circle r=1 /></svg><math><mi>x</mi></math>
        <template><i>t</i></template><input name=q value=2></div></body></html>
        """;

    // The documents of the queries are shared: the queries do not mutate them
    private static readonly Lazy<Document> QueryDocument = new(() => HtmlDocumentParser.ParseDocument(QueryDocumentMarkup));
    private static readonly Lazy<Document> XPathDocument = new(() => HtmlDocumentParser.ParseDocument(XPathDocumentMarkup));

    // The cost of an expression grows with its nesting (see XPathDocumentMarkup): the longer inputs are truncated
    private const int XPathMaxLength = 256;

    /// <summary>The targets: the text targets receive the decoded input (<see cref="FuzzText.Decode"/>), the binary targets the bytes.</summary>
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("webplatform-html", data => Html(FuzzText.Decode(data))) { Seeds = () => GetSeeds("html") },
        new("webplatform-xml", data => Xml(FuzzText.Decode(data))) { Seeds = () => GetSeeds("xml") },
        new("webplatform-css", data => Css(FuzzText.Decode(data))) { Seeds = () => GetSeeds("css") },
        new("webplatform-selectors", data => Selectors(FuzzText.Decode(data))) { Seeds = () => GetSeeds("selectors") },
        new("webplatform-xpath", data => XPath(FuzzText.Decode(data.Length > XPathMaxLength ? data[..XPathMaxLength] : data))) { Seeds = () => GetSeeds("xpath") },
        new("webplatform-url", data => Url(FuzzText.Decode(data))) { Seeds = () => GetSeeds("url") },
        new("webplatform-urlpattern", data => UrlPattern(FuzzText.Decode(data))) { Seeds = () => GetSeeds("url") },
        new("webplatform-font", data => Font(data.ToArray())) { Seeds = () => GetSeeds("font") },
    ];

    /// <summary>The embedded seeds of a target (the resources <c>Seeds/&lt;directory&gt;/*</c>).</summary>
    private static IEnumerable<FuzzSeed> GetSeeds(string directory)
    {
        var assembly = typeof(WebPlatformFuzzTargets).Assembly;
        var prefix = "Seeds/" + directory + "/";
        // The names of the resources use the directory separator of the OS that built the project (%(RecursiveDir))
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[prefix.Length..], memory.ToArray());
        }
    }

    /// <summary>Parses an HTML document and fragments, serializes and formats them, and parses the serialization.</summary>
    public static void Html(string input)
    {
        var document = HtmlDocumentParser.ParseDocument(input);
        var html = document.DocumentElement?.OuterHtml ?? "";
        _ = document.ToHtml(new HtmlPrettyFormatter());
        _ = document.ToHtml(new HtmlMinifyFormatter());
        _ = document.TextContent;
        _ = HtmlDocumentParser.ParseDocument(html).DocumentElement?.OuterHtml;

        var parser = new HtmlDocumentParser();
        foreach (var context in new[] { "body", "template", "table", "select", "svg", "textarea" })
        {
            var element = context == "svg" ? document.CreateElementNS("http://www.w3.org/2000/svg", "svg") : document.CreateElement(context);
            _ = parser.ParseFragment(input, element).ChildNodes.Length;
        }
    }

    /// <summary>Parses an XML document with the parser and with DOMParser, and serializes it.</summary>
    public static void Xml(string input)
    {
        try
        {
            var document = XmlDocumentParser.ParseDocument(input);
            Serialize(document);
        }
        catch (XmlException)
        {
            // Not namespace-well-formed
        }

        Serialize(new DomParser().ParseFromString(input, DomParserSupportedType.ApplicationXml));
        Serialize(new DomParser().ParseFromString(input, DomParserSupportedType.ImageSvgXml));

        static void Serialize(Document document)
        {
            try
            {
                _ = new XmlSerializer().SerializeToString(document);
                _ = document.DocumentElement?.OuterHtml;
            }
            catch (DomException)
            {
                // The document is not well-formed (e.g. a comment with "--"): outerHTML requires a well-formed XML document
            }
        }
    }

    /// <summary>Parses a style sheet, a declaration block and a condition, and serializes them.</summary>
    public static void Css(string input)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(input);
        var text = Serialize(sheet.CssRules);

        // The serialization is parsed again (it must be valid CSS)
        var reparsed = new CssStyleSheet();
        reparsed.ReplaceSync(text);
        _ = Serialize(reparsed.CssRules);

        var element = CreateDocument().CreateElement<HtmlElement>("div");
        element.Style.CssText = input;
        _ = element.Style.CssText;
        for (var i = 0u; i < element.Style.Length; i++)
        {
            var property = element.Style.Item(i);
            _ = element.Style.GetPropertyValue(property);
        }

        _ = CssApi.Supports(input);

        static string Serialize(CssRuleList rules)
        {
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < rules.Length; i++)
            {
                result.Append(rules[i].CssText).Append('\n');
            }

            return result.ToString();
        }
    }

    /// <summary>Parses selectors and matches them against a document.</summary>
    public static void Selectors(string input)
    {
        var document = QueryDocument.Value;
        try
        {
            _ = document.QuerySelectorAll(input).Length;
            _ = document.GetElementById("main")!.Matches(input);
            _ = document.QuerySelector("b")!.Closest(input);
        }
        catch (DomException)
        {
            // Invalid selector (SyntaxError)
        }
    }

    /// <summary>Evaluates an XPath expression against a document.</summary>
    public static void XPath(string input)
    {
        var document = XPathDocument.Value;
        try
        {
            var result = document.Evaluate(input, document.Body ?? (Node)document);
            switch (result.ResultType)
            {
                case XPathResult.NumberType:
                    _ = result.NumberValue;
                    break;
                case XPathResult.StringType:
                    _ = result.StringValue;
                    break;
                case XPathResult.BooleanType:
                    _ = result.BooleanValue;
                    break;
                default:
                    while (result.IterateNext() is not null)
                    {
                    }

                    break;
            }

            _ = document.Evaluate(input, document, type: XPathResult.OrderedNodeSnapshotType).SnapshotLength;
        }
        catch (DomException)
        {
            // Invalid expression (SyntaxError, NamespaceError)
        }
        catch (TypeErrorException)
        {
            // The result cannot be converted to the requested type
        }
    }

    /// <summary>Parses a URL, and checks that parsing its serialization returns the same URL.</summary>
    public static void Url(string input)
    {
        foreach (var @base in new string?[] { null, "https://user:pass@example.com:8080/a/b/c?d#e", "file:///C:/a/b", "data:text/plain,a", "web+demo:/a/b" })
        {
            var url = DomUrl.Parse(input, @base);
            if (url is null)
                continue;

            var href = url.Href;
            _ = url.Origin;
            _ = url.SearchParams.ToString();
            var reparsed = DomUrl.Parse(href) ?? throw new InvalidOperationException($"The serialization of a URL is not a valid URL: '{href}' (input '{input}', base '{@base}')");
            if (!string.Equals(reparsed.Href, href, StringComparison.Ordinal))
                throw new InvalidOperationException($"The serialization of a URL is not idempotent: '{href}' is parsed as '{reparsed.Href}' (input '{input}', base '{@base}')");
        }
    }

    /// <summary>Compiles a URL pattern (its regular expressions), and matches it.</summary>
    public static void UrlPattern(string input)
    {
        try
        {
            var pattern = new UrlPattern(input, "https://example.com/a/b");
            _ = pattern.Test("https://example.com/a/b?c#d");
            _ = pattern.Exec(input, "https://example.com/");
            _ = pattern.HasRegExpGroups;
        }
        catch (TypeErrorException)
        {
            // Invalid pattern
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // Catastrophic backtracking of the regular expression of the pattern (see Program.cs)
        }
    }

    /// <summary>
    /// Loads a font file (OpenType, collection, WOFF, WOFF2) with the managed font reader, and reads the metrics, the
    /// names, the glyphs, the advances, the kerning and the variations of its faces.
    /// </summary>
    public static void Font(byte[] input)
    {
        if (FontFile.Load(input) is not { } file)
            return;

        foreach (var font in file.Faces)
        {
            _ = font.Metrics;
            _ = font.FullName;
            _ = font.GetName(OpenTypeFont.NameId.License);
            var settings = new List<FontVariationSetting>();
            foreach (var axis in font.Variations.Axes)
            {
                settings.Add(new FontVariationSetting(axis.Tag, axis.MaxValue));
            }

            var instance = font.Variations.CreateInstance(settings.ToArray());
            _ = font.GetMetrics(instance);
            var previous = (ushort)0;
            foreach (var c in "AVTo0x H\u00E9\u03B1\u0416\u4E00\U0001F600")
            {
                var glyph = font.GetGlyphId(c);
                _ = font.GetAdvanceWidth(glyph, instance);
                _ = font.GetLeftSideBearing(glyph);
                _ = font.GetAdvanceHeight(glyph);
                _ = font.GetTopSideBearing(glyph);
                _ = font.TryGetGlyphBounds(glyph, out _);
                _ = font.TryGetGlyphId(c, 0xFE00, out _);
                _ = font.GetKerning(previous, glyph, instance);
                _ = font.GetPairAdjustment(previous, glyph, OpenTypeTag.VerticalKerning);
                previous = glyph;
            }

            for (var glyph = 0; glyph < Math.Min(font.GlyphCount, 256); glyph++)
            {
                _ = font.GetAdvanceWidth((ushort)glyph, instance);
                _ = font.GetKerning((ushort)glyph, previous, instance);
            }

            // The shaping engine (GSUB, GPOS, GDEF) with the default and the Arabic shapers
            using var buffer = new ShapingBuffer();
            font.Shape("office AVAT\u0301a\u00E9 fi\u200D", UnicodeScript.Latin, ShapingDirection.LeftToRight, "tr", features: null, instance, buffer);
            font.Shape("\u0644\u0627 \u0628\u0628\u064E\u200C\u0628", UnicodeScript.Arabic, ShapingDirection.RightToLeft, "ar", features: null, instance, buffer);
            font.Shape("\u1820\u180B\u1821\u180E\u1822", UnicodeScript.Mongolian, ShapingDirection.TopToBottom, language: null, features: null, instance: null, buffer);
            font.Shape("\u4E00\u3001", UnicodeScript.Han, ShapingDirection.TopToBottom, "ja", features: null, instance: null, buffer);
        }
    }

    private static Document CreateDocument() => QueryDocument.Value.Implementation.CreateHtmlDocument("");
}
