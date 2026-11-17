using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Fuzz;

namespace Meziantou.Framework.Markdown.Fuzz;

/// <summary>
/// Markdown documents, as CommonMark and as GitHub Flavored Markdown: the invariants every tree shares, which include that
/// the tree reproduces the text, and that every character that is not whitespace or a line ending is read by a construct:
/// a token, or the marker of a container on its continuation lines. The HTML renderer reads the tree without throwing.
/// </summary>
public static class MarkdownFuzzTargets
{
    private const string ExampleFence = "````````````````````````````````";

    private static readonly string[] Tokens =
    [
        "\n", "\r\n", "\r", "\t", "    ", " ", "  \n", "\\\n", "> ", ">", "- ", "* ", "+ ", "1. ", "2) ", "#", "# ", "###### ", "=", "---", "***", "___",
        "```", "~~~", "`", "``", "*", "**", "_", "__", "[", "]", "](", ")", "![", "[a]: /url", "\"title\"", "'t'", "(t)", "<", ">", "<a>", "</a>", "<!-- -->",
        "<?x?>", "<!X>", "<![CDATA[x]]>", "<div>", "<script>", "</script>", "<http://a.b>", "<a@b.c>", "&amp;", "&#35;", "&#x22;", "&nbsp;", "\\*", "\\[", "\u00A0",
    ];

    private static readonly string[] GfmTokens =
    [
        .. Tokens, "|", "| ", " |", "\\|", "| --- |", "|:-|", ":-:", "-:", "~", "~~", "~~~", "[ ] ", "[x] ", "[X] ", "www.", "www.a.b", "http://", "https://a.b",
        "ftp://", "@", "a@b.c", ".", "_", "(", "&hl;", "<title>", "</style>", "<xmp/>", "<scripT", "\v", "\f",
    ];

    private static readonly string[] ExtensionTokens =
    [
        .. GfmTokens, "^", "++", "==", "~a~", "^a^", "&plus;", "&reg;", "\u2212", "\u2020", "$", "$$", "$$\n", "\\$", "---\n", "...\n", "```mermaid\n", "```nomnoml\n", "> [!NOTE]\n", "[!TIP]", "[!x]  \n", "*[A]: ", "*[A B]: x\n", "A ", "[^1]", "[^1]: ", "\n    ", "[^a]:\n", "{", "}", "{#a}", "{.b}", "{c=d}", "{e=\"f\"}", "{g}\n", "# a\n", "[a]", "a\n===\n", "\u00E9", "\u00DF", "\u00C6", ":::", "::::", ":::x\n", ":::\n", "::", ":   ", "~   ", "\n:   ", "a. ", "B) ", "iv. ", "XI) ", "'", "\"", "''", "<<", ">>", "...", "--", "---", ":smile:", ":)", ":*", "<3", "]:)", "->", "o:)", "^^^", "^^^ c\n", "^^ ", "\"\"", "+---+", "+===+", "ABC-12", "(X-1)", "![](a.mp4)", "![](https://youtu.be/x)", "+:-:+", "+---+---+\n", "| a | b |\n", "||", "|-|", "| :--: |", "\\-", "\\:", "|{.c}", "-|-\n", " | ", "\u0645", "\u05D0", "\u3002", "\u30C6", "\uFF01",
    ];

    // Every extension, with the pipe tables and the autolinks of Markdig. A text of odd length has the tables and the autolinks
    // of GFM instead, HTML disabled, and identifiers made as GitHub makes them. A text whose length is 2 modulo 4 has the pipe
    // tables without delimiter row, their column count from the first row, and the widths of their columns.
    private static readonly MarkdownParseOptions AllExtensions = new() { Extensions = Enum.GetValues<MarkdownExtensions>().Aggregate(MarkdownExtensions.None, (all, extension) => all | extension) };
    private static readonly MarkdownParseOptions AllExtensionsWithPipeTableOptions = AllExtensions with { PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false, UseHeaderForColumnCount = true, InferColumnWidthsFromSeparator = true } };
    private static readonly MarkdownParseOptions AllExtensionsWithoutHtml = AllExtensions with { Extensions = AllExtensions.Extensions & ~(MarkdownExtensions.PipeTables | MarkdownExtensions.MarkdigAutolinks), AutolinkOptions = new MarkdownAutolinkOptions { OpenInNewWindow = true, UseHttpsForWwwLinks = true, AllowDomainWithoutPeriod = true }, DisableHtml = true, AutoIdentifierOptions = MarkdownAutoIdentifierOptions.GitHub | MarkdownAutoIdentifierOptions.AutoLink };

    private static readonly MarkdownHtmlRendererOptions RendererOptions = new()
    {
        PragmaLines = true,
        BaseUrl = new Uri("https://example.com/a/"),
        LinkRewriter = url => url + "#x",
        LinkRel = "nofollow",
        EscapeNonAsciiUrlCharacters = false,
    };

    // Inline extensions whose content is text, markers or inlines, and a delimiter extension, with GFM or with every extension
    // whose characters they do not use
    private static readonly GlossaryExtension Glossary = new();
    private static readonly MarkdownSyntaxExtension[] SampleSyntaxExtensions =
    [
        new MentionExtension(), new SpanExtension(), new GlossaryTermExtension(Glossary), new WikiLinkExtension(), new MarkdownDelimiterExtension("highlight", '%'), new MarkdownDelimiterExtension("spoiler", '|', minimumCount: 1, maximumCount: 3),
        Glossary, new FenceExtension("admonition", "!!!", MarkdownFencedBlockContent.Blocks) { ParseInfoAsInlines = true }, new FenceExtension("raw", "%%%", MarkdownFencedBlockContent.RawLines) { CanInterruptParagraph = false }, new TranscriptExtension(),
    ];
    private static readonly MarkdownParseOptions SyntaxExtensionOptions = new() { SyntaxExtensions = SampleSyntaxExtensions };
    private static readonly MarkdownParseOptions GfmSyntaxExtensionOptions = new() { Extensions = MarkdownExtensions.GitHubFlavored & ~MarkdownExtensions.Tables, SyntaxExtensions = SampleSyntaxExtensions };
    private static readonly MarkdownParseOptions AllSyntaxExtensionOptions = AllExtensionsWithoutHtml with { Extensions = AllExtensionsWithoutHtml.Extensions & ~(MarkdownExtensions.Tables | MarkdownExtensions.GridTables), SyntaxExtensions = SampleSyntaxExtensions };

    private static readonly string[] SyntaxExtensionTokens = [.. ExtensionTokens, "@", "@a-b", "a@b", "{{", "}}", "{{a}}", "[[", "]]", "[[a]]", "%", "%%", "%%%", "%%a%%", "|", "||", "|||", "!!!", "!!! note\n", "\n!!!\n", "%%%\n", "%%% glossary\n", "A: b\n", "Alice: ", "\nBob: ", "[A]"];

    private static readonly MarkdownHtmlNodeRenderer[] NodeRenderers = [new DecliningNodeRenderer(), new DefaultNodeRenderer()];

    private static readonly string[] EscaperTokens = ["\\", "`", "``", "```", "~~~", "*", "_", "[", "]", "(", ")", "<", ">", "&", "&amp;", "&#35;", "\"", "'", "|", "#", "-", "=", "!", " ", "\t", "\n", "\r\n", "\n\n", "\u00A0", "\u0000", "\u0085", "https://"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("markdown", data => Parse(data, MarkdownParseOptions.Default)) { Seeds = GetSeeds, Dictionary = Tokens },
        new("markdown-gfm", data => Parse(data, MarkdownParseOptions.GitHubFlavored)) { Seeds = GetGfmSeeds, Dictionary = GfmTokens },
        new("markdown-extensions", data => Parse(data, (data.Length % 4) switch { 0 => AllExtensions, 2 => AllExtensionsWithPipeTableOptions, _ => AllExtensionsWithoutHtml })) { Seeds = GetExtensionSeeds, Dictionary = ExtensionTokens },
        new("markdown-escaper", Escape) { Seeds = GetEscaperSeeds, Dictionary = EscaperTokens },
        new("markdown-formatter", Format) { Seeds = GetExtensionSeeds, Dictionary = ExtensionTokens },
        new("markdown-syntax-extensions", ParseWithSyntaxExtensions) { Seeds = GetSyntaxExtensionSeeds, Dictionary = SyntaxExtensionTokens },
    ];

    private static readonly MarkdownParseOptions[] FormatterParseOptions = [MarkdownParseOptions.Default, MarkdownParseOptions.GitHubFlavored, AllExtensions, AllExtensionsWithPipeTableOptions, AllExtensionsWithoutHtml];

    // The default style, and the other choices of each option
    private static readonly MarkdownFormattingOptions[] FormattingOptions =
    [
        MarkdownFormattingOptions.Default,
        new() { NewLine = "\r\n", BulletListMarker = '*', RenumberOrderedLists = false, UseAtxHeadings = false, UseFencedCodeBlocks = false, CodeFenceCharacter = '~', ThematicBreakCharacter = '_', AlignTables = false },
        new() { BulletListMarker = null, ThematicBreakCharacter = '*' },
        new() { BulletListMarker = '+' },
    ];

    // The formatted text renders as the same HTML as the text, and formatting it again does not change it. The formatter
    // keeps the text when what it writes does not render as the same HTML.
    private static void Format(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var parseOptions = FormatterParseOptions[data.Length % FormatterParseOptions.Length];
        var formattingOptions = FormattingOptions[data.Length / FormatterParseOptions.Length % FormattingOptions.Length];

        var tree = MarkdownSyntaxTree.ParseText(text, parseOptions);
        var formatted = MarkdownFormatter.Format(tree, formattingOptions);
        var formattedText = formatted.GetRoot().ToFullString();
        var html = MarkdownHtmlRenderer.ToHtml(tree).ReplaceLineEndings("\n");
        FuzzAssert.True(MarkdownHtmlRenderer.ToHtml(formatted).ReplaceLineEndings("\n") == html, $"The formatted text {FuzzAssert.Format(formattedText)} does not render as the text {FuzzAssert.Format(text)}");
        FuzzAssert.True(MarkdownFormatter.FormatText(formatted, formattingOptions) == formattedText, $"Formatting the formatted text {FuzzAssert.Format(formattedText)} changes it: {FuzzAssert.Format(text)}");

        // The formatter compares the HTML with its line endings as \n, without replacing them
        CheckEqualsIgnoringLineEndings(text, formattedText);
        CheckEqualsIgnoringLineEndings(text, text.Replace("\r\n", "\r", StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetKindsAndValues(MarkdownSyntaxTree tree) => tree.GetRoot().DescendantTokens().Select(token => $"{token.RawKind} {(token.Value is int value ? value : null)}");

    private static void CheckEqualsIgnoringLineEndings(string text, string other)
    {
        static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        var expected = Normalize(text) == Normalize(other);
        FuzzAssert.True(MarkdownHtmlRenderer.EqualsIgnoringLineEndings(text, other) == expected && MarkdownHtmlRenderer.EqualsIgnoringLineEndings(other, text) == expected, $"{FuzzAssert.Format(text)} and {FuzzAssert.Format(other)} are not compared as the same text with \\n line endings");
    }

    // The text that MarkdownEscaper writes is parsed back to the same text, code, destination, title and label
    private static void Escape(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);

        // The whitespace around the lines of a paragraph is not text, a blank line ends it, and the parser replaces NUL
        var line = ToMarkdownLineEndings(text).Replace('\n', ' ').Replace('\0', ' ').Trim(' ', '\t');
        if (line.Length > 0)
        {
            var escaped = MarkdownEscaper.Escape(line);
            var paragraph = MarkdownSyntaxTree.ParseText(escaped).GetRoot().Blocks.Single();
            FuzzAssert.True(paragraph is MarkdownParagraphSyntax && paragraph.GetPlainText() == line, $"The escaped text {FuzzAssert.Format(escaped)} is not the text {FuzzAssert.Format(line)}");

            var cell = MarkdownEscaper.EscapeTableCell(line);
            var table = MarkdownSyntaxTree.ParseText($"| a |\n|---|\n| {cell} |\n", MarkdownParseOptions.GitHubFlavored).GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();
            FuzzAssert.True(table.Rows.Single().Cells.Single().GetPlainText() == line, $"The escaped cell {FuzzAssert.Format(cell)} is not the text {FuzzAssert.Format(line)}");
        }

        var content = text.Replace('\0', ' ');
        if (content.Length > 0)
        {
            var codeSpan = MarkdownEscaper.CreateCodeSpan(content);
            var code = MarkdownSyntaxTree.ParseText(codeSpan).GetRoot().DescendantNodes().OfType<MarkdownCodeSpanSyntax>().ToList();
            var expected = ToMarkdownLineEndings(content).Replace('\n', ' ');
            FuzzAssert.True(content.AsSpan().ContainsAny('\r', '\n') || (code.Count == 1 && code[0].Code == expected), $"The code span {FuzzAssert.Format(codeSpan)} is not the content {FuzzAssert.Format(content)}");
        }

        var destination = MarkdownEscaper.EscapeLinkDestination(content);
        var title = MarkdownEscaper.EscapeLinkTitle(content);
        var link = MarkdownSyntaxTree.ParseText($"[x]({destination} {title})").GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().ToList();
        FuzzAssert.True(link.Count == 1 && link[0].Destination == content && link[0].Title == content, $"The link destination {FuzzAssert.Format(destination)} or title {FuzzAssert.Format(title)} is not the text {FuzzAssert.Format(content)}");

        var fence = MarkdownEscaper.CreateFencedCodeBlock(ToMarkdownLineEndings(content));
        var block = MarkdownSyntaxTree.ParseText(fence).GetRoot().Blocks[0];
        var expectedCode = ToMarkdownLineEndings(content);
        FuzzAssert.True(block is MarkdownCodeBlockSyntax { IsFenced: true } codeBlock && codeBlock.Code == (expectedCode.Length == 0 || expectedCode[^1] == '\n' ? expectedCode : expectedCode + "\n"), $"The fenced code block {FuzzAssert.Format(fence)} is not the code {FuzzAssert.Format(content)}");
    }

    // The line endings of Markdown: string.ReplaceLineEndings also replaces U+0085, U+2028 and U+2029, which are text
    private static string ToMarkdownLineEndings(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // The constructs of the syntax extensions agree with their nodes, and the renderers write them
    private static void ParseWithSyntaxExtensions(ReadOnlySpan<byte> data)
    {
        var options = (data.Length % 3) switch { 0 => SyntaxExtensionOptions, 1 => GfmSyntaxExtensionOptions, _ => AllSyntaxExtensionOptions };
        Parse(data, options);

        var text = FuzzText.Decode(data);
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionBlockSyntax>())
        {
            FuzzAssert.True(Array.Exists(SampleSyntaxExtensions, extension => extension.Name == node.Name), $"The block at {node.Span} has the name {FuzzAssert.Format(node.Name)}: {FuzzAssert.Format(text)}");
            FuzzAssert.True(node.Markers.Count > 0 || node.Texts.Count > 0, $"The block {node.Name} at {node.Span} has no marker and no text: {FuzzAssert.Format(text)}");
            FuzzAssert.True(node.IsFenced == node.Name is "admonition" or "raw" or "glossary", $"The block {node.Name} at {node.Span} is fenced: {node.IsFenced}: {FuzzAssert.Format(text)}");
            _ = (node.Info, node.Content, node.Lines, node.Blocks, node.Texts, node.GetPlainText());
        }

        foreach (var node in tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>())
        {
            FuzzAssert.True(Array.Exists(SampleSyntaxExtensions, extension => extension.Name == node.Name), $"The construct at {node.Span} has the name {FuzzAssert.Format(node.Name)}: {FuzzAssert.Format(text)}");
            FuzzAssert.True(node.Markers.Count > 0, $"The construct {node.Name} at {node.Span} has no marker: {FuzzAssert.Format(text)}");
            _ = (node.Texts, node.Text, node.GetPlainText());
        }

        _ = MarkdownHtmlRenderer.ToHtml(tree, SyntaxExtensionRendererOptions);
        _ = MarkdownPlainTextRenderer.ToPlainText(tree, SyntaxExtensionRendererOptions);
        _ = MarkdownFormatter.Format(tree);
    }

    private static readonly MarkdownHtmlRendererOptions SyntaxExtensionRendererOptions = new() { NodeRenderers = [new SyntaxExtensionRenderer()] };

    // The seeds of the unit tests of the syntax extensions, and the examples of the extensions
    private static IEnumerable<FuzzSeed> GetSyntaxExtensionSeeds()
    {
        string[] texts =
        [
            "Thanks @octo-cat, and *@hubot*!", "Mail me@example.com", "Some %%important *and* urgent%% text, *nested %%both* ways%%.", "a {{*b* [c](d)}} e", "[a {{b]}}(c)", "*a {{b* c}}",
            "[a {{b}} c](d)", "{{a {{b}} c}}", "{{a", "`{{a}}` \\{{b}}", "> {{a\n> *b*}} c", "[[Home page]] and [a link](b)", "- [[Home\n  page]] x\n", "# See [[Home]]", "a [[b]] c",
            "* @a %%b%%\n\n|x|y|\n|-|-|\n|@c|%%d%%|\n", "*a %%b* c%%", "%%a *b%% c*", "%%%a%%% %%b %%c%% d%%", "|a| ||b|| |||c||| ||||d||||", "@a{{@b [[c]] %%d%%}}|e|",
            "!!! warning\nDo not *push* on Friday.\n\n- Really.\n!!!\n", "> %%% chart\n>   bar: <1>\n> *2*\n> %%%\n\nafter\n", "%%%\na\n", "> %%%\n> a\nb\n", "  %%%\n    a\n %%%\n", "%%%\n%%% x\n%%%\n",
            "text\n%%%\na\n%%%\n", "    %%%\n", "!!! note\n```\ncode\n!!!\nafter\n", "!!! A *warning* \nb\n!!!\n", "The meeting went like this:\nAlice: Can we ship on *Friday*?\nBob: Only if the tests pass.\nThen we went for lunch.\n",
            "Alice: a\nBob: b\nx\nCarol: c\nDan: d\ny\n", "[r]: /u\nAlice: [r]\nBob: b\n", "Alice: a\nBob: b\n===\n", "> Alice: a\nBob:   *b*  \n", "Call the [API] from a [link](https://example.com).\n\n%%% glossary\nAPI: Application programming interface\n%%%\n",
            "[A] [B]\n\n%%% glossary\nA: a\n%%%\n\n%%% glossary\nB: b\n%%%\n", "- !!! x\n  Alice: a\n  Bob: b\n  !!!\n", "[^1]: %%%\n    a\n\n    %%%\n",
        ];
        for (var i = 0; i < texts.Length; i++)
        {
            yield return FuzzSeed.FromText($"syntax-extensions-{i + 1}", texts[i]);
        }

        foreach (var seed in GetExtensionSeeds())
        {
            yield return seed;
        }
    }

    private static IEnumerable<FuzzSeed> GetEscaperSeeds()
    {
        string[] texts = ["plain text", "*test* **test** ***test*** _test_ ~test~", "1. test", "- test", "> test", "# header", "---", "===", "[test](url) ![test](url)", "&quot; &#35; &amp", "<div> <https://example.com> <a@b.c>", "`code` ``code``", "C:\\path\\file.txt \\\\ \\", "a | b", "a\n# b\n- c\n> d", "\u00E9 \u2713", "`teste```testr`", " a ", "a(b", "<a>", "a\"b'c", "a\n\nb", "```\ncode\n```", "    ```"];
        for (var i = 0; i < texts.Length; i++)
        {
            yield return FuzzSeed.FromText($"escaper-{i + 1}", texts[i]);
        }
    }

    private static void Parse(ReadOnlySpan<byte> data, MarkdownParseOptions options)
    {
        var text = FuzzText.Decode(data);
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        SyntaxTreeInvariants.Check(tree, text, "Markdown", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);

        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                FuzzAssert.True(trivia.RawKind != (int)SyntaxKind.SkippedTextTrivia, $"The text {FuzzAssert.Format(trivia.ToString())} at {trivia.Span} is read by no construct: {FuzzAssert.Format(text)}");
            }
        }

        _ = MarkdownHtmlRenderer.ToHtml(tree);

        // The inlines of the lines that follow each other with \n are read in the text itself, and the others in a copy: the
        // line endings do not change the tokens, their values, or the HTML
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lfTree = MarkdownSyntaxTree.ParseText(lf, options);
        var crlfTree = MarkdownSyntaxTree.ParseText(lf.Replace("\n", "\r\n", StringComparison.Ordinal), options);
        var lfHtml = MarkdownHtmlRenderer.ToHtml(lfTree).Replace("\r\n", "\n", StringComparison.Ordinal);
        var crlfHtml = MarkdownHtmlRenderer.ToHtml(crlfTree).Replace("\r\n", "\n", StringComparison.Ordinal);
        FuzzAssert.True(lfHtml == crlfHtml, $"The text renders as {FuzzAssert.Format(lfHtml)} with \\n and as {FuzzAssert.Format(crlfHtml)} with \\r\\n: {FuzzAssert.Format(text)}");
        FuzzAssert.True(GetKindsAndValues(lfTree).SequenceEqual(GetKindsAndValues(crlfTree), StringComparer.Ordinal), $"The tokens of the text are not the same with \\n and with \\r\\n: {FuzzAssert.Format(text)}");

        // The formatter finds whether a text is a single paragraph from its blocks, without parsing its inlines
        if (Parsing.BlockParser.IsSingleParagraph(text, options) is { } single)
        {
            FuzzAssert.True(single == (tree.GetRoot().Blocks is [MarkdownParagraphSyntax]), $"The blocks of the text tell that it is a single paragraph: {single}, but its tree does not: {FuzzAssert.Format(text)}");
        }

        // The plain text has no HTML: the HTML of the text is not written
        var plainText = MarkdownPlainTextRenderer.ToPlainText(tree);
        FuzzAssert.True(plainText.Length == 0 || plainText[^1] == '\n', $"The plain text {FuzzAssert.Format(plainText)} does not end with a line ending: {FuzzAssert.Format(text)}");
        if (options != MarkdownParseOptions.Default)
        {
            // The options of the renderer that read the whole tree: the pragma lines, and the URLs
            var html = MarkdownHtmlRenderer.ToHtml(tree, RendererOptions);
            var ids = System.Text.RegularExpressions.Regex.Matches(html, "id=\"(?<id>pragma-line-[0-9]+)\"", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)).Select(match => match.Groups["id"].Value).ToList();
            FuzzAssert.True(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), $"A pragma line is the id of two elements: {FuzzAssert.Format(text)}");
            _ = tree.GetRoot().FindClosestLine(text.Length % 7);

            // Node renderers that decline every node, or that write the built-in rendering of every node, change nothing
            var optionsPlainText = MarkdownPlainTextRenderer.ToPlainText(tree, RendererOptions);
            foreach (var renderer in NodeRenderers)
            {
                var withRenderer = RendererOptions with { NodeRenderers = [renderer] };
                var rendered = MarkdownHtmlRenderer.ToHtml(tree, withRenderer);
                FuzzAssert.True(rendered == html, $"The node renderer {renderer.GetType().Name} changes the HTML {FuzzAssert.Format(html)} to {FuzzAssert.Format(rendered)}: {FuzzAssert.Format(text)}");
                var renderedPlainText = MarkdownPlainTextRenderer.ToPlainText(tree, withRenderer);
                FuzzAssert.True(renderedPlainText == optionsPlainText, $"The node renderer {renderer.GetType().Name} changes the plain text {FuzzAssert.Format(optionsPlainText)} to {FuzzAssert.Format(renderedPlainText)}: {FuzzAssert.Format(text)}");
            }
        }

        CheckTypedApi(tree, text);
    }

    // The typed nodes read their tokens without throwing, the walker visits every node, a rewrite that changes nothing keeps
    // the tree, and the editing helpers give trees for any block
    private static void CheckTypedApi(MarkdownSyntaxTree tree, string text)
    {
        var root = tree.GetRoot();
        var count = 0;
        foreach (var node in root.DescendantNodesAndSelf().Cast<MarkdownSyntaxNode>())
        {
            count++;
            _ = node.GetPlainText();
            switch (node)
            {
                case MarkdownHeadingSyntax heading:
                    FuzzAssert.True(heading.Level is >= 1 and <= 6, $"The heading at {heading.Span} has the level {heading.Level}: {FuzzAssert.Format(text)}");
                    if (tree.Options.Extensions.HasFlag(MarkdownExtensions.AutoIdentifiers))
                    {
                        FuzzAssert.True(heading.Identifier is { Length: > 0 }, $"The heading at {heading.Span} has no identifier: {FuzzAssert.Format(text)}");
                    }

                    break;
                case MarkdownListSyntax list:
                    _ = (list.IsTight, list.StartNumber, list.MarkerCharacter);
                    FuzzAssert.True(list.Items.Count > 0, $"The list at {list.Span} has no item: {FuzzAssert.Format(text)}");
                    break;
                case MarkdownListItemSyntax item:
                    _ = (item.IsTask, item.IsChecked, item.MarkerToken);
                    break;
                case MarkdownCodeBlockSyntax code:
                    _ = (code.Code, code.Language, code.ClosingFenceToken);
                    break;
                case MarkdownHtmlBlockSyntax html:
                    _ = html.Html;
                    break;
                case MarkdownLinkReferenceDefinitionSyntax definition:
                    _ = (definition.Label, definition.Destination, definition.Title);
                    break;
                case MarkdownGridTableSyntax grid:
                    // The cells are in the columns of the table, and their content is a document of its own
                    FuzzAssert.True(grid.Columns.Count > 0, $"The grid table at {grid.Span} has no column: {FuzzAssert.Format(text)}");
                    foreach (var cell in grid.Rows.SelectMany(row => row.Cells))
                    {
                        FuzzAssert.True(cell.ColumnIndex >= 0 && cell.ColumnSpan > 0 && cell.ColumnIndex + cell.ColumnSpan <= grid.Columns.Count && cell.RowSpan > 0, $"The cell of the grid table at {grid.Span} has the columns {cell.ColumnIndex} to {cell.ColumnIndex + cell.ColumnSpan} and {cell.RowSpan} rows: {FuzzAssert.Format(text)}");
                        FuzzAssert.True(cell.Content.GetRoot().ToFullString() == cell.Text, $"The content of the cell of the grid table at {grid.Span} does not reproduce its text: {FuzzAssert.Format(text)}");
                    }

                    break;
                case MarkdownTableSyntax { IsPipeTable: true, DelimiterRow: { } delimiterRow } pipeTable:
                    // A pipe table has a header cell and a delimiter row, whose columns are read from its text
                    FuzzAssert.True(pipeTable.Alignments.Count > 0 && pipeTable.Alignments.Count >= delimiterRow.DelimiterTokens.Count, $"The pipe table at {pipeTable.Span} has {pipeTable.Alignments.Count} columns for {delimiterRow.DelimiterTokens.Count} delimiters: {FuzzAssert.Format(text)}");
                    FuzzAssert.True(pipeTable.HeaderRow is { Cells.Count: > 0 }, $"The pipe table at {pipeTable.Span} has no header cell: {FuzzAssert.Format(text)}");
                    _ = pipeTable.Rows;
                    break;
                case MarkdownTableSyntax { IsPipeTable: true } pipeTable:
                    // A pipe table without a delimiter row is allowed by the options, and has only rows
                    FuzzAssert.True(!tree.Options.PipeTableOptions.RequireHeaderSeparator && pipeTable.HeaderRow is null && pipeTable.Rows.Count > 0 && pipeTable.Alignments.Count == 0, $"The pipe table at {pipeTable.Span} has no delimiter row: {FuzzAssert.Format(text)}");
                    break;
                case MarkdownTableSyntax table:
                    FuzzAssert.True(table.HeaderRow is not null && table.Alignments.Count == table.HeaderRow.Cells.Count, $"The table at {table.Span} has {table.Alignments.Count} columns and {table.HeaderRow?.Cells.Count} header cells: {FuzzAssert.Format(text)}");
                    _ = table.Rows;
                    break;
                case MarkdownCodeSpanSyntax codeSpan:
                    _ = codeSpan.Code;
                    break;
                case MarkdownLinkSyntax link:
                    _ = (link.LinkKind, link.Destination, link.Title, link.Label);
                    _ = link.Heading;
                    FuzzAssert.True(link.LinkKind == MarkdownLinkKind.Inline || link.Definition is not null || link.Heading is not null, $"The reference link at {link.Span} has no definition and no heading: {FuzzAssert.Format(text)}");
                    if (link.LinkKind != MarkdownLinkKind.Inline && link.Definition is null)
                    {
                        FuzzAssert.True(link.Destination.StartsWith('#', StringComparison.Ordinal), $"The link to a heading at {link.Span} has the destination {FuzzAssert.Format(link.Destination)}: {FuzzAssert.Format(text)}");
                    }

                    break;
                case MarkdownAutolinkSyntax autolink:
                    _ = (autolink.Destination, autolink.IsEmail);
                    break;
                case MarkdownRawHtmlSyntax rawHtml:
                    _ = rawHtml.Html;
                    break;
            }
        }

        var walker = new CountingWalker();
        walker.Visit(root);
        FuzzAssert.True(walker.Count == count, $"The walker visits {walker.Count} nodes of {count}: {FuzzAssert.Format(text)}");
        FuzzAssert.True(ReferenceEquals(new MarkdownSyntaxRewriter().Visit(root), root), $"A rewrite that changes nothing changes the tree: {FuzzAssert.Format(text)}");

        if (text.Length < 4096 && root.DescendantNodes().OfType<MarkdownBlockSyntax>().LastOrDefault() is { } block)
        {
            _ = tree.RemoveBlock(block);
            _ = tree.InsertBlocksBefore(block, "- a\n\n> b");
            _ = tree.InsertBlocksAfter(block, "\nc\n");
        }
    }

    private sealed class CountingWalker : MarkdownSyntaxWalker
    {
        public int Count { get; private set; }

        public override void DefaultVisit(MarkdownSyntaxNode node)
        {
            Count++;
            base.DefaultVisit(node);
        }
    }

    // The examples of the CommonMark specification
    private static IEnumerable<FuzzSeed> GetSeeds() => GetSeeds("markdown.CommonMark.md", "commonmark");

    // The examples of the CommonMark specification and of the extensions of GitHub Flavored Markdown
    private static IEnumerable<FuzzSeed> GetGfmSeeds() => GetSeeds("markdown.CommonMark.md", "commonmark").Concat(GetSeeds("markdown.GfmExtensions.md", "gfm"));

    // The examples of the GFM extensions and of the extensions of the 1.x library
    private static IEnumerable<FuzzSeed> GetExtensionSeeds()
    {
        var seeds = GetGfmSeeds();
        foreach (var name in typeof(MarkdownFuzzTargets).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("markdown.extensions.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            seeds = seeds.Concat(GetSeeds(name, name["markdown.extensions.".Length..^".md".Length]));
        }

        return seeds;
    }

    private static IEnumerable<FuzzSeed> GetSeeds(string resourceName, string prefix)
    {
        var lines = FuzzResources.ReadText(typeof(MarkdownFuzzTargets).Assembly, resourceName).ReplaceLineEndings("\n").Split('\n');
        var number = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(ExampleFence + " example", StringComparison.Ordinal))
            {
                continue;
            }

            var markdown = new List<string>();
            for (i++; i < lines.Length && lines[i] != "."; i++)
            {
                markdown.Add(lines[i]);
            }

            number++;
            var text = markdown.Count == 0 ? "" : (string.Join('\n', markdown) + "\n").Replace('→', '\t');
            yield return FuzzSeed.FromText($"{prefix}-{number}", text);
        }
    }

    private sealed class DecliningNodeRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer) => false;
    }

    private sealed class DefaultNodeRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            writer.WriteDefault(node);
            return true;
        }
    }

    // @name, unless the @ follows a letter or a digit
    private sealed class MentionExtension() : MarkdownInlineExtension("mention", "@")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            if (char.IsLetterOrDigit(context.PreviousCharacter))
            {
                return false;
            }

            var length = 1;
            while (length < context.Text.Length && (char.IsAsciiLetterOrDigit(context.Text[length]) || context.Text[length] == '-'))
            {
                length++;
            }

            if (length == 1)
            {
                return false;
            }

            context.AddMarker(0, 1);
            context.AddText(1, length - 1);
            context.Complete(length);
            return true;
        }
    }

    // {{content}}, whose content is inlines, with balanced braces
    private sealed class SpanExtension() : MarkdownInlineExtension("span", "{")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var text = context.Text;
            if (!text.StartsWith("{{", StringComparison.Ordinal))
            {
                return false;
            }

            var depth = 0;
            for (var i = 0; i < text.Length - 1; i++)
            {
                if (text[i] == '{' && text[i + 1] == '{')
                {
                    depth++;
                    i++;
                }
                else if (text[i] == '}' && text[i + 1] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        context.AddMarker(0, 2);
                        if (i > 2)
                        {
                            context.AddInlines(2, i - 2);
                        }

                        context.AddMarker(i, 2);
                        context.Complete(i + 2);
                        return true;
                    }

                    i++;
                }
            }

            return false;
        }
    }

    // [[page]], whose text can span lines
    private sealed class WikiLinkExtension() : MarkdownInlineExtension("wiki", "[")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var text = context.Text;
            var end = text.IndexOf("]]", StringComparison.Ordinal);
            if (!text.StartsWith("[[", StringComparison.Ordinal) || end < 3)
            {
                return false;
            }

            context.AddMarker(0, 2);
            context.AddText(2, end - 2);
            context.AddMarker(end, 2);
            context.Complete(end + 2);
            return true;
        }
    }

    // !!! or %%% fences
    private sealed class FenceExtension(string name, string fence, MarkdownFencedBlockContent content) : MarkdownFencedBlockExtension(name, fence[..1], content)
    {
        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith(fence, StringComparison.Ordinal) ? fence.Length : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith(openingFence, StringComparison.Ordinal) ? openingFence.Length : 0;
    }

    // %%% glossary, then lines "Term: definition", which the state of the document records
    private sealed class GlossaryExtension() : MarkdownFencedBlockExtension("glossary", "%", MarkdownFencedBlockContent.RawLines)
    {
        public override object? CreateState() => new HashSet<string>(StringComparer.Ordinal);

        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("%%% glossary", StringComparison.Ordinal) ? 3 : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith("%%%", StringComparison.Ordinal) ? 3 : 0;

        public override void OnClosed(ref MarkdownFencedBlockContext context)
        {
            var terms = (HashSet<string>)context.State!;
            for (var i = 0; i < context.LineCount; i++)
            {
                var line = context.GetLine(i);
                if (line.IndexOf(": ", StringComparison.Ordinal) is > 0 and var colon)
                {
                    terms.Add(line[..colon].ToString());
                }
            }
        }
    }

    // [Term], when the glossary defines the term
    private sealed class GlossaryTermExtension(GlossaryExtension glossary) : MarkdownInlineExtension("glossary-term", "[")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var end = context.Text.IndexOf(']');
            if (end < 2 || !((HashSet<string>)context.GetState(glossary)!).Contains(context.Text[1..end].ToString()))
            {
                return false;
            }

            context.AddMarker(0, 1);
            context.AddText(1, end - 1);
            context.AddMarker(end, 1);
            context.Complete(end + 1);
            return true;
        }
    }

    // The runs of two lines or more "Speaker: message" of a paragraph, whose speakers are text and whose messages are inlines
    private sealed class TranscriptExtension() : MarkdownParagraphExtension("transcript")
    {
        public override bool TryConvert(ref MarkdownParagraphConvertContext context)
        {
            var first = -1;
            var count = 0;
            for (var i = 0; i < context.LineCount; i++)
            {
                var line = context.GetLine(i);
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon <= 0 || line[..colon].ContainsAnyExceptInRange('A', 'z'))
                {
                    if (first >= 0)
                    {
                        break;
                    }

                    continue;
                }

                first = first < 0 ? i : first;
                context.AddText(i, 0, colon);
                context.AddMarker(i, colon, 1);
                if (line.Length > colon + 2)
                {
                    context.AddInlines(i, colon + 2, line.Length - colon - 2);
                }

                count++;
            }

            if (count < 2)
            {
                return false;
            }

            context.Complete(first, count);
            return true;
        }
    }

    // Writes every construct of a syntax extension with the writer: its markers, its text and its inlines, or a block in a <div>
    private sealed class SyntaxExtensionRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is MarkdownExtensionBlockSyntax block)
            {
                writer.EnsureNewLine();
                writer.WriteRawHtml("<div class=\"");
                writer.WriteAttributeValue(block.Name);
                writer.WriteRawHtml("\"");
                writer.WriteAttributes(block);
                writer.WriteRawHtml(">\n");
                writer.WriteDefault(block);
                writer.EnsureNewLine();
                writer.WriteRawHtml("</div>\n");
                return true;
            }

            if (node is not MarkdownExtensionInlineSyntax extension)
            {
                return false;
            }

            writer.WriteRawHtml("<span class=\"");
            writer.WriteAttributeValue(extension.Name);
            writer.WriteRawHtml("\"");
            writer.WriteAttributes(extension);
            writer.WriteRawHtml(">");
            writer.WriteText(extension.Markers[0].Text);
            writer.WriteInlines(extension.Inlines);
            writer.WriteRawHtml("</span>");
            return true;
        }
    }
}
