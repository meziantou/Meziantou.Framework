using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The lookups of the Unicode Character Database: the information and the name of each character of a text, and the enumeration of every character.</summary>
[MemoryDiagnoser]
public class CharacterInfoBenchmarks
{
    private Rune[] _runes = [];

    [Params("English", "Mixed")]
    public string Text { get; set; } = "English";

    [GlobalSetup]
    public void Setup() => _runes = [.. BenchmarkTexts.Get(Text).EnumerateRunes()];

    [Benchmark]
    public int GetCharacterInfo()
    {
        var result = 0;
        foreach (var rune in _runes)
        {
            if (Unicode.TryGetCharacterInfo(rune, out var info))
            {
                result += (int)info.Category + info.CanonicalCombiningClass;
            }
        }

        return result;
    }

    [Benchmark]
    public int GetName()
    {
        var result = 0;
        foreach (var rune in _runes)
        {
            if (Unicode.TryGetCharacterInfo(rune, out var info))
            {
                result += info.Name.Length;
            }
        }

        return result;
    }

    [Benchmark]
    public int AllCharacters()
    {
        var result = 0;
        foreach (var info in Unicode.AllCharacters)
        {
            result += info.Name.Length;
        }

        return result;
    }
}
