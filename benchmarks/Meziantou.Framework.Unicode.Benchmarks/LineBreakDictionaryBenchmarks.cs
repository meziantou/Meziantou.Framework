using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The line breaks of a Thai text, with the word lists of ICU.</summary>
[MemoryDiagnoser]
public class LineBreakDictionaryBenchmarks
{
    private LineBreakDictionary _dictionary = null!;
    private string _text = "";
    private int[] _breaks = [];

    [GlobalSetup]
    public void Setup()
    {
        _dictionary = IcuWordDictionary.CreateLineBreakDictionary();
        _text = BenchmarkTexts.Thai;
        _breaks = new int[_text.Length];
    }

    [Benchmark]
    public int FindBreaks()
    {
        var count = 0;
        foreach (var range in _text.AsSpan().Split(' '))
        {
            count += _dictionary.FindBreaks(_text.AsSpan(range), _breaks);
        }

        return count;
    }

    [Benchmark]
    public int LineBreaks()
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateLineBreaks(_text, _dictionary))
        {
            count++;
        }

        return count;
    }
}
