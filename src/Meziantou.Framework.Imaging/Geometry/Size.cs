using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>Represents a non-negative two-dimensional size, in pixels.</summary>
/// <remarks>
/// <para>
/// A <see cref="Size"/> can be empty (zero width or height). APIs that create or resize images reject
/// empty sizes with an <see cref="ArgumentOutOfRangeException"/> instead of silently clamping them.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Size : IEquatable<Size>
{
    /// <summary>Initializes a new instance of the <see cref="Size"/> struct.</summary>
    /// <param name="width">The width, in pixels. Must be zero or positive.</param>
    /// <param name="height">The height, in pixels. Must be zero or positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is negative.</exception>
    public Size(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
    }

    /// <summary>Gets a size whose width and height are zero.</summary>
    public static Size Empty => default;

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the width or the height is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Gets the number of pixels covered by this size, computed without overflow.</summary>
    public long Area => (long)Width * Height;

    /// <summary>Deconstructs the size into its width and height.</summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    public void Deconstruct(out int width, out int height)
    {
        width = Width;
        height = Height;
    }

    /// <inheritdoc />
    public bool Equals(Size other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Size other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <summary>Returns the size formatted as <c>{Width}x{Height}</c>.</summary>
    /// <returns>A string representation of the size.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");

    /// <summary>Compares two sizes for equality.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> if both sizes are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Size left, Size right) => left.Equals(right);

    /// <summary>Compares two sizes for inequality.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> if the sizes differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Size left, Size right) => !left.Equals(right);
}
