using Markdig;
using Xunit.Sdk;

namespace Meziantou.Framework.HtmlToMarkdownTests;

public sealed class MarkdownRoundTrippingTests
{
    private static readonly Lazy<List<MarkdigTestCase>> AllCases = new(LoadAllCases);

    private static List<MarkdigTestCase> LoadAllCases()
    {
        var cases = new List<MarkdigTestCase>();
        var assembly = typeof(MarkdownRoundTrippingTests).Assembly;

        // Find all embedded .md resources in the Specs folder
        var prefix = "Meziantou.Framework.HtmlToMarkdownTests.TestData.Specs.";
        foreach (var resourceName in assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var content = reader.ReadToEnd();

            // Extract filename: e.g., "PipeTableSpecs.md" from full resource name
            var fileName = resourceName[prefix.Length..];

            cases.AddRange(SpecificationTestParser.Parse(content, fileName));
        }

        return cases;
    }

    public static TheoryData<MarkdigTestCase> GetTestCases()
    {
        var data = new TheoryData<MarkdigTestCase>();
        foreach (var testCase in AllCases.Value)
        {
            data.Add(testCase);
        }
        return data;
    }

    /// <summary>
    /// Round-trip test: take the HTML from Markdig's spec files, convert to Markdown using our converter,
    /// then convert back to HTML using Markdig with advanced extensions, and verify the HTML is semantically equivalent.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void RoundTrip(MarkdigTestCase testCase)
    {
        // Some tests may not round-trip due to extension-specific parsing behaviors
        if (ShouldSkip(testCase, out var reason))
        {
            Assert.Skip(reason);
            return;
        }

        // 1. Convert the spec HTML to Markdown using our converter
        var markdown = HtmlToMarkdown.Convert(testCase.Html);

        // 2. Convert the Markdown back to HTML using Markdig with advanced extensions
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
        var roundTrippedHtml = Markdown.ToHtml(markdown, pipeline);

        // 3. Compare the HTML outputs
        HtmlNormalizer.AssertEquivalent(testCase.Html, roundTrippedHtml);
    }

    private static bool ShouldSkip(MarkdigTestCase testCase, out string reason)
    {
        // CommonMark.md contains the full CommonMark spec. We test those separately
        // in CommonMarkSpecTests. Skip the duplicate here to avoid 600+ duplicate tests.
        if (testCase.FileName == "CommonMark.md")
        {
            reason = "CommonMark spec is tested separately in CommonMarkSpecTests";
            return true;
        }

        if (SkippedExamples.TryGetValue((testCase.FileName, testCase.Example), out var skippedReason))
        {
            reason = skippedReason!;
            return true;
        }

        reason = "";
        return false;
    }

    private static readonly Dictionary<(string FileName, int Example), string> SkippedExamples = new()
    {
        [("EmphasisExtraSpecs.md", 6)] = "Emphasis delimiter parsing differs after round-trip",
        [("ListExtraSpecs.md", 1)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 2)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 3)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 4)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 5)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 6)] = "List tight/loose semantics differ after round-trip",
        [("ListExtraSpecs.md", 7)] = "List tight/loose semantics differ after round-trip",
        [("PipeTableSpecs.md", 17)] = "Pipe table link title encoding differs after round-trip",
        [("PipeTableSpecs.md", 20)] = "Pipe table escaping semantics differ after round-trip",
    };
}

#pragma warning disable MA0048 // File name must match type name
public sealed class MarkdigTestCase : IXunitSerializable
{
    public string Markdown { get; set; } = "";
    public string Html { get; set; } = "";
    public int Example { get; set; }
    public string FileName { get; set; } = "";

    public override string ToString() => $"{FileName} Example {Example}";

    public void Deserialize(IXunitSerializationInfo info)
    {
        Markdown = info.GetValue<string>(nameof(Markdown))!;
        Html = info.GetValue<string>(nameof(Html))!;
        Example = info.GetValue<int>(nameof(Example));
        FileName = info.GetValue<string>(nameof(FileName))!;
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Markdown), Markdown);
        info.AddValue(nameof(Html), Html);
        info.AddValue(nameof(Example), Example);
        info.AddValue(nameof(FileName), FileName);
    }
}
