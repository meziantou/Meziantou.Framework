using Xunit.Sdk;

namespace Meziantou.Framework.Imaging.Tests.Documentation;

/// <summary>
/// Keeps the published C# examples compiled: every C# block of the published Markdown files (the package readme.md
/// of src/Meziantou.Framework.Imaging) must be preceded by <c>&lt;!-- snippet: name --&gt;</c> and be
/// exactly the snippet <c>name</c> of the sample sources (<c>// begin-snippet: name</c> ... <c>// end-snippet</c>), which are
/// compiled by the samples project and by this test project (<see cref="DocumentationExampleTests"/> runs them).
/// The files are copied to <c>&lt;test output&gt;/Documentation</c> by the project file.
/// </summary>
public sealed class DocumentationSnippetTests
{
    private const string SnippetMarker = "<!-- snippet:";
    private const string BeginMarker = "// begin-snippet:";
    private const string EndMarker = "// end-snippet";

    private static readonly FullPath Root = FullPath.FromPath(AppContext.BaseDirectory) / "Documentation";

    public static TheoryData<string> MarkdownFiles => [.. GetMarkdownFiles()];

    [Fact]
    public void PublishedMarkdownFilesAreCopied()
    {
        var files = GetMarkdownFiles();
        Assert.Contains("src/Meziantou.Framework.Imaging/readme.md", files);
    }

    [Theory]
    [MemberData(nameof(MarkdownFiles))]
    public void CSharpBlocksAreCompiledSnippets(string relativePath)
    {
        var snippets = LoadSnippets();
        var lines = ReadLines(Root / relativePath);
        for (var i = 0; i < lines.Length; i++)
        {
            var fence = lines[i].TrimStart();
            if (!fence.StartsWith("```", StringComparison.Ordinal))
                continue;

            var end = FindClosingFence(lines, i, relativePath);
            if (IsCSharp(fence[3..].Trim()))
            {
                var name = GetSnippetName(lines, i)
                    ?? throw new XunitException($"{relativePath}:{i + 1}: a C# block must be preceded by '{SnippetMarker} <name> -->' naming a compiled snippet of samples/Meziantou.Framework.Imaging.Samples.");
                Assert.True(snippets.TryGetValue(name, out var expected), $"{relativePath}:{i + 1}: unknown snippet '{name}' (known: {string.Join(", ", snippets.Keys.Order(StringComparer.Ordinal))}).");

                var actual = string.Join('\n', lines[(i + 1)..end]);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new XunitException($"{relativePath}:{i + 1}: the block differs from snippet '{name}'. Replace it with:\n{expected}");
            }

            i = end;
        }
    }

    [Fact]
    public void SnippetsAreWellFormed()
    {
        var snippets = LoadSnippets();
        Assert.Contains("readme-quickstart", snippets.Keys);
        Assert.Contains("read-frame-into", snippets.Keys);
        Assert.All(snippets, snippet => Assert.False(string.IsNullOrWhiteSpace(snippet.Value), $"Snippet '{snippet.Key}' is empty."));
    }

    private static List<string> GetMarkdownFiles()
        => [.. Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Select(path => FullPath.FromPath(path).MakePathRelativeTo(Root).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private static bool IsCSharp(string language)
        => language.Equals("csharp", StringComparison.OrdinalIgnoreCase) || language.Equals("cs", StringComparison.OrdinalIgnoreCase) || language.Equals("c#", StringComparison.OrdinalIgnoreCase);

    private static int FindClosingFence(string[] lines, int start, string relativePath)
    {
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "```")
                return i;
        }

        throw new XunitException($"{relativePath}:{start + 1}: unterminated code block.");
    }

    private static string? GetSnippetName(string[] lines, int fenceIndex)
    {
        var previous = fenceIndex - 1;
        while (previous >= 0 && string.IsNullOrWhiteSpace(lines[previous]))
            previous--;

        if (previous < 0)
            return null;

        var marker = lines[previous].Trim();
        if (!marker.StartsWith(SnippetMarker, StringComparison.Ordinal) || !marker.EndsWith("-->", StringComparison.Ordinal))
            return null;

        return marker[SnippetMarker.Length..^3].Trim();
    }

    /// <summary>Reads every snippet of the copied sample sources; a snippet with several regions joins them with a blank line.</summary>
    internal static Dictionary<string, string> LoadSnippets()
    {
        var parts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Root / "samples", "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var file = FullPath.FromPath(path).MakePathRelativeTo(Root).Replace('\\', '/');
            var lines = ReadLines(path);
            string? current = null;
            var start = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith(BeginMarker, StringComparison.Ordinal))
                {
                    if (current is not null)
                        throw new XunitException($"{file}:{i + 1}: nested snippet.");

                    current = line[BeginMarker.Length..].Trim();
                    Assert.Matches("^[a-z0-9-]+$", current);
                    Assert.True(owners.TryAdd(current, file) || owners[current] == file, $"{file}:{i + 1}: snippet '{current}' is also defined in {owners[current]}.");
                    start = i + 1;
                }
                else if (line == EndMarker)
                {
                    if (current is null)
                        throw new XunitException($"{file}:{i + 1}: '{EndMarker}' without '{BeginMarker}'.");

                    if (!parts.TryGetValue(current, out var list))
                    {
                        list = [];
                        parts.Add(current, list);
                    }

                    list.Add(Dedent(lines[start..i]));
                    current = null;
                }
            }

            if (current is not null)
                throw new XunitException($"{file}: snippet '{current}' is not terminated.");
        }

        return parts.ToDictionary(pair => pair.Key, pair => string.Join("\n\n", pair.Value), StringComparer.Ordinal);
    }

    private static string Dedent(string[] lines)
    {
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var last = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0)
            return "";

        var body = lines[first..(last + 1)];
        var indent = body.Where(line => !string.IsNullOrWhiteSpace(line)).Min(line => line.Length - line.TrimStart(' ').Length);
        return string.Join('\n', body.Select(line => string.IsNullOrWhiteSpace(line) ? "" : line[indent..].TrimEnd()));
    }

    private static string[] ReadLines(string path) => File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
}
