using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Extensions.JiraLinks;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Fuzz;

/// <summary>
/// An input is a pipeline (the extensions of one of the specifications), a set of flags, then the Markdown. Markdown is
/// never invalid: the only exception the library documents is the <see cref="ArgumentException"/> of a document nested
/// too deeply. The syntax tree must have spans inside the text, the HTML must not depend on the thresholds above which the
/// inline processor tracks its open containers (the tests lower them to use that path on small documents), raw HTML
/// disabled must produce well-formed tags with the attributes the renderers write, and a document parsed with trivia must
/// be written back exactly as it was parsed.
/// </summary>
public static class MarkdownFuzzTargets
{
    // The extensions of the specifications (tools/Meziantou.Framework.Markdown.Specs.Generator), in the syntax of
    // MarkdownExtensions.Configure. A seed starts with the index of the extensions of its specification.
    private static readonly string[] Extensions =
    [
        "common", "advanced", "alerts", "pipetables", "pipetables+advanced", "gfm-pipetables", "footnotes", "footnotes+advanced", "attributes", "attributes+advanced",
        "emphasisextras", "emphasisextras+advanced", "hardlinebreak", "advanced+hardlinebreak", "gridtables", "gridtables+advanced", "customcontainers+attributes",
        "customcontainers+attributes+advanced", "definitionlists+attributes", "definitionlists+attributes+advanced", "pipetables+emojis", "advanced+emojis", "abbreviations",
        "abbreviations+advanced", "listextras", "listextras+advanced", "mathematics", "mathematics+advanced", "bootstrap+pipetables+figures+attributes+alerts", "medialinks",
        "advanced+medialinks", "pipetables+smartypants", "advanced+smartypants", "autoidentifiers", "autoidentifiers+advanced", "tasklists", "tasklists+advanced", "diagrams",
        "diagrams+advanced", "nohtml", "yaml", "autolinks", "autolinks+advanced", "jiralinks", "globalization+advanced+emojis", "figures+footers+citations",
        "figures+footers+citations+advanced", "cjk-friendly-emphasis",
        "advanced+emojis+smartypants+yaml+autolinks+tasklists+diagrams+bootstrap+globalization+cjk-friendly-emphasis+nofollowlinks",
    ];

    private const byte DisableHtmlFlag = 1;
    private const byte PreciseSourceLocationFlag = 2;

    // A pipeline is immutable once built, so the targets share them (Meziantou.Framework.Fuzzing.Tests runs the targets in parallel)
    private static readonly ConcurrentDictionary<(int Extensions, byte Flags, bool TrackTrivia, bool MinimumThresholds), MarkdownPipeline> Pipelines = new();

    private static readonly string[] Tokens =
    [
        "*", "**", "***", "_", "__", "`", "``", "```", "~~~", "~", "~~", "#", "# ", "###### ", "> ", "- ", "+ ", "* ", "1. ", "1) ", "10. ", "[", "]", "](", "![", "](/url \"title\")",
        "[a]", "[a]: /url", "[a]: /url 'title'", "<", ">", "<div>", "</div>", "<pre>", "<script>", "<!--", "-->", "<?", "?>", "<!DOCTYPE html>", "<![CDATA[", "]]>",
        "<http://a.b>", "<a@b.c>", "\\", "&amp;", "&#35;", "&#x22;", "&nbsp;", "|", "| a | b |", "|---|---|", ":-:", ":--", "--:", "---", "===", "    ", "\t", "\n", "\r\n", "\r",
        "\n\n", "[^1]", "[^1]: ", "{#id .class key=value}", "{.class}", ":::", "::", "$$", "$", "^^^", "^^", "^", "++", "==", "- [ ]", "- [x]", "*[HTML]: ", "a. ", "i. ",
        "+---+---+", "+===+===+", ":smile:", ":)", "\"\"", "--", "...", "<<", ">>", "> [!NOTE]", "```mermaid", "www.", "https://", "mailto:", "ABC-123", "Term\n:   ", "\u00A0",
        "\u3001", "\u05D0",
    ];

    // The tags and attributes that the HTML renderers write (the alert icons are SVG), which is all that the output may contain
    // when raw HTML is disabled, with the attributes that the filter of the generic attributes lets through
    private static readonly HashSet<string> RenderedTags = new([
        "a", "abbr", "audio", "blockquote", "br", "caption", "cite", "code", "col", "colgroup", "dd", "del", "div", "dl", "dt", "em", "figcaption", "figure", "footer", "h1", "h2", "h3",
        "h4", "h5", "h6", "hr", "iframe", "img", "input", "ins", "li", "mark", "ol", "p", "path", "pre", "source", "span", "strong", "sub", "sup", "svg", "table", "tbody", "td",
        "th", "thead", "tr", "ul", "video",
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> RenderedAttributes = new([
        "allowfullscreen", "alt", "checked", "class", "controls", "d", "disabled", "frameborder", "href", "id", "src", "style", "version", "viewBox",
    ], StringComparer.Ordinal);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("markdown-parser", Parser) { Seeds = GetSeeds, Dictionary = Tokens },
        new("markdown-html", Html) { Seeds = GetSeeds, Dictionary = Tokens },
        new("markdown-roundtrip", Roundtrip) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void Parser(ReadOnlySpan<byte> data)
    {
        var (extensions, flags, markdown) = ReadInput(data);
        var pipeline = GetPipeline(extensions, flags, trackTrivia: false, minimumThresholds: false);
        if (!TryParse(markdown, pipeline, out var document))
            return;

        foreach (var item in document.Descendants().Prepend(document))
        {
            var span = item.Span;
            FuzzAssert.True(span.IsEmpty || (span.Start >= 0 && span.End < markdown.Length), $"The span {span} of {Describe(item)} is outside the text ({markdown.Length} characters)");
            FuzzAssert.True(item.Line >= 0 && item.Column >= 0, $"The position {item.Line}:{item.Column} of {Describe(item)} is negative");
        }
    }

    private static void Html(ReadOnlySpan<byte> data)
    {
        var (extensions, flags, markdown) = ReadInput(data);
        if (!TryRender(markdown, GetPipeline(extensions, flags, trackTrivia: false, minimumThresholds: false), out var html))
            return;

        var tracked = TryRender(markdown, GetPipeline(extensions, flags, trackTrivia: false, minimumThresholds: true), out var trackedHtml);
        FuzzAssert.True(tracked, "The document is nested too deeply with the minimum thresholds only");
        FuzzAssert.Equal(html, trackedHtml, "The minimum thresholds of the open containers and openers bottoms change the HTML");

        if ((flags & DisableHtmlFlag) != 0)
        {
            CheckRenderedHtml(html);
        }

        _ = MarkdownConverter.ToPlainText(markdown, GetPipeline(extensions, flags, trackTrivia: false, minimumThresholds: false));
    }

    private static void Roundtrip(ReadOnlySpan<byte> data)
    {
        var (extensions, flags, markdown) = ReadInput(data);
        var pipeline = GetPipeline(extensions, flags, trackTrivia: true, minimumThresholds: false);
        if (!TryParse(markdown, pipeline, out var document))
            return;

        using var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);
        FuzzAssert.Equal(markdown, writer.ToString(), "The document parsed with trivia is not written back as it was parsed");
    }

    private static (int Extensions, byte Flags, string Markdown) ReadInput(ReadOnlySpan<byte> data)
    {
        var extensions = FuzzInput.ReadByte(ref data) % Extensions.Length;
        var flags = (byte)(FuzzInput.ReadByte(ref data) & (DisableHtmlFlag | PreciseSourceLocationFlag));
        return (extensions, flags, FuzzInput.ReadText(data));
    }

    private static MarkdownPipeline GetPipeline(int extensions, byte flags, bool trackTrivia, bool minimumThresholds)
    {
        return Pipelines.GetOrAdd((extensions, flags, trackTrivia, minimumThresholds), static key => CreatePipeline(key.Extensions, key.Flags, key.TrackTrivia, key.MinimumThresholds));
    }

    private static MarkdownPipeline CreatePipeline(int extensions, byte flags, bool trackTrivia, bool minimumThresholds)
    {
        var builder = new MarkdownPipelineBuilder();
        foreach (var extension in Extensions[extensions].Split('+'))
        {
            _ = extension is "jiralinks" ? builder.UseJiraLinks(new JiraLinkOptions("http://your.company.abc")) : builder.Configure(extension);
        }

        if ((flags & DisableHtmlFlag) != 0)
        {
            builder.DisableHtml();
        }

        if ((flags & PreciseSourceLocationFlag) != 0)
        {
            builder.UsePreciseSourceLocation();
        }

        if (trackTrivia)
        {
            builder.EnableTrackTrivia();
        }

        var pipeline = builder.Build();
        if (minimumThresholds)
        {
            // Same as TestParser.UseMinimumThresholds: the chain of open containers and the openers bottoms are used from the
            // first container and delimiter instead of only for long paragraphs
            pipeline.OpenContainersTrackingThreshold = 1;
            foreach (var parser in pipeline.InlineParsers.OfType<EmphasisInlineParser>())
            {
                parser.OpenersBottomThreshold = 0;
            }
        }

        return pipeline;
    }

    private static bool TryParse(string markdown, MarkdownPipeline pipeline, [NotNullWhen(true)] out MarkdownDocument? document)
    {
        try
        {
            document = MarkdownConverter.Parse(markdown, pipeline);
            return true;
        }
        catch (ArgumentException ex) when (IsDepthLimitExceeded(ex))
        {
            document = null;
            return false;
        }
    }

    private static bool TryRender(string markdown, MarkdownPipeline pipeline, [NotNullWhen(true)] out string? html)
    {
        try
        {
            html = MarkdownConverter.ToHtml(markdown, pipeline);
            return true;
        }
        catch (ArgumentException ex) when (IsDepthLimitExceeded(ex))
        {
            html = null;
            return false;
        }
    }

    private static bool IsDepthLimitExceeded(ArgumentException ex) => ex.GetType() == typeof(ArgumentException) && ex.Message.StartsWith("Markdown elements in the input are too deeply nested", StringComparison.Ordinal);

    // With raw HTML disabled, every '<' of the output starts a tag that a renderer writes: <name attr="value" ...> or </name>,
    // the values being quoted without '"' or '<', so that the text of the document cannot inject markup
    private static void CheckRenderedHtml(string html)
    {
        var index = 0;
        while ((index = html.IndexOf('<', index, StringComparison.Ordinal)) >= 0)
        {
            var start = index;
            index++;
            var isEndTag = index < html.Length && html[index] is '/';
            if (isEndTag)
            {
                index++;
            }

            var name = ReadName(html, ref index, isAttribute: false);
            FuzzAssert.True(RenderedTags.Contains(name), $"The HTML contains the tag <{(isEndTag ? "/" : "")}{name}> at {start}, which no renderer writes: {FuzzAssert.Format(Excerpt(html, start))}");
            while (!isEndTag && index < html.Length && html[index] is ' ')
            {
                index++;
                if (index < html.Length && html[index] is '/' or '>')
                    break;

                var attribute = ReadName(html, ref index, isAttribute: true);
                FuzzAssert.True(RenderedAttributes.Contains(attribute) || GenericAttributesExtension.IsSafeAttributeName(attribute), $"The HTML contains the attribute '{attribute}' at {index}, which no renderer writes: {FuzzAssert.Format(Excerpt(html, start))}");
                if (index < html.Length && html[index] is '=')
                {
                    FuzzAssert.True(index + 1 < html.Length && html[index + 1] is '"', $"The value of the attribute '{attribute}' at {index} is not quoted: {FuzzAssert.Format(Excerpt(html, start))}");
                    var end = html.IndexOfAny(['"', '<'], index + 2);
                    FuzzAssert.True(end >= 0 && html[end] is '"', $"The value of the attribute '{attribute}' at {index} is not closed before a '<': {FuzzAssert.Format(Excerpt(html, start))}");
                    index = end + 1;
                }
            }

            if (!isEndTag && index + 1 < html.Length && html[index] is '/' && html[index + 1] is '>')
            {
                index++;
            }

            FuzzAssert.True(index < html.Length && html[index] is '>', $"The tag at {start} is not closed: {FuzzAssert.Format(Excerpt(html, start))}");
            index++;
        }

        // The generic attributes extension parses attribute names with '_', ':' and '.', which HTML allows in attribute names
        static string ReadName(string html, ref int index, bool isAttribute)
        {
            var start = index;
            while (index < html.Length && (char.IsAsciiLetterOrDigit(html[index]) || html[index] is '-' || (isAttribute && html[index] is '_' or ':' or '.')))
            {
                index++;
            }

            return html[start..index];
        }

        static string Excerpt(string html, int start) => html.Substring(start, Math.Min(200, html.Length - start));
    }

    private static string Describe(MarkdownObject item) => item is LiteralInline literal ? $"the literal {FuzzAssert.Format(literal.ToString())}" : item.GetType().Name;

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        // The specifications that are not rendered with their own extensions in the tests are fuzzed with the closest ones
        foreach (var (resource, extensions) in new (string Resource, string[] Extensions)[]
        {
            ("CommonMark", ["common", "advanced"]),
            ("AlertBlockSpecs", ["advanced"]),
            ("PipeTableSpecs", ["pipetables", "pipetables+advanced"]),
            ("PipeTableGfmSpecs", ["gfm-pipetables"]),
            ("GfmTableSpecs", ["gfm-pipetables"]),
            ("FootnotesSpecs", ["footnotes", "footnotes+advanced"]),
            ("GenericAttributesSpecs", ["attributes", "attributes+advanced"]),
            ("EmphasisExtraSpecs", ["emphasisextras", "emphasisextras+advanced"]),
            ("HardlineBreakSpecs", ["hardlinebreak", "advanced+hardlinebreak"]),
            ("GridTableSpecs", ["gridtables", "gridtables+advanced"]),
            ("CustomContainerSpecs", ["customcontainers+attributes", "customcontainers+attributes+advanced"]),
            ("DefinitionListSpecs", ["definitionlists+attributes", "definitionlists+attributes+advanced"]),
            ("EmojiSpecs", ["pipetables+emojis", "advanced+emojis"]),
            ("AbbreviationSpecs", ["abbreviations", "abbreviations+advanced"]),
            ("ListExtraSpecs", ["listextras", "listextras+advanced"]),
            ("MathSpecs", ["mathematics", "mathematics+advanced"]),
            ("BootstrapSpecs", ["bootstrap+pipetables+figures+attributes+alerts"]),
            ("MediaSpecs", ["medialinks", "advanced+medialinks"]),
            ("SmartyPantsSpecs", ["pipetables+smartypants", "advanced+smartypants"]),
            ("AutoIdentifierSpecs", ["autoidentifiers", "autoidentifiers+advanced"]),
            ("TaskListSpecs", ["tasklists", "tasklists+advanced"]),
            ("DiagramsSpecs", ["diagrams", "diagrams+advanced"]),
            ("NoHtmlSpecs", ["nohtml"]),
            ("YamlSpecs", ["yaml"]),
            ("AutoLinks", ["autolinks", "autolinks+advanced"]),
            ("JiraLinks", ["jiralinks"]),
            ("GlobalizationSpecs", ["globalization+advanced+emojis"]),
            ("FigureFooterAndCiteSpecs", ["figures+footers+citations", "figures+footers+citations+advanced"]),
            ("CJKFriendlyEmphasis", ["cjk-friendly-emphasis"]),
        })
        {
            var examples = ReadExamples(resource);
            foreach (var name in extensions)
            {
                var index = Array.IndexOf(Extensions, name);
                if (index < 0)
                    throw new InvalidOperationException($"The extensions '{name}' of {resource} are not in the list");

                for (var i = 0; i < examples.Count; i++)
                {
                    yield return FuzzSeed.FromText($"{resource}-{i + 1}-{name}", [(byte)index, 0], examples[i]);
                }
            }

            // Raw HTML disabled and precise source locations, with every extension
            if (resource is "CommonMark")
            {
                var all = (byte)(Extensions.Length - 1);
                for (var i = 0; i < examples.Count; i++)
                {
                    yield return FuzzSeed.FromText($"{resource}-{i + 1}-nohtml-precise", [all, DisableHtmlFlag | PreciseSourceLocationFlag], examples[i]);
                }
            }
        }
    }

    // The examples of a specification: the lines between a "```...``` example" line and a "." line, with '→' for tabs and
    // without the last line ending, as in the generated tests
    private static List<string> ReadExamples(string resource)
    {
        var lines = FuzzResources.ReadText(typeof(MarkdownFuzzTargets).Assembly, $"Specs.{resource}.md").ReplaceLineEndings("\n").Split('\n');
        var examples = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i] is not "```````````````````````````````` example")
                continue;

            var end = Array.IndexOf(lines, ".", i + 1);
            examples.Add(string.Join('\n', lines[(i + 1)..end]).Replace('→', '\t'));
            i = end;
        }

        if (examples.Count is 0)
            throw new InvalidOperationException($"The specification {resource} has no example");

        return examples;
    }
}
