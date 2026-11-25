using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The segmentation algorithms (UAX #29, UAX #14, UAX #24) over a whole text.</summary>
[MemoryDiagnoser]
public class SegmentationBenchmarks
{
    private string _text = "";

    [Params("English", "French", "Mixed")]
    public string Text { get; set; } = "English";

    [GlobalSetup]
    public void Setup() => _text = BenchmarkTexts.Get(Text);

    [Benchmark]
    public int GraphemeClusters()
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateGraphemeClusters(_text))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int WordBoundaries()
    {
        var count = 0;
        var enumerator = Unicode.EnumerateWordBoundaries(_text);
        while (enumerator.MoveNext())
        {
            if (enumerator.CurrentKind is not UnicodeWordKind.None)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int SentenceBoundaries()
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateSentenceBoundaries(_text))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int LineBreaks()
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateLineBreaks(_text))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int ScriptRuns()
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateScriptRuns(_text))
        {
            count++;
        }

        return count;
    }
}
