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

    // The number of round trips (parse, serialize) after which the serialization of an HTML document is the same markup
    private const int HtmlMaxRoundTrips = 4;

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
        new("webplatform-text-decoder", TextDecoding) { Seeds = () => GetSeeds("text-decoder") },
        new("webplatform-cookies", Cookies) { Seeds = () => GetSeeds("cookies") },
    ];

    /// <summary>
    /// The labels of the text decoder target and the name of their encoding (null when the label is not supported): a label
    /// of every encoding, a few aliases (in any case, with the whitespace the labels are stripped of), and unsupported labels
    /// (the replacement encoding is not supported by TextDecoder).
    /// </summary>
    private static readonly (string Label, string? Encoding)[] TextDecoderLabels =
    [
        ("utf-8", "utf-8"), ("ibm866", "ibm866"), ("iso-8859-2", "iso-8859-2"), ("iso-8859-3", "iso-8859-3"), ("iso-8859-4", "iso-8859-4"),
        ("iso-8859-5", "iso-8859-5"), ("iso-8859-6", "iso-8859-6"), ("iso-8859-7", "iso-8859-7"), ("iso-8859-8", "iso-8859-8"),
        ("iso-8859-8-i", "iso-8859-8-i"), ("iso-8859-10", "iso-8859-10"), ("iso-8859-13", "iso-8859-13"), ("iso-8859-14", "iso-8859-14"),
        ("iso-8859-15", "iso-8859-15"), ("iso-8859-16", "iso-8859-16"), ("koi8-r", "koi8-r"), ("koi8-u", "koi8-u"), ("macintosh", "macintosh"),
        ("windows-874", "windows-874"), ("windows-1250", "windows-1250"), ("windows-1251", "windows-1251"), ("windows-1252", "windows-1252"),
        ("windows-1253", "windows-1253"), ("windows-1254", "windows-1254"), ("windows-1255", "windows-1255"), ("windows-1256", "windows-1256"),
        ("windows-1257", "windows-1257"), ("windows-1258", "windows-1258"), ("x-mac-cyrillic", "x-mac-cyrillic"), ("gbk", "gbk"),
        ("gb18030", "gb18030"), ("big5", "big5"), ("euc-jp", "euc-jp"), ("iso-2022-jp", "iso-2022-jp"), ("shift_jis", "shift_jis"),
        ("euc-kr", "euc-kr"), ("utf-16be", "utf-16be"), ("utf-16le", "utf-16le"), ("x-user-defined", "x-user-defined"),
        ("UTF8", "utf-8"), (" unicode-1-1-utf-8\t", "utf-8"), ("\nlatin1\f", "windows-1252"), ("ascii", "windows-1252"), ("utf-16", "utf-16le"),
        ("x-sjis", "shift_jis"), ("csiso2022jp", "iso-2022-jp"), ("x-x-big5", "big5"), ("ks_c_5601-1987", "euc-kr"), ("x-gbk", "gbk"),
        ("replacement", null), ("csiso2022kr", null), ("hz-gb-2312", null), ("iso-2022-cn", null), ("utf-7", null), ("utf-32", null), ("", null), ("utf\u00A0-8", null),
    ];

    /// <summary>The URLs of the cookie target: secure and non-secure, with paths, IP addresses, an IDN, a public suffix, and a URL without host.</summary>
    private static readonly string[] CookieUrls =
    [
        "https://example.com/",
        "http://example.com/dir/page",
        "https://www.example.com/a/b/c?q=1#f",
        "https://xn--lve-6lad.example.com/dir/",
        "http://127.0.0.1:8080/x/y",
        "https://[::1]/",
        "http://localhost/path",
        "https://sub.example.co.uk/",
        "file:///C:/dir/file",
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

        // The serialization of a document is not always parsed as the same tree: the parser fixes up the trees that the markup
        // cannot express (e.g. the elements that it moves out of a table, or the form that a misnested end tag leaves open, so
        // that the next form is nested in it), and fixing up a tree can create another one (the nested form is dropped, which
        // closes its parent elements differently). The round trips reach a fixed point after a few rounds: the serialization of
        // the parsed serialization is the same markup. Except after a plaintext element: its end tag is serialized, but the
        // parser reads the rest of the markup as its text, so each round trip adds the end tags to the text
        // (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments). The parser only creates a
        // plaintext element (in a template too) for a "<plaintext" start tag of the markup.
        var markup = document.ToHtml();
        if (!markup.Contains("<plaintext", StringComparison.OrdinalIgnoreCase))
        {
            var rounds = new List<string> { markup };
            while (true)
            {
                var serialized = HtmlDocumentParser.ParseDocument(rounds[^1]).ToHtml();
                if (string.Equals(serialized, rounds[^1], StringComparison.Ordinal))
                    break;

                rounds.Add(serialized);
                if (rounds.Count > HtmlMaxRoundTrips)
                    FuzzAssert.Fail($"The serialization does not reach a fixed point after {HtmlMaxRoundTrips} round trips:{Environment.NewLine}{string.Join(Environment.NewLine, rounds.Select(FuzzAssert.Format))}");
            }
        }

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

        // The formatters only change the whitespace and the notation of the values: their output is parsed as the same rules.
        // The minifier removes the rules that have no effect, which changes the rules, unless RemoveEmptyRules is false. Some
        // texts are kept as specified (the condition of @supports, the unknown media features, the values with var()), so the
        // minifier changes their serialization: the rules are compared without the whitespace that the minifier may remove
        var formatted = sheet.ToCss(new CssPrettyFormatter());
        var formattedSheet = new CssStyleSheet();
        formattedSheet.ReplaceSync(formatted);
        FuzzAssert.Equal(text, Serialize(formattedSheet.CssRules), $"The output of the pretty formatter is parsed as different rules: {FuzzAssert.Format(formatted)}");

        formatted = sheet.ToCss(new CssMinifyFormatter { RemoveEmptyRules = false });
        formattedSheet = new CssStyleSheet();
        formattedSheet.ReplaceSync(formatted);
        FuzzAssert.Equal(NormalizeWhitespace(text), NormalizeWhitespace(Serialize(formattedSheet.CssRules)), $"The output of the minify formatter is parsed as different rules: {FuzzAssert.Format(formatted)}");

        // The minified output is minified: minifying it again does not change it
        var minify = new CssMinifyFormatter();
        var minified = sheet.ToCss(minify);
        var minifiedSheet = new CssStyleSheet();
        minifiedSheet.ReplaceSync(minified);
        FuzzAssert.Equal(minified, minifiedSheet.ToCss(minify), "Minifying the minified style sheet changes it");

        var element = CreateDocument().CreateElement<HtmlElement>("div");
        element.Style.CssText = input;
        _ = element.Style.CssText;
        for (var i = 0u; i < element.Style.Length; i++)
        {
            var property = element.Style.Item(i);
            _ = element.Style.GetPropertyValue(property);
        }

        _ = CssApi.Supports(input);

        // Collapses the whitespace, and removes it where the minifier may remove it (after punctuation, and before the
        // punctuation other than "(", ":" and "["): the whitespace between two words (identifiers, numbers, strings, the
        // operators of calc()) is significant, and so is the whitespace before a parenthesis (a function), a colon (a
        // pseudo-class) or a bracket (an attribute selector)
        static string NormalizeWhitespace(string text)
        {
            var result = new System.Text.StringBuilder();
            var pendingSpace = false;
            foreach (var c in text)
            {
                if (c is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace && IsWord(result[^1]) && (IsWord(c) || c is '(' or ':' or '['))
                {
                    result.Append(' ');
                }

                pendingSpace = false;
                result.Append(c);
            }

            return result.ToString();

            static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '%' or '#' or '\\' or '"' or '\'' or '+' or '@' or '&' || c > '\x7F';
        }

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

            // The shaper (GSUB, GPOS, GDEF) with the default and the Arabic shapers: a malformed layout table is reported as such
            Shape(font, "office AVAT\u0301a\u00E9 fi\u200D", UnicodeScript.Latin, isRightToLeft: false, isVertical: false, "tr", instance);
            Shape(font, "\u0644\u0627 \u0628\u0628\u064E\u200C\u0628", UnicodeScript.Arabic, isRightToLeft: true, isVertical: false, "ar", instance);
            Shape(font, "\u1820\u180B\u1821\u180E\u1822", UnicodeScript.Mongolian, isRightToLeft: false, isVertical: true, language: null, instance: null);
            Shape(font, "\u4E00\u3001", UnicodeScript.Han, isRightToLeft: false, isVertical: true, "ja", instance: null);

            static void Shape(OpenTypeFont font, string text, UnicodeScript script, bool isRightToLeft, bool isVertical, string? language, FontVariationInstance? instance)
            {
                try
                {
                    _ = font.Shape(text, script, isRightToLeft, isVertical, language, instance);
                }
                catch (Meziantou.Framework.Fonts.FontException)
                {
                    // The documented failure of a malformed font: the text shaping draws the characters without the layout tables
                }
            }
        }
    }

    /// <summary>
    /// Decodes bytes with TextDecoder: in one pass, and in stream mode over chunks, which returns the same text (or fails
    /// for the same input in fatal mode). The input is the label byte (an index in <see cref="TextDecoderLabels"/>, or a
    /// label read from the input up to a NUL byte), the option byte (fatal, ignoreBOM), the two lengths of the chunks
    /// (alternately), then the bytes to decode.
    /// </summary>
    public static void TextDecoding(ReadOnlySpan<byte> data)
    {
        var labelByte = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var chunkLength1 = FuzzInput.ReadByte(ref data) % 8;
        var chunkLength2 = FuzzInput.ReadByte(ref data) % 8;
        if (chunkLength1 == 0 && chunkLength2 == 0)
        {
            chunkLength1 = 1;
        }

        var isKnownLabel = labelByte < TextDecoderLabels.Length;
        var (label, expectedEncoding) = isKnownLabel ? TextDecoderLabels[labelByte] : (System.Text.Encoding.Latin1.GetString(FuzzInput.ReadUntil(ref data, 0)), null);
        var decoderOptions = new TextDecoderOptions { Fatal = (options & 1) != 0, IgnoreBom = (options & 2) != 0 };
        TextDecoder decoder;
        try
        {
            decoder = new TextDecoder(label, decoderOptions);
        }
        catch (RangeErrorException)
        {
            FuzzAssert.False(expectedEncoding is not null, $"The label {FuzzAssert.Format(label)} is not supported");
            return;
        }

        if (isKnownLabel)
        {
            FuzzAssert.Equal(expectedEncoding, decoder.Encoding, $"The encoding of the label {FuzzAssert.Format(label)}");
        }

        FuzzAssert.Equal(decoderOptions.Fatal, decoder.Fatal, "The fatal flag");
        FuzzAssert.Equal(decoderOptions.IgnoreBom, decoder.IgnoreBom, "The ignoreBOM flag");

        var expected = Decode(decoder, data);

        // Stream mode: the decoder keeps its state (an incomplete sequence, the mode of ISO-2022-JP) between the chunks
        var streamDecoder = new TextDecoder(label, decoderOptions);
        var streamed = new System.Text.StringBuilder();
        var stream = new TextDecodeOptions { Stream = true };
        string? actual = null;
        try
        {
            var remaining = data;
            for (var i = 0; !remaining.IsEmpty; i++)
            {
                var length = Math.Min(remaining.Length, i % 2 == 0 ? chunkLength1 : chunkLength2);
                streamed.Append(streamDecoder.Decode(remaining[..length], stream));
                remaining = remaining[length..];
            }

            streamed.Append(streamDecoder.Decode());
            actual = streamed.ToString();
        }
        catch (TypeErrorException) when (decoder.Fatal)
        {
            // Invalid data in fatal mode
        }

        FuzzAssert.Equal(expected, actual, $"The streamed decoding (chunks of {chunkLength1} and {chunkLength2} bytes) differs from the decoding in one pass ({decoder.Encoding})");

        // A decode without the stream option resets the state of a decoder that was used in stream mode
        FuzzAssert.Equal(expected, Decode(streamDecoder, data), "The decoding with a decoder that was used in stream mode");

        if (decoder.Fatal)
        {
            // The fatal mode fails exactly when the replacement mode reports an error (with U+FFFD)
            var replaced = new TextDecoder(label, new TextDecoderOptions { IgnoreBom = decoder.IgnoreBom }).Decode(data);
            if (expected is null)
            {
                FuzzAssert.True(replaced.Contains('\uFFFD', StringComparison.Ordinal), $"The decoding fails in fatal mode, but the replacement mode does not report an error: {FuzzAssert.Format(replaced)}");
            }
            else
            {
                FuzzAssert.Equal(expected, replaced, "The decoding in fatal mode differs from the decoding in replacement mode");
            }
        }

        if (expected is null)
            return;

        // The decoded text is made of scalar values: its UTF-8 encoding (TextEncoder) decodes to the same text
        for (var i = 0; i < expected.Length; i++)
        {
            if (char.IsHighSurrogate(expected[i]) && i + 1 < expected.Length && char.IsLowSurrogate(expected[i + 1]))
            {
                i++;
            }
            else
            {
                FuzzAssert.False(char.IsSurrogate(expected[i]), $"The decoded text contains a lone surrogate at {i}: {FuzzAssert.Format(expected)}");
            }
        }

        var encoder = new TextEncoder();
        var encoded = encoder.Encode(expected);
        FuzzAssert.Equal(expected, new TextDecoder("utf-8", new TextDecoderOptions { Fatal = true, IgnoreBom = true }).Decode(encoded), "The UTF-8 round trip of the decoded text");

        var destination = new byte[encoded.Length / 2];
        var result = encoder.EncodeInto(expected, destination);
        FuzzAssert.True(result.Written <= (ulong)destination.Length && result.Read <= (ulong)expected.Length, $"encodeInto reads {result.Read} units and writes {result.Written} bytes");
        FuzzAssert.True(destination.AsSpan(0, (int)result.Written).SequenceEqual(encoded.AsSpan(0, (int)result.Written)), "encodeInto writes a prefix of the result of encode");
        FuzzAssert.Equal(expected[..(int)result.Read], System.Text.Encoding.UTF8.GetString(encoded, 0, (int)result.Written), "encodeInto writes the encoding of the units it reads");

        static string? Decode(TextDecoder decoder, ReadOnlySpan<byte> data)
        {
            try
            {
                return decoder.Decode(data);
            }
            catch (TypeErrorException) when (decoder.Fatal)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Stores the cookies of Set-Cookie headers (one per line, the first byte picks the URL in <see cref="CookieUrls"/>) and
    /// retrieves them: a cookie that is stored and not expired is returned for the URL when its path matches.
    /// </summary>
    public static void Cookies(ReadOnlySpan<byte> data)
    {
        var url = FuzzInput.Pick(CookieUrls, FuzzInput.ReadByte(ref data));
        var path = DomUrl.Parse(url)!.Pathname;
        var clock = new FixedTimeProvider();
        var now = clock.GetUtcNow();
        var jar = new CookieJar(clock);
        foreach (var header in FuzzInput.ReadText(data).Split('\n').Take(64))
        {
            _ = jar.SetCookie(url, header);

            // Each cookie in a jar of its own too: the cookies of the shared jar replace each other
            var single = new CookieJar(clock);
            var stored = single.SetCookie(url, header);
            var cookies = single.GetCookies();
            FuzzAssert.True(cookies.Count <= (stored ? 1 : 0), $"The jar has {cookies.Count} cookies after the header {FuzzAssert.Format(header)} (stored: {stored})");
            if (cookies.Count == 0)
                continue;

            var cookie = cookies[0];
            FuzzAssert.True(cookie.Expires is null || (cookie.Expires > now && cookie.Expires <= now.AddDays(400)), $"The cookie of the header {FuzzAssert.Format(header)} expires at {cookie.Expires}");
            FuzzAssert.True(cookie.Path.StartsWith('/', StringComparison.Ordinal), $"The path of the cookie is {FuzzAssert.Format(cookie.Path)}");
            var retrieved = single.GetCookies(url);
            if (PathMatches(path, cookie.Path))
            {
                FuzzAssert.True(retrieved.Count == 1 && IsSameCookie(cookie, retrieved[0]), $"The cookie of the header {FuzzAssert.Format(header)} is not returned for {url}");
            }
            else
            {
                FuzzAssert.Equal(0, retrieved.Count, $"The cookie of the header {FuzzAssert.Format(header)} (path {FuzzAssert.Format(cookie.Path)}) is returned for {url}");
            }
        }

        var all = jar.GetCookies();
        foreach (var other in CookieUrls)
        {
            foreach (var cookie in jar.GetCookies(other))
            {
                FuzzAssert.True(all.Any(item => IsSameCookie(item, cookie)), $"The cookie {FuzzAssert.Format(cookie.Name)} returned for {other} is not in the jar");
            }
        }

        static bool IsSameCookie(StoredCookie a, StoredCookie b) => a.Name == b.Name && a.Value == b.Value && a.Domain == b.Domain && a.Path == b.Path && a.HostOnly == b.HostOnly && a.Secure == b.Secure;

        // https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-paths-and-path-match
        static bool PathMatches(string requestPath, string cookiePath)
            => requestPath == cookiePath || (requestPath.StartsWith(cookiePath, StringComparison.Ordinal) && (cookiePath.EndsWith('/', StringComparison.Ordinal) || requestPath[cookiePath.Length] == '/'));
    }

    /// <summary>A clock that does not move, so that the expiry of the cookies does not depend on the time of the run.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static Document CreateDocument() => QueryDocument.Value.Implementation.CreateHtmlDocument("");
}
