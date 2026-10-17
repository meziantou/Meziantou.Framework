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
    /// Gets the paint behind the text of the style, or <see langword="null"/> (the default) for none: a highlight, the
    /// background of a label.
    /// </summary>
    /// <remarks>
    /// The background is painted by <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/> before the text of
    /// the whole layout. On each line, it is one rectangle as wide as the advances of the text of the style, the
    /// white space between its words included (not the white space that hangs at the end of a wrapped line), and as
    /// tall as the primary font of the style: from its ascent to its descent, whatever the height of the line. It is
    /// not part of the outlines of the text: <see cref="DrawingCanvas.StrokeText(Pen, TextLayout, PointD)"/> and
    /// <see cref="TextLayout.ToPath"/> leave it out.
    /// </remarks>
    public Brush? BackgroundBrush { get; init; }

    /// <summary>
    /// Gets the pen that strokes the outlines of the glyphs of the style once the text is filled, or
    /// <see langword="null"/> (the default) for none: a text with a contour.
    /// </summary>
    /// <remarks>
    /// The stroke is drawn by <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/> after the text of the
    /// whole layout is filled, over it. A centered pen covers the edge of the glyphs by half its width; with
    /// <see cref="PenAlignment.Outside"/>, the stroke is around the glyphs and leaves them as they are filled. The
    /// glyphs of one pen are stroked as one shape; the decorations, the glyphs a font draws with its own colors and
    /// what a synthetic bold adds to a glyph are not stroked. The width of the pen is in pixels, whatever the size of
    /// the text.
    /// </remarks>
    public Pen? Outline { get; init; }

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
    /// Gets a value indicating whether the text of the style is set horizontally in the room of one upright character
    /// when it is in a vertical line (tate-chu-yoko): a few digits or letters across a column of Japanese or Chinese
    /// text. <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The text of each span of the style is one unit of its line: it is shaped as a horizontal line of one font
    /// (the first font of the stack that has all its characters), centered across the line and on the middle of one
    /// em down the line, and it advances by one em. Text that is wider than one em takes the forms its font has for
    /// half, a third or a quarter of an em when it is two, three or four characters long (<c>hwid</c>, <c>twid</c>,
    /// <c>qwid</c>), and is narrowed to one em if it is still too wide. A line never ends inside it, and the letter
    /// spacing, the word spacing and justification do not change it. Its decorations are those of upright text.
    /// </para>
    /// <para>
    /// It is the <c>all</c> value of the <c>text-combine-upright</c> property of CSS;
    /// <see cref="TextLayoutOptions.CombineUprightDigits"/> combines the short numbers of a text. It changes nothing in
    /// a horizontal layout, nor in one whose orientation is <see cref="TextOrientation.Sideways"/>.
    /// </para>
    /// </remarks>
    public bool IsCombinedUpright { get; init; }

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
