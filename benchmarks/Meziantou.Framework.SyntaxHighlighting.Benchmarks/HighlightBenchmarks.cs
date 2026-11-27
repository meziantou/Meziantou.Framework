using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.SyntaxHighlighting.Benchmarks;

/// <summary>
/// Highlights real files of the repository: a whole file, and its first 40 lines, the size of a code block in a page.
/// </summary>
[MemoryDiagnoser]
public class HighlightBenchmarks
{
    private string _text = null!;

    [Params("csharp", "javascript", "html", "css", "json", "xml", "python", "yaml", "markdown", "powershell", "razor")]
    public string Language { get; set; } = null!;

    [Params("Snippet", "File")]
    public string Size { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var text = Samples.Get(Language);
        _text = Size is "Snippet" ? Samples.FirstLines(text, 40) : text;

        // Compile the grammar outside of the measurement.
        SyntaxHighlighter.Highlight(_text, Language);
    }

    [Benchmark]
    public string Highlight() => SyntaxHighlighter.Highlight(_text, Language);
}
