using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The vertical metrics of a font: in font units for <see cref="Font.Metrics"/>, in pixels for
/// <see cref="Font.GetMetrics(double)"/>.
/// </summary>
/// <remarks>Every value is a distance: its direction is the one its description gives, whatever the direction of the y axis.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct FontMetrics : IEquatable<FontMetrics>
{
    internal FontMetrics(double ascent, double descent, double lineGap, double capHeight, double xHeight, double underlinePosition, double underlineThickness, double strikethroughPosition, double strikethroughThickness)
    {
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
        CapHeight = capHeight;
        XHeight = xHeight;
        UnderlinePosition = underlinePosition;
        UnderlineThickness = underlineThickness;
        StrikethroughPosition = strikethroughPosition;
        StrikethroughThickness = strikethroughThickness;
    }

    /// <summary>Gets the distance from the baseline to the top of a line, above the baseline.</summary>
    public double Ascent { get; }

    /// <summary>Gets the distance from the baseline to the bottom of a line, below the baseline.</summary>
    public double Descent { get; }

    /// <summary>Gets the additional space the font recommends between two lines.</summary>
    public double LineGap { get; }

    /// <summary>Gets the distance between two baselines the font recommends: <see cref="Ascent"/> + <see cref="Descent"/> + <see cref="LineGap"/>.</summary>
    public double LineHeight => Ascent + Descent + LineGap;

    /// <summary>Gets the height of the capital letters above the baseline, or 0 when the font does not declare it.</summary>
    public double CapHeight { get; }

    /// <summary>Gets the height of the lowercase letter x above the baseline, or 0 when the font does not declare it.</summary>
    public double XHeight { get; }

    /// <summary>Gets the distance from the baseline to the top of the underline, below the baseline.</summary>
    public double UnderlinePosition { get; }

    /// <summary>Gets the thickness of the underline.</summary>
    public double UnderlineThickness { get; }

    /// <summary>Gets the distance from the baseline to the top of the strikethrough line, above the baseline.</summary>
    public double StrikethroughPosition { get; }

    /// <summary>Gets the thickness of the strikethrough line.</summary>
    public double StrikethroughThickness { get; }

    /// <inheritdoc />
    public bool Equals(FontMetrics other)
    {
        return Ascent == other.Ascent && Descent == other.Descent && LineGap == other.LineGap
            && CapHeight == other.CapHeight && XHeight == other.XHeight
            && UnderlinePosition == other.UnderlinePosition && UnderlineThickness == other.UnderlineThickness
            && StrikethroughPosition == other.StrikethroughPosition && StrikethroughThickness == other.StrikethroughThickness;
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FontMetrics other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Ascent);
        hash.Add(Descent);
        hash.Add(LineGap);
        hash.Add(CapHeight);
        hash.Add(XHeight);
        hash.Add(UnderlinePosition);
        hash.Add(UnderlineThickness);
        hash.Add(StrikethroughPosition);
        hash.Add(StrikethroughThickness);
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(FontMetrics left, FontMetrics right) => left.Equals(right);

    /// <summary>Determines whether two values are different.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are different.</returns>
    public static bool operator !=(FontMetrics left, FontMetrics right) => !left.Equals(right);

    internal FontMetrics Scale(double factor)
    {
        return new FontMetrics(
            Ascent * factor,
            Descent * factor,
            LineGap * factor,
            CapHeight * factor,
            XHeight * factor,
            UnderlinePosition * factor,
            UnderlineThickness * factor,
            StrikethroughPosition * factor,
            StrikethroughThickness * factor);
    }
}
