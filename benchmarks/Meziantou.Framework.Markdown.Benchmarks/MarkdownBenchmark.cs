using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Markdown.Benchmarks;

/// <summary>Parses and renders documents with Meziantou.Framework.Markdown.</summary>
[MemoryDiagnoser]
public class MarkdownBenchmark
{
    private static readonly MarkdownParseOptions AdvancedOptions = new() { Extensions = MarkdownExtensions.Advanced };

    private string _text = "";
    private MarkdownSyntaxTree _tree = null!;

    [Params("CommonMark", "Readme", "Docs")]
    public string Document { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(MarkdownBenchmark).Assembly.GetManifestResourceStream(Document + ".md")!;
        using var reader = new StreamReader(stream);
        _text = reader.ReadToEnd();
        _tree = MarkdownSyntaxTree.ParseText(_text, AdvancedOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public MarkdownSyntaxTree Parse() => MarkdownSyntaxTree.ParseText(_text);

    [Benchmark]
    [BenchmarkCategory("ToHtml")]
    public string ToHtml() => MarkdownConverter.ToHtml(_text);

    [Benchmark]
    [BenchmarkCategory("ToHtmlGfm")]
    public string ToHtml_Gfm() => MarkdownConverter.ToHtml(_text, MarkdownParseOptions.GitHubFlavored);

    [Benchmark]
    [BenchmarkCategory("ToHtmlAdvanced")]
    public string ToHtml_Advanced() => MarkdownConverter.ToHtml(_text, AdvancedOptions);

    [Benchmark]
    [BenchmarkCategory("Render")]
    public string RenderParsedTree() => MarkdownHtmlRenderer.ToHtml(_tree);

    [Benchmark]
    [BenchmarkCategory("PlainText")]
    public string ToPlainText() => MarkdownConverter.ToPlainText(_text, AdvancedOptions);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format() => MarkdownFormatter.FormatText(_tree, MarkdownFormattingOptions.Default);
}
