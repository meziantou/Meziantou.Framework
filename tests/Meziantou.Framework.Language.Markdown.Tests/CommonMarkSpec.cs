namespace Meziantou.Framework.Language.Markdown.Tests;

/// <summary>The examples of the CommonMark specification: a Markdown document and the HTML it renders as.</summary>
internal static class CommonMarkSpec
{
    private const string Fence = "````````````````````````````````";

    private static readonly Lazy<IReadOnlyList<Example>> LazyExamples = new(Load);

    public static IReadOnlyList<Example> Examples => LazyExamples.Value;

    public static TheoryData<int> ExampleNumbers()
    {
        var data = new TheoryData<int>();
        foreach (var example in Examples)
        {
            data.Add(example.Number);
        }

        return data;
    }

    public static Example Get(int number) => Examples[number - 1];

    private static List<Example> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Specs", "CommonMark.md");
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
}
