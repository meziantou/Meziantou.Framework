using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The levels of the Unicode Bidirectional Algorithm, for texts that need the whole algorithm.</summary>
[MemoryDiagnoser]
public class BidiBenchmarks
{
    private string[] _paragraphs = [];
    private string[] _isolatedParagraphs = [];
    private byte[] _levels = [];
    private int[] _workspace = [];

    [Params("Arabic", "Mixed")]
    public string Text { get; set; } = "Arabic";

    [GlobalSetup]
    public void Setup()
    {
        var text = BenchmarkTexts.Get(Text);
        _paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // The same paragraphs with explicit formatting characters: each bracketed text is in an isolate
        _isolatedParagraphs = text.Replace("(", "\u2068(", StringComparison.Ordinal).Replace(")", ")\u2069", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _levels = new byte[text.Length * 2];
        _workspace = new int[UnicodeBidi.GetWorkspaceLength(_levels.Length)];
    }

    [Benchmark]
    public int ResolveLevels() => Resolve(_paragraphs, UnicodeBidiDirection.Auto);

    [Benchmark]
    public int ResolveLevelsRightToLeft() => Resolve(_paragraphs, UnicodeBidiDirection.RightToLeft);

    [Benchmark]
    public int ResolveLevelsWithIsolates() => Resolve(_isolatedParagraphs, UnicodeBidiDirection.Auto);

    private int Resolve(string[] paragraphs, UnicodeBidiDirection direction)
    {
        var result = 0;
        foreach (var paragraph in paragraphs)
        {
            result += UnicodeBidi.ResolveLevels(paragraph, direction, _levels, _workspace);
        }

        return result;
    }
}
