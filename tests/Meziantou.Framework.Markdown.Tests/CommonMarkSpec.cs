namespace Meziantou.Framework.Markdown.Tests;

/// <summary>
/// The examples of the CommonMark specification, and of the extensions of GitHub Flavored Markdown: a Markdown document
/// and the HTML it renders as.
/// </summary>
internal static class CommonMarkSpec
{
    private const string Fence = "````````````````````````````````";

    private static readonly Lazy<IReadOnlyList<Example>> LazyExamples = new(() => Load("CommonMark.md"));
    private static readonly Lazy<IReadOnlyList<Example>> LazyGfmExamples = new(() => Load("GfmExtensions.md"));

    public static IReadOnlyList<Example> Examples => LazyExamples.Value;

    public static IReadOnlyList<Example> GfmExamples => LazyGfmExamples.Value;

    public static TheoryData<int> ExampleNumbers() => GetNumbers(Examples);

    public static TheoryData<int> GfmExampleNumbers() => GetNumbers(GfmExamples);

    public static Example Get(int number) => Examples[number - 1];

    public static Example GetGfm(int number) => GfmExamples[number - 1];

    private static TheoryData<int> GetNumbers(IReadOnlyList<Example> examples)
    {
        var data = new TheoryData<int>();
        foreach (var example in examples)
        {
            data.Add(example.Number);
        }

        return data;
    }

    /// <summary>Loads the examples of a specification file of the <c>Specs</c> folder.</summary>
    public static List<Example> Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Specs", fileName);
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
        var examples = new List<Example>();
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) || lines[i].StartsWith("# ", StringComparison.Ordinal))
            {
                section = lines[i].TrimStart('#', ' ');
                continue;
            }

            if (!lines[i].StartsWith(Fence + " example", StringComparison.Ordinal))
            {
                continue;
            }

            var markdown = new List<string>();
            i++;
            while (lines[i] != ".")
            {
                markdown.Add(lines[i]);
                i++;
            }

            var html = new List<string>();
            i++;
            while (lines[i] != Fence)
            {
                html.Add(lines[i]);
                i++;
            }

            // The specification shows a tab as an arrow
            examples.Add(new Example(examples.Count + 1, section, ToText(markdown), ToText(html)));
        }

        return examples;
    }

    private static string ToText(List<string> lines) => lines.Count == 0 ? "" : (string.Join('\n', lines) + "\n").Replace('→', '\t');

    public sealed record Example(int Number, string Section, string Markdown, string Html);

    // The default style, and the other choices of each option
    private static readonly MarkdownFormattingOptions[] FormattingOptions =
    [
        MarkdownFormattingOptions.Default,
        new() { NewLine = "\r\n", BulletListMarker = '*', RenumberOrderedLists = false, UseAtxHeadings = false, UseFencedCodeBlocks = false, CodeFenceCharacter = '~', ThematicBreakCharacter = '_', AlignTables = false },
        new() { BulletListMarker = null, ThematicBreakCharacter = '*' },
        new() { BulletListMarker = '+' },
    ];

    /// <summary>Asserts that the formatted text renders as the same HTML, and that formatting it again does not change it.</summary>
    public static void AssertFormattingKeepsTheHtml(string markdown, MarkdownParseOptions options, string message)
    {
        var tree = MarkdownSyntaxTree.ParseText(markdown, options);
        var html = MarkdownHtmlRenderer.ToHtml(tree).ReplaceLineEndings("\n");
        foreach (var formattingOptions in FormattingOptions)
        {
            var formatted = MarkdownFormatter.Format(tree, formattingOptions);
            var text = formatted.GetRoot().ToFullString();

            Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(formatted).ReplaceLineEndings("\n"), $"{message}, formatted as:\n{text}");
            Assert.Equal(text, MarkdownFormatter.FormatText(formatted, formattingOptions), $"{message}, formatted twice");
        }
    }
}
