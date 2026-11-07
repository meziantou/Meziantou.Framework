using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A pixel with half-precision floating-point red, green, blue and straight (non-premultiplied) alpha components, for
/// scene-referred and high dynamic range data stored in 8 bytes per pixel.
/// </summary>
/// <remarks>
/// The values have the meaning of those of <see cref="RgbaFloat"/>, with the range and the precision of
/// <see cref="Half"/> (11 significant bits, finite values up to 65,504). Components are stored in native endianness.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct RgbaHalf : IEquatable<RgbaHalf>
{
    /// <summary>The red component.</summary>
    public Half R;

    /// <summary>The green component.</summary>
    public Half G;

    /// <summary>The blue component.</summary>
    public Half B;

    /// <summary>The straight alpha component (0 is fully transparent, 1 is fully opaque).</summary>
    public Half A;

    /// <summary>Initializes a new instance of the <see cref="RgbaHalf"/> struct with a fully opaque alpha.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public RgbaHalf(Half r, Half g, Half b)
        : this(r, g, b, Half.One)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RgbaHalf"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component.</param>
    public RgbaHalf(Half r, Half g, Half b, Half a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <inheritdoc />
    /// <remarks>Components are compared with <see cref="Half.Equals(Half)"/>: a component that is not a number equals itself.</remarks>
    public readonly bool Equals(RgbaHalf other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is RgbaHalf other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"RgbaHalf({R}, {G}, {B}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RgbaHalf left, RgbaHalf right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RgbaHalf left, RgbaHalf right) => !left.Equals(right);
}
