using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Markdown.Extensions.AutoIdentifiers;
using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Extensions.Globalization;
using Meziantou.Framework.Markdown.Extensions.JiraLinks;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Renderers.Normalize;
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
/// be written back exactly as it was parsed, and the attributes of its blocks and inlines changed after the parsing must be read back
/// from the Markdown it is written to. The normalized document must be normalized to itself and rendered as the
/// original one, and the heading identifiers must only contain the characters they allow.
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

    // The extensions that have a normalize renderer: the others write no Markdown for their syntax. An input of
    // markdown-normalize is an index in this list, the flags below, a bullet character, then the Markdown.
    private static readonly string[] NormalizeExtensions =
    [
        "common", "pipetables", "gfm-pipetables", "tasklists", "autolinks", "jiralinks", "pipetables+tasklists+autolinks+jiralinks",
    ];

    private static readonly char?[] ListItemCharacters = [null, '-', '+', '*'];

    private const byte SpaceAfterQuoteBlockFlag = 1;
    private const byte EmptyLineAfterCodeBlockFlag = 2;
    private const byte EmptyLineAfterHeadingFlag = 4;
    private const byte EmptyLineAfterThematicBreakFlag = 8;
    private const byte ExpandAutoLinksFlag = 16;
    private const byte NormalizeDisableHtmlFlag = 32;
    private const byte NormalizeTrackTriviaFlag = 64;
    private const byte NormalizePreciseSourceLocationFlag = 128;
    private const byte DefaultNormalizeOptions = SpaceAfterQuoteBlockFlag | EmptyLineAfterCodeBlockFlag | EmptyLineAfterHeadingFlag | EmptyLineAfterThematicBreakFlag | ExpandAutoLinksFlag;

    private static readonly ConcurrentDictionary<(int Extensions, byte Flags), MarkdownPipeline> NormalizePipelines = new();

    private const byte DisableHtmlFlag = 1;
    private const byte PreciseSourceLocationFlag = 2;

    // A pipeline is immutable once built, so the targets share them (Meziantou.Framework.Fuzzing.Tests runs the targets in parallel)
    private static readonly ConcurrentDictionary<(int Extensions, byte Flags, bool TrackTrivia, bool MinimumThresholds, bool GenericAttributes), MarkdownPipeline> Pipelines = new();

    // The values that the edits of markdown-roundtrip-edit give to the attributes of a heading. An identifier or a class cannot
    // have a whitespace or '}', and the names are the ones the default filter of the generic attributes keeps.
    private static readonly string[] EditIdentifiers =
    [
        "x", "a-b", "a.b", "_", "1", "#", ".", "{", "a{b", "=", "'", "\"", "\\", "*", "`", "<b>", "&amp;", "\u00E9", "[x]", "x#", "{#x", "b=",
    ];

    private static readonly string[] EditPropertyNames = ["title", "lang", "role", "aria-label", "hidden"];

    private static readonly string?[] EditPropertyValues =
    [
        null, "v", "", "a b", "}", "{", "{#x}", "a\"b", "a'b c", "'", "\"", "\\", "<", "&amp;", "*a*", "` `", "\u00E9", "#", "=",
    ];

    private const int EditKindCount = 7;

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
        new("markdown-roundtrip-edit", RoundtripEdit) { Seeds = GetEditSeeds, Dictionary = Tokens },
        new("markdown-normalize", Normalize) { Seeds = GetNormalizeSeeds, Dictionary = Tokens },
        new("markdown-helpers", Helpers) { Seeds = GetHelperSeeds, Dictionary = Tokens },
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

        FuzzAssert.Equal(markdown, WriteRoundtrip(document, pipeline), "The document parsed with trivia is not written back as it was parsed");
    }

    // An input is the extensions and the flags of the other targets, the number of edits, three bytes for each edit (the index of
    // the block or the inline, the kind of edit and its value), then the Markdown. The generic attributes are always enabled.
    private static void RoundtripEdit(ReadOnlySpan<byte> data)
    {
        var extensions = FuzzInput.ReadByte(ref data) % Extensions.Length;
        var flags = (byte)(FuzzInput.ReadByte(ref data) & (DisableHtmlFlag | PreciseSourceLocationFlag));
        var edits = new (byte Object, byte Kind, byte Value)[1 + (FuzzInput.ReadByte(ref data) % 4)];
        for (var i = 0; i < edits.Length; i++)
        {
            edits[i] = (FuzzInput.ReadByte(ref data), FuzzInput.ReadByte(ref data), FuzzInput.ReadByte(ref data));
        }

        var markdown = FuzzInput.ReadText(data);
        var pipeline = GetPipeline(extensions, flags, trackTrivia: false, minimumThresholds: false, genericAttributes: true);
        var triviaPipeline = GetPipeline(extensions, flags, trackTrivia: true, minimumThresholds: false, genericAttributes: true);
        if (!TryParse(markdown, triviaPipeline, out var triviaDocument) || !TryParse(markdown, pipeline, out var expectedDocument))
            return;

        // A document that is not written back as it was parsed fails markdown-roundtrip, and the trivia can change the blocks and
        // the inlines, which the edits are applied to in both documents
        var triviaObjects = GetEditableObjects(triviaDocument);
        var expectedObjects = GetEditableObjects(expectedDocument);
        if (triviaObjects.Count == 0 || !triviaObjects.Select(obj => obj.GetType()).SequenceEqual(expectedObjects.Select(obj => obj.GetType())) || WriteRoundtrip(triviaDocument, triviaPipeline) != markdown)
            return;

        // The trivia can change the objects that the attributes of the text are attached to. A grid table is written as it is in
        // the source.
        for (var i = 0; i < triviaObjects.Count; i++)
        {
            if (triviaObjects[i] is Table { SourceLines: not null } || !HaveSameAttributes(triviaObjects[i].TryGetAttributes(), expectedObjects[i].TryGetAttributes()))
                return;
        }

        // The attributes before the edits, which the parsing adds again when they are not written in the text
        var parsedAttributes = expectedObjects.Select(obj => CopyAttributes(obj.TryGetAttributes())).ToList();
        var hasAutoIdentifiers = pipeline.Extensions.Any(extension => extension is AutoIdentifierExtension);
        var editedIdentifiers = new HashSet<int>();
        foreach (var (index, kind, value) in edits)
        {
            var i = index % triviaObjects.Count;
            var keepIdentifier = hasAutoIdentifiers && triviaObjects[i] is HeadingBlock;
            _ = ApplyEdit(triviaObjects[i].GetAttributes(), kind, value, keepIdentifier);
            if (ApplyEdit(expectedObjects[i].GetAttributes(), kind, value, keepIdentifier))
            {
                editedIdentifiers.Add(i);
            }
        }

        string edited;
        try
        {
            edited = WriteRoundtrip(triviaDocument, triviaPipeline);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("The attributes of the ", StringComparison.Ordinal))
        {
            // The renderer documents the attributes that it cannot write
            return;
        }

        FuzzAssert.True(TryParse(edited, pipeline, out var actualDocument), $"The edited document is nested too deeply: {FuzzAssert.Format(edited)}");
        var actualObjects = GetEditableObjects(actualDocument);
        FuzzAssert.Equal(expectedObjects.Count, actualObjects.Count, $"The edited document has a different number of blocks and inlines: {FuzzAssert.Format(edited)}");

        // The attributes that the parsing adds by itself are added again, even when the edits removed them. An identifier generated
        // from the content depends on the identifiers of the other headings, and the direction of the text is computed again.
        var hasGlobalization = pipeline.Extensions.Any(extension => extension is GlobalizationExtension);
        for (var i = 0; i < expectedObjects.Count; i++)
        {
            var expected = expectedObjects[i];
            var actual = actualObjects[i];
            FuzzAssert.Equal(expected.GetType(), actual.GetType(), $"The edited document has a {actual.GetType().Name} in place of a {expected.GetType().Name}: {FuzzAssert.Format(edited)}");
            var compareIdentifier = !(hasAutoIdentifiers && expected is HeadingBlock) || editedIdentifiers.Contains(i);
            var implicitAttributes = GetAttributesAddedByParsing(parsedAttributes[i], AttributesTrivia.Get(triviaObjects[i])?.Source);
            CompareAttributes(expected, actual, implicitAttributes, compareIdentifier, hasGlobalization, edited);
        }

        // The rest of the document is the same
        foreach (var obj in expectedDocument.Descendants().Concat(actualDocument.Descendants()))
        {
            obj.SetAttributes(new HtmlAttributes());
        }

        FuzzAssert.Equal(RenderHtml(expectedDocument, pipeline), RenderHtml(actualDocument, pipeline), $"The edited document has another content: {FuzzAssert.Format(edited)}");
    }

    // The blocks and the inlines whose attributes the edits change, which the written document has in the same order. The text of
    // the literals depends on the attributes written in it.
    private static List<MarkdownObject> GetEditableObjects(MarkdownDocument document)
    {
        return document.Descendants().Where(obj => obj is not (GenericAttributesInline or LiteralInline or LineBreakInline or DelimiterInline or ContainerInline { Parent: null, ParentBlock: not null })).ToList();
    }

    private static bool HaveSameAttributes(HtmlAttributes? left, HtmlAttributes? right)
    {
        return left?.Id == right?.Id &&
            (left?.Classes ?? []).SequenceEqual(right?.Classes ?? [], StringComparer.Ordinal) &&
            (left?.Properties ?? []).SequenceEqual(right?.Properties ?? []);
    }

    private static HtmlAttributes CopyAttributes(HtmlAttributes? attributes)
    {
        return new HtmlAttributes
        {
            Id = attributes?.Id,
            Classes = attributes?.Classes is { } classes ? [.. classes] : null,
            Properties = attributes?.Properties is { } properties ? [.. properties] : null,
        };
    }

    // The attributes that the parsing added without them being written in the text
    private static HtmlAttributes GetAttributesAddedByParsing(HtmlAttributes parsed, HtmlAttributes? source)
    {
        return new HtmlAttributes
        {
            Id = parsed.Id is { } id && id != source?.Id ? id : null,
            Classes = parsed.Classes?.Where(cssClass => source?.Classes?.Contains(cssClass) is not true).ToList(),
            Properties = parsed.Properties?.Where(property => source?.Properties?.Contains(property) is not true).ToList(),
        };
    }

    private static void CompareAttributes(MarkdownObject expected, MarkdownObject actual, HtmlAttributes implicitAttributes, bool compareIdentifier, bool hasGlobalization, string edited)
    {
        var expectedAttributes = expected.TryGetAttributes() ?? new HtmlAttributes();
        var actualAttributes = actual.TryGetAttributes() ?? new HtmlAttributes();
        var description = $"{expected.GetType().Name} {FuzzAssert.Format(Describe(expected))}";
        if (compareIdentifier && expectedAttributes.Id != actualAttributes.Id && !(expectedAttributes.Id is null && actualAttributes.Id == implicitAttributes.Id))
        {
            FuzzAssert.Fail($"The identifier of the {description} is {FuzzAssert.Format(actualAttributes.Id)} instead of {FuzzAssert.Format(expectedAttributes.Id)}: {FuzzAssert.Format(edited)}");
        }

        var expectedClasses = expectedAttributes.Classes ?? [];
        var actualClasses = actualAttributes.Classes ?? [];
        foreach (var cssClass in expectedClasses.Except(actualClasses, StringComparer.Ordinal))
        {
            FuzzAssert.Fail($"The {description} does not have the class {FuzzAssert.Format(cssClass)}: {FuzzAssert.Format(edited)}");
        }

        foreach (var cssClass in actualClasses.Except(expectedClasses, StringComparer.Ordinal))
        {
            FuzzAssert.True(implicitAttributes.Classes?.Contains(cssClass) is true, $"The {description} has the class {FuzzAssert.Format(cssClass)}: {FuzzAssert.Format(edited)}");
        }

        var expectedProperties = (expectedAttributes.Properties ?? []).Where(property => !(hasGlobalization && property.Key is "dir")).ToList();
        var actualProperties = (actualAttributes.Properties ?? []).Where(property => !(hasGlobalization && property.Key is "dir")).ToList();
        foreach (var property in expectedProperties.Except(actualProperties))
        {
            FuzzAssert.Fail($"The {description} does not have the attribute {FuzzAssert.Format(property.Key)}={FuzzAssert.Format(property.Value)}: {FuzzAssert.Format(edited)}");
        }

        foreach (var property in actualProperties.Except(expectedProperties))
        {
            FuzzAssert.True(implicitAttributes.Properties?.Contains(property) is true, $"The {description} has the attribute {FuzzAssert.Format(property.Key)}={FuzzAssert.Format(property.Value)}: {FuzzAssert.Format(edited)}");
        }
    }

    // Returns whether the edit sets the identifier
    private static bool ApplyEdit(HtmlAttributes attributes, byte kind, byte value, bool keepIdentifier)
    {
        switch (kind % EditKindCount)
        {
            case 0:
                attributes.Id = FuzzInput.Pick(EditIdentifiers, value);
                return true;

            // A heading without an identifier gets one generated from its content again when it is parsed
            case 1 when !keepIdentifier:
                attributes.Id = null;
                return true;

            case 1:
                attributes.Id = FuzzInput.Pick(EditIdentifiers, value);
                return true;

            case 2:
                attributes.AddClass(FuzzInput.Pick(EditIdentifiers, value));
                return false;

            case 3:
                attributes.Classes = null;
                return false;

            case 4:
                attributes.AddPropertyIfNotExist(FuzzInput.Pick(EditPropertyNames, value), FuzzInput.Pick(EditPropertyValues, (byte)(value / EditPropertyNames.Length)));
                return false;

            case 5:
                attributes.Properties = null;
                return false;

            default:
                attributes.Classes = null;
                attributes.Properties = null;
                if (keepIdentifier)
                    return false;

                attributes.Id = null;
                return true;
        }
    }

    private static string WriteRoundtrip(MarkdownDocument document, MarkdownPipeline pipeline)
    {
        using var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);
        return writer.ToString();
    }

    private static string RenderHtml(MarkdownDocument document, MarkdownPipeline pipeline)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document);
        return writer.ToString();
    }

    private static void Normalize(ReadOnlySpan<byte> data)
    {
        var extensions = FuzzInput.ReadByte(ref data) % NormalizeExtensions.Length;
        var flags = FuzzInput.ReadByte(ref data);
        var options = new NormalizeOptions
        {
            SpaceAfterQuoteBlock = (flags & SpaceAfterQuoteBlockFlag) != 0,
            EmptyLineAfterCodeBlock = (flags & EmptyLineAfterCodeBlockFlag) != 0,
            EmptyLineAfterHeading = (flags & EmptyLineAfterHeadingFlag) != 0,
            EmptyLineAfterThematicBreak = (flags & EmptyLineAfterThematicBreakFlag) != 0,
            ExpandAutoLinks = (flags & ExpandAutoLinksFlag) != 0,
            ListItemCharacter = FuzzInput.Pick(ListItemCharacters, FuzzInput.ReadByte(ref data)),
        };
        var markdown = FuzzInput.ReadText(data);

        var pipeline = GetNormalizePipeline(extensions, flags);
        if (!TryNormalize(markdown, options, pipeline, out var normalized))
            return;

        // With trivia, the parser keeps the whitespace after a list marker or before a continuation line in the literals,
        // which the renderer writes after its own separators: the trivia is for the roundtrip renderer, so only the
        // exceptions are checked
        if ((flags & NormalizeTrackTriviaFlag) != 0)
            return;

        // A vertical tab or a form feed ends a paragraph line as trailing whitespace, but prevents the line from being an HTML
        // block or a setext underline: the normalized line, without it, can start another block, and cannot be escaped when it
        // starts with raw HTML
        if (markdown.AsSpan().ContainsAny('\v', '\f'))
            return;

        var hasDocument = TryParse(markdown, pipeline, out var document);

        // A tab in the indentation of a line in a container is expanded to the next tab stop from its column in the source,
        // which the normalized markers of the container change, while the HTML and code blocks keep the tab
        if (hasDocument && markdown.Contains('\t', StringComparison.Ordinal) && document!.Descendants<ContainerBlock>().Any(container => container is QuoteBlock or ListItemBlock && container.Descendants<LeafBlock>().Any(block => block is HtmlBlock or CodeBlock)))
            return;
        if (hasDocument && HasLiteralOpeningBracket(document!))
            return;

        // The autolinks parser scans the text of a URL up to the next whitespace, so the text of a link or a code span right after
        // a URL, such as its destination, is part of the URL or not depending on how it is written
        if (hasDocument && NormalizeExtensions[extensions].Contains("autolinks", StringComparison.Ordinal) && HasLinkAfterText(document!))
            return;

        // The escape parser takes the line ending after a backslash for a hard line break before the inline parser of the pipe
        // tables sees it, so a row that ends with a backslash is not ended: the lines after it are merged or lost. And the
        // emphasis delimiters next to a pipe are not resolved as in the cell written with spaces around its content, and raw HTML
        // or a code span that spans cells depends on how the cells are written.
        // A pipe next to a link can make a table of the normalized paragraph, as the link is written differently. The block parser
        // of these tables takes a line of dashes, pipes, colons and spaces (but not tabs) after a paragraph for a delimiter row,
        // so the spaces that replace the tab after a list marker can make a list item text.
        var hasInlineTables = NormalizeExtensions[extensions].Split('+').Contains("pipetables", StringComparer.Ordinal);
        if (hasDocument && hasInlineTables && ((markdown.Contains("\\\n", StringComparison.Ordinal) || markdown.Contains("\\\r", StringComparison.Ordinal) || HasDelimiterNextToPipe(markdown, anyText: false) || markdown.AsSpan().ContainsAny('<', '`')) && document!.Descendants<Table>().Any(table => table.Parser is null)
            || HasDelimiterNextToPipe(markdown, anyText: NormalizeExtensions[extensions].Contains("links", StringComparison.Ordinal))
            || (markdown.AsSpan().ContainsAny('\t', '`', '<') && markdown.Contains('|', StringComparison.Ordinal))))
            return;

        if (hasDocument && (HasDefinitionBetweenBlocks(document!) || HasInlineAcrossRows(markdown, document!)))
            return;

        // The indented first line of an HTML block after a list would continue its last item when the content of the item cannot
        // be indented further, as when it starts with an indented code block: the HTML block keeps its indentation
        if (hasDocument && document!.Descendants<ContainerBlock>().Prepend(document!).Any(container => container.Zip(container.Skip(1)).Any(pair => pair is (ListBlock, HtmlBlock { Lines.Count: > 0 } html) && html.Lines.Lines[0].Slice.AsSpan() is [' ' or '\t', ..])))
            return;

        // A link reference definition is parsed before the code spans, so a link at the start of a paragraph whose text has a
        // code span with a ']' can be a definition once its label or destination is written differently
        if (hasDocument && document!.Descendants<LeafBlock>().Any(block => block.Inline?.FirstChild is LinkInline link && link.Descendants<CodeInline>().Any(code => code.Content.Contains(']', StringComparison.Ordinal))))
            return;

        // The labels are compared with the invariant culture, which ignores the control characters and the combining marks, so
        // an empty label written for a link matches the definition of such a label
        if (hasDocument && document!.Descendants<LinkReferenceDefinition>().Any(definition => definition.Label is { } label
            && label.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.EnclosingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.Surrogate or System.Globalization.UnicodeCategory.PrivateUse or System.Globalization.UnicodeCategory.OtherNotAssigned)))
            return;

        // The parser does not count the blank line that ends a quote whose fenced code block is not closed, so the list can be
        // tight with two quotes in an item, which only a blank line can separate once normalized
        if (hasDocument && document!.Descendants<ListItemBlock>().Any(item => item.Parent is ListBlock { IsLoose: false } && item.Zip(item.Skip(1)).Any(pair => pair is (QuoteBlock, QuoteBlock))))
            return;

        FuzzAssert.True(TryNormalize(normalized, options, pipeline, out var renormalized), "The normalized document is nested too deeply");
        FuzzAssert.Equal(normalized, renormalized, "Normalize is not idempotent");

        if (hasDocument && !HasParagraphBeforeInlineTable(markdown, document!) && TryRender(markdown, pipeline, out var html))
        {
            FuzzAssert.True(TryRender(normalized, pipeline, out var normalizedHtml), "The normalized document is nested too deeply to be rendered");
            FuzzAssert.Equal(html, normalizedHtml, "The normalized document is not rendered as the original one");
        }
    }

    // A '[' that does not open a link changes how the parser matches the brackets and parses the inlines after it, which
    // depends on how they are written: the normalized destination of a link after it can close it ("[](<" before "[](<c>)",
    // written "[](c)"), and escaping it would create the autolinks and the pipe tables that the parsers do not create after
    // a '[' that can still open a link, or a link reference definition whose label contains it. The normalizer writes it as
    // is, so these documents are not checked.
    private static bool HasLiteralOpeningBracket(MarkdownDocument document)
    {
        foreach (var inline in document.Descendants<Inline>())
        {
            if (inline is LinkDelimiterInline || (inline is LiteralInline literal && literal.Content.AsSpan()[Math.Min(literal.IsFirstCharacterEscaped ? 1 : 0, literal.Content.Length)..].Contains('[')))
            {
                return true;
            }
        }

        return false;
    }

    // The inline parser of the pipe tables (not the GFM ones) parses the inlines of the paragraph before splitting it into rows,
    // so an inline can span rows or cells, such as a link whose text starts in a row and ends in the next one, which the cells
    // cannot contain once written
    private static bool HasInlineAcrossRows(string markdown, MarkdownDocument document)
    {
        foreach (var table in document.Descendants<Table>())
        {
            // A task list item is parsed in the first cell too, which is not the start of the list item once written
            if (table.Descendants<Meziantou.Framework.Markdown.Extensions.TaskLists.TaskList>().Any())
            {
                return true;
            }

            if (table.Parser is null && table.Descendants<Inline>().Any(inline => inline is ContainerInline and not { Parent: null } && inline.Span.Start >= 0 && inline.Span.End < markdown.Length && inline.Span.Start <= inline.Span.End
                && markdown.AsSpan(inline.Span.Start, inline.Span.Length).ContainsAny('\n', '\r', '|')))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLinkAfterText(MarkdownDocument document)
    {
        foreach (var inline in document.Descendants<Inline>())
        {
            if (inline is LinkInline { IsAutoLink: false } or CodeInline or HtmlInline or HtmlEntityInline && inline.PreviousSibling is LiteralInline { Content.Length: > 0 } previous && !char.IsWhiteSpace(previous.Content[previous.Content.End]))
            {
                return true;
            }
        }

        return false;
    }

    // The parser moves the link reference definitions to the end of the document, so a blank line next to a definition in a
    // list item, which makes the list loose, has no block to separate once normalized
    // The same goes for the blocks around a definition, such as two indented code blocks, which are merged once normalized.
    private static bool HasDefinitionBetweenBlocks(MarkdownDocument document)
    {
        var definitions = document.Descendants<LinkReferenceDefinition>().ToList();
        if (definitions.Count is 0)
            return false;

        foreach (var list in document.Descendants<ListBlock>())
        {
            if (list.IsLoose && definitions.Any(definition => definition.Span.Start >= list.Span.Start && definition.Span.End <= list.Span.End))
            {
                return true;
            }
        }

        foreach (var container in document.Descendants<ContainerBlock>().Prepend(document))
        {
            for (var i = 1; i < container.Count; i++)
            {
                var (before, after) = (container[i - 1], container[i]);
                if (definitions.Any(definition => definition.Span.Start > before.Span.Start && definition.Span.Start < after.Span.Start))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The parsers of the autolinks and of the JIRA links look at the characters around a link, which can be a pipe
    private static bool HasDelimiterNextToPipe(string markdown, bool anyText)
    {
        for (var i = 0; i < markdown.Length; i++)
        {
            if (markdown[i] is '|' && ((i > 0 && IsDelimiter(markdown[i - 1])) || (i + 1 < markdown.Length && IsDelimiter(markdown[i + 1]))))
            {
                return true;
            }
        }

        return false;

        bool IsDelimiter(char c) => anyText ? !char.IsWhiteSpace(c) && c is not '|' : c is '*' or '_' or '~' or '^' or '=' or '+';
    }

    // The inline parser of the pipe tables (not the GFM ones) parses the lines of the paragraph before a table with the
    // table, where the table boundaries win over the emphasis that spans lines. The normalized document separates the
    // paragraph from the table with a blank line, so its emphasis is parsed as in any other paragraph.
    private static bool HasParagraphBeforeInlineTable(string markdown, MarkdownDocument document)
    {
        foreach (var table in document.Descendants<Table>())
        {
            if (table.Parser is null && table.Parent is { } parent && parent.IndexOf(table) is > 0 and var index && parent[index - 1] is ParagraphBlock paragraph
                && paragraph.Span.Start < table.Span.Start && markdown.AsSpan(Math.Min(paragraph.Span.End, table.Span.Start)..table.Span.Start).Count('\n') <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalize(string markdown, NormalizeOptions options, MarkdownPipeline pipeline, [NotNullWhen(true)] out string? normalized)
    {
        try
        {
            normalized = MarkdownConverter.Normalize(markdown, options, pipeline);
            return true;
        }
        catch (ArgumentException ex) when (IsDepthLimitExceeded(ex))
        {
            normalized = null;
            return false;
        }
    }

    private static MarkdownPipeline GetNormalizePipeline(int extensions, byte flags)
    {
        var key = (extensions, (byte)(flags & (NormalizeDisableHtmlFlag | NormalizeTrackTriviaFlag | NormalizePreciseSourceLocationFlag)));
        return NormalizePipelines.GetOrAdd(key, static key =>
        {
            var builder = new MarkdownPipelineBuilder();
            foreach (var extension in NormalizeExtensions[key.Extensions].Split('+'))
            {
                _ = extension is "jiralinks" ? builder.UseJiraLinks(new JiraLinkOptions("http://your.company.abc")) : builder.Configure(extension);
            }

            if ((key.Flags & NormalizeDisableHtmlFlag) != 0)
            {
                builder.DisableHtml();
            }

            if ((key.Flags & NormalizeTrackTriviaFlag) != 0)
            {
                builder.EnableTrackTrivia();
            }

            if ((key.Flags & NormalizePreciseSourceLocationFlag) != 0)
            {
                builder.UsePreciseSourceLocation();
            }

            return builder.Build();
        });
    }

    // The heading identifiers and the unescaping of link destinations
    private static void Helpers(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        var allowOnlyAscii = (flags & 1) != 0;
        var keepOpeningDigits = (flags & 2) != 0;

        var urilized = LinkHelper.Urilize(text, allowOnlyAscii, keepOpeningDigits);
        FuzzAssert.Equal(urilized, LinkHelper.Urilize(text.AsSpan(), allowOnlyAscii, keepOpeningDigits), "The string and span overloads of Urilize differ");
        FuzzAssert.Equal(urilized, LinkHelper.Urilize(urilized, allowOnlyAscii, keepOpeningDigits), "Urilize is not idempotent");
        if (urilized.Length > 0)
        {
            FuzzAssert.False(urilized[^1] is '-' or '_' or '.', $"Urilize ends with a punctuation character: {FuzzAssert.Format(urilized)}");
            FuzzAssert.True(keepOpeningDigits || char.IsLetter(urilized[0]), $"Urilize does not start with a letter: {FuzzAssert.Format(urilized)}");
        }

        foreach (var c in urilized)
        {
            var isAllowed = (allowOnlyAscii ? char.IsAsciiLetterLower(c) : char.IsLetter(c)) || char.IsAsciiDigit(c) || c is '-' or '_' or '.';
            FuzzAssert.True(isAllowed, $"Urilize contains the character {FuzzAssert.Format(c)}: {FuzzAssert.Format(urilized)}");
        }

        var gfm = LinkHelper.UrilizeAsGfm(text);
        FuzzAssert.Equal(gfm, LinkHelper.UrilizeAsGfm(text.AsSpan()), "The string and span overloads of UrilizeAsGfm differ");
        FuzzAssert.Equal(gfm, LinkHelper.UrilizeAsGfm(gfm), "UrilizeAsGfm is not idempotent");
        foreach (var c in gfm)
        {
            FuzzAssert.True(char.IsLetterOrDigit(c) || c is '-' or '_', $"UrilizeAsGfm contains the character {FuzzAssert.Format(c)}: {FuzzAssert.Format(gfm)}");
        }

        var unescaped = HtmlHelper.Unescape(text);
        var unescapedEntities = HtmlHelper.Unescape(text, removeBackSlash: false);
        if (!text.Contains('&', StringComparison.Ordinal))
        {
            FuzzAssert.Equal(text, unescapedEntities, "Unescape changes a text without entities when it keeps the backslashes");
        }

        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            FuzzAssert.Equal(unescapedEntities, unescaped, "Unescape changes a text without backslashes depending on removeBackSlash");
        }

        // Without entities, a backslash before a punctuation character is removed, and the escaped character is text, even a
        // backslash
        if (!text.Contains('&', StringComparison.Ordinal))
        {
            var expected = new System.Text.StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1].IsEscapableSymbol())
                {
                    i++;
                }

                expected.Append(text[i]);
            }

            FuzzAssert.Equal(expected.ToString(), unescaped, "Unescape does not remove the backslashes before the punctuation characters");
        }
    }

    private static (int Extensions, byte Flags, string Markdown) ReadInput(ReadOnlySpan<byte> data)
    {
        var extensions = FuzzInput.ReadByte(ref data) % Extensions.Length;
        var flags = (byte)(FuzzInput.ReadByte(ref data) & (DisableHtmlFlag | PreciseSourceLocationFlag));
        return (extensions, flags, FuzzInput.ReadText(data));
    }

    private static MarkdownPipeline GetPipeline(int extensions, byte flags, bool trackTrivia, bool minimumThresholds, bool genericAttributes = false)
    {
        return Pipelines.GetOrAdd((extensions, flags, trackTrivia, minimumThresholds, genericAttributes), static key => CreatePipeline(key.Extensions, key.Flags, key.TrackTrivia, key.MinimumThresholds, key.GenericAttributes));
    }

    private static MarkdownPipeline CreatePipeline(int extensions, byte flags, bool trackTrivia, bool minimumThresholds, bool genericAttributes)
    {
        var builder = new MarkdownPipelineBuilder();
        var names = Extensions[extensions].Split('+');
        foreach (var extension in names)
        {
            _ = extension is "jiralinks" ? builder.UseJiraLinks(new JiraLinkOptions("http://your.company.abc")) : builder.Configure(extension);
        }

        if (genericAttributes && !names.Contains("attributes", StringComparer.Ordinal) && !names.Contains("advanced", StringComparer.Ordinal))
        {
            builder.UseGenericAttributes();
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

    // The seeds of the other targets, with one edit
    private static IEnumerable<FuzzSeed> GetEditSeeds()
    {
        var i = 0;
        foreach (var seed in GetSeeds())
        {
            var data = seed.Data.Span;
            yield return new FuzzSeed(seed.Name, (byte[])[.. data[..2], 0, (byte)(i / 7), (byte)(i % EditKindCount), (byte)(i / EditKindCount), .. data[2..]]);
            i++;
        }
    }

    private static IEnumerable<FuzzSeed> GetNormalizeSeeds()
    {
        foreach (var (resource, extensions) in new (string Resource, string[] Extensions)[]
        {
            ("CommonMark", ["common", "pipetables+tasklists+autolinks+jiralinks"]),
            ("NormalizeHeadings", ["common"]),
            ("PipeTableSpecs", ["pipetables"]),
            ("PipeTableGfmSpecs", ["gfm-pipetables"]),
            ("GfmTableSpecs", ["gfm-pipetables"]),
            ("TaskListSpecs", ["tasklists"]),
            ("AutoLinks", ["autolinks"]),
            ("JiraLinks", ["jiralinks"]),
        })
        {
            var examples = ReadExamples(resource);
            foreach (var name in extensions)
            {
                var index = Array.IndexOf(NormalizeExtensions, name);
                if (index < 0)
                    throw new InvalidOperationException($"The extensions '{name}' of {resource} are not in the list");

                for (var i = 0; i < examples.Count; i++)
                {
                    yield return FuzzSeed.FromText($"{resource}-{i + 1}-{name}", [(byte)index, DefaultNormalizeOptions, 0], examples[i]);
                }
            }

            // The options other than the defaults, a bullet character, raw HTML disabled, trivia and precise source locations
            if (resource is "CommonMark")
            {
                for (var i = 0; i < examples.Count; i++)
                {
                    yield return FuzzSeed.FromText($"{resource}-{i + 1}-options", [0, NormalizeDisableHtmlFlag | NormalizeTrackTriviaFlag | NormalizePreciseSourceLocationFlag, (byte)(1 + (i % 3))], examples[i]);
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetHelperSeeds()
    {
        foreach (var resource in (string[])["AutoIdentifierSpecs", "GlobalizationSpecs", "CommonMark"])
        {
            var examples = ReadExamples(resource);
            for (var i = 0; i < examples.Count; i++)
            {
                for (byte flags = 0; flags < 4; flags++)
                {
                    yield return FuzzSeed.FromText($"{resource}-{i + 1}-{flags}", [flags], examples[i]);
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
