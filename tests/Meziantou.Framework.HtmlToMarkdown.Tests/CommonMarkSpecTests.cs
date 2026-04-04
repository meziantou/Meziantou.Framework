using System.Text.Json;
using System.Text.Json.Serialization;
using Markdig;
using Xunit.Sdk;

namespace Meziantou.Framework.HtmlToMarkdownTests;

public sealed class CommonMarkSpecTests
{
    private static readonly Lazy<List<CommonMarkSpecTestCase>> TestCases = new(LoadTestCases);

    private static List<CommonMarkSpecTestCase> LoadTestCases()
    {
        using var stream = typeof(CommonMarkSpecTests).Assembly.GetManifestResourceStream(
            "Meziantou.Framework.HtmlToMarkdownTests.TestData.commonmark-spec-0.31.2.json");
        if (stream is null)
        {
            throw new InvalidOperationException("Could not find embedded resource 'commonmark-spec-0.31.2.json'");
        }

        return JsonSerializer.Deserialize<List<CommonMarkSpecTestCase>>(stream);
    }

    public static TheoryData<CommonMarkSpecTestCase> GetTestCases()
    {
        var data = new TheoryData<CommonMarkSpecTestCase>();
        foreach (var testCase in TestCases.Value)
        {
            data.Add(testCase);
        }
        return data;
    }

    /// <summary>
    /// Round-trip test: take the HTML from the CommonMark spec, convert to Markdown using our converter,
    /// then convert back to HTML using Markdig, and verify the HTML is semantically equivalent.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void RoundTrip(CommonMarkSpecTestCase testCase)
    {
        // Some test cases cannot round-trip. Skip them with documented reasons.
        if (SkippedExamples.TryGetValue(testCase.Example, out var reason))
        {
            Assert.Skip(reason);
            return;
        }

        // 1. Convert the spec HTML to Markdown using our converter
        var markdown = HtmlToMarkdown.Convert(testCase.Html);

        // 2. Convert the Markdown back to HTML using Markdig
        var pipeline = new MarkdownPipelineBuilder().Build();
        var roundTrippedHtml = Markdown.ToHtml(markdown, pipeline);

        // 3. Compare the HTML outputs
        HtmlNormalizer.AssertEquivalent(testCase.Html, roundTrippedHtml);
    }

    // Examples that cannot round-trip HTML→Markdown→HTML due to fundamental limitations.
    // Each skip must have a documented reason.
    private static readonly Dictionary<int, string> SkippedExamples = new()
    {
        // HTML blocks: raw HTML is preserved as-is in Markdown, but our converter
        // processes it as structured HTML, losing the "raw HTML block" semantics.
        // CommonMark examples 148-160 test HTML block parsing.
        [148] = "HTML block: <pre> with Markdown-like content cannot round-trip",
        [149] = "HTML block: <script> tag preserved as raw HTML",
        [150] = "HTML block: <style> tag preserved as raw HTML",
        [151] = "HTML block: raw HTML comment block",
        [152] = "HTML block: processing instruction",
        [153] = "HTML block: CDATA section",
        [154] = "HTML block: custom element",
        [155] = "HTML block: pre tag with blank lines",
        [156] = "HTML block: script tag with blank lines",
        [157] = "HTML block: HTML block interaction with other constructs",
        [158] = "HTML block: HTML block interaction with other constructs",
        [159] = "HTML block: HTML block with lazy continuation",
        [160] = "HTML block: HTML block with lazy continuation",
        [161] = "HTML block: custom elements",
        [162] = "HTML block: custom elements with content",
        [163] = "HTML block: custom elements closing",
        [164] = "HTML block: comment",
        [165] = "HTML block: comment",
        [166] = "HTML block: comment not on first line",
        [167] = "HTML block: processing instruction",
        [168] = "HTML block: declaration",
        [169] = "HTML block: CDATA",
        [170] = "HTML block: opening tag on first line",
        [171] = "HTML block: closing tag on first line",
        [172] = "HTML block: misc",
        [173] = "HTML block: misc",
        [174] = "HTML block: misc",
        [175] = "HTML block: misc",
        [176] = "HTML block: misc",
        [177] = "HTML block: in other containers",
        [178] = "HTML block: in other containers",
        [179] = "HTML block: in other containers",
        [180] = "HTML block: self-closing tag",
        [181] = "HTML block: not starting with recognized tag",
        [182] = "HTML block: type 6 with blank line",
        [183] = "HTML block: type 6 with blank line",
        [184] = "HTML block: not starting with recognized tag",
        [185] = "HTML block: div",
        [186] = "HTML block: div",
        [187] = "HTML block: div blank line end",
        [188] = "HTML block: type 7",
        [189] = "HTML block: type 7",
        [190] = "HTML block: type 7",
        [191] = "HTML block: type 7 not closing",

        // Raw HTML inlines (section "Raw HTML") - these test raw HTML within inline context
        // Our converter processes these as structured HTML.
        [632] = "Raw HTML: inline open tag",
        [633] = "Raw HTML: inline closing tag",
        [634] = "Raw HTML: inline comment",
        [635] = "Raw HTML: inline processing instruction",
        [636] = "Raw HTML: inline declaration",
        [637] = "Raw HTML: inline CDATA",
        [638] = "Raw HTML: entity in tag",
        [639] = "Raw HTML: not a tag",
        [640] = "Raw HTML: not a tag",
        [641] = "Raw HTML: not a tag",
        [642] = "Raw HTML: not a tag",
        [643] = "Raw HTML: not a tag",
        [644] = "Raw HTML: not a tag",
        [645] = "Raw HTML: not a tag",
        [646] = "Raw HTML: not a tag",
        [647] = "Raw HTML: not a tag",
        [648] = "Raw HTML: not a tag",
        [649] = "Raw HTML: not a tag",
        [650] = "Raw HTML: not a tag",
        [651] = "Raw HTML: not a tag",
        [652] = "Raw HTML: not a tag",
    };
}

#pragma warning disable MA0048 // File name must match type name
public sealed class CommonMarkSpecTestCase : IXunitSerializable
{
    [JsonPropertyName("markdown")]
    public string Markdown { get; set; } = "";

    [JsonPropertyName("html")]
    public string Html { get; set; } = "";

    [JsonPropertyName("example")]
    public int Example { get; set; }

    [JsonPropertyName("section")]
    public string Section { get; set; } = "";

    [JsonPropertyName("start_line")]
    public int StartLine { get; set; }

    [JsonPropertyName("end_line")]
    public int EndLine { get; set; }

    public override string ToString() => $"Example {Example}: {Section}";

    public void Deserialize(IXunitSerializationInfo info)
    {
        Markdown = info.GetValue<string>(nameof(Markdown))!;
        Html = info.GetValue<string>(nameof(Html))!;
        Example = info.GetValue<int>(nameof(Example));
        Section = info.GetValue<string>(nameof(Section))!;
        StartLine = info.GetValue<int>(nameof(StartLine));
        EndLine = info.GetValue<int>(nameof(EndLine));
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Markdown), Markdown);
        info.AddValue(nameof(Html), Html);
        info.AddValue(nameof(Example), Example);
        info.AddValue(nameof(Section), Section);
        info.AddValue(nameof(StartLine), StartLine);
        info.AddValue(nameof(EndLine), EndLine);
    }
}
