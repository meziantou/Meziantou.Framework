---
title: Color adjustments and effects
description: Convert to grayscale, adjust brightness, contrast and saturation, apply photo presets, replace a color, and add vignettes, mosaics and other effects.
---

These operations change the colors of the pixels and keep the size and the pixel format of the image. They apply to
every frame and to the poster frame, and also exist for one frame. No color profile is applied: the colors are those
of the color space of the image (see [Color management](../09-Color/03-ColorManagement.md)).

## Grayscale

`image.Grayscale()` replaces every color by its Rec. 709 luma, at the precision of the storage. The pixel format and
alpha are kept: an `Rgba32` image stays an `Rgba32` image with gray pixels. To store one sample per pixel, convert the
image with `CloneAs<Gray8>()` or `CloneAs<Gray16>()` (see [Pixel formats](../04-Concepts/05-PixelFormats.md)).
`Grayscale` throws an `UnsupportedImageFeatureException` when the ICC profile of the image does not match its pixel
format.

## Color matrices

`ApplyColorMatrix` gives each pixel a color computed from its own color with a `ColorMatrix`: four rows, for the red,
green, blue and alpha of the result, of five coefficients, for the red, green, blue and alpha of the pixel and a
constant. This is the matrix of the `feColorMatrix` filter of SVG.

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ColorMatrix;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ApplyColorFilters(string photoPath, string iconPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    using var icon = Image.Load(iconPath);

    // Matrices are immutable: create them once. Two matrices become one, which is applied once per pixel
    var warm = ColorMatrix.CreateSaturation(0.6).Then(ColorMatrix.CreateSepia(0.4)).Then(ColorMatrix.CreateContrast(1.1));
    var dim = ColorMatrix.CreateBrightness(0.5);
    var accent = ColorMatrix.CreateTint(Colors.Gold);

    // The whole image, every frame of it
    photo.ApplyColorMatrix(warm);

    photo.Draw(canvas =>
    {
        // What is already drawn, inside a clip: the lower third of the photo is darker
        var band = new Rectangle(0, canvas.Height * 2 / 3, canvas.Width, canvas.Height - (canvas.Height * 2 / 3));
        canvas.Clip(band, clipped => clipped.FilterBackdrop(dim));

        // What a callback draws: the icon keeps its shape and its transparency, and gets one color
        canvas.Filter(accent, tinted => tinted.DrawImage(icon.Frames[0], new PointD(8, band.Y + 8)));
    });

    photo.Save(outputPath);
}
```
<!-- endref -->

The ready-made matrices are the filter functions of CSS:

| Matrix | CSS | Amount |
| --- | --- | --- |
| `CreateBrightness(amount)` | `brightness()` | 1 changes nothing, 0 gives black, more than 1 brightens |
| `CreateContrast(amount)` | `contrast()` | 1 changes nothing, 0 gives the middle gray, more than 1 increases the contrast |
| `CreateSaturation(amount)` | `saturate()` | 1 changes nothing, 0 leaves no color, more than 1 makes the colors more vivid |
| `CreateHueRotation(degrees)` | `hue-rotate()` | An angle: 180 gives each color its opposite hue |
| `CreateGrayscale(amount)` | `grayscale()` | 1 (default) gives gray levels, 0 changes nothing |
| `CreateSepia(amount)` | `sepia()` | 1 (default) gives sepia tones, 0 changes nothing |
| `CreateInvert(amount)` | `invert()` | 1 (default) gives the negative, 0 changes nothing |
| `CreateOpacity(amount)` | `opacity()` | 1 changes nothing, 0 makes every pixel transparent |

Other matrices:

- `CreateTint(color)` gives every pixel one color and keeps its alpha: an icon drawn in another color.
- `CreateDuotone(dark, light)` gives every pixel the color between two inks that its gray level says.
- `CreateLightness(amount)` adds `amount`, from -1 to 1, to every color.
- `CreateColorBlindness(type)` shows the colors as a person with protanopia, deuteranopia, tritanopia, or an anomaly
  of the same cones (`ColorBlindnessType`) sees them. Its matrices are defined on linear light: they are exact on a
  linear-light `RgbaFloat` image, and an approximation on usual sRGB images, as in most simulators.
- `Polaroid`, `Kodachrome`, `Lomograph` and `BlackWhite` are photo presets.

`Then` makes one matrix of two: the colors are computed once, and are not rounded or clamped in between, so a chain of
adjustments is applied in one pass. Matrices are immutable: create them once and reuse them.

The computation uses only multiplications and additions on the stored samples: the result is the same on every
platform, and `CreateGrayscale()` gives the pixels of `Grayscale`, bit for bit. A pixel format without alpha gets the
color of the result over black, and a gray format gets the luma of the result. In a drawing, `canvas.Filter` and
`canvas.FilterBackdrop` apply a matrix to a part of the image, as in the example above (see
[Layers, masks and filters](../07-Drawing/05-LayersMasksAndFilters.md)).

## Replace a color

`ReplaceColor(color, replacement, tolerance)` gives `replacement` to every pixel whose color is `color`, or within
`tolerance` of it: a plain background made transparent, a color of a logo changed:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_ReplaceColor;format=md-fenced-code-block;dedent=true -->
```csharp
public static void MakeBackgroundTransparent(string inputPath, string outputPath)
{
    // Load the pixels with an alpha channel: a JPEG image has none, and could not store the transparency
    using var image = Image.Load<Rgba32>(inputPath);

    // Every pixel within 4 levels of white on each sample (JPEG noise) becomes transparent
    image.ReplaceColor(Colors.White, Colors.Transparent, tolerance: 4.0 / 255);
    image.Save(outputPath);
}
```
<!-- endref -->

The tolerance is a fraction of the range of a sample, from 0 (the exact color only) to 1; `1 / 255` accepts one 8-bit
level on each side. Each of the four samples of a pixel, alpha included, must be within the tolerance. The replacement
is converted to the pixel format as a drawn color is: on a format without alpha, its alpha is lost, which is why the
example loads the image as `Rgba32`.

## Spatial effects

The spatial effects read the neighbors of a pixel, or its position:

| Effect | What it does |
| --- | --- |
| `Vignette(color, radiusX, radiusY, rectangle)` | An elliptic gradient: transparent at the center of the rectangle (the canvas by default), `color` on the ellipse of half-axes `radiusX` and `radiusY` and beyond |
| `Glow(color, radiusX, radiusY, rectangle)` | The opposite gradient: `color` at the center, transparent on the ellipse and beyond |
| `Pixelate(size)` | Every block of `size` by `size` pixels gets the mean of its pixels |
| `OilPaint(levels, radius)` | Every pixel gets the mean of the pixels around it whose intensity is the most frequent there, among `levels` levels |
| `Flatten(backgroundColor)` | Every pixel is composited over an opaque color and becomes opaque; the pixel format is kept |

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Effects;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ApplyPhotoEffects(string photoPath, string outputDirectory)
{
    using var photo = Image.Load(photoPath);

    // A preset matrix, then the dark corners of an old lens: with the half-diagonal as radius, the corners get the
    // whole color and the center none of it
    using (var lomo = photo.Clone())
    {
        lomo.ApplyColorMatrix(ColorMatrix.Lomograph);
        var halfDiagonal = Math.Sqrt((lomo.Width * lomo.Width) + (lomo.Height * lomo.Height)) / 2;
        lomo.Vignette(Colors.Black, halfDiagonal, halfDiagonal);
        lomo.Save(Path.Combine(outputDirectory, "lomo.png"));
    }

    // Spatial effects: blocks of 8x8 pixels, and brush strokes of 7x7 pixels with 20 levels of intensity
    using (var mosaic = photo.Clone())
    {
        mosaic.Pixelate(8);
        mosaic.Save(Path.Combine(outputDirectory, "mosaic.png"));
    }

    using (var painting = photo.Clone())
    {
        painting.OilPaint(levels: 20, radius: 3);
        painting.Save(Path.Combine(outputDirectory, "painting.png"));
    }

    // The colors as a person without green cones sees them
    using (var preview = photo.Clone())
    {
        preview.ApplyColorMatrix(ColorMatrix.CreateColorBlindness(ColorBlindnessType.Deuteranopia));
        preview.Save(Path.Combine(outputDirectory, "deuteranopia.png"));
    }

    // Transparent pixels composited onto white: every pixel is opaque, and the image keeps its pixel format
    photo.Flatten(Colors.White);
    photo.Save(Path.Combine(outputDirectory, "flattened.png"));
}
```
<!-- endref -->

- `Vignette` and `Glow` keep the alpha of every pixel: a transparent pixel stays transparent. To paint over
  transparent pixels too, draw a radial gradient instead (see [Brushes and pens](../07-Drawing/03-BrushesAndPens.md)).
- The means of `Pixelate` and `OilPaint` are weighted by alpha, so the hidden color of a transparent pixel takes no
  part in them.
- `Flatten` gives the pixels of a conversion to a format without alpha with a background color, and a format without
  alpha is left as it is. An encoder of a format without alpha, such as `JpegEncoder`, can also composite the pixels
  itself with its `BackgroundColor` (see [Saving images](../05-LoadingAndSaving/04-SavingImages.md)).
