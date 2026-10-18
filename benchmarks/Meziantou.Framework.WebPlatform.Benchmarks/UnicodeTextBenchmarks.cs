using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;
using Meziantou.Framework.WebPlatform.Urls;
using BrowsingContext = Meziantou.Framework.WebPlatform.Dom.BrowsingContext;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// The Unicode algorithms of the layout on the text of the Wikipedia article (English) and on a mixed paragraph (Chinese,
/// Japanese, Arabic, Hebrew, Devanagari, emoji): the line breaking opportunities, the bidirectional levels, the grapheme
/// clusters and the word boundaries, the uppercase transformation, and the conversion of internationalized host names.
/// </summary>
[MemoryDiagnoser]
public class UnicodeTextBenchmarks
{
    private const string MixedParagraph =
        "\u4E2D\u6587\u7684\u6587\u672C\uFF0C\u5305\u542B\u6807\u70B9\u7B26\u53F7\u3002\u65E5\u672C\u8A9E\u306E\u6587\u7AE0\u3067\u3059\u3002 " +
        "\u0627\u0644\u0639\u0631\u0628\u064A\u0629 \u0646\u0635 \u0645\u0646 \u0627\u0644\u064A\u0645\u064A\u0646 123 \u05E2\u05D1\u05E8\u05D9\u05EA (\u05D8\u05E7\u05E1\u05D8) " +
        "\u0939\u093F\u0928\u094D\u0926\u0940 \u0915\u094D\u0937\u0924\u094D\u0930\u093F\u092F \u092A\u093E\u0920 " +
        "\U0001F468\u200D\U0001F469\u200D\U0001F467 \U0001F44D\U0001F3FD \U0001F1EB\U0001F1F7 caf\u00E9 na\u0308ive stra\u00DFe \uFB01nal. ";

    private static readonly string[] Hosts =
    [
        "www.example.com", "B\u00FCcher.example", "xn--bcher-kva.example", "\u4F8B\u3048.\u30C6\u30B9\u30C8", "\u0645\u062B\u0627\u0644.\u0625\u062E\u062A\u0628\u0627\u0631",
        "fa\u00DF.de", "\uFF37\uFF37\uFF37\u3002example\u3002com", "\u043F\u0440\u0438\u043C\u0435\u0440.\u0438\u0441\u043F\u044B\u0442\u0430\u043D\u0438\u0435",
    ];

    private readonly LineBreaker _lineBreaker = new();
    private string _english = "";
    private string _mixed = "";
    private LineBreakOpportunity[] _opportunities = [];
    private bool[] _boundaries = [];

    [GlobalSetup]
    public void Setup()
    {
        using var context = new BrowsingContext();
        var article = context.Navigate("https://example.com/", _ => BenchmarkData.Get("wikipedia"));
        var text = new StringBuilder();
        foreach (var node in article.Body!.Descendants())
        {
            if (node is Text textNode && !string.IsNullOrWhiteSpace(textNode.Data))
            {
                text.Append(textNode.Data).Append(' ');
            }
        }

        _english = text.ToString();
        _mixed = string.Concat(Enumerable.Repeat(MixedParagraph, Math.Max(1, _english.Length / MixedParagraph.Length)));
        var length = Math.Max(_english.Length, _mixed.Length);
        _opportunities = new LineBreakOpportunity[length + 1];
        _boundaries = new bool[length + 1];
    }

    [Params("English", "Mixed")]
    public string Text { get; set; } = "English";

    private string Value => Text == "English" ? _english : _mixed;

    [Benchmark]
    public int LineBreaks()
    {
        var text = Value;
        _lineBreaker.FindOpportunities(text, new LineBreakOptions(), _opportunities.AsSpan(0, text.Length + 1));
        return (int)_opportunities[text.Length / 2];
    }

    [Benchmark]
    public int LineBreaksAnywhere()
    {
        var text = Value;
        _lineBreaker.FindOpportunities(text, new LineBreakOptions { Strictness = LineBreakStrictness.Anywhere }, _opportunities.AsSpan(0, text.Length + 1));
        return (int)_opportunities[text.Length / 2];
    }

    [Benchmark]
    public byte BidiLevels() => BidiResolver.GetLevels(Value, BidiParagraphDirection.AutoLeftToRight)[^1];

    [Benchmark]
    public bool GraphemeClusterBoundaries()
    {
        var text = Value;
        GraphemeClusters.GetBoundaries(text, _boundaries.AsSpan(0, text.Length + 1));
        return _boundaries[text.Length / 2];
    }

    [Benchmark]
    public bool WordBoundaries()
    {
        var text = Value;
        Unicode.WordBoundaries.GetBoundaries(text, _boundaries.AsSpan(0, text.Length + 1));
        return _boundaries[text.Length / 2];
    }

    [Benchmark]
    public int Uppercase() => TextTransformer.Transform(Value, TextTransform.Uppercase, CaseLanguage.None).Length;

    [Benchmark]
    public int Hosts_ToAscii()
    {
        var length = 0;
        foreach (var host in Hosts)
        {
            length += HostParser.ParseDomain(host, beStrict: false)?.Length ?? 0;
        }

        return length;
    }
}
