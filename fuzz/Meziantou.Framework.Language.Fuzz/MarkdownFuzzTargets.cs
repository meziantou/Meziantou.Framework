using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Markdown;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// Markdown documents: the invariants every tree shares, which include that the tree reproduces the text, and that every
/// character that is not whitespace or a line ending is read by a construct: a token, or the marker of a container on its
/// continuation lines. The HTML renderer reads the tree without throwing.
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

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-markdown", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var tree = MarkdownSyntaxTree.ParseText(text);
        SyntaxTreeInvariants.Check(tree, text, "Markdown", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);

        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                FuzzAssert.True(trivia.RawKind != (int)SyntaxKind.SkippedTextTrivia, $"The text {FuzzAssert.Format(trivia.ToString())} at {trivia.Span} is read by no construct: {FuzzAssert.Format(text)}");
            }
        }

        _ = MarkdownHtmlRenderer.ToHtml(tree);
    }

    // The examples of the CommonMark specification
    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        var lines = FuzzResources.ReadText(typeof(MarkdownFuzzTargets).Assembly, "markdown.CommonMark.md").ReplaceLineEndings("\n").Split('\n');
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
            yield return FuzzSeed.FromText($"commonmark-{number}", text);
        }
    }
}
