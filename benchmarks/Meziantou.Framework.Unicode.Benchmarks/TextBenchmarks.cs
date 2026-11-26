using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The algorithms that transform or check a whole text: normalization, case mapping, bidi levels and character properties.</summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    private string _text = "";
    private string _nfd = "";
    private string[] _paragraphs = [];
    private byte[] _levels = [];

    [Params("English", "French", "Mixed")]
    public string Text { get; set; } = "English";

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkTexts.Get(Text);
        _nfd = UnicodeNormalization.Normalize(_text, NormalizationForm.FormD);
        _paragraphs = _text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _levels = new byte[_text.Length];
    }

    [Benchmark]
    public string NormalizeNfc() => UnicodeNormalization.Normalize(_text, NormalizationForm.FormC);

    [Benchmark]
    public string NormalizeNfdToNfc() => UnicodeNormalization.Normalize(_nfd, NormalizationForm.FormC);

    [Benchmark]
    public string NormalizeNfd() => UnicodeNormalization.Normalize(_text, NormalizationForm.FormD);

    [Benchmark]
    public string NormalizeNfkc() => UnicodeNormalization.Normalize(_text, NormalizationForm.FormKC);

    [Benchmark]
    public bool IsNormalizedNfc() => UnicodeNormalization.IsNormalized(_text, NormalizationForm.FormC);

    [Benchmark]
    public string ToUpper() => UnicodeCaseMapping.ToUpper(_text);

    [Benchmark]
    public string ToLower() => UnicodeCaseMapping.ToLower(_text);

    [Benchmark]
    public int BidiLevels()
    {
        var result = 0;
        foreach (var paragraph in _paragraphs)
        {
            result += UnicodeBidi.ResolveLevels(paragraph, UnicodeBidiDirection.Auto, _levels);
        }

        return result;
    }

    [Benchmark]
    public int GeneralCategories()
    {
        var result = 0;
        foreach (var rune in _text.EnumerateRunes())
        {
            result += (int)UnicodeProperties.GetGeneralCategory(rune.Value);
        }

        return result;
    }

    [Benchmark]
    public bool IsMixedScript() => Unicode.IsMixedScript(_text);
}
