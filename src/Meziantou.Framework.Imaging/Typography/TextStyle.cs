using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How a text, or a part of a text, is set: its fonts, its size, and what is drawn with it.</summary>
/// <remarks>A style is immutable and can be shared by any number of texts and layouts.</remarks>
public sealed class TextStyle
{
    /// <summary>Initializes a style.</summary>
    /// <param name="fonts">The fonts of the text; a <see cref="Font"/> converts to a stack of one font.</param>
    /// <param name="size">The size of the fonts, in pixels per em: a positive number up to 65536.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive number up to 65536.</exception>
    public TextStyle(FontStack fonts, double size)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Font.ValidateSize(size, nameof(size));
        Fonts = fonts;
        Size = size;
    }

    /// <summary>Gets the fonts of the text, by order of preference.</summary>
    public FontStack Fonts { get; }

    /// <summary>Gets the size of the fonts, in pixels per em.</summary>
    public double Size { get; }

    /// <summary>
    /// Gets the paint of the text, or <see langword="null"/> (the default) for the brush given to
    /// <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/>.
    /// </summary>
    public Brush? Brush { get; init; }

    /// <summary>
    /// Gets the features of the OpenType Layout tables to turn on or off, besides the ones that are applied by default.
    /// The last setting of a feature wins. Empty by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IReadOnlyList<FontFeature> Features
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = new ReadOnlyCollection<FontFeature>([.. value]);
        }
    } = ReadOnlyCollection<FontFeature>.Empty;

    /// <summary>
    /// Gets the language of the text, as a BCP 47 language tag (<c>"sr"</c>, <c>"tr"</c>, <c>"zh-Hant"</c>), or
    /// <see langword="null"/> (the default) when it is not known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A font can draw the same characters differently for different languages: the Cyrillic letters of Serbian and
    /// of Bulgarian, the ideographs of Chinese, Japanese and Korean, the letters around the dotless i of Turkish. It
    /// does so with a language system of its OpenType Layout tables for each of these languages. The language of the
    /// style selects the language system of each font: the first one the font has among the tags the registry of
    /// OpenType gives for the language, or else its default language system, as without a language.
    /// </para>
    /// <para>
    /// The tag is not checked against the registry of language subtags, and the casing of its subtags does not
    /// matter. The language is found from the language subtag, and from the script, region and variant subtags where
    /// they select a written form: <c>zh-Hans</c>, <c>zh-Hant</c>, <c>zh-HK</c>, <c>el-polyton</c>, <c>und-fonipa</c>.
    /// A language the registry of OpenType has no tag for changes nothing.
    /// </para>
    /// <para>The language changes nothing else: neither the fonts that are chosen nor where lines break.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The value does not have the form of a BCP 47 language tag.</exception>
    public string? Language
    {
        get;
        init
        {
            if (value is not null && !OpenTypeLanguage.IsWellFormed(value))
                throw new ArgumentException("The language is a BCP 47 language tag: subtags of one to eight letters and digits separated by hyphens.", nameof(value));

            field = value;
            LanguageTags = value is null ? [] : OpenTypeLanguage.GetTags(value);
        }
    }

    /// <summary>Gets the tags of the language systems of <see cref="Language"/>, by order of preference.</summary>
    internal uint[] LanguageTags { get; private init; } = [];

    /// <summary>
    /// Gets what is made from the glyphs of the fonts to imitate a bold or an italic font that the caller does not have.
    /// <see cref="FontSynthesis.None"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FontSynthesis.Oblique"/> slants every outline to the right, around the baseline; the marks of a
    /// letter follow it, the advances do not change and the decorations stay straight.
    /// </para>
    /// <para>
    /// <see cref="FontSynthesis.Bold"/> thickens the glyphs of a filled text: each glyph is filled together with a
    /// stroke of its outline, 1/24 of the size wide, whose corners are kept sharp unless they are sharper than 60
    /// degrees. Each glyph that takes room takes 1/24 of the size more and is drawn in the middle of it. A stroked
    /// text (<c>StrokeText</c>) and the path of a layout (<see cref="TextLayout.ToPath"/>) have the outlines of the
    /// font, which the bold does not change.
    /// </para>
    /// <para>The glyphs a font draws with its own colors, such as emoji, are drawn as they are.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value has an undefined flag.</exception>
    public FontSynthesis Synthesis
    {
        get;
        init
        {
            if ((value & ~(FontSynthesis.Bold | FontSynthesis.Oblique)) != FontSynthesis.None)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The synthesis is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the space added after each character, in pixels; negative to tighten. 0 by default.</summary>
    /// <remarks>
    /// The space is added after each cluster: a character with its marks, a ligature. Ligatures the font forms by
    /// default are still formed; turn the feature <c>liga</c> off to space their letters.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or its magnitude exceeds 65536.</exception>
    public double LetterSpacing
    {
        get;
        init
        {
            ValidateSpacing(value);
            field = value;
        }
    }

    /// <summary>Gets the space added to each space between words, in pixels; negative to tighten. 0 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or its magnitude exceeds 65536.</exception>
    public double WordSpacing
    {
        get;
        init
        {
            ValidateSpacing(value);
            field = value;
        }
    }

    /// <summary>Gets the lines drawn with the text. <see cref="TextDecorations.None"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has an undefined flag.</exception>
    public TextDecorations Decorations
    {
        get;
        init
        {
            if ((value & ~(TextDecorations.Underline | TextDecorations.Strikethrough | TextDecorations.Overline)) != TextDecorations.None)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The decorations are not valid.");

            field = value;
        }
    }

    /// <summary>Gets the paint of the decorations, or <see langword="null"/> (the default) for the paint of the text.</summary>
    public Brush? DecorationBrush { get; init; }

    /// <summary>
    /// Gets the palette the color glyphs of the text take their colors from: an index in <see cref="Font.ColorPalettes"/>
    /// of the font of each glyph. 0 by default; a font that does not have the palette uses its first one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 65535.</exception>
    public int ColorPaletteIndex
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
            field = value;
        }
    }

    /// <summary>
    /// Gets the hyphenation patterns of the language of the text, or <see langword="null"/> (the default). They are
    /// used when the layout hyphenates automatically (<see cref="TextHyphenation.Automatic"/>).
    /// </summary>
    public Hyphenator? Hyphenator { get; init; }

    private static void ValidateSpacing(double value)
    {
        if (!(Math.Abs(value) <= Font.MaxSize))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A spacing is a finite number of pixels, up to 65536 in magnitude.");
    }
}
