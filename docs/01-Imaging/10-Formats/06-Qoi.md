---
title: QOI
description: Read and write QOI files, a simple lossless format for one 8-bit RGB or RGBA image.
---

QOI (the "Quite OK Image" format) stores one lossless 8-bit RGB or RGBA still image, and nothing else: no animation and
no metadata. `QoiEncoder` keeps every sample, including the colors of fully transparent pixels, and its output is
byte-identical to the one of the reference encoder.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Pixels | 3 channels (loaded as `Rgb24`) or 4 channels (loaded as `Rgba32`) | 4 channels for pixel formats with alpha, 3 otherwise; gray images are stored as RGB |
| Colorspace field | Reported as `Metadata.TransferFunction` | Written from `Metadata.TransferFunction` |
| Metadata | None | None |

## Save a QOI file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveQoi;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveQoi(string inputPath, string qoiPath, string linearQoiPath, string pngPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0]; // QOI stores one still image
    frame.Save(qoiPath, new QoiEncoder { AllowBitDepthReduction = true });

    // A QOI colorspace of 1 (linear samples) is reported as a label, never applied: other formats cannot store it, so
    // saving the pixels elsewhere requires an explicit decision instead of silently relabeling them as sRGB
    using var linear = Image.Load(linearQoiPath);
    if (linear.Metadata.TransferFunction == ColorTransferFunction.Linear)
    {
        linear.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
    }
}
```
<!-- endref -->

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `AllowBitDepthReduction` | Lets 16-bit pixel formats be rounded to 8 bits | `false` |
| `MetadataHandling` | What to do with metadata, which QOI cannot store | `Strict` |

## Pitfalls

- QOI stores one frame: an animation, or an image with a poster frame, throws an `UnsupportedImageFeatureException`.
  Save one frame with `image.Frames[i].Save`.
- QOI stores no metadata: an image with an ICC profile, EXIF, XMP, an orientation, a resolution or text entries throws
  by default. Set `MetadataHandling` to `DiscardUnsupported` or `Strip`, after `AutoOrient` if the orientation matters.
- A colorspace of 1 (linear samples) is a label, never applied: the pixels load as they are stored, labeled
  `ColorTransferFunction.Linear`. Saving them to a format that cannot store the label follows `MetadataHandling`.
  `MetadataHandling.Strip` writes a colorspace of 0.
- 16-bit images need `AllowBitDepthReduction`.
