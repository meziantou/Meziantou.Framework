using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics.Benchmarks;

[MemoryDiagnoser]
public class TextMeasureBenchmarks
{
    private const string ShortText = "Revenue (k€)";
    private const string Paragraph = "The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! Sphinx of black quartz, judge my vow. Waltz, bad nymph, for quick jigs vex.";

    private static readonly SceneFont Font = new(12);

    private FontFaceTextMeasurer _fontFace = null!;

    [GlobalSetup]
    public void Setup()
    {
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024);
        var face = FontLoader.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Regular.woff2")), environment)[0];
        _fontFace = new FontFaceTextMeasurer(face);
    }

    [Benchmark]
    public double Afm_Short() => AfmTextMeasurer.Default.Measure(ShortText, Font).Width;

    [Benchmark]
    public double Afm_Paragraph() => AfmTextMeasurer.Default.Measure(Paragraph, Font).Width;

    [Benchmark]
    public double FontFace_Short() => _fontFace.Measure(ShortText, Font).Width;

    [Benchmark]
    public double FontFace_Paragraph() => _fontFace.Measure(Paragraph, Font).Width;

    [Benchmark]
    public int Afm_Wrap() => TextFitting.Wrap(AfmTextMeasurer.Default, Paragraph, Font, 150).Count;

    [Benchmark]
    public string? Afm_Truncate() => TextFitting.Truncate(AfmTextMeasurer.Default, Paragraph, Font, 150);

    [Benchmark]
    public int FontFace_Wrap() => TextFitting.Wrap(_fontFace, Paragraph, Font, 150).Count;
}
