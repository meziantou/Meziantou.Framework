using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Helpers of the font tests: fonts built in memory, and outlines written as SVG path data.</summary>
internal static class FontTestSupport
{
    public static Font Load(FontBuilder builder, FontLoadOptions? options = null) => Font.Load(builder.Build(), options);

    /// <summary>
    /// Gets the outline of a glyph as SVG path data in font units, with the y axis up, in the form the corpus generator
    /// records it: absolute M, L, Q, C and Z commands, and numbers with at most two decimals.
    /// </summary>
    public static string GetOutline(Font font, int glyphId, ImageConfiguration? configuration = null)
    {
        using var scratch = new GlyphScratch(AllocationScope.Create(configuration, "FontTests"));
        var budget = new GlyphBudget(font.Face.Data.Limits);
        var sink = new SvgPathSink();
        font.Face.DecodeOutline(glyphId, sink, scratch, ref budget);
        return sink.ToString();
    }

    private sealed class SvgPathSink : OutlineSink
    {
        private readonly StringBuilder _path = new();

        public override void MoveTo(double x, double y) => _path.Append(CultureInfo.InvariantCulture, $"M{Format(x)},{Format(y)}");

        public override void LineTo(double x, double y) => _path.Append(CultureInfo.InvariantCulture, $"L{Format(x)},{Format(y)}");

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
            => _path.Append(CultureInfo.InvariantCulture, $"Q{Format(controlX)},{Format(controlY)} {Format(x)},{Format(y)}");

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
            => _path.Append(CultureInfo.InvariantCulture, $"C{Format(control1X)},{Format(control1Y)} {Format(control2X)},{Format(control2Y)} {Format(x)},{Format(y)}");

        public override void Close() => _path.Append('Z');

        public override string ToString() => _path.ToString();

        private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
