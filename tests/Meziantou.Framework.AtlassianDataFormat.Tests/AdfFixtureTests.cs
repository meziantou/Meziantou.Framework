using System.Reflection;
using System.Text;

namespace Meziantou.Framework.AtlassianDataFormat.Tests;

/// <summary>
/// Converts the third-party ADF corpora under <c>TestData/third-party</c> and compares the result
/// with the golden files checked in next to them. See <c>TestData/third-party/NOTICE.md</c>.
/// </summary>
public sealed class AdfFixtureTests
{
    private const string Root = "TestData/third-party/";

    /// <summary>
    /// The golden files are generated with this option so they carry no trailing whitespace. The
    /// default style is covered by <see cref="AdfToMarkdownTests"/>.
    /// </summary>
    private static AdfToMarkdownOptions CreateOptions() => new() { LineBreakStyle = AdfLineBreakStyle.Backslash };

    private static readonly Assembly Assembly = typeof(AdfFixtureTests).Assembly;

    public static TheoryData<string, string, string> GetFixtures()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var corpus in new[] { "jira-adf-converter", "atlas-doc-parser" })
        {
            var expected = ParseGoldenFile(corpus);
            foreach (var (name, markdown) in expected)
            {
                data.Add(corpus, name, markdown);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GetFixtures))]
    public void ConvertsToTheExpectedMarkdown(string corpus, string name, string expected)
    {
        var json = ReadResource(Root + corpus + "/" + name);
        Assert.Equal(expected, AdfToMarkdown.Convert(json, CreateOptions()));
    }

    [Theory]
    [MemberData(nameof(GetFixtures))]
    public void RoundTripsThroughJson(string corpus, string name, string expected)
    {
        var document = AdfDocument.Parse(ReadResource(Root + corpus + "/" + name));
        var roundTripped = AdfDocument.Parse(document.ToJsonString());

        Assert.Equal(document.ToJsonString(), roundTripped.ToJsonString());
        Assert.Equal(expected, roundTripped.ToMarkdown(CreateOptions()));
    }

    /// <summary>Guards against a golden file silently losing entries.</summary>
    [Theory]
    [InlineData("jira-adf-converter", 201)]
    [InlineData("atlas-doc-parser", 35)]
    public void EveryFixtureOfTheCorpusHasAGoldenEntry(string corpus, int expectedCount)
    {
        var prefix = Root + corpus + "/";
        var fixtures = Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[prefix.Length..])
            .ToList();

        Assert.HasCount(expectedCount, fixtures);

        var golden = ParseGoldenFile(corpus);
        Assert.DoesNotContain(fixtures, f => !golden.ContainsKey(f));
        Assert.DoesNotContain(golden.Keys, k => !fixtures.Contains(k));
    }

    private static Dictionary<string, string> ParseGoldenFile(string corpus)
    {
        var content = ReadResource(Root + corpus + ".expected.md");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        string? currentName = null;
        var currentValue = new StringBuilder();

        foreach (var line in content.Split('\n'))
        {
            if (line.StartsWith("<!--@ ", StringComparison.Ordinal) && line.EndsWith(" @-->", StringComparison.Ordinal))
            {
                Flush(result, currentName, currentValue);
                currentName = line["<!--@ ".Length..^" @-->".Length];
                currentValue.Clear();
            }
            else if (currentName is not null)
            {
                currentValue.Append(line).Append('\n');
            }
        }

        Flush(result, currentName, currentValue);
        return result;

        static void Flush(Dictionary<string, string> result, string? name, StringBuilder value)
        {
            if (name is not null)
            {
                result.Add(name, value.ToString().Trim('\n'));
            }
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Could not find the embedded resource '{name}'");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
