using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with 10-bit red, green and blue components and a 2-bit straight (non-premultiplied) alpha, packed in a 32-bit value.</summary>
/// <remarks>
/// <para>
/// <see cref="PackedValue"/> is stored in native endianness and holds red in its 10 low bits, then green, then blue, and
/// alpha in its 2 high bits: the layout of <c>DXGI_FORMAT_R10G10B10A2_UNORM</c> and of the <c>RGBA_1010102</c> bitmaps of
/// Android. The colors go from 0 to 1023 and the alpha from 0 (fully transparent) to 3 (fully opaque).
/// </para>
/// <para>
/// A component is the fraction of its largest value: conversions to and from the other formats round to the nearest
/// sample, so the conversion to a format with 16 bits per component and back is exact, and a conversion to this format
/// reduces the precision (alpha keeps 4 levels).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgba1010102 : IEquatable<Rgba1010102>
{
    private const int MaxColor = 1023;
    private const int MaxAlpha = 3;

    /// <summary>The packed components: red in bits 0 to 9, green in bits 10 to 19, blue in bits 20 to 29 and alpha in bits 30 and 31.</summary>
    public uint PackedValue;

    /// <summary>Initializes a new instance of the <see cref="Rgba1010102"/> struct.</summary>
    /// <param name="r">The red component, from 0 to 1023.</param>
    /// <param name="g">The green component, from 0 to 1023.</param>
    /// <param name="b">The blue component, from 0 to 1023.</param>
    /// <param name="a">The straight alpha component, from 0 to 3. Defaults to fully opaque.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component is larger than the largest value of its bits.</exception>
    public Rgba1010102(ushort r, ushort g, ushort b, byte a = MaxAlpha)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(r, MaxColor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(g, MaxColor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, MaxColor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(a, MaxAlpha);
        PackedValue = r | ((uint)g << 10) | ((uint)b << 20) | ((uint)a << 30);
    }

    /// <summary>Gets or sets the red component, from 0 to 1023.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 1023.</exception>
    public ushort R
    {
        readonly get => (ushort)(PackedValue & MaxColor);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxColor);
            PackedValue = (PackedValue & ~(uint)MaxColor) | value;
        }
    }

    /// <summary>Gets or sets the green component, from 0 to 1023.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 1023.</exception>
    public ushort G
    {
        readonly get => (ushort)((PackedValue >> 10) & MaxColor);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxColor);
            PackedValue = (PackedValue & ~((uint)MaxColor << 10)) | ((uint)value << 10);
        }
    }

    /// <summary>Gets or sets the blue component, from 0 to 1023.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 1023.</exception>
    public ushort B
    {
        readonly get => (ushort)((PackedValue >> 20) & MaxColor);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxColor);
            PackedValue = (PackedValue & ~((uint)MaxColor << 20)) | ((uint)value << 20);
        }
    }

    /// <summary>Gets or sets the straight alpha component, from 0 (fully transparent) to 3 (fully opaque).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 3.</exception>
    public byte A
    {
        readonly get => (byte)(PackedValue >> 30);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxAlpha);
            PackedValue = (PackedValue & 0x3FFFFFFFu) | ((uint)value << 30);
        }
    }

    /// <inheritdoc />
    public readonly bool Equals(Rgba1010102 other) => PackedValue == other.PackedValue;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgba1010102 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => (int)PackedValue;

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgba1010102(R: {R}, G: {G}, B: {B}, A: {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgba1010102 left, Rgba1010102 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgba1010102 left, Rgba1010102 right) => !left.Equals(right);
}
