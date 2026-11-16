using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with a half-precision floating-point gray level and a straight (non-premultiplied) alpha component, in 4 bytes per pixel.</summary>
/// <remarks>
/// The values have the meaning of those of <see cref="LaFloat"/>, with the range and the precision of <see cref="Half"/>
/// (11 significant bits, finite values up to 65,504). Components are stored in native endianness.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct LaHalf : IEquatable<LaHalf>
{
    /// <summary>The gray level.</summary>
    public Half L;

    /// <summary>The straight alpha component (0 is fully transparent, 1 is fully opaque).</summary>
    public Half A;

    /// <summary>Initializes a new instance of the <see cref="LaHalf"/> struct with a fully opaque alpha.</summary>
    /// <param name="l">The gray level.</param>
    public LaHalf(Half l)
        : this(l, Half.One)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LaHalf"/> struct.</summary>
    /// <param name="l">The gray level.</param>
    /// <param name="a">The straight alpha component.</param>
    public LaHalf(Half l, Half a)
    {
        L = l;
        A = a;
    }

    /// <inheritdoc />
    /// <remarks>Components are compared with <see cref="Half.Equals(Half)"/>: a component that is not a number equals itself.</remarks>
    public readonly bool Equals(LaHalf other) => L.Equals(other.L) && A.Equals(other.A);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is LaHalf other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(L, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"LaHalf({L}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(LaHalf left, LaHalf right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(LaHalf left, LaHalf right) => !left.Equals(right);
}
