using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// Measures text with the metrics of Helvetica, Helvetica-Bold, Helvetica-Oblique and Helvetica-BoldOblique, the standard fonts of PDF, without any font file or
/// system font: the measurement is the same on every machine.
/// </summary>
/// <remarks>
/// <para>
/// The characters are mapped to the glyphs of the font through the Adobe Glyph List, and the kerning pairs of the font
/// are applied. The text is measured by grapheme cluster: a character the font lacks counts as 1 em when it is wide
/// (East Asian width Wide or Fullwidth, such as CJK ideographs and most emoji), as nothing when it is a combining mark,
/// and as the average width of the font otherwise.
/// </para>
/// <para>Arial and Liberation Sans have the same widths as Helvetica, so the text the SVG draws with
/// <c>Helvetica, Arial, sans-serif</c> fits the layout.</para>
/// </remarks>
public sealed class AfmTextMeasurer : ITextMeasurer
{
    private static readonly Lazy<AfmTextMeasurer> DefaultInstance = new(() => new AfmTextMeasurer());

    private readonly FontMetrics _regular;
    private readonly FontMetrics _bold;
    private readonly FontMetrics _italic;
    private readonly FontMetrics _boldItalic;

    private AfmTextMeasurer()
    {
        var glyphList = LoadGlyphList();
        _regular = new FontMetrics(AfmFontMetrics.GetStandardFont("Helvetica")!, glyphList);
        _bold = new FontMetrics(AfmFontMetrics.GetStandardFont("Helvetica-Bold")!, glyphList);
        _italic = new FontMetrics(AfmFontMetrics.GetStandardFont("Helvetica-Oblique")!, glyphList);
        _boldItalic = new FontMetrics(AfmFontMetrics.GetStandardFont("Helvetica-BoldOblique")!, glyphList);
    }

    /// <summary>Gets the measurer. It is thread-safe.</summary>
    public static AfmTextMeasurer Default => DefaultInstance.Value;

    /// <summary>Gets <c>Helvetica, Arial, sans-serif</c>.</summary>
    public string FontFamily => "Helvetica, Arial, sans-serif";

    /// <inheritdoc/>
    public TextMetrics Measure(string text, SceneFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        var metrics = (font.Bold, font.Italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };
        var scale = font.Size / 1000;
        return new TextMetrics(metrics.MeasureUnits(text) * scale, metrics.Ascender * scale, metrics.Descender * scale);
    }

    private static Dictionary<string, int> LoadGlyphList()
    {
        using var stream = typeof(AfmTextMeasurer).Assembly.GetManifestResourceStream("Meziantou.Framework.VectorGraphics.glyphlist.txt")
            ?? throw new InvalidOperationException("The glyph list is not embedded in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length is 0 || line[0] is '#')
                continue;

            // name;XXXX, or name;XXXX YYYY for a sequence of characters, which a single glyph cannot measure
            var separator = line.IndexOf(';', StringComparison.Ordinal);
            if (separator < 0 || line.IndexOf(' ', separator, StringComparison.Ordinal) >= 0)
                continue;

            if (int.TryParse(line.AsSpan(separator + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
            {
                result.TryAdd(line[..separator], codePoint);
            }
        }

        return result;
    }

    private sealed class FontMetrics
    {
        private readonly AfmFontMetrics _font;
        private readonly Dictionary<int, (string Name, double Width)> _characters = [];

        public FontMetrics(AfmFontMetrics font, Dictionary<string, int> glyphList)
        {
            _font = font;
            foreach (var character in font.CharacterMetrics)
            {
                if (character.Name is null || character.Width is not { } width)
                    continue;

                if (glyphList.TryGetValue(character.Name, out var codePoint))
                {
                    _characters.TryAdd(codePoint, (character.Name, width.X));
                }
            }

            var total = 0.0;
            var count = 0;
            for (var c = 0x21; c <= 0x7E; c++)
            {
                if (_characters.TryGetValue(c, out var value))
                {
                    total += value.Width;
                    count++;
                }
            }

            AverageWidth = count is 0 ? 500 : total / count;
            Ascender = font.Ascender ?? 718;
            Descender = -(font.Descender ?? -207);
        }

        public double AverageWidth { get; }

        public double Ascender { get; }

        public double Descender { get; }

        public double MeasureUnits(string text)
        {
            var width = 0.0;
            string? previousName = null;
            foreach (var range in Unicode.EnumerateGraphemeClusters(text))
            {
                var cluster = text.AsSpan(range);
                _ = Rune.DecodeFromUtf16(cluster, out var rune, out _);
                var codePoint = rune.Value;
                if (_characters.TryGetValue(codePoint, out var character))
                {
                    if (previousName is not null)
                    {
                        width += _font.GetKerning(previousName, character.Name).X;
                    }

                    width += character.Width;
                    previousName = character.Name;
                    continue;
                }

                previousName = null;
                width += GetFallbackWidth(codePoint);
            }

            return width;
        }

        private double GetFallbackWidth(int codePoint)
        {
            var category = UnicodeProperties.GetGeneralCategory(codePoint);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Control or UnicodeCategory.Format)
                return 0;

            return UnicodeProperties.GetEastAsianWidth(codePoint) is UnicodeEastAsianWidth.Wide or UnicodeEastAsianWidth.Fullwidth ? 1000 : AverageWidth;
        }
    }
}
