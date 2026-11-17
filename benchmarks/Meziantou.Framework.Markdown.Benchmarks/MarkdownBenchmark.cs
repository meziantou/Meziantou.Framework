using BenchmarkDotNet.Attributes;
using Markdig;

namespace Meziantou.Framework.Markdown.Benchmarks;

/// <summary>Parses and renders documents with Meziantou.Framework.Markdown, and with Markdig as a baseline.</summary>
[MemoryDiagnoser]
public class MarkdownBenchmark
{
    private static readonly MarkdownParseOptions AdvancedOptions = new() { Extensions = MarkdownExtensions.Advanced };
    private static readonly MarkdownPipeline MarkdigCommonMarkPipeline = new MarkdownPipelineBuilder().Build();
    private static readonly MarkdownPipeline MarkdigGfmPipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough).UseTaskLists().UseAutoLinks().Build();
    private static readonly MarkdownPipeline MarkdigAdvancedPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

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
    [BenchmarkCategory("Parse")]
    public Markdig.Syntax.MarkdownDocument Markdig_Parse() => Markdig.Markdown.Parse(_text, MarkdigCommonMarkPipeline);

    [Benchmark]
    [BenchmarkCategory("ToHtml")]
    public string ToHtml() => MarkdownConverter.ToHtml(_text);

    [Benchmark]
    [BenchmarkCategory("ToHtml")]
    public string Markdig_ToHtml() => Markdig.Markdown.ToHtml(_text, MarkdigCommonMarkPipeline);

    [Benchmark]
    [BenchmarkCategory("ToHtmlGfm")]
    public string ToHtml_Gfm() => MarkdownConverter.ToHtml(_text, MarkdownParseOptions.GitHubFlavored);

    [Benchmark]
    [BenchmarkCategory("ToHtmlGfm")]
    public string Markdig_ToHtml_Gfm() => Markdig.Markdown.ToHtml(_text, MarkdigGfmPipeline);

    [Benchmark]
    [BenchmarkCategory("ToHtmlAdvanced")]
    public string ToHtml_Advanced() => MarkdownConverter.ToHtml(_text, AdvancedOptions);

    [Benchmark]
    [BenchmarkCategory("ToHtmlAdvanced")]
    public string Markdig_ToHtml_Advanced() => Markdig.Markdown.ToHtml(_text, MarkdigAdvancedPipeline);

    [Benchmark]
    [BenchmarkCategory("Render")]
    public string RenderParsedTree() => MarkdownHtmlRenderer.ToHtml(_tree);

    [Benchmark]
    [BenchmarkCategory("PlainText")]
    public string ToPlainText() => MarkdownConverter.ToPlainText(_text, AdvancedOptions);

    [Benchmark]
    [BenchmarkCategory("PlainText")]
    public string Markdig_ToPlainText() => Markdig.Markdown.ToPlainText(_text, MarkdigAdvancedPipeline);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format() => MarkdownFormatter.FormatText(_tree, MarkdownFormattingOptions.Default);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Markdig_Normalize() => Markdig.Markdown.Normalize(_text, pipeline: MarkdigAdvancedPipeline);
}
