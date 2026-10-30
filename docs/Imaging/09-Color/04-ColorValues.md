---
title: Color values
description: Convert single colors between RGB, CIELAB, Oklch, HSL and other spaces, read CSS notations and compare colors.
---

Single colors have value types of their own, in the [Meziantou.Framework.Imaging](cref:Meziantou.Framework.Imaging) namespace. Use them to build a
palette, find a lighter tint, measure how different two colors look, or read the colors of a style sheet. They are
immutable `readonly struct`s of `double` components, and their operations allocate nothing except the strings they
format.

## Convert, compare and mix colors

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ColorValues;format=md-fenced-code-block;dedent=true -->
```csharp
public static (string Lab, double Difference, Rgba32[] Palette, Rgba32 Tint, Rgba32 InDisplayP3) DescribeColors()
{
    // Colors of CSS, as sRGB colors: hexadecimal, rgb(), hsl(), hwb(), lab(), lch(), oklab() and oklch()
    var brand = Rgba32.ParseCss("oklch(62% 0.2 29)");
    var accent = Rgba32.ParseCss("#2A9D8F");

    // CIELAB, relative to D50 as in CSS, and its CSS notation
    var lab = CieLab.FromRgb(brand);
    var labText = lab.ToCssString();

    // How different two colors look: about 1 is a just noticeable difference
    var difference = ColorDifference.DeltaE2000(lab, CieLab.FromRgb(accent));

    // Five colors evenly spaced in Oklch, from one color to the other along the shorter arc of hues
    var from = Oklch.FromRgb(brand);
    var to = Oklch.FromRgb(accent);
    var palette = new Rgba32[5];
    for (var i = 0; i < palette.Length; i++)
    {
        palette[i] = Oklch.Lerp(from, to, i / 4.0).ToRgba32();
    }

    // A lighter tint of the same hue and saturation
    var hsl = Hsl.FromRgb(brand);
    var tint = new Hsl(hsl.H, hsl.S, Math.Min(1, hsl.L + 0.2)).ToRgba32();

    // The same color in the samples of a Display P3 image, which its CICP description gives
    var displayP3 = RgbColorSpace.FromCicp(new CicpColorDescription(12, 13, 0, fullRange: true));
    var inDisplayP3 = lab.ToRgba32(displayP3);
    return (labText, difference, palette, tint, inDisplayP3);
}
```
<!-- endref -->

| Type | Color space | CSS notation |
| --- | --- | --- |
| [CieXyz](cref:Meziantou.Framework.Imaging.CieXyz) | CIE XYZ, relative to D50 | |
| [CieLab](cref:Meziantou.Framework.Imaging.CieLab), [CieLch](cref:Meziantou.Framework.Imaging.CieLch) | CIELAB and its cylindrical form | `lab()`, `lch()` |
| [CieLuv](cref:Meziantou.Framework.Imaging.CieLuv) | CIELUV | |
| [Oklab](cref:Meziantou.Framework.Imaging.Oklab), [Oklch](cref:Meziantou.Framework.Imaging.Oklch) | Oklab and its cylindrical form, as CSS defines them | `oklab()`, `oklch()` |
| [Hsl](cref:Meziantou.Framework.Imaging.Hsl), [Hsv](cref:Meziantou.Framework.Imaging.Hsv), [Hwb](cref:Meziantou.Framework.Imaging.Hwb) | Cylindrical forms of RGB values | `hsl()`, `hwb()` |
| [Cmyk](cref:Meziantou.Framework.Imaging.Cmyk) | Device CMYK: the naive conversion of CSS Color 5, without color profile | |

Each type converts from and to [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) and [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) (`FromRgb`, `ToRgba32`, `ToRgba64`).

## RGB values have a color space

`FromRgb`, `ToRgba32` and `ToRgba64` take an [RgbColorSpace](cref:Meziantou.Framework.Imaging.RgbColorSpace), which is sRGB by default:

| [RgbColorSpace](cref:Meziantou.Framework.Imaging.RgbColorSpace) | For |
| --- | --- |
| [RgbColorSpace.Srgb](cref:Meziantou.Framework.Imaging.RgbColorSpace.Srgb) | sRGB colors, such as those of CSS and of untagged images |
| `RgbColorSpace.FromIccProfile(profile)` | The samples of an image with an RGB ICC profile, with the relative colorimetric intent |
| `RgbColorSpace.FromCicp(description)` | The samples of an image with a CICP color description, such as Display P3 or BT.2020; PQ and HLG are rejected, as their light needs tone mapping |

- Create a color space once and reuse it.
- Alpha is not a color coordinate: `FromRgb` ignores it, and the colors `ToRgba32` and `ToRgba64` give are opaque.
- A color outside the gamut of the space is clipped component by component (CSS maps it into the gamut instead).
- [Hsl](cref:Meziantou.Framework.Imaging.Hsl), [Hsv](cref:Meziantou.Framework.Imaging.Hsv), [Hwb](cref:Meziantou.Framework.Imaging.Hwb) and [Cmyk](cref:Meziantou.Framework.Imaging.Cmyk) describe the stored RGB values as they are, whatever their color space: they take no
  space. [Cmyk](cref:Meziantou.Framework.Imaging.Cmyk) does not describe what a press prints: to prepare colors for print, use an
  [ICC conversion](03-ColorManagement.md#convert-sample-buffers).

## The white is D50

[CieXyz](cref:Meziantou.Framework.Imaging.CieXyz) is relative to [CieXyz.D50](cref:Meziantou.Framework.Imaging.CieXyz.D50) in every conversion, as the `lab()` colors of CSS and the connection space of ICC
profiles are. [CieLab.FromXyz](cref:Meziantou.Framework.Imaging.CieLab.FromXyz) and [CieLuv.FromXyz](cref:Meziantou.Framework.Imaging.CieLuv.FromXyz) take another white, and [CieXyz.Adapt](cref:Meziantou.Framework.Imaging.CieXyz.Adapt) moves a color from a white
to another with the Bradford transform. The conversions use the numbers of CSS Color 4, and are tested against its
sample code.

## Compare colors

[ColorDifference](cref:Meziantou.Framework.Imaging.ColorDifference) measures how different two colors look:

| Method | Formula |
| --- | --- |
| [DeltaE76](cref:Meziantou.Framework.Imaging.ColorDifference.DeltaE76) | The distance in CIELAB |
| [DeltaE94](cref:Meziantou.Framework.Imaging.ColorDifference.DeltaE94) | CIE 1994, with the weights of graphic arts (the default) or textiles; the first color is the reference |
| [DeltaE2000](cref:Meziantou.Framework.Imaging.ColorDifference.DeltaE2000) | CIEDE2000, with optional weights of lightness, chroma and hue |
| [DeltaEOK](cref:Meziantou.Framework.Imaging.ColorDifference.DeltaEOK) | The distance in Oklab, as CSS uses it |

With CIEDE2000, a difference of about 1 is the smallest one people notice.

## Mix colors and change hues

[Oklab.Lerp](cref:Meziantou.Framework.Imaging.Oklab.Lerp) and [Oklch.Lerp](cref:Meziantou.Framework.Imaging.Oklch.Lerp) mix two colors, as the palette of the example above does. The hues of [Oklch.Lerp](cref:Meziantou.Framework.Imaging.Oklch.Lerp)
follow the arc a [HueInterpolation](cref:Meziantou.Framework.Imaging.HueInterpolation) selects: [Shorter](cref:Meziantou.Framework.Imaging.HueInterpolation.Shorter) (the default, as in CSS), [Longer](cref:Meziantou.Framework.Imaging.HueInterpolation.Longer), [Increasing](cref:Meziantou.Framework.Imaging.HueInterpolation.Increasing) or
[Decreasing](cref:Meziantou.Framework.Imaging.HueInterpolation.Decreasing).

Hues are in degrees, in [0, 360). A gray has no hue: when one of the colors is gray, [Oklch.Lerp](cref:Meziantou.Framework.Imaging.Oklch.Lerp) takes the hue of the
other one.

## Read and write CSS notations

[Rgba32.ParseCss](cref:Meziantou.Framework.Imaging.Rgba32.ParseCss) (and [TryParseCss](cref:Meziantou.Framework.Imaging.Rgba32.TryParseCss)) reads a CSS color and converts it to sRGB: the hexadecimal notation, `rgb()`,
`rgba()`, `hsl()`, `hsla()`, `hwb()`, `lab()`, `lch()`, `oklab()` and `oklch()`. [Rgba32.ParseHex](cref:Meziantou.Framework.Imaging.Rgba32.ParseHex) reads the
hexadecimal notation only.

- `Parse` and `TryParse` of [CieLab](cref:Meziantou.Framework.Imaging.CieLab), [CieLch](cref:Meziantou.Framework.Imaging.CieLch), [Oklab](cref:Meziantou.Framework.Imaging.Oklab), [Oklch](cref:Meziantou.Framework.Imaging.Oklch), [Hsl](cref:Meziantou.Framework.Imaging.Hsl) and [Hwb](cref:Meziantou.Framework.Imaging.Hwb) read their own notation, with the
  modern syntax (`/ alpha`, `none`), and for `hsl()` the legacy syntax with commas too. The alpha is returned apart.
- Percentages, angles (`deg`, `grad`, `rad`, `turn`) and the clamping of CSS are applied as the specification does.
- `ToCssString` writes the modern syntax, with the shortest numbers that read back the same values.
- Named colors (`red`), `calc()`, relative colors and `color()` are not supported. The [Colors](cref:Meziantou.Framework.Imaging.Colors) class has the named
  colors of CSS as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) values.

The values of a color are sample values: drawn on an image, they are not converted to its color profile. See
[Color management](03-ColorManagement.md).
