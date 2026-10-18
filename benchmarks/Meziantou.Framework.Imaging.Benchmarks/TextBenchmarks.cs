using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Text: loading a font, laying out a paragraph (the runs of the text, shaping, line breaking, justification) and
/// drawing a layout. The fonts are those of the test corpus, each with a paragraph of its script: Noto Sans for Latin
/// (kerning and ligatures), Noto Naskh Arabic (joining forms, marks, right to left), Noto Sans Devanagari (the Indic
/// model: syllables that are reordered) and Noto Serif Tibetan (the universal model, with a font that has more than
/// two thousand substitution subtables).
/// </summary>
/// <remarks>
/// A layout is created once and drawn many times: creating it allocates (the glyphs, the lines), drawing it does not.
/// The paragraphs are wrapped at 480 pixels and justified; they are drawn at 16 pixels on a 512x768 image.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class TextBenchmarks : IDisposable
{
    private const string LatinText = "The quick brown fox jumps over the lazy dog. Text is shaped glyph by glyph: letters become ligatures, pairs of letters are kerned, and a line is broken where the rules of Unicode allow it. A paragraph that is justified stretches the spaces of each line but the last one, so that both of its edges are straight. The quick brown fox jumps over the lazy dog. Text is shaped glyph by glyph: letters become ligatures, pairs of letters are kerned, and a line is broken where the rules of Unicode allow it. A paragraph that is justified stretches the spaces of each line but the last one, so that both of its edges are straight. The quick brown fox jumps over the lazy dog. Text is shaped glyph by glyph: letters become ligatures, pairs of letters are kerned, and a line is broken where the rules of Unicode allow it. A paragraph that is justified stretches the spaces of each line but the last one, so that both of its edges are straight.";

    private const string ArabicText = "\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A. \u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A. \u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A. \u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A. \u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A. \u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645. \u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645 \u0648\u0631\u062D\u0645\u0629 \u0627\u0644\u0644\u0647. \u0643\u062A\u0628 \u0627\u0644\u0637\u0627\u0644\u0628 \u0627\u0644\u062F\u0631\u0633 \u0628\u0627\u0644\u0642\u0644\u0645 \u0641\u064A \u0627\u0644\u0628\u064A\u062A.";

    private const string DevanagariText = "\u092F\u0942\u0928\u093F\u0915\u094B\u0921 \u092A\u094D\u0930\u0924\u094D\u092F\u0947\u0915 \u0905\u0915\u094D\u0937\u0930 \u0915\u0947 \u0932\u093F\u090F \u090F\u0915 \u0935\u093F\u0936\u0947\u0937 \u0938\u0902\u0916\u094D\u092F\u093E \u092A\u094D\u0930\u0926\u093E\u0928 \u0915\u0930\u0924\u093E \u0939\u0948, \u091A\u093E\u0939\u0947 \u0915\u094B\u0908 \u092D\u0940 \u0915\u0902\u092A\u094D\u092F\u0942\u091F\u0930 \u092A\u094D\u0932\u0947\u091F\u092B\u0949\u0930\u094D\u092E, \u092A\u094D\u0930\u094B\u0917\u094D\u0930\u093E\u092E \u0905\u0925\u0935\u093E \u0915\u094B\u0908 \u092D\u0940 \u092D\u093E\u0937\u093E \u0939\u094B\u0964 \u092F\u0942\u0928\u093F\u0915\u094B\u0921 \u092A\u094D\u0930\u0924\u094D\u092F\u0947\u0915 \u0905\u0915\u094D\u0937\u0930 \u0915\u0947 \u0932\u093F\u090F \u090F\u0915 \u0935\u093F\u0936\u0947\u0937 \u0938\u0902\u0916\u094D\u092F\u093E \u092A\u094D\u0930\u0926\u093E\u0928 \u0915\u0930\u0924\u093E \u0939\u0948, \u091A\u093E\u0939\u0947 \u0915\u094B\u0908 \u092D\u0940 \u0915\u0902\u092A\u094D\u092F\u0942\u091F\u0930 \u092A\u094D\u0932\u0947\u091F\u092B\u0949\u0930\u094D\u092E, \u092A\u094D\u0930\u094B\u0917\u094D\u0930\u093E\u092E \u0905\u0925\u0935\u093E \u0915\u094B\u0908 \u092D\u0940 \u092D\u093E\u0937\u093E \u0939\u094B\u0964 \u092F\u0942\u0928\u093F\u0915\u094B\u0921 \u092A\u094D\u0930\u0924\u094D\u092F\u0947\u0915 \u0905\u0915\u094D\u0937\u0930 \u0915\u0947 \u0932\u093F\u090F \u090F\u0915 \u0935\u093F\u0936\u0947\u0937 \u0938\u0902\u0916\u094D\u092F\u093E \u092A\u094D\u0930\u0926\u093E\u0928 \u0915\u0930\u0924\u093E \u0939\u0948, \u091A\u093E\u0939\u0947 \u0915\u094B\u0908 \u092D\u0940 \u0915\u0902\u092A\u094D\u092F\u0942\u091F\u0930 \u092A\u094D\u0932\u0947\u091F\u092B\u0949\u0930\u094D\u092E, \u092A\u094D\u0930\u094B\u0917\u094D\u0930\u093E\u092E \u0905\u0925\u0935\u093E \u0915\u094B\u0908 \u092D\u0940 \u092D\u093E\u0937\u093E \u0939\u094B\u0964 \u092F\u0942\u0928\u093F\u0915\u094B\u0921 \u092A\u094D\u0930\u0924\u094D\u092F\u0947\u0915 \u0905\u0915\u094D\u0937\u0930 \u0915\u0947 \u0932\u093F\u090F \u090F\u0915 \u0935\u093F\u0936\u0947\u0937 \u0938\u0902\u0916\u094D\u092F\u093E \u092A\u094D\u0930\u0926\u093E\u0928 \u0915\u0930\u0924\u093E \u0939\u0948, \u091A\u093E\u0939\u0947 \u0915\u094B\u0908 \u092D\u0940 \u0915\u0902\u092A\u094D\u092F\u0942\u091F\u0930 \u092A\u094D\u0932\u0947\u091F\u092B\u0949\u0930\u094D\u092E, \u092A\u094D\u0930\u094B\u0917\u094D\u0930\u093E\u092E \u0905\u0925\u0935\u093E \u0915\u094B\u0908 \u092D\u0940 \u092D\u093E\u0937\u093E \u0939\u094B\u0964";

    private const string TibetanText = "\u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F53\u0F72\u0F0B\u0F56\u0F7C\u0F51\u0F0B\u0F62\u0F72\u0F42\u0F66\u0F0B\u0F40\u0FB1\u0F72\u0F0B\u0F66\u0F90\u0F51\u0F0B\u0F61\u0F72\u0F42\u0F0B\u0F61\u0F72\u0F53\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B\u0F58\u0F50\u0F60\u0F0B\u0F51\u0F44\u0F0B\u0F62\u0F92\u0FB1\u0F74\u0F51\u0F0B\u0F66\u0FA1\u0F7A\u0F0B\u0F66\u0FA3\u0F0B\u0F5A\u0F7C\u0F42\u0F66\u0F0B\u0F61\u0F7C\u0F51\u0F0D";

    private static readonly TextLayoutOptions Paragraph = new() { MaxWidth = 480, Alignment = TextAlignment.Justify };

    private static readonly string LongLatinText = string.Join(' ', Enumerable.Repeat(LatinText, 30));

    private byte[] _tibetanData = null!;
    private Font _latinFont = null!;
    private TextStyle _latin = null!;
    private TextStyle _arabic = null!;
    private TextStyle _devanagari = null!;
    private TextStyle _tibetan = null!;
    private TextLayout _latinLayout = null!;
    private TextLayout _arabicLayout = null!;
    private TextLayout _lineLayout = null!;
    private VectorPath _curve = null!;
    private PositionedGlyph[] _glyphs = null!;
    private Image<Rgba32> _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tibetanData = ReadResource("fonts/NotoSerifTibetan-Regular.ttf");
        _latinFont = Font.Load(ReadResource("fonts/NotoSans-Regular.subset.ttf"));
        _latin = new TextStyle(_latinFont, 16);
        _arabic = new TextStyle(Font.Load(ReadResource("fonts/NotoNaskhArabic-Regular.subset.ttf")), 16);
        _devanagari = new TextStyle(Font.Load(ReadResource("fonts/NotoSansDevanagari-Regular.ttf")), 16);
        _tibetan = new TextStyle(Font.Load(_tibetanData), 16);
        _latinLayout = TextLayout.Create(LatinText, _latin, Paragraph);
        _arabicLayout = TextLayout.Create(ArabicText, _arabic, Paragraph);
        _lineLayout = TextLayout.Create(LatinText[..60], _latin);
        _curve = new VectorPathBuilder().MoveTo(16, 600).CubicBezierTo(120, 300, 380, 300, 496, 600).Build();
        _image = new Image<Rgba32>(512, 768, new Rgba32(255, 255, 255));

        // The glyphs of the Latin text on lines of 56, at origins that fall on every quarter of a pixel
        _glyphs = new PositionedGlyph[LatinText.Length];
        for (var i = 0; i < _glyphs.Length; i++)
        {
            _glyphs[i] = new PositionedGlyph(_latinFont.Face.MapCodePoint(LatinText[i]), new PointD(16 + (8.6 * (i % 56)), 32 + (20 * (i / 56))));
        }

        // A text the font has no glyph for would measure the missing glyph, not the script.
        foreach (var layout in new[] { _latinLayout, _arabicLayout, TextLayout.Create(DevanagariText, _devanagari, Paragraph), TextLayout.Create(TibetanText, _tibetan, Paragraph) })
        {
            if (layout.HasMissingGlyphs || layout.Lines.Count < 4)
                throw new InvalidOperationException("A workload text is not the paragraph it is meant to be.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _image?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    [Workload("Noto Serif Tibetan (610 KB, 1,900 glyphs): load")]
    public int LoadFont() => Font.Load(_tibetanData).GlyphCount;

    [Benchmark(Baseline = true)]
    [Workload("Latin paragraph, 930 characters: layout")]
    public int LayoutLatin() => TextLayout.Create(LatinText, _latin, Paragraph).Lines.Count;

    [Benchmark]
    [Workload("Latin paragraph, 28,000 characters: layout")]
    public int LayoutLongLatin() => TextLayout.Create(LongLatinText, _latin, Paragraph).Lines.Count;

    [Benchmark]
    [Workload("Arabic paragraph, 500 characters: layout")]
    public int LayoutArabic() => TextLayout.Create(ArabicText, _arabic, Paragraph).Lines.Count;

    [Benchmark]
    [Workload("Devanagari paragraph, 500 characters: layout")]
    public int LayoutDevanagari() => TextLayout.Create(DevanagariText, _devanagari, Paragraph).Lines.Count;

    [Benchmark]
    [Workload("Tibetan paragraph, 580 characters: layout")]
    public int LayoutTibetan() => TextLayout.Create(TibetanText, _tibetan, Paragraph).Lines.Count;

    [Benchmark]
    [Workload("Latin paragraph, 16 px on 512x768 Rgba32: fill")]
    public int FillLatin()
    {
        _image.Draw(_latinLayout, static (canvas, layout) => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(16, 16)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Arabic paragraph, 16 px on 512x768 Rgba32: fill")]
    public int FillArabic()
    {
        _image.Draw(_arabicLayout, static (canvas, layout) => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(16, 16)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Latin paragraph, 16 px on 512x768 Rgba32: fill as positioned glyphs, without a glyph cache")]
    public int FillGlyphs()
    {
        _image.Draw(this, static (canvas, benchmarks) => canvas.FillGlyphs(new SolidBrush(new Rgba32(0, 0, 0)), benchmarks._latinFont, 16, benchmarks._glyphs));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Latin line of 60 characters along a curve: fill")]
    public int FillOnPath()
    {
        _image.Draw((Layout: _lineLayout, Path: _curve), static (canvas, state) => canvas.FillText(new Rgba32(0, 0, 0), state.Layout, state.Path));
        return _image.Width;
    }

    private static byte[] ReadResource(string name)
    {
        using var resource = typeof(TextBenchmarks).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The resource '{name}' is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
