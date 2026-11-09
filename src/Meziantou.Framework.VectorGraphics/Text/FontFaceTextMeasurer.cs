using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// Measures text by shaping it with a font of <c>Meziantou.Framework.Fonts</c>: every script, ligatures, kerning and
/// the advances of the font are taken into account.
/// </summary>
/// <remarks>The text is split into runs of one script; each run is shaped in the direction of its first strong character.</remarks>
public sealed class FontFaceTextMeasurer : ITextMeasurer
{
    private readonly FontFace _regular;
    private readonly FontFace _bold;
    private readonly FontFace _italic;
    private readonly FontFace _boldItalic;
    private readonly Lock _lock = new();

    /// <summary>Initializes a new instance of the <see cref="FontFaceTextMeasurer"/> class.</summary>
    /// <param name="regular">The face of the regular text, loaded with <see cref="FontLoader"/>.</param>
    /// <param name="bold">The face of the bold text, or <see langword="null"/> to measure bold text with <paramref name="regular"/>.</param>
    /// <param name="fontFamily">The CSS <c>font-family</c> of the text, or <see langword="null"/> for the family name of <paramref name="regular"/>.</param>
    public FontFaceTextMeasurer(FontFace regular, FontFace? bold = null, string? fontFamily = null)
        : this(regular, bold, italic: null, boldItalic: null, fontFamily)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FontFaceTextMeasurer"/> class.</summary>
    /// <param name="regular">The face of the regular text, loaded with <see cref="FontLoader"/>.</param>
    /// <param name="bold">The face of the bold text, or <see langword="null"/> to measure bold text with <paramref name="regular"/>.</param>
    /// <param name="italic">The face of the italic text, or <see langword="null"/> to measure italic text with <paramref name="regular"/>.</param>
    /// <param name="boldItalic">The face of the bold italic text, or <see langword="null"/> to measure it with <paramref name="bold"/>, then <paramref name="italic"/>, then <paramref name="regular"/>.</param>
    /// <param name="fontFamily">The CSS <c>font-family</c> of the text, or <see langword="null"/> for the family name of <paramref name="regular"/>.</param>
    public FontFaceTextMeasurer(FontFace regular, FontFace? bold, FontFace? italic, FontFace? boldItalic, string? fontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(regular);
        _regular = regular;
        _bold = bold ?? regular;
        _italic = italic ?? regular;
        _boldItalic = boldItalic ?? bold ?? italic ?? regular;
        FontFamily = fontFamily ?? regular.FamilyName;
    }

    /// <inheritdoc/>
    public string FontFamily { get; }

    /// <inheritdoc/>
    public TextMetrics Measure(string text, SceneFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        var face = (font.Bold, font.Italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };
        var scale = font.Size / face.UnitsPerEm;
        var ascender = face.UseTypoMetrics ? face.TypoAscender : face.HorizontalAscender;
        var descender = face.UseTypoMetrics ? face.TypoDescender : face.HorizontalDescender;

        var units = 0L;
        if (text.Length > 0)
        {
            lock (_lock)
            {
                var buffer = new GlyphBuffer(face.Data.Limits);
                foreach (var run in Unicode.EnumerateScriptRuns(text))
                {
                    var span = text.AsSpan(run.Index, run.Length);
                    buffer.Clear();
                    TextShaper.AddText(buffer, span);
                    TextShaper.Shape(buffer, face, run.Script, languages: [], IsRightToLeft(span), features: []);
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        units += buffer.Positions[i].XAdvance;
                    }
                }
            }
        }

        return new TextMetrics(units * scale, ascender * scale, -descender * scale);
    }

    private static bool IsRightToLeft(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            switch (UnicodeProperties.GetBidiClass(rune.Value))
            {
                case UnicodeBidirectionalCategory.LeftToRight:
                    return false;
                case UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic:
                    return true;
            }
        }

        return false;
    }
}
