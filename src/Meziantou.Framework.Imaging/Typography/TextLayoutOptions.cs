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
    /// widest one.
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
    /// The lines that do not fit are not part of the layout, except the first one.
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
