using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with 8-bit components and straight (non-premultiplied) alpha, stored in ABGR byte order.</summary>
/// <remarks>
/// The fields are declared in storage order (<see cref="A"/>, <see cref="B"/>, <see cref="G"/>, <see cref="R"/>), the
/// memory layout of a <c>0xRRGGBBAA</c> value stored in little-endian byte order, but the constructor takes components in
/// logical red, green, blue, alpha order, like every other pixel type. It converts to <see cref="Rgba32"/> and back
/// without any loss.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Abgr32 : IEquatable<Abgr32>
{
    /// <summary>The straight alpha component (byte offset 0).</summary>
    public byte A;

    /// <summary>The blue component (byte offset 1).</summary>
    public byte B;

    /// <summary>The green component (byte offset 2).</summary>
    public byte G;

    /// <summary>The red component (byte offset 3).</summary>
    public byte R;

    /// <summary>Initializes a new instance of the <see cref="Abgr32"/> struct from components in logical RGBA order.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public Abgr32(byte r, byte g, byte b, byte a = byte.MaxValue)
    {
        A = a;
        B = b;
        G = g;
        R = r;
    }

    /// <inheritdoc />
    public readonly bool Equals(Abgr32 other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Abgr32 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Abgr32(R: {R}, G: {G}, B: {B}, A: {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Abgr32 left, Abgr32 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Abgr32 left, Abgr32 right) => !left.Equals(right);
}
