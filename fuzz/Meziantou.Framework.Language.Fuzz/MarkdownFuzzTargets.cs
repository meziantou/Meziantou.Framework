using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Markdown;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// Markdown documents, as CommonMark and as GitHub Flavored Markdown: the invariants every tree shares, which include that
/// the tree reproduces the text, and that every character that is not whitespace or a line ending is read by a construct:
/// a token, or the marker of a container on its continuation lines. The HTML renderer reads the tree without throwing.
/// </summary>
internal static class MarkdownFuzzTargets
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

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-markdown", data => Parse(data, MarkdownParseOptions.Default)) { Seeds = GetSeeds, Dictionary = Tokens },
        new("language-markdown-gfm", data => Parse(data, MarkdownParseOptions.GitHubFlavored)) { Seeds = GetGfmSeeds, Dictionary = GfmTokens },
        new("language-markdown-extensions", data => Parse(data, (data.Length % 4) switch { 0 => AllExtensions, 2 => AllExtensionsWithPipeTableOptions, _ => AllExtensionsWithoutHtml })) { Seeds = GetExtensionSeeds, Dictionary = ExtensionTokens },
    ];

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
        if (options != MarkdownParseOptions.Default)
        {
            // The options of the renderer that read the whole tree: the pragma lines, and the URLs
            var html = MarkdownHtmlRenderer.ToHtml(tree, RendererOptions);
            var ids = System.Text.RegularExpressions.Regex.Matches(html, "id=\"(?<id>pragma-line-[0-9]+)\"", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)).Select(match => match.Groups["id"].Value).ToList();
            FuzzAssert.True(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), $"A pragma line is the id of two elements: {FuzzAssert.Format(text)}");
            _ = tree.GetRoot().FindClosestLine(text.Length % 7);
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
}
