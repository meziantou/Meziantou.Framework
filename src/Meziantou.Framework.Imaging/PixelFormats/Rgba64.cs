using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with 16-bit red, green, blue and straight (non-premultiplied) alpha components.</summary>
/// <remarks>
/// Components are stored as <see cref="ushort"/> values in native endianness. Codecs convert from/to the
/// endianness of the encoded format; raw byte access (<see cref="ImageFrame.CopyPixelBytesTo(Span{byte}, int)"/>)
/// exposes native-endian values.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgba64 : IEquatable<Rgba64>
{
    /// <summary>The red component.</summary>
    public ushort R;

    /// <summary>The green component.</summary>
    public ushort G;

    /// <summary>The blue component.</summary>
    public ushort B;

    /// <summary>The straight alpha component (0 is fully transparent, 65535 is fully opaque).</summary>
    public ushort A;

    /// <summary>Initializes a new instance of the <see cref="Rgba64"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public Rgba64(ushort r, ushort g, ushort b, ushort a = ushort.MaxValue)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Creates an <see cref="Rgba64"/> from an <see cref="Rgba32"/> using exact full-range expansion (<c>v * 257</c>).</summary>
    /// <param name="value">The 8-bit pixel.</param>
    /// <returns>The widened pixel.</returns>
    public static Rgba64 FromRgba32(Rgba32 value) => value.ToRgba64();

    /// <inheritdoc />
    public readonly bool Equals(Rgba64 other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgba64 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgba64({R}, {G}, {B}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgba64 left, Rgba64 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgba64 left, Rgba64 right) => !left.Equals(right);
}
