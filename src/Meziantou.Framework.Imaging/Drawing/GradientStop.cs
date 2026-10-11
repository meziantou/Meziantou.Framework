using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A color at a position of a gradient.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct GradientStop : IEquatable<GradientStop>
{
    /// <summary>Initializes a new instance of the <see cref="GradientStop"/> struct.</summary>
    /// <param name="offset">The position along the gradient, from 0 (its start) to 1 (its end).</param>
    /// <param name="color">The color, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is not between 0 and 1.</exception>
    public GradientStop(double offset, Rgba64 color)
    {
        if (!(offset >= 0 && offset <= 1))
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The offset must be between 0 and 1.");

        Offset = offset;
        Color = color;
    }

    /// <summary>Gets the position along the gradient, from 0 (its start) to 1 (its end).</summary>
    public double Offset { get; }

    /// <summary>Gets the color, with straight alpha.</summary>
    public Rgba64 Color { get; }

    /// <inheritdoc />
    public bool Equals(GradientStop other) => Offset.Equals(other.Offset) && Color.Equals(other.Color);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is GradientStop other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Offset, Color);

    /// <summary>Returns the stop formatted as <c>{Offset}: {Color}</c>.</summary>
    /// <returns>A string representation of the stop.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Offset}: {Color}");

    /// <summary>Compares two stops for equality.</summary>
    /// <param name="left">The first stop.</param>
    /// <param name="right">The second stop.</param>
    /// <returns><see langword="true"/> if both stops are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(GradientStop left, GradientStop right) => left.Equals(right);

    /// <summary>Compares two stops for inequality.</summary>
    /// <param name="left">The first stop.</param>
    /// <param name="right">The second stop.</param>
    /// <returns><see langword="true"/> if the stops differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(GradientStop left, GradientStop right) => !left.Equals(right);
}
