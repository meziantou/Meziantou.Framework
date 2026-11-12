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

    // The buffer of the shaping, reused under the lock, and the limits it was created with
    private GlyphBuffer? _buffer;
    private FontResourceLimits? _bufferLimits;

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
    /// <remarks>The measurer has no <see cref="Fonts"/>: each renderer draws the text with its own fonts. Use <see cref="FontFaceTextMeasurer(SceneFontCollection)"/> so the renderers draw with the fonts that measure.</remarks>
    public FontFaceTextMeasurer(FontFace regular, FontFace? bold, FontFace? italic, FontFace? boldItalic, string? fontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(regular);
        _regular = regular;
        _bold = bold ?? regular;
        _italic = italic ?? regular;
        _boldItalic = boldItalic ?? bold ?? italic ?? regular;
        FontFamily = fontFamily ?? regular.FamilyName;
    }

    /// <summary>Initializes a new instance of the <see cref="FontFaceTextMeasurer"/> class that measures the text with the fonts of a collection.</summary>
    /// <param name="fonts">The fonts. A chart or a diagram sets them as the fonts of its scene, so the renderers draw the text with them.</param>
    /// <remarks>
    /// The family of a text is <see cref="SceneFont.Family"/>, or <see cref="SceneFontCollection.DefaultFamily"/>. The
    /// characters its face does not have are measured with the face that draws them (<see cref="SceneFontCollection"/>).
    /// The ascent and the descent are the ones of the face of the family.
    /// </remarks>
    public FontFaceTextMeasurer(SceneFontCollection fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
        _regular = _bold = _italic = _boldItalic = fonts.Faces[0].Face;
        FontFamily = fonts.FontFamily;
    }

    /// <inheritdoc/>
    public string FontFamily { get; }

    /// <summary>Gets the fonts the text is measured with, or <see langword="null"/> when the measurer was created from faces of <c>Meziantou.Framework.Fonts</c>.</summary>
    public SceneFontCollection? Fonts { get; }

    // A letter spacing turns the optional ligatures off, as in CSS and in the renderers
    private static readonly ShapingFeature[] NoLigatures = [new("liga", 0), new("clig", 0), new("dlig", 0)];

    /// <inheritdoc/>
    public TextMetrics Measure(string text, SceneFont font) => Measure(text, font, 0, 0);

    /// <inheritdoc/>
    /// <remarks>
    /// With a letter spacing, the text is shaped without its optional ligatures, and the spacing is added after each
    /// cluster of glyphs (a character with its marks, a ligature the script requires), as the renderers draw it.
    /// </remarks>
    public TextMetrics Measure(string text, SceneFont font, double letterSpacing, double wordSpacing)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spacing = (Letter: TextSpacing.Normalize(letterSpacing), Word: TextSpacing.Normalize(wordSpacing));
        if (Fonts is not null)
            return MeasureWithFallback(text, font, Fonts.GetFaces(font), spacing);

        var face = (font.IsBold, font.Italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };
        var scale = font.Size / face.UnitsPerEm;
        var (ascender, descender) = GetVerticalMetrics(face);
        var width = text.Length is 0 ? 0 : Shape(face, text, scale, spacing);
        return new TextMetrics(width, ascender * scale, -descender * scale);
    }

    private TextMetrics MeasureWithFallback(string text, SceneFont font, IReadOnlyList<SceneFontFace> stack, (double Letter, double Word) spacing)
    {
        var primary = stack[0].Face;
        var (ascender, descender) = GetVerticalMetrics(primary);
        var primaryScale = font.Size / primary.UnitsPerEm;
        var width = 0.0;
        foreach (var run in SceneFontCollection.SplitRuns(text, stack))
        {
            width += Shape(run.Face.Face, text.Substring(run.Start, run.Length), font.Size / run.Face.Face.UnitsPerEm, spacing);
        }

        return new TextMetrics(width, ascender * primaryScale, -descender * primaryScale);
    }

    private static (int Ascender, int Descender) GetVerticalMetrics(FontFace face)
    {
        return face.UseTypoMetrics ? (face.TypoAscender, face.TypoDescender) : (face.HorizontalAscender, face.HorizontalDescender);
    }

    // The width of the shaped text: the sum of the advances, scaled to pixels, and the spacing after each cluster of
    // glyphs: the letter spacing, and the word spacing for a space
    private double Shape(FontFace face, string text, double scale, (double Letter, double Word) spacing)
    {
        var units = 0L;
        var spacingWidth = 0.0;
        lock (_lock)
        {
            if (_buffer is null || !ReferenceEquals(_bufferLimits, face.Data.Limits))
            {
                _buffer = new GlyphBuffer(face.Data.Limits);
                _bufferLimits = face.Data.Limits;
            }

            var buffer = _buffer;
            foreach (var run in Unicode.EnumerateScriptRuns(text))
            {
                var span = text.AsSpan(run.Index, run.Length);
                buffer.Clear();
                TextShaper.AddText(buffer, span);
                TextShaper.Shape(buffer, face, run.Script, languages: [], IsRightToLeft(span), features: spacing.Letter is 0 ? [] : NoLigatures);
                var info = buffer.Info;
                for (var i = 0; i < buffer.Length; i++)
                {
                    units += buffer.Positions[i].XAdvance;
                    if ((spacing.Letter != 0 || spacing.Word != 0) && (i is 0 || info[i].Cluster != info[i - 1].Cluster))
                    {
                        spacingWidth += spacing.Letter + (TextSpacing.IsWordSeparator(span[info[i].Cluster]) ? spacing.Word : 0);
                    }
                }
            }
        }

        return (units * scale) + spacingWidth;
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
