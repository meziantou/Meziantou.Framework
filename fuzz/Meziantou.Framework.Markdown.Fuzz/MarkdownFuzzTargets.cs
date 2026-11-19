using System.Net;
using System.Text.RegularExpressions;
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
        .. GfmTokens, "^", "++", "==", "~a~", "^a^", "&plus;", "&reg;", "\u2212", "\u2020", "$", "$$", "$$\n", "\\$", "---\n", "...\n", "```mermaid\n", "```nomnoml\n", "> [!NOTE]\n", "[!TIP]", "[!x]  \n", "*[A]: ", "*[A B]: x\n", "A ", "[^1]", "[^1]: ", "\n    ", "[^a]:\n", "{", "}", "{#a}", "{.b}", "{c=d}", "{e=\"f\"}", "{g}\n", "# a\n", "[a]", "a\n===\n", "\u00E9", "\u00DF", "\u00C6", ":::", "::::", ":::x\n", ":::\n", "::", ":   ", "~   ", "\n:   ", "a. ", "B) ", "iv. ", "XI) ", "'", "\"", "''", "<<", ">>", "...", "--", "---", ":smile:", ":)", ":*", "<3", "]:)", "->", "o:)", "^^^", "^^^ c\n", "^^ ", "\"\"", "+---+", "+===+", "ABC-12", "(X-1)", "![](a.mp4)", "![](https://youtu.be/x)", "+:-:+", "+---+---+\n", "| a | b |\n", "||", "|-|", "| :--: |", "\\-", "\\:", "|{.c}", "-|-\n", " | ", "\u0645", "\u05D0", "\u3002", "\u30C6", "\uFF01", "| [^1] |\n", "| # a |\n", "| > [!NOTE] |\n", "| --- |\n",
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
        IdPrefix = "u-",
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

    // With AllowedUrlSchemes, the HTML is the one without it, where the href and the src of each tag whose URL has a scheme that
    // is not allowed are empty: a URL with such a scheme never comes out, and the other URLs are kept. The scheme is read as a
    // browser reads it, independently of the renderer. Raw HTML is disabled: the filter does not apply to it.
    private static readonly string[] AllowedUrlSchemes = ["http", "https", "mailto"];
    private static readonly MarkdownParseOptions[] UrlFilterParseOptions = [MarkdownParseOptions.GitHubFlavored with { DisableHtml = true }, AllExtensionsWithoutHtml];
    private static readonly MarkdownHtmlRendererOptions UnfilteredUrlOptions = new() { LinkRel = "nofollow", PragmaLines = true };
    private static readonly MarkdownHtmlRendererOptions FilteredUrlOptions = UnfilteredUrlOptions with { AllowedUrlSchemes = AllowedUrlSchemes };
    private static readonly Regex HtmlTag = new("<[a-z][^<>]*>", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
    private static readonly Regex UrlAttribute = new(" (?<name>href|src)=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
    private static readonly Regex UrlScheme = new("^(?<scheme>[A-Za-z][A-Za-z0-9+.-]*):", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
    private static readonly string[] UrlFilterTokens = [.. ExtensionTokens, "javascript:", "JavaScript:", "vbscript:", "data:", "data:text/html,", "mailto:", "http:", "https:", "c:", "a+b.c-d:", "//", "%09", "&#9;", "&#x0A;", "&colon;", "\\:", "\u0001", "\u001F", "<javascript:x>", "](javascript:", "![](data:", "](<java\tscript:x>)", "{href=x}", "{src=x}"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("markdown", data => Parse(data, MarkdownParseOptions.Default)) { Seeds = GetSeeds, Dictionary = Tokens },
        new("markdown-gfm", data => Parse(data, MarkdownParseOptions.GitHubFlavored)) { Seeds = GetGfmSeeds, Dictionary = GfmTokens },
        new("markdown-extensions", data => Parse(data, (data.Length % 4) switch { 0 => AllExtensions, 2 => AllExtensionsWithPipeTableOptions, _ => AllExtensionsWithoutHtml })) { Seeds = GetExtensionSeeds, Dictionary = ExtensionTokens },
        new("markdown-url-filter", FilterUrls) { Seeds = GetUrlFilterSeeds, Dictionary = UrlFilterTokens },
        new("markdown-edits", Edit) { Seeds = GetEditSeeds, Dictionary = ExtensionTokens },
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

    private static void FilterUrls(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var tree = MarkdownSyntaxTree.ParseText(text, UrlFilterParseOptions[data.Length % UrlFilterParseOptions.Length]);
        var unfiltered = MarkdownHtmlRenderer.ToHtml(tree, UnfilteredUrlOptions);
        var filtered = MarkdownHtmlRenderer.ToHtml(tree, FilteredUrlOptions);

        foreach (Match tag in HtmlTag.Matches(filtered))
        {
            foreach (Match attribute in UrlAttribute.Matches(tag.Value))
            {
                FuzzAssert.True(IsAllowedUrl(attribute.Groups["value"].Value), $"The URL {FuzzAssert.Format(attribute.Value)} has a scheme that is not allowed: {FuzzAssert.Format(text)}");
            }
        }

        var expected = HtmlTag.Replace(unfiltered, tag => UrlAttribute.Replace(tag.Value, attribute => IsAllowedUrl(attribute.Groups["value"].Value) ? attribute.Value : $" {attribute.Groups["name"].Value}=\"\""));
        FuzzAssert.True(filtered == expected, $"The filtered HTML {FuzzAssert.Format(filtered)} is not the HTML {FuzzAssert.Format(expected)}: {FuzzAssert.Format(text)}");

        // The browsers ignore the C0 controls and the spaces at the start of a URL, and remove its tabs and line endings
        static bool IsAllowedUrl(string value)
        {
            var url = WebUtility.HtmlDecode(value).TrimStart(Enumerable.Range(0, 0x21).Select(c => (char)c).ToArray()).Replace("\t", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
            return UrlScheme.Match(url) is not { Success: true } match || AllowedUrlSchemes.Contains(match.Groups["scheme"].Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<FuzzSeed> GetUrlFilterSeeds()
    {
        string[] texts =
        [
            "[a](javascript:alert(1)) [b](https://example.com) [c](/relative) ![d](data:image/png;base64,AA==) <vbscript:x>", "[a](JavaScript:x)", "[a](<java\tscript:x>)", "[a](HTTPS://a.b)",
            "[a](./javascript:x) [b](#javascript:x) [c](?javascript:x) [d](//a.b/c)", "[a](c:/x) [b](1a:x)", "<mailto:a@b.c> <a@b.c>", "![](https://youtu.be/x) ![](javascript:a.mp4)",
            "[a]: javascript:x\n\n[a] ![a]", "[a](&#106;avascript:x) [b](java&#x09;script:x) [c](javascript&colon;x)", "x www.example.com javascript:x mailto:a@b.c tel:+1", "[a](u){href=javascript:x src=data:x}",
        ];
        for (var i = 0; i < texts.Length; i++)
        {
            yield return FuzzSeed.FromText($"url-filter-{i + 1}", texts[i]);
        }

        foreach (var seed in GetGfmSeeds())
        {
            yield return seed;
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
            if (options.DisableHtml)
            {
                // The HTML of the text is text, so every id is one the renderer writes: the ids of the document have the prefix, and
                // so do the links to the footnotes and back
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, "(?: id=\"|href=\"#)(?<id>[^\"]*)\"", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
                {
                    var id = match.Groups["id"].Value;
                    FuzzAssert.True(id.StartsWith("u-", StringComparison.Ordinal) || (match.Value.StartsWith(" id", StringComparison.Ordinal) && id.StartsWith("pragma-line-", StringComparison.Ordinal)) || (match.Value.StartsWith("href", StringComparison.Ordinal) && !id.StartsWith("fn", StringComparison.Ordinal)), $"The id {FuzzAssert.Format(id)} of {FuzzAssert.Format(match.Value)} has no prefix: {FuzzAssert.Format(text)}");
                }
            }
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
        CheckNestingDepth(text, options);
    }

    // With a small limit, the deeper constructs are text: the tree still reproduces the text, and the blocks and the inlines
    // are nested in at most as many blocks or inlines as the limit, and the blocks that a block adds at once, such as a list
    // and its first item
    private static void CheckNestingDepth(string text, MarkdownParseOptions options)
    {
        var limit = 1 + (text.Length % 3);
        var tree = MarkdownSyntaxTree.ParseText(text, options with { MaximumNestingDepth = limit });
        SyntaxTreeInvariants.Check(tree, text, "Markdown", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case MarkdownBlockSyntax:
                    var blocks = node.Ancestors().Count(ancestor => ancestor is MarkdownBlockSyntax);
                    FuzzAssert.True(blocks <= limit + 3, $"The block at {node.Span} is nested in {blocks} blocks, more than the limit {limit}: {FuzzAssert.Format(text)}");
                    break;

                case MarkdownInlineSyntax:
                    var inlines = node.Ancestors().TakeWhile(ancestor => ancestor is not MarkdownBlockSyntax).Count(ancestor => ancestor is MarkdownInlineSyntax);
                    FuzzAssert.True(inlines <= limit, $"The inline at {node.Span} is nested in {inlines} inlines, more than the limit {limit}: {FuzzAssert.Format(text)}");
                    break;
            }
        }

        _ = MarkdownHtmlRenderer.ToHtml(tree);
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
                        CheckCellContent(tree, grid, cell, text);
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

        // With the cells of the grid tables, the walker also visits the document of each cell, and the nodes in it
        var cellWalker = new CountingWalker(visitGridTableCells: true);
        cellWalker.Visit(root);
        var cellCount = CountNodesWithCells(root);
        FuzzAssert.True(cellWalker.Count == cellCount, $"The walker of the grid table cells visits {cellWalker.Count} nodes of {cellCount}: {FuzzAssert.Format(text)}");
        if (tree.Options.Extensions.HasFlag(MarkdownExtensions.AutoIdentifiers))
        {
            // The identifiers that are not ids of generic attributes are unique, the ones of the headings of the cells included
            var identifiers = cellWalker.Headings.Select(heading => heading.Identifier!).Where(identifier => !cellWalker.Ids.Contains(identifier)).ToList();
            FuzzAssert.True(identifiers.Count == identifiers.Distinct(StringComparer.Ordinal).Count(), $"Two headings have the same identifier: {FuzzAssert.Format(string.Join(", ", identifiers))}: {FuzzAssert.Format(text)}");
        }
        FuzzAssert.True(ReferenceEquals(new MarkdownSyntaxRewriter().Visit(root), root), $"A rewrite that changes nothing changes the tree: {FuzzAssert.Format(text)}");

        if (text.Length < 4096 && root.DescendantNodes().OfType<MarkdownBlockSyntax>().LastOrDefault() is { } block)
        {
            _ = tree.RemoveBlock(block);
            _ = tree.InsertBlocksBefore(block, "- a\n\n> b");
            _ = tree.InsertBlocksAfter(block, "\nc\n");
            _ = tree.ReplaceBlock(block, "- d\n\n> e\n");
        }

        if (text.Length < 4096)
        {
            CheckEdits(tree, text);
        }
    }

    private static int CountNodesWithCells(MarkdownSyntaxNode node)
    {
        var count = 0;
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            count++;
            if (descendant is MarkdownGridTableSyntax grid)
            {
                count += grid.Rows.SelectMany(row => row.Cells).Sum(cell => CountNodesWithCells(cell.Content.GetRoot()));
            }
        }

        return count;
    }

    // The spans of the content of a cell map to the text of the table, where the text of a line of the cell is the same, and the
    // footnotes of the cell are the ones of the document
    private static void CheckCellContent(MarkdownSyntaxTree tree, MarkdownGridTableSyntax grid, MarkdownGridTableCell cell, string text)
    {
        var content = cell.Content;
        foreach (var token in content.GetRoot().DescendantTokens())
        {
            var span = cell.MapSpan(token.Span);
            FuzzAssert.True(span.Start >= grid.Span.Start && span.End <= grid.Span.End, $"The token at {token.Span} of a cell maps to {span}, out of the grid table at {grid.Span}: {FuzzAssert.Format(text)}");
            if (!token.Text.Contains('\n', StringComparison.Ordinal))
            {
                FuzzAssert.True(string.Equals(text.Substring(span.Start, span.Length), token.Text, StringComparison.Ordinal), $"The token {FuzzAssert.Format(token.Text)} at {token.Span} of a cell maps to {FuzzAssert.Format(text.Substring(span.Start, span.Length))}: {FuzzAssert.Format(text)}");
            }
        }

        FuzzAssert.True(!content.GetRoot().Blocks.OfType<MarkdownFootnoteDefinitionSyntax>().Any() && !content.GetRoot().Blocks.OfType<MarkdownFrontMatterSyntax>().Any(), $"A cell of the grid table at {grid.Span} has a footnote or a front matter: {FuzzAssert.Format(text)}");
        foreach (var reference in content.GetRoot().DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>())
        {
            FuzzAssert.True(reference.Definition is null || ReferenceEquals(reference.Definition.SyntaxTree, tree), $"The footnote reference at {reference.Span} of a cell has a definition out of the document: {FuzzAssert.Format(text)}");
        }
    }

    private const string EditHeading = "fzq";

    // The inputs of the unit tests of the edits, then the examples of the extensions
    private static readonly string[] EditSeeds =
    [
        "p\n\n    code\n    more\n", "^^ footer text\n", "> ^^ footer text\n", "[^longlabel]: text\n\n    more\n", "Term\n:   text\n", "para1\n# h\npara2\n", "a\n***\n---\n",
        "> a\n> # h\nb\n", "- a\n- b\n- c\n", "- a\n\n  b\n", "- a\n\n      code\n", "> - # a\n>   b\n", "```\ncode\n", "> - ~~~\n>   code\n", ">- a\n", " 1.  a\n", ">\t\tcode\n",
        "> a  \n> *b\n> c*\n> ---", "a #\n===\n", "# a\n\nb\n\n- c\n- d\n\ne\n", "- a\n\n  b\n\n  c\n\n  d\n", "[^1]: ```\n    code\n", "::: x\na\n", "-\ta\n\n\tb\n",
        "para1\r\n# h\r\npara2\r\n", "> a\n# h\n> b\n", "- \n  a\n\n  b\n", "- a\n  - -\n", "> [a]: /u\nb\n", "* a\n  > b\n  >\n* c\n", "::: a\n::: b\n```\nc\n",
        "::: a\n::: b\nc\n:::\n", "> [!NOTE]\n> text\n>\n> more\n", "> -     code\n",
    ];

    private static IEnumerable<FuzzSeed> GetEditSeeds() => EditSeeds.Select((text, index) => FuzzSeed.FromText($"edit-{index + 1}", text)).Concat(GetExtensionSeeds());

    // A heading that replaces a block, or that is inserted before or after it, is in the containers of the block: the markers of the
    // containers are written on its lines, and the indentation of an indented code block or a fenced block that is not closed do not
    // take it. Removing a block keeps the blocks around it apart. Several changes of an editor are read as each of them, and every tree
    // renders as its text parsed again.
    private static void Edit(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (text.Length > 2048)
            return;

        var options = data.Length % 2 == 0 ? AllExtensions : MarkdownParseOptions.GitHubFlavored;
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        var blocks = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Where(IsEditableBlock).Take(6).ToList();
        foreach (var block in blocks)
        {
            var removed = tree.RemoveBlock(block);
            CheckReparse(removed, text);
            var replaced = tree.ReplaceBlock(block, "# " + EditHeading + "\n\n");
            var before = tree.InsertBlocksBefore(block, "# " + EditHeading + "\n\n");
            var after = tree.InsertBlocksAfter(block, "\n# " + EditHeading + "\n");
            CheckReparse(replaced, text);
            CheckReparse(before, text);
            CheckReparse(after, text);
            if (HasMarkdigPlacement(block))
                continue;

            CheckRemoval(block, removed, text);
            CheckHeading(tree, block, replaced, "replacing", text);
            CheckHeading(tree, block, before, "inserting before", text);

            // An HTML block or a block of an extension that is not closed reads the lines after it. In a custom container or a figure that
            // is not closed, the fence that would close a custom container or a figure in it closes the outer one first.
            var lastBlocks = block.DescendantNodesAndSelf().OfType<MarkdownBlockSyntax>().Where(node => node.Span.End == block.Span.End);
            if (!lastBlocks.Any(node => node is MarkdownHtmlBlockSyntax or MarkdownExtensionBlockSyntax) && !(IsInFencedContainer(block) && lastBlocks.Any(node => node is MarkdownCustomContainerSyntax or MarkdownFigureSyntax)))
            {
                CheckHeading(tree, block, after, "inserting after", text);
            }
        }

        // The leaf blocks, none of which contains another, replaced together
        var editor = new MarkdownSyntaxTreeEditor(tree);
        var leaves = blocks.Where(block => !block.DescendantNodes().OfType<MarkdownBlockSyntax>().Any() && !HasMarkdigPlacement(block)).ToList();
        foreach (var leaf in leaves)
        {
            editor.ReplaceBlock(leaf, "# " + EditHeading + "\n\n");
        }

        var edited = editor.GetChangedTree();
        CheckReparse(edited, text);
        foreach (var leaf in leaves)
        {
            var chain = GetContainerChain(leaf);
            FuzzAssert.True(edited.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().Count(heading => heading.GetPlainText() == EditHeading && GetContainerChain(heading) == chain) >= leaves.Count(other => GetContainerChain(other) == chain), $"The editor does not replace the block at {leaf.Span} with a heading in its containers: {FuzzAssert.Format(text)}");
        }
    }

    // The blocks around a removed block keep their kinds: the lines around it are not read as one block, such as two paragraphs or a
    // paragraph and a thematic break that would make it a setext heading. The check is for a block between two blocks whose lines can
    // only be read as such blocks: the first line of a block after a table or a link reference definition, or a lazy continuation
    // line, can depend on the lines before it. A paragraph that starts with : or ~ can be the definition of the paragraph before it.
    private static void CheckRemoval(MarkdownBlockSyntax block, MarkdownSyntaxTree removed, string text)
    {
        var parent = block.Parent!;
        if (parent is not (MarkdownDocumentSyntax or MarkdownBlockQuoteSyntax or MarkdownListItemSyntax) || block is MarkdownLinkReferenceDefinitionSyntax or MarkdownAbbreviationDefinitionSyntax || block.DescendantNodes().OfType<MarkdownBlockSyntax>().Any() || text.Contains('\t', StringComparison.Ordinal) || text.Contains('|', StringComparison.Ordinal))
            return;

        var siblings = parent.ChildNodes().OfType<MarkdownBlockSyntax>().ToList();
        var index = siblings.IndexOf(block);
        if (index == 0 || index == siblings.Count - 1 || !IsSimpleBlock(siblings[index - 1]) || !IsSimpleBlock(siblings[index + 1]) || siblings[index + 1].ToString().TrimStart().StartsWith(':', StringComparison.Ordinal) || siblings[index + 1].ToString().TrimStart().StartsWith('~', StringComparison.Ordinal))
            return;

        // The container in the new tree, at the same place in its ancestors
        SyntaxNode? updated = removed.GetRoot();
        foreach (var ancestor in parent.AncestorsAndSelf().Reverse().Skip(1))
        {
            var position = ancestor.Parent!.ChildNodes().ToList().IndexOf(ancestor);
            updated = updated?.ChildNodes().ElementAtOrDefault(position);
        }

        var expected = siblings.Where(sibling => sibling != block).Select(sibling => sibling.RawKind);
        FuzzAssert.True(updated is not null && updated.RawKind == parent.RawKind && updated.ChildNodes().OfType<MarkdownBlockSyntax>().Select(child => child.RawKind).SequenceEqual(expected), $"Removing the block at {block.Span} changes the other blocks of its container: {FuzzAssert.Format(text)} gives {FuzzAssert.Format(removed.GetRoot().ToFullString())}");

        static bool IsSimpleBlock(MarkdownBlockSyntax block) => block is MarkdownParagraphSyntax or MarkdownHeadingSyntax or MarkdownThematicBreakSyntax or MarkdownCodeBlockSyntax { IsFenced: true };
    }

    // Whether Markdig places the blocks around the block by rules that the edits do not follow: a definition reads the lines that are
    // not indented after it, and the blocks they start are after the definition list or in it. A line that is not indented after a
    // footnote continues it, and a blank line closes the blocks of a footnote, such as a list. The lines after a definition list, at the
    // indentation of a definition, continue it, and a grid table reads the lines after it as Markdig does.
    private static bool HasMarkdigPlacement(MarkdownBlockSyntax block)
    {
        // The lines of a grid table that is not one are the paragraph they continue, and the lines after it continue the blocks of its
        // cells as Markdig reads them
        var text = block.SyntaxTree!.GetText().ToString();
        var extensions = ((MarkdownSyntaxTree)block.SyntaxTree).Options.Extensions;
        if (extensions.HasFlag(MarkdownExtensions.GridTables) && (text.StartsWith('+', StringComparison.Ordinal) || text.Contains("\n+", StringComparison.Ordinal)))
            return true;

        // A --- that starts the text is front matter when a line closes it, which an inserted line can be
        if (extensions.HasFlag(MarkdownExtensions.YamlFrontMatter) && text.StartsWith("---", StringComparison.Ordinal))
            return true;

        if (block is MarkdownDefinitionListSyntax or MarkdownGridTableSyntax || block.Ancestors().Any(ancestor => ancestor is MarkdownDefinitionItemSyntax or MarkdownFootnoteDefinitionSyntax))
            return true;

        var siblings = block.Parent!.ChildNodes().OfType<MarkdownBlockSyntax>().ToList();
        var index = siblings.IndexOf(block);
        return index > 0 && siblings[index - 1] is MarkdownDefinitionListSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownGridTableSyntax;
    }

    private static bool IsInFencedContainer(MarkdownBlockSyntax block) => block.Ancestors().Any(ancestor => ancestor is MarkdownCustomContainerSyntax or MarkdownFigureSyntax);

    // A block in a container that is not a list or a table, which can have any block next to it, except the paragraph that starts a task
    // list item, after its [ ]
    private static bool IsEditableBlock(MarkdownBlockSyntax block)
        => block is not (MarkdownDefinitionTermSyntax or MarkdownFigureCaptionSyntax)
            && !(block.Parent is MarkdownListItemSyntax { IsTask: true } item && item.Blocks[0] == block)
            && block.Parent is MarkdownDocumentSyntax or MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownDefinitionItemSyntax or MarkdownFooterSyntax or MarkdownCustomContainerSyntax or MarkdownFigureSyntax;

    private static void CheckHeading(MarkdownSyntaxTree tree, MarkdownBlockSyntax block, MarkdownSyntaxTree updated, string edit, string text)
    {
        CheckReparse(updated, text);
        var chain = GetContainerChain(block);
        FuzzAssert.True(updated.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().Count(heading => heading.GetPlainText() == EditHeading && GetContainerChain(heading) == chain) > tree.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().Count(heading => heading.GetPlainText() == EditHeading && GetContainerChain(heading) == chain), $"After {edit} the block at {block.Span}, the heading is not in the containers of the block: {FuzzAssert.Format(text)} gives {FuzzAssert.Format(updated.GetRoot().ToFullString())}");
    }

    // The tree of an edit is the one of its text
    private static void CheckReparse(MarkdownSyntaxTree tree, string text)
    {
        var reparsed = MarkdownSyntaxTree.ParseText(tree.GetRoot().ToFullString(), tree.Options);
        FuzzAssert.True(MarkdownHtmlRenderer.ToHtml(reparsed) == MarkdownHtmlRenderer.ToHtml(tree), $"The tree of an edit does not render as its text: {FuzzAssert.Format(text)}");
    }

    private static string GetContainerChain(SyntaxNode node) => string.Join('/', node.Ancestors().Select(ancestor => ancestor.RawKind));

    // The nodes that the With methods return are read back with the values they were given. A node is read alone, when it is read
    // alone as it is in the tree: in the tree, the new text can change how the text around it is read, as any Markdown text, such as
    // a title removed from a definition that lets the next line be its title. The tree with the node replaced is parsed again.
    private static void CheckEdits(MarkdownSyntaxTree tree, string text)
    {
        const string Destination = "a b|(\\c<";
        const string Title = "t\"'|)\\\n2";
        const string Info = "~x`y {#a} \\& z";

        var options = tree.Options;
        var root = tree.GetRoot();
        foreach (var heading in root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Take(3))
        {
            var level = heading.IsSetext ? 3 - heading.Level : (heading.Level % 6) + 1;
            var leveled = heading.WithLevel(level);
            Replace(tree, heading, leveled);
            if (ParseAlone<MarkdownHeadingSyntax>(heading, options) is { } alone && alone.Level == heading.Level && alone.IsSetext == heading.IsSetext)
            {
                FuzzAssert.True(ParseAlone<MarkdownHeadingSyntax>(leveled, options)?.Level == level, $"The heading at {heading.Span} is not of level {level} once changed: {FuzzAssert.Format(text)}");
            }

            // A setext heading of level 3 to 6 becomes an ATX heading, on one line
            if (heading.IsSetext)
            {
                var atxLevel = heading.Level + 4;
                var atx = heading.WithLevel(atxLevel);
                Replace(tree, heading, atx);
                FuzzAssert.True(!atx.IsSetext && !atx.ToString().AsSpan().ContainsAny('\r', '\n'), $"The heading at {heading.Span} is not an ATX heading on one line once of level {atxLevel}: {FuzzAssert.Format(text)}");
                if (ParseAlone<MarkdownHeadingSyntax>(heading, options) is { IsSetext: true } setext && setext.Level == heading.Level)
                {
                    FuzzAssert.True(ParseAlone<MarkdownHeadingSyntax>(atx, options)?.Level == atxLevel, $"The heading at {heading.Span} is not of level {atxLevel} once changed: {FuzzAssert.Format(text)}");
                }
            }
        }

        // The tree checks that the block gets the attributes it is given, and refuses the ones it cannot write: the block of the same
        // kind has them in the new tree
        if (options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
        {
            foreach (var block in root.DescendantNodes().OfType<MarkdownBlockSyntax>().Take(4))
            {
                foreach (var attributes in (MarkdownGenericAttributes?[])[FuzzAttributes, null])
                {
                    MarkdownSyntaxTree updated;
                    try
                    {
                        updated = tree.WithGenericAttributes(block, attributes);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    var expected = attributes ?? MarkdownGenericAttributes.Empty;
                    FuzzAssert.True(updated.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Any(node => node.RawKind == block.RawKind && node.GetGenericAttributes().Equals(expected)), $"The block at {block.Span} does not have the attributes it was given: {FuzzAssert.Format(text)}");
                }
            }
        }

        foreach (var link in root.DescendantNodes().OfType<MarkdownLinkSyntax>().Where(link => link.LinkKind == MarkdownLinkKind.Inline).Take(3))
        {
            var changed = link.WithDestination(Destination).WithTitle(Title);
            var untitled = link.WithTitle(null);
            Replace(tree, link, changed);
            Replace(tree, link, untitled);
            // A < in the text can start raw HTML that the > of the destination in angle brackets would end
            if (!link.Text.Any(child => child.ToString().Contains('<', StringComparison.Ordinal)) && StartsOnTheFirstLine(link, link.DestinationToken) && ParseAlone<MarkdownLinkSyntax>(link, options) is { LinkKind: MarkdownLinkKind.Inline } alone && alone.Destination == link.Destination && alone.Title == link.Title)
            {
                FuzzAssert.True(ParseAlone<MarkdownLinkSyntax>(changed, options) is { } read && read.Destination == Destination && read.Title == Title, $"The link at {link.Span} does not have the destination and the title it was given: {FuzzAssert.Format(text)}");
                FuzzAssert.True(ParseAlone<MarkdownLinkSyntax>(untitled, options) is { Title: null } removed && removed.Destination == link.Destination, $"The link at {link.Span} keeps a title once removed: {FuzzAssert.Format(text)}");
            }
        }

        foreach (var definition in root.DescendantNodes().OfType<MarkdownLinkReferenceDefinitionSyntax>().Take(3))
        {
            var changed = definition.WithDestination(Destination).WithTitle(Title);
            var untitled = definition.WithTitle(null);
            Replace(tree, definition, changed);
            Replace(tree, definition, untitled);
            if (StartsOnTheFirstLine(definition, definition.DestinationToken) && ParseAlone<MarkdownLinkReferenceDefinitionSyntax>(definition, options) is { } alone && alone.Destination == definition.Destination && alone.Title == definition.Title)
            {
                FuzzAssert.True(ParseAlone<MarkdownLinkReferenceDefinitionSyntax>(changed, options) is { } read && read.Destination == Destination && read.Title == Title, $"The definition at {definition.Span} does not have the destination and the title it was given: {FuzzAssert.Format(text)}");
                FuzzAssert.True(ParseAlone<MarkdownLinkReferenceDefinitionSyntax>(untitled, options) is { Title: null } removed && removed.Destination == definition.Destination, $"The definition at {definition.Span} keeps a title once removed: {FuzzAssert.Format(text)}");
            }
        }

        foreach (var codeBlock in root.DescendantNodes().OfType<MarkdownCodeBlockSyntax>().Where(block => block.IsFenced).Take(3))
        {
            var changed = codeBlock.WithInfo(Info);
            var removed = codeBlock.WithInfo(null);
            Replace(tree, codeBlock, changed);
            Replace(tree, codeBlock, removed);
            if (ParseAlone<MarkdownCodeBlockSyntax>(codeBlock, options) is { IsFenced: true } alone && alone.Info == codeBlock.Info && alone.Code == codeBlock.Code)
            {
                FuzzAssert.True(ParseAlone<MarkdownCodeBlockSyntax>(changed, options) is { } read && read.Info == Info && read.Code == codeBlock.Code, $"The code block at {codeBlock.Span} does not have the info string it was given: {FuzzAssert.Format(text)}");
                FuzzAssert.True(ParseAlone<MarkdownCodeBlockSyntax>(removed, options) is { Info: null } read2 && read2.Code == codeBlock.Code, $"The code block at {codeBlock.Span} keeps an info string once removed: {FuzzAssert.Format(text)}");
            }
        }
    }

    // Whether the token is on the first line of the node: a new token at the end of a line can change how the start of the line is
    // read, such as a line of a label that starts with ``` and becomes a fence once the backticks after it are gone
    private static bool StartsOnTheFirstLine(MarkdownSyntaxNode node, SyntaxToken token)
    {
        var end = token.RawKind == 0 ? node.Span.End : token.SpanStart;
        return !node.SyntaxTree!.GetText().ToString().AsSpan(node.SpanStart, end - node.SpanStart).ContainsAny('\r', '\n');
    }

    private static readonly MarkdownGenericAttributes FuzzAttributes = new("fz-id", ["fz"], [new("data-fz", "a b'"), new("fz", null)]);

    // The text of the tree with the node replaced reproduces the text around the node, and is parsed again
    private static void Replace(MarkdownSyntaxTree tree, MarkdownSyntaxNode node, MarkdownSyntaxNode replacement)
    {
        var text = tree.GetRoot().ToFullString();
        var updated = tree.GetRoot().ReplaceNode(node, replacement).ToFullString();
        var expected = string.Concat(text.AsSpan(0, node.FullSpan.Start), replacement.ToFullString(), text.AsSpan(node.FullSpan.End));
        FuzzAssert.True(updated == expected, $"Replacing the node at {node.Span} changes the text around it: {FuzzAssert.Format(text)}");
        _ = MarkdownSyntaxTree.ParseText(updated, tree.Options).GetRoot().DescendantNodes().Count();
    }

    // Parses the text of the node, without the trivia around it, and finds the node of the same type that it is read as, whole: the
    // whitespace that starts it can be trivia, such as a heading whose text is a form feed, before the attributes it is given
    private static TNode? ParseAlone<TNode>(TNode node, MarkdownParseOptions options)
        where TNode : MarkdownSyntaxNode
    {
        var text = node.ToString();
        return MarkdownSyntaxTree.ParseText(text, options).GetRoot().DescendantNodes().OfType<TNode>().FirstOrDefault(other => other.Span.End == text.Length && text.AsSpan(0, other.SpanStart).IsWhiteSpace());
    }

    private sealed class CountingWalker(bool visitGridTableCells = false) : MarkdownSyntaxWalker(SyntaxWalkerDepth.Node, visitGridTableCells)
    {
        public int Count { get; private set; }

        public List<MarkdownHeadingSyntax> Headings { get; } = [];

        // The ids of the generic attributes
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

        public override void DefaultVisit(MarkdownSyntaxNode node)
        {
            Count++;
            switch (node)
            {
                case MarkdownHeadingSyntax heading:
                    Headings.Add(heading);
                    break;
                case MarkdownGenericAttributesSyntax { Id: { } id }:
                    Ids.Add(id);
                    break;
            }

            base.DefaultVisit(node);
        }
    }

    // The examples of the CommonMark specification
    private static IEnumerable<FuzzSeed> GetSeeds() => GetSeeds("markdown.CommonMark.md", "commonmark");

    // The examples of the CommonMark specification and of the extensions of GitHub Flavored Markdown
    private static IEnumerable<FuzzSeed> GetGfmSeeds() => GetSeeds("markdown.CommonMark.md", "commonmark").Concat(GetSeeds("markdown.GfmExtensions.md", "gfm"));

    // The examples of the GFM extensions and of the extensions of the 1.x library
    // The inputs of the unit tests of the content of the grid table cells: footnotes, headings, front matter and alerts
    private static readonly string[] GridTableCellSeeds =
    [
        "+-------+\n| a[^1] |\n+-------+\n\n[^1]: note\n",
        "+---------+\n| y[^b]   |\n+---------+\n\nz[^a] w[^b]\n\n[^a]: A\n[^b]: B\n",
        "+-----------+\n| [^1]: a   |\n|           |\n| b         |\n+-----------+\n",
        "+-------------+\n| [^1]: a     |\n|             |\n| b [^1]      |\n+-------------+\n\nc [^2]\n\n[^2]: z\n",
        "+---------+\n| ---     |\n| secret  |\n| ---     |\n| visible |\n+---------+\n",
        "+-----------+\n| > [!NOTE] |\n| > a       |\n+-----------+\n",
        "# h\n\n+-----+\n| # h |\n+-----+\n\n# h\n",
        "+-----------+\n| # h {#x}  |\n+-----------+\n\n# x\n",
        "# a {#b}\n# b\n# b\n",
        "Intro\n\n+---------------+\n| See [a](/u)   |\n| and [b](/v)   |\n+---------------+\n",
        "+---+\n| a |\n+===+\n| b |\n+===+\n| c |\n+---+\n",
        "+---+---+---+\n| a\n+---+---+---+\n| b | c\n+---+---+---+\n",
        "-\0a\n",
    ];

    private static IEnumerable<FuzzSeed> GetExtensionSeeds()
    {
        var seeds = GetGfmSeeds().Concat(GridTableCellSeeds.Select((text, i) => FuzzSeed.FromText($"grid-cells-{i + 1}", text)));
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
