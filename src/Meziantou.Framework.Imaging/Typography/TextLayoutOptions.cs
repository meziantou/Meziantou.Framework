namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How a text is laid out: the box it must fit, how its lines are broken and aligned.</summary>
/// <remarks>The options are immutable; <see cref="Default"/> lays out a text of any width on one line per paragraph.</remarks>
public sealed class TextLayoutOptions
{
    /// <summary>Gets the default options: no maximum size, lines aligned at their start.</summary>
    public static TextLayoutOptions Default { get; } = new();

    /// <summary>
    /// Gets the width lines are wrapped at and aligned in, in pixels; <see cref="double.PositiveInfinity"/> (the
    /// default) for no limit: lines are then only broken at the line breaks of the text, and aligned in the width of the
    /// widest one. In a vertical layout (<see cref="WritingMode"/>), it is the width the lines must fit, as
    /// <see cref="MaxHeight"/> is in a horizontal one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or is not a number.</exception>
    public double MaxWidth
    {
        get;
        init
        {
            ValidateExtent(value);
            field = value;
        }
    } = double.PositiveInfinity;

    /// <summary>
    /// Gets the height the lines must fit, in pixels; <see cref="double.PositiveInfinity"/> (the default) for no limit.
    /// The lines that do not fit are not part of the layout, except the first one. In a vertical layout
    /// (<see cref="WritingMode"/>), it is the height lines are wrapped at and aligned in, as <see cref="MaxWidth"/> is
    /// in a horizontal one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or is not a number.</exception>
    public double MaxHeight
    {
        get;
        init
        {
            ValidateExtent(value);
            field = value;
        }
    } = double.PositiveInfinity;

    /// <summary>Gets the largest number of lines; <see cref="int.MaxValue"/> (the default) for no limit. The lines after are not part of the layout.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxLines
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = int.MaxValue;

    /// <summary>Gets whether a line that is wider than <see cref="MaxWidth"/> continues on the next line. <see cref="TextWrapping.Wrap"/> by default.</summary>
    public TextWrapping Wrapping
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextWrapping.NoWrap, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets where a line can be broken inside a word. <see cref="TextWordBreak.Normal"/> by default.</summary>
    public TextWordBreak WordBreak
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextWordBreak.KeepAll, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets how strict the line breaking rules of Chinese and Japanese text are. <see cref="TextLineBreak.Normal"/> by default.</summary>
    public TextLineBreak LineBreak
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextLineBreak.Anywhere, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the words of the languages that are written without spaces between words (Thai, Lao, Khmer, Burmese), or
    /// <see langword="null"/> (the default).
    /// </summary>
    /// <remarks>
    /// The Unicode line breaking algorithm leaves the text of these scripts to a dictionary: without one, a line of
    /// such text is only broken at a space, or anywhere when <see cref="OverflowWrap"/> allows it. With one, a line can
    /// also end before each word the dictionary finds, as <see cref="LineBreakDictionary"/> specifies. A dictionary
    /// can hold the words of several languages. No word list is provided with the library.
    /// </remarks>
    public LineBreakDictionary? LineBreakDictionary { get; init; }

    /// <summary>Gets what happens to a word that is wider than <see cref="MaxWidth"/>. <see cref="TextOverflowWrap.Normal"/> by default.</summary>
    public TextOverflowWrap OverflowWrap
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextOverflowWrap.Anywhere, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets whether words are hyphenated at the end of a line. <see cref="TextHyphenation.Manual"/> by default.</summary>
    public TextHyphenation Hyphenation
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextHyphenation.Automatic, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the text drawn at the end of a line that ends inside a hyphenated word, or <see langword="null"/> (the
    /// default) for the hyphen (U+2010) when the font has it and the hyphen-minus (U+002D) otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">The value is longer than 16 characters.</exception>
    public string? HyphenCharacter
    {
        get;
        init
        {
            if (value is { Length: > MaxInsertedTextLength })
                throw new ArgumentException("The hyphen is at most 16 characters.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets how the lines are placed in the width of the layout. <see cref="TextAlignment.Start"/> by default.</summary>
    public TextAlignment Alignment
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextAlignment.Justify, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets the base direction of the paragraphs. <see cref="TextDirection.Auto"/> by default.</summary>
    public TextDirection Direction
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextDirection.RightToLeft, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the direction of the lines: horizontal (the default), or vertical with lines that follow each other to
    /// the left or to the right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In a vertical layout, what is said of the width of a line is said of its height, and the other way around:
    /// <see cref="MaxHeight"/> is the length lines are wrapped at and aligned in, <see cref="MaxWidth"/> is the room
    /// the lines must fit, <see cref="LineHeight"/> is the width of a line, <see cref="Alignment"/> places a line
    /// from its top, and <see cref="FirstLineIndent"/> moves the first line of a paragraph down.
    /// <see cref="Orientation"/> says which characters are upright.
    /// </para>
    /// <para>
    /// A vertical line has a central baseline, in the middle of its width: upright characters are centered on it
    /// (with the vertical metrics of their font: <c>vhea</c>, <c>vmtx</c>, <c>VORG</c>, and the vertical forms of the
    /// <c>vert</c> feature), and the text that is on its side has the middle of its font on it: the middle of the
    /// ideographic em box the <c>BASE</c> table of the font gives for its script, or of its ascent and its descent.
    /// With <see cref="TextOrientation.Sideways"/>, the line is a horizontal line turned a quarter of a turn
    /// clockwise, and its baseline is the one of its text.
    /// </para>
    /// <para>
    /// The features a style turns on for one direction apply to the other one where the font has their counterpart:
    /// <c>kern</c>, <c>palt</c>, <c>halt</c>, <c>chws</c> and <c>hkna</c> are applied to upright text as
    /// <c>vkrn</c>, <c>vpal</c>, <c>vhal</c>, <c>vchw</c> and <c>vkna</c>, and the vertical ones to the text that is
    /// on its side as the horizontal ones. The text that is on its side has the forms its font has for that
    /// (<c>vrtr</c>). A style that turns <c>vrt2</c> on uses the rotated glyphs of the font instead: the characters
    /// the font has a rotated glyph for are set upright with that glyph, in the place of the vertical forms, and
    /// the others are turned.
    /// </para>
    /// </remarks>
    public TextWritingMode WritingMode
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextWritingMode.VerticalLeftToRight, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets how the characters of a vertical line are set: each one as the Unicode Standard says (the default), all
    /// upright, or all on their side. It is not used in a horizontal layout.
    /// </summary>
    /// <remarks>
    /// With <see cref="TextOrientation.Mixed"/>, a character the standard sets upright with another glyph than in a
    /// horizontal line (a small kana, an ideographic comma) is upright, and one it sets on its side unless the font
    /// has a vertical form for it (a bracket, a long vowel mark) is upright when the font has that form, and on its
    /// side otherwise. Upright text is laid out from the top, whatever its script: among right-to-left text, it
    /// keeps the order of the text.
    /// </remarks>
    public TextOrientation Orientation
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextOrientation.Sideways, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the length of the runs of digits that are set horizontally in the room of one upright character of a
    /// vertical line (tate-chu-yoko): 0 (the default) for none, or 2, 3 or 4.
    /// </summary>
    /// <remarks>
    /// A run of ASCII digits (0 to 9) that is not longer than this, between two characters that are not digits and in
    /// one span, is set as the text of a style that is combined upright (<see cref="TextStyle.IsCombinedUpright"/>):
    /// the date 2026 or the number 12 in a column of Japanese text. Longer numbers are on their side, as other text
    /// is. It is the <c>digits</c> value of the <c>text-combine-upright</c> property of CSS. It is not used in a
    /// horizontal layout, nor with <see cref="TextOrientation.Sideways"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not 0, 2, 3 or 4.</exception>
    public int CombineUprightDigits
    {
        get;
        init
        {
            if (value is not (0 or 2 or 3 or 4))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The length of the combined digits is 0, 2, 3 or 4.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the height of each line, in pixels, or <see langword="null"/> (the default) for the height the fonts of the
    /// line ask for: the largest ascent, descent and line gap of the fonts used on it.
    /// </summary>
    /// <remarks>The glyphs of a line of a fixed height are centered in it: what the height adds to or takes from the height of the fonts is split above and below.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number up to 1048576.</exception>
    public double? LineHeight
    {
        get;
        init
        {
            if (value is { } height && !(height > 0 && height <= 1048576))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The height of a line is a positive number of pixels, up to 1048576.");

            field = value;
        }
    }

    /// <summary>Gets the distance between two tabulation stops, in spaces of the font of the tabulation. 8 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 1024.</exception>
    public int TabSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1024);
            field = value;
        }
    } = 8;

    /// <summary>Gets what replaces the text that does not fit the layout. <see cref="TextTrimming.None"/> by default.</summary>
    public TextTrimming Trimming
    {
        get;
        init
        {
            ValidateEnum((int)value, (int)TextTrimming.Word, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the text that ends a trimmed line, or <see langword="null"/> (the default) for the horizontal ellipsis
    /// (U+2026) when the font has it and three periods otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">The value is longer than 16 characters.</exception>
    public string? Ellipsis
    {
        get;
        init
        {
            if (value is { Length: > MaxInsertedTextLength })
                throw new ArgumentException("The ellipsis is at most 16 characters.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the space before the first line of each paragraph, in pixels; negative for a hanging indent. 0 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or its magnitude exceeds 1048576.</exception>
    public double FirstLineIndent
    {
        get;
        init
        {
            if (!(Math.Abs(value) <= 1048576))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The indent is a finite number of pixels, up to 1048576 in magnitude.");

            field = value;
        }
    }

    /// <summary>The longest hyphen or ellipsis.</summary>
    internal const int MaxInsertedTextLength = 16;

    private static void ValidateExtent(double value)
    {
        if (!(value >= 0))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A size is a positive number of pixels, or infinity.");
    }

    private static void ValidateEnum(int value, int maximum, string name)
    {
        // Not Enum.IsDefined: it allocates, and the values of these enumerations are consecutive from 0.
        if ((uint)value > (uint)maximum)
            throw new ArgumentOutOfRangeException(name, value, "The value is not a member of the enumeration.");
    }
}
