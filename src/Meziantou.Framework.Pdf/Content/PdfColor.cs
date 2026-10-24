using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A color in a device color space (ISO 32000-2, 8.6.4): gray, RGB or CMYK, with components from 0 to 1.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfColor : IEquatable<PdfColor>
{
    private readonly double _c0;
    private readonly double _c1;
    private readonly double _c2;
    private readonly double _c3;

    private PdfColor(PdfDeviceColorSpace colorSpace, double c0, double c1, double c2, double c3)
    {
        ColorSpace = colorSpace;
        _c0 = c0;
        _c1 = c1;
        _c2 = c2;
        _c3 = c3;
    }

    /// <summary>Gets black, in the gray color space.</summary>
    public static PdfColor Black { get; } = FromGray(0);

    /// <summary>Gets white, in the gray color space.</summary>
    public static PdfColor White { get; } = FromGray(1);

    /// <summary>Gets the color space.</summary>
    public PdfDeviceColorSpace ColorSpace { get; }

    /// <summary>Indicates whether two colors are equal.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> when the colors are equal.</returns>
    public static bool operator ==(PdfColor left, PdfColor right) => left.Equals(right);

    /// <summary>Indicates whether two colors are different.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> when the colors are different.</returns>
    public static bool operator !=(PdfColor left, PdfColor right) => !left.Equals(right);

    /// <summary>Creates a gray color.</summary>
    /// <param name="gray">The gray level, from 0 (black) to 1 (white).</param>
    /// <returns>The color.</returns>
    public static PdfColor FromGray(double gray) => new(PdfDeviceColorSpace.Gray, Check(gray, nameof(gray)), 0, 0, 0);

    /// <summary>Creates an RGB color.</summary>
    /// <param name="red">The red component, from 0 to 1.</param>
    /// <param name="green">The green component, from 0 to 1.</param>
    /// <param name="blue">The blue component, from 0 to 1.</param>
    /// <returns>The color.</returns>
    public static PdfColor FromRgb(double red, double green, double blue) => new(PdfDeviceColorSpace.Rgb, Check(red, nameof(red)), Check(green, nameof(green)), Check(blue, nameof(blue)), 0);

    /// <summary>Creates an RGB color from components from 0 to 255.</summary>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    /// <returns>The color.</returns>
    public static PdfColor FromRgb24(byte red, byte green, byte blue) => new(PdfDeviceColorSpace.Rgb, red / 255.0, green / 255.0, blue / 255.0, 0);

    /// <summary>Creates a CMYK color.</summary>
    /// <param name="cyan">The cyan component, from 0 to 1.</param>
    /// <param name="magenta">The magenta component, from 0 to 1.</param>
    /// <param name="yellow">The yellow component, from 0 to 1.</param>
    /// <param name="black">The black component, from 0 to 1.</param>
    /// <returns>The color.</returns>
    public static PdfColor FromCmyk(double cyan, double magenta, double yellow, double black) => new(PdfDeviceColorSpace.Cmyk, Check(cyan, nameof(cyan)), Check(magenta, nameof(magenta)), Check(yellow, nameof(yellow)), Check(black, nameof(black)));

    /// <summary>Gets the components of the color: one for gray, three for RGB, four for CMYK.</summary>
    /// <returns>The components.</returns>
    public double[] GetComponents()
    {
        return ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => [_c0],
            PdfDeviceColorSpace.Rgb => [_c0, _c1, _c2],
            _ => [_c0, _c1, _c2, _c3],
        };
    }

    /// <inheritdoc/>
    public bool Equals(PdfColor other) => ColorSpace == other.ColorSpace && _c0.Equals(other._c0) && _c1.Equals(other._c1) && _c2.Equals(other._c2) && _c3.Equals(other._c3);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ColorSpace, _c0, _c1, _c2, _c3);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ColorSpace} {string.Join(' ', GetComponents())}");

    private static double Check(double value, string name)
    {
        if (!(value is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(name, value, "A color component must be between 0 and 1");

        return value;
    }
}
