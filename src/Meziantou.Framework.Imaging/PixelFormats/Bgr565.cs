using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque pixel with a 5-bit red, a 6-bit green and a 5-bit blue component, packed in a 16-bit value.</summary>
/// <remarks>
/// <para>
/// <see cref="PackedValue"/> is stored in native endianness and holds blue in its 5 low bits, green in the next 6 bits and red
/// in its 5 high bits: the layout of <c>DXGI_FORMAT_B5G6R5_UNORM</c> and of the <c>RGB_565</c> bitmaps of Android. The
/// components go from 0 to 31 (red and blue) or 63 (green), the largest value of their bits.
/// </para>
/// <para>
/// A component is the fraction of its largest value: conversions to and from the other formats round to the nearest
/// sample, so the conversion to a format with 8 bits or more per component and back is exact, and a conversion to this
/// format reduces the precision.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Bgr565 : IEquatable<Bgr565>
{
    private const int MaxRedBlue = 31;
    private const int MaxGreen = 63;

    /// <summary>The packed components: blue in bits 0 to 4, green in bits 5 to 10 and red in bits 11 to 15.</summary>
    public ushort PackedValue;

    /// <summary>Initializes a new instance of the <see cref="Bgr565"/> struct.</summary>
    /// <param name="r">The red component, from 0 to 31.</param>
    /// <param name="g">The green component, from 0 to 63.</param>
    /// <param name="b">The blue component, from 0 to 31.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component is larger than the largest value of its bits.</exception>
    public Bgr565(byte r, byte g, byte b)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(r, MaxRedBlue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(g, MaxGreen);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, MaxRedBlue);
        PackedValue = (ushort)((r << 11) | (g << 5) | b);
    }

    /// <summary>Gets or sets the red component, from 0 to 31.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 31.</exception>
    public byte R
    {
        readonly get => (byte)(PackedValue >> 11);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRedBlue);
            PackedValue = (ushort)((PackedValue & 0x07FF) | (value << 11));
        }
    }

    /// <summary>Gets or sets the green component, from 0 to 63.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 63.</exception>
    public byte G
    {
        readonly get => (byte)((PackedValue >> 5) & MaxGreen);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxGreen);
            PackedValue = (ushort)((PackedValue & 0xF81F) | (value << 5));
        }
    }

    /// <summary>Gets or sets the blue component, from 0 to 31.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is larger than 31.</exception>
    public byte B
    {
        readonly get => (byte)(PackedValue & MaxRedBlue);
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRedBlue);
            PackedValue = (ushort)((PackedValue & 0xFFE0) | value);
        }
    }

    /// <inheritdoc />
    public readonly bool Equals(Bgr565 other) => PackedValue == other.PackedValue;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Bgr565 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => PackedValue;

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Bgr565(R: {R}, G: {G}, B: {B})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Bgr565 left, Bgr565 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Bgr565 left, Bgr565 right) => !left.Equals(right);
}
