using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A tabulation stop of a line: where the text after a tabulation goes, and how it is aligned there.</summary>
/// <remarks>
/// The position is measured from where the text of the line starts (after the indent of the line, as the stops of
/// <see cref="TextLayoutOptions.TabSize"/> are), along the line: from the left of a left-to-right paragraph, from the
/// right of a right-to-left one, and from the top of a vertical line.
/// </remarks>
/// <seealso cref="TextLayoutOptions.TabStops"/>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextTabStop : IEquatable<TextTabStop>
{
    /// <summary>Initializes a tabulation stop.</summary>
    /// <param name="position">The distance of the stop from the start of the text of the line, in pixels: a positive number up to 1048576.</param>
    /// <param name="alignment">How the text after the tabulation is placed at the stop.</param>
    /// <param name="decimalSeparator">The character a <see cref="TextTabAlignment.DecimalSeparator"/> stop aligns: the period by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is not a positive number up to 1048576, or <paramref name="alignment"/> is not a
    /// member of <see cref="TextTabAlignment"/>.
    /// </exception>
    public TextTabStop(double position, TextTabAlignment alignment = TextTabAlignment.Start, char decimalSeparator = '.')
    {
        if (!(position > 0 && position <= 1048576))
            throw new ArgumentOutOfRangeException(nameof(position), position, "The position of a tabulation stop is a positive number of pixels, up to 1048576.");

        if ((uint)alignment > (uint)TextTabAlignment.DecimalSeparator)
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "The value is not a member of the enumeration.");

        Position = position;
        Alignment = alignment;
        DecimalSeparator = decimalSeparator;
    }

    /// <summary>Gets the distance of the stop from the start of the text of the line, in pixels.</summary>
    public double Position { get; }

    /// <summary>Gets how the text after the tabulation is placed at the stop.</summary>
    public TextTabAlignment Alignment { get; }

    /// <summary>Gets the character a <see cref="TextTabAlignment.DecimalSeparator"/> stop aligns.</summary>
    public char DecimalSeparator { get; }

    /// <inheritdoc />
    public bool Equals(TextTabStop other) => Position.Equals(other.Position) && Alignment == other.Alignment && DecimalSeparator == other.DecimalSeparator;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextTabStop other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Position, Alignment, DecimalSeparator);

    /// <summary>Returns the stop in the form <c>120 End</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Position} {Alignment}");

    /// <summary>Determines whether two tabulation stops are the same.</summary>
    public static bool operator ==(TextTabStop left, TextTabStop right) => left.Equals(right);

    /// <summary>Determines whether two tabulation stops differ.</summary>
    public static bool operator !=(TextTabStop left, TextTabStop right) => !left.Equals(right);
}
