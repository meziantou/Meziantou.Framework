using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Drawing;

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
