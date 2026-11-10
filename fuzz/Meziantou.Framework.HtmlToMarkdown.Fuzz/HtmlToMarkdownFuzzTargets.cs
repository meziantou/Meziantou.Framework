using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.HtmlToMarkdownFuzz;

/// <summary>
/// Converts HTML with options read from the first bytes of the input. Besides not throwing, the options that only change
/// how some constructs are written must agree with the default code path when the input has none of these constructs:
/// the CSS white-space handling without a white-space declaration, the span handling without a colspan or rowspan
/// attribute. The raw HTML of a complex table must be the only difference between the two table handlings.
/// </summary>
public static class HtmlToMarkdownFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "<sub>", "</sub>", "<sup>", "</sup>", "<u>", "</u>", "<kbd>", "</kbd>",
        "<video", "</video>", "<audio", "</audio>", "<iframe", "</iframe>", "<source", " src=", " title=", " controls",
        " style=", "white-space:", "white-space-collapse:", "pre", "pre-wrap", "pre-line", "break-spaces", "nowrap",
        "preserve", "preserve-breaks", "preserve-spaces", "collapse", "!important", "inherit",
        "<table>", "</table>", "<thead>", "<tbody>", "<tfoot>", "<tr>", "<th>", "<td>", " colspan=", " rowspan=", " align=",
        "<p>", "<div>", "<ul>", "<ol>", "<li>", "<pre>", "<code>", "<br>", "<blockquote>", "<h1>", "<h2>", "<hr>", "<dl>",
        "<a href=", "<img", "<strong>", "<em>", "<del>", "<script>", "<span>", "<input type=checkbox checked>",
        "&nbsp;", "&amp;", "\t", "\n", "~", "^", "++", "`", "\\", "*", "_", "#", "1.",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("html-to-markdown", Convert) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void Convert(ReadOnlySpan<byte> data)
    {
        var options = ReadOptions(ref data);
        var html = FuzzInput.ReadText(data);

        var markdown = HtmlToMarkdown.Convert(html, options);
        FuzzAssert.False(markdown.Length > 0 && (markdown[0] is '\n' or '\r' || markdown[^1] is '\n' or '\r'), "The Markdown starts or ends with a line break");

        if (!html.Contains("white-space", StringComparison.OrdinalIgnoreCase))
        {
            var other = HtmlToMarkdown.Convert(html, CopyOptions(options, o => o.UseCssWhiteSpace = !options.UseCssWhiteSpace));
            FuzzAssert.Equal(markdown, other, "UseCssWhiteSpace changes the Markdown of an input without white-space declaration");
        }

        if (!html.Contains("span", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var spanHandling in Enum.GetValues<TableSpanHandling>())
            {
                var other = HtmlToMarkdown.Convert(html, CopyOptions(options, o => o.TableSpanHandling = spanHandling));
                FuzzAssert.Equal(markdown, other, $"TableSpanHandling.{spanHandling} changes the Markdown of an input without span");
            }
        }

        var flatten = options.ComplexTableHandling is ComplexTableHandling.Flatten
            ? markdown
            : HtmlToMarkdown.Convert(html, CopyOptions(options, o => o.ComplexTableHandling = ComplexTableHandling.Flatten));
        var raw = options.ComplexTableHandling is ComplexTableHandling.Html
            ? markdown
            : HtmlToMarkdown.Convert(html, CopyOptions(options, o => o.ComplexTableHandling = ComplexTableHandling.Html));
        if (flatten != raw)
        {
            FuzzAssert.True(raw.Contains("<table", StringComparison.Ordinal), "ComplexTableHandling.Html changes the Markdown without writing a table as HTML");
        }
    }

    private static HtmlToMarkdownOptions ReadOptions(ref ReadOnlySpan<byte> data)
    {
        var b0 = FuzzInput.ReadByte(ref data);
        var b1 = FuzzInput.ReadByte(ref data);
        var b2 = FuzzInput.ReadByte(ref data);
        return new HtmlToMarkdownOptions
        {
            SubscriptStyle = (SubscriptStyle)(b0 & 3),
            SuperscriptStyle = (SuperscriptStyle)((b0 >> 2) & 3),
            UnderlineStyle = (UnderlineStyle)((b0 >> 4) & 3),
            KeyboardStyle = (KeyboardStyle)((b0 >> 6) & 3),
            EmbeddedContentStyle = (EmbeddedContentStyle)(b1 & 3),
            TableSpanHandling = (TableSpanHandling)(((b1 >> 2) & 3) % 3),
            ComplexTableHandling = (ComplexTableHandling)((b1 >> 4) & 1),
            UseCssWhiteSpace = ((b1 >> 5) & 1) == 0,
            UnknownElementHandling = (UnknownElementHandling)(((b1 >> 6) & 3) % 3),
            LineBreakStyle = (LineBreakStyle)(b2 & 1),
            EmphasisMarker = (EmphasisMarker)((b2 >> 1) & 1),
            HeadingStyle = (HeadingStyle)((b2 >> 2) & 1),
            CodeBlockStyle = (CodeBlockStyle)((b2 >> 3) & 1),
            UseSimplePunctuation = ((b2 >> 4) & 1) == 1,
            EmojiShortcodeMode = (EmojiShortcodeMode)(((b2 >> 5) & 3) % 3),
        };
    }

    private static HtmlToMarkdownOptions CopyOptions(HtmlToMarkdownOptions options, Action<HtmlToMarkdownOptions> configure)
    {
        var copy = new HtmlToMarkdownOptions
        {
            EmphasisMarker = options.EmphasisMarker,
            HeadingStyle = options.HeadingStyle,
            CodeBlockStyle = options.CodeBlockStyle,
            CodeBlockFenceCharacter = options.CodeBlockFenceCharacter,
            UnorderedListMarker = options.UnorderedListMarker,
            ThematicBreak = options.ThematicBreak,
            LineBreakStyle = options.LineBreakStyle,
            UseSimplePunctuation = options.UseSimplePunctuation,
            EmojiShortcodeMode = options.EmojiShortcodeMode,
            UnknownElementHandling = options.UnknownElementHandling,
            SubscriptStyle = options.SubscriptStyle,
            SuperscriptStyle = options.SuperscriptStyle,
            UnderlineStyle = options.UnderlineStyle,
            KeyboardStyle = options.KeyboardStyle,
            EmbeddedContentStyle = options.EmbeddedContentStyle,
            UseCssWhiteSpace = options.UseCssWhiteSpace,
            TableSpanHandling = options.TableSpanHandling,
            ComplexTableHandling = options.ComplexTableHandling,
        };
        configure(copy);
        return copy;
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        // Each input gets one of these option sets in turn: the defaults, and the options of the new features
        byte[][] prefixes =
        [
            [0, 0, 0],
            [0b10_10_10_10, 0b00_0_1_10_10, 0],
            [0b11_01_01_01, 0b01_0_0_01_01, 0b0_01_1_0_1_1_1],
            [0b01_11_11_11, 0b10_1_1_00_11, 0b1_00_0_1_0_0_0],
        ];

        var index = 0;
        foreach (var html in GetSeedInputs())
        {
            yield return FuzzSeed.FromText($"seed-{index:D4}", prefixes[index % prefixes.Length], html);
            index++;
        }
    }

    private static IEnumerable<string> GetSeedInputs()
    {
        // The inputs of the unit tests of the new features
        yield return "<p>H<sub>2</sub>O, E = mc<sup>2</sup>, <u>underlined</u>, press <kbd>Ctrl</kbd>+<kbd>C</kbd></p>";
        yield return "<p>a<sub>b<sub>c</sub></sub> <sub><code>b c</code></sub> <sub><a href=\"/x\">b c</a></sub> ~a~ 2^10 C++</p>";
        yield return "<p><kbd><kbd>Ctrl</kbd>+<kbd>`</kbd></kbd></p>";
        yield return "<p>Demo: <video controls title=\"Product demo\"><source src=\"demo.mp4\" type=\"video/mp4\"></video></p><iframe src=\"https://example.com/map\"></iframe>";
        yield return "<audio><source><source src=\"b.ogg\"></audio><video title=\"[x]\" src=\"a b.mp4\"></video><video>\n\n<source src=\"a.mp4\">\n  \n</video>";
        yield return "<p style=\"white-space: pre\">Name:  Alice\n  Role: admin\tx</p>";
        yield return "<div style=\"white-space: pre-line\">\n  <p>a\n# b\n1. c</p>\n  <p style=\"white-space-collapse: preserve-spaces\">a  b\nc</p>\n</div>";
        yield return "<table><tr><td style=\"white-space: pre\">a\nb</td></tr></table><h2 style=\"white-space: pre\">a\nb</h2>";
        yield return "<table><tr><th>Fruit</th><th colspan=\"2\" align=\"right\">Price</th></tr><tr><td rowspan=\"2\">Apple</td><td>$1.20</td><td>each</td></tr><tr><td>$10</td><td>per box</td></tr></table>";
        yield return "<table><thead><tr><th rowspan=\"0\">A</th><th>B</th></tr></thead><tbody><tr><td rowspan=\"0\">x</td><td colspan=\" +2px\">1</td></tr><tr><td>2</td></tr></tbody><tfoot><tr><td>f</td></tr></tfoot></table>";
        yield return "<table><tr><td><pre>a\n\nb</pre><script>alert(1)</script></td><td><p>c</p><p>d</p></td></tr><tr><td><ul><li>a</li></ul></td></tr></table>";
        yield return "<p style=\"white-space: pre !important; white-space: normal\"><span>a</span>\tb\n\nc</p><mark title=\"a\n\nb\"><!--a\n\nsecret\n-->x</mark><x-a><pre>a\n<script>x</script>\nb</pre><textarea>a\n\nb</textarea></x-a>";
        yield return "<table>" + string.Concat(Enumerable.Repeat("<tr><td colspan=\"1000\" rowspan=\"0\">x</td></tr>", 20)) + "</table>";

        var assembly = typeof(HtmlToMarkdownFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Specs.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            foreach (var html in GetExampleHtml(FuzzResources.ReadText(assembly, name)))
            {
                yield return html;
            }
        }
    }

    // The examples of the specifications: the Markdown, a line with ".", then the HTML, between fences ending with "example"
    private static IEnumerable<string> GetExampleHtml(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("````", StringComparison.Ordinal) || !lines[i].EndsWith("example", StringComparison.Ordinal))
                continue;

            var html = new List<string>();
            var inHtml = false;
            for (i++; i < lines.Length && !lines[i].StartsWith("````", StringComparison.Ordinal); i++)
            {
                if (!inHtml && lines[i] == ".")
                {
                    inHtml = true;
                }
                else if (inHtml)
                {
                    html.Add(lines[i]);
                }
            }

            if (html.Count > 0)
                yield return string.Join('\n', html).Replace('→', '\t');
        }
    }
}
