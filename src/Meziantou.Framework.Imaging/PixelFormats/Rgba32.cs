using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with 8-bit red, green, blue and straight (non-premultiplied) alpha components, stored in RGBA byte order.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgba32 : IEquatable<Rgba32>
{
    /// <summary>The red component.</summary>
    public byte R;

    /// <summary>The green component.</summary>
    public byte G;

    /// <summary>The blue component.</summary>
    public byte B;

    /// <summary>The straight alpha component (0 is fully transparent, 255 is fully opaque).</summary>
    public byte A;

    /// <summary>Initializes a new instance of the <see cref="Rgba32"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public Rgba32(byte r, byte g, byte b, byte a = byte.MaxValue)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Converts this pixel to <see cref="Rgba64"/> using exact full-range expansion (<c>v * 257</c>).</summary>
    /// <returns>The widened pixel.</returns>
    public readonly Rgba64 ToRgba64() => new((ushort)(R * 257), (ushort)(G * 257), (ushort)(B * 257), (ushort)(A * 257));

    /// <inheritdoc />
    public readonly bool Equals(Rgba32 other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgba32 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgba32({R}, {G}, {B}, {A})");

    /// <summary>Converts a pixel to <see cref="Rgba64"/> using exact full-range expansion. The conversion is lossless.</summary>
    /// <param name="value">The pixel to convert.</param>
    public static implicit operator Rgba64(Rgba32 value) => value.ToRgba64();

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgba32 left, Rgba32 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgba32 left, Rgba32 right) => !left.Equals(right);
}
