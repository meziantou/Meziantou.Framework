using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The hyphenation of the words of a text with the patterns of hyph-utf8, and the parsing of the patterns.</summary>
[MemoryDiagnoser]
public class HyphenationBenchmarks
{
    private string _patterns = "";
    private Hyphenator _hyphenator = null!;
    private string[] _words = [];
    private int[] _breaks = [];

    [Params("English", "French")]
    public string Text { get; set; } = "English";

    [GlobalSetup]
    public void Setup()
    {
        _patterns = File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(HyphenationBenchmarks).Assembly.Location)!, "hyphenation", Text is "English" ? "hyph-en-us.tex" : "hyph-fr.tex"));
        _hyphenator = Hyphenator.Parse(_patterns);

        var text = BenchmarkTexts.Get(Text);
        var words = new List<string>();
        var enumerator = Unicode.EnumerateWordBoundaries(text);
        while (enumerator.MoveNext())
        {
            if (enumerator.CurrentKind is UnicodeWordKind.Letter)
            {
                words.Add(text[enumerator.Current]);
            }
        }

        _words = [.. words];
        _breaks = new int[words.Max(word => word.Length)];
    }

    [Benchmark]
    public int Hyphenate()
    {
        var count = 0;
        foreach (var word in _words)
        {
            count += _hyphenator.Hyphenate(word, _breaks);
        }

        return count;
    }

    [Benchmark]
    public int Parse() => Hyphenator.Parse(_patterns).PatternCount;
}
