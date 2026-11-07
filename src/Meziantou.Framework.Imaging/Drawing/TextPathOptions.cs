namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How a text is placed along a path.</summary>
/// <remarks>The options are immutable; <see cref="Default"/> starts the text at the start of the path, on its left.</remarks>
public sealed class TextPathOptions
{
    /// <summary>Gets the default options.</summary>
    public static TextPathOptions Default { get; } = new();

    /// <summary>Gets the distance the text is moved along the path from where <see cref="Alignment"/> puts it, in pixels; negative to move it back. 0 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or its magnitude exceeds 16777216.</exception>
    public double StartOffset
    {
        get;
        init
        {
            Validate(value);
            field = value;
        }
    }

    /// <summary>Gets where the text is placed along the path. <see cref="TextPathAlignment.Start"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enumeration.</exception>
    public TextPathAlignment Alignment
    {
        get;
        init
        {
            if ((uint)value > (uint)TextPathAlignment.End)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The alignment is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the distance from the path to the baseline of the first line of the text, in pixels: positive to move the
    /// text away from the path, on the side it stands on; negative to sink it across the path. 0 by default: the
    /// baseline is on the path.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or its magnitude exceeds 16777216.</exception>
    public double BaselineOffset
    {
        get;
        init
        {
            Validate(value);
            field = value;
        }
    }

    /// <summary>Gets the side of the path the text is drawn on. <see cref="TextPathSide.Left"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enumeration.</exception>
    public TextPathSide Side
    {
        get;
        init
        {
            if ((uint)value > (uint)TextPathSide.Right)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The side is not valid.");

            field = value;
        }
    }

    /// <summary>Gets what happens to the text beyond the ends of the path. <see cref="TextPathOverflow.Extend"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enumeration.</exception>
    public TextPathOverflow Overflow
    {
        get;
        init
        {
            if ((uint)value > (uint)TextPathOverflow.Clip)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The overflow is not valid.");

            field = value;
        }
    }

    private static void Validate(double value)
    {
        if (!(Math.Abs(value) <= 16777216))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A distance is a finite number of pixels, up to 16777216 in magnitude.");
    }
}
