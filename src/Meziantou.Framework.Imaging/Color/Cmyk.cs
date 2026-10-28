using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A device CMYK color: the amounts of cyan, magenta, yellow and black inks, without color profile.</summary>
/// <remarks>
/// <para>
/// The conversions are the naive ones of CSS Color 5 (<c>device-cmyk()</c>), on the encoded RGB values as they are: the
/// black is what the three inks share (full gray component replacement), and the inks do not interact. They do not
/// describe what a press prints: an image of CMYK samples is converted with its ICC profile
/// (<see cref="CmykConversionOptions"/>), and colors with <see cref="IccColorTransform"/>.
/// </para>
/// <para>The values are not validated: the conversion to RGB clips each component to [0, 1].</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Cmyk : IEquatable<Cmyk>
{
    /// <summary>Initializes a new instance of the <see cref="Cmyk"/> struct.</summary>
    /// <param name="c">The cyan ink, from 0 to 1.</param>
    /// <param name="m">The magenta ink, from 0 to 1.</param>
    /// <param name="y">The yellow ink, from 0 to 1.</param>
    /// <param name="k">The black ink, from 0 to 1.</param>
    public Cmyk(double c, double m, double y, double k)
    {
        C = c;
        M = m;
        Y = y;
        K = k;
    }

    /// <summary>Gets the cyan ink, from 0 to 1.</summary>
    public double C { get; }

    /// <summary>Gets the magenta ink, from 0 to 1.</summary>
    public double M { get; }

    /// <summary>Gets the yellow ink, from 0 to 1.</summary>
    public double Y { get; }

    /// <summary>Gets the black ink, from 0 to 1.</summary>
    public double K { get; }

    /// <summary>Converts an 8-bit RGB color to device CMYK. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Cmyk FromRgb(Rgba32 color) => FromRgb(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to device CMYK. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Cmyk FromRgb(Rgba64 color) => FromRgb(color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

    /// <summary>Converts this color to an opaque 8-bit RGB color, each component clipped to [0, 1].</summary>
    /// <returns>The color.</returns>
    public Rgba32 ToRgba32()
    {
        var (r, g, b) = ToRgb();
        return new Rgba32(ColorValueMath.ToByte(r), ColorValueMath.ToByte(g), ColorValueMath.ToByte(b));
    }

    /// <summary>Converts this color to an opaque 16-bit RGB color, each component clipped to [0, 1].</summary>
    /// <returns>The color.</returns>
    public Rgba64 ToRgba64()
    {
        var (r, g, b) = ToRgb();
        return new Rgba64(ColorValueMath.ToUInt16(r), ColorValueMath.ToUInt16(g), ColorValueMath.ToUInt16(b), ushort.MaxValue);
    }

    private static Cmyk FromRgb(double red, double green, double blue)
    {
        var black = 1 - Math.Max(red, Math.Max(green, blue));
        if (black == 1)
            return new Cmyk(0, 0, 0, 1);

        return new Cmyk((1 - red - black) / (1 - black), (1 - green - black) / (1 - black), (1 - blue - black) / (1 - black), black);
    }

    internal (double Red, double Green, double Blue) ToRgb()
        => (1 - Math.Min(1, (C * (1 - K)) + K), 1 - Math.Min(1, (M * (1 - K)) + K), 1 - Math.Min(1, (Y * (1 - K)) + K));

    /// <inheritdoc />
    public bool Equals(Cmyk other) => C.Equals(other.C) && M.Equals(other.M) && Y.Equals(other.Y) && K.Equals(other.K);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Cmyk other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(C, M, Y, K);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Cmyk({C}, {M}, {Y}, {K})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Cmyk left, Cmyk right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Cmyk left, Cmyk right) => !left.Equals(right);
}
