---
title: Color management
description: Convert images to sRGB, tone map HDR images, load CMYK files, convert sample buffers and choose a rendering intent.
---

The samples of an image only mean a color together with a description of their color space: an ICC profile, a CICP
color description, or nothing, which means sRGB. The library treats these descriptions as labels. It keeps them when it
loads and saves an image, and it converts colors only when you ask for it.

## Profiles are labels

- Decoders keep the ICC profile of a file in `image.Metadata.IccProfile`, and encoders write it. A conversion of the
  pixel format keeps it when it can still describe the pixels (a gray profile for gray formats, an RGB profile for
  color formats).
- `Metadata.Cicp` holds the CICP color description of AVIF, HEIC, PNG (`cICP`) and JPEG XL files: color primaries,
  transfer characteristics, matrix coefficients and range. The decoder applies the matrix and the range to produce RGB;
  the primaries and the transfer characteristics stay a label, so the samples of an HDR image are returned as they are
  stored.
- `Metadata.TransferFunction` is [Linear](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TransferFunction) for the linear-light samples of QOI and JPEG XL files, [Srgb](cref:Meziantou.Framework.Imaging.Metadata.ColorTransferFunction.Srgb) otherwise.
- [Metadata.ContentLightLevel](cref:Meziantou.Framework.Imaging.Metadata.ContentLightLevel) and `Metadata.MasteringDisplay` describe how HDR samples were mastered.

Only [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile), [IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform) and a load with [CmykConversionOptions](cref:Meziantou.Framework.Imaging.CmykConversionOptions) convert colors, and the pixels
and their label always change together. To draw sRGB colors on an image of another color space, see
[Drawing](../07-Drawing/01-Overview.md).

## Convert an image to sRGB

`image.ConvertColorProfile(profile)` converts every frame from the color space of the image to the one of a profile,
then labels the image with it:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertToSrgb;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertToSrgb(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Decoders keep the embedded profile as a label; converting the colors is always explicit.
    // An image without profile is already sRGB (sGray for grayscale pixels): nothing is converted then.
    var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
    image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb);

    // Untagged pixels are read as sRGB: the label can be removed for formats that cannot store a profile
    image.Metadata.IccProfile = null;
    image.Save(outputPath);
}
```
<!-- endref -->

- The source is the ICC profile of the image; without one, its CICP color description; without either, sRGB
  ([IccProfile.Srgb](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Srgb)), sGray for gray pixels ([IccProfile.SrgbGray](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.SrgbGray)), or their linear-light forms when
  [TransferFunction](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TransferFunction) is [Linear](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TransferFunction).
- The pixel format is kept, so the destination must be an RGB profile for color formats and a gray profile for [Gray8](cref:Meziantou.Framework.Imaging.Gray8)
  and [Gray16](cref:Meziantou.Framework.Imaging.Gray16). To change the color model, convert the pixel format, or convert sample buffers with an
  [IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform) (see below).
- Colors are converted at the precision of the storage: 16-bit images stay 16-bit. Alpha is copied unchanged. Colors
  outside the destination gamut are clipped per channel.
- The conversion is all or nothing: on failure or cancellation, the image keeps its pixels and its profile.
- After the conversion, the profile describes the pixels: a color description that is not sRGB, the light levels and
  the mastering display are removed, and so is the EXIF thumbnail.
- When both profiles have the same bytes, nothing is converted: converting an untagged image to [IccProfile.Srgb](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Srgb) only
  labels it.

Convert to sRGB before saving to a format that cannot store a profile, or before showing an image where color
management is not available: its colors then look right everywhere.

## Convert an HDR image to a standard one

The samples of high dynamic range images (the PQ and HLG transfer characteristics, as in HDR AVIF and HEIC photos) are
brighter than the white of an ICC profile. [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) only converts them when
[IccColorTransformOptions.ToneMapping](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.ToneMapping) says how to compress their highlights:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertHdrToSrgb;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertHdrToSrgb(string avifPath, string pngPath)
{
    using var image = Image.Load(avifPath);

    // The image has no ICC profile: its color description (BT.2020 primaries with PQ samples, for
    // example) is what the colors are converted from. PQ and HLG samples are only converted when
    // the options say how the light above the white of the result is compressed.
    var options = new IccColorTransformOptions
    {
        ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Bt2390 },
    };
    var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
    image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb, options);

    // The pixels are sRGB ones: the color description and the light levels went with the conversion
    image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
}
```
<!-- endref -->

| [HdrToneMappingOptions](cref:Meziantou.Framework.Imaging.HdrToneMappingOptions) | Default | What it does |
| --- | --- | --- |
| [TargetWhiteLuminance](cref:Meziantou.Framework.Imaging.HdrToneMappingOptions.TargetWhiteLuminance) | 203 cd/m² | The light that becomes the white of the result |
| [SourcePeakLuminance](cref:Meziantou.Framework.Imaging.HdrToneMappingOptions.SourcePeakLuminance) | From the metadata | The brightest light of the source, mapped to that white: the maximum content light level (PQ), else the peak of the mastering display, else 10,000 cd/m² (PQ) or 1,000 cd/m² (HLG) |
| [Operator](cref:Meziantou.Framework.Imaging.HdrToneMappingOptions.Operator) | [Bt2390](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Bt2390) | The curve: [Bt2390](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Bt2390) keeps the light below a knee and rolls off above; also [Clip](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Clip), [Reinhard](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Reinhard), [Hable](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Hable) and [Mobius](cref:Meziantou.Framework.Imaging.HdrToneMappingOperator.Mobius) |

- Every curve applies to the largest component of a color and scales the three together, so highlights keep their hue.
- A source whose peak is below the white is neither compressed nor brightened.
- An ICC profile can also describe HDR samples with a `cicp` tag ([IccProfile.Cicp](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Cicp)): the samples are then converted
  from the color space of the tag.

## Load CMYK images

No pixel format holds CMYK samples, so a CMYK image (a JPEG with four components, a TIFF page of separated data or a
JPEG 2000 file) is only loaded by converting its colors. Without a conversion, [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException); [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) always works and reports [ImageColorModel.Cmyk](cref:Meziantou.Framework.Imaging.ImageColorModel.Cmyk) or [Ycck](cref:Meziantou.Framework.Imaging.ImageColorModel.Ycck):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadCmykJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image LoadCmykJpeg(string path)
{
    // Default options: converted to sRGB from the CMYK profile of the file, or from
    // IccProfile.Cgats001CompatibleCmyk when it has none. Images that are not CMYK are loaded as usual.
    var conversion = new CmykConversionOptions
    {
        Transform = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual },
    };

    // The pixels are RGB, labeled with the destination profile (image.Metadata.IccProfile)
    return Image.Load(path, new ImageDecodeOptions { CmykConversion = conversion });
}
```
<!-- endref -->

| [CmykConversionOptions](cref:Meziantou.Framework.Imaging.CmykConversionOptions) | Default | What it does |
| --- | --- | --- |
| [DestinationProfile](cref:Meziantou.Framework.Imaging.CmykConversionOptions.DestinationProfile) | [IccProfile.Srgb](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Srgb) | The RGB profile of the loaded pixels, which are labeled with it |
| [AssumedSourceProfile](cref:Meziantou.Framework.Imaging.CmykConversionOptions.AssumedSourceProfile) | [IccProfile.Cgats001CompatibleCmyk](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Cgats001CompatibleCmyk) | The profile of a file without a CMYK profile; `null` rejects such files |
| [Transform](cref:Meziantou.Framework.Imaging.CmykConversionOptions.Transform) | [IccColorTransformOptions.Default](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.Default) | The rendering intent and the black point compensation |

- The source is the CMYK profile of the file. A malformed or unsupported embedded profile is an error: it is never
  replaced by the assumed profile.
- [IccProfile.Cgats001CompatibleCmyk](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Cgats001CompatibleCmyk) describes web offset printing on coated paper. It is a compact profile that
  reproduces the overall appearance of that printing condition, not the exact colors of a press profile.
- The loaded image has [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24) pixels. A TIFF page keeps its alpha ([Rgba32](cref:Meziantou.Framework.Imaging.Rgba32)) and its 16-bit precision ([Rgba64](cref:Meziantou.Framework.Imaging.Rgba64)).
- Images that are not CMYK are loaded as usual: the setting can be used for every load.
- Every pixel goes through the ICC conversion: a CMYK image is much slower to load than an RGB image of the same size.

[ImageReaderOptions.CmykConversion](cref:Meziantou.Framework.Imaging.ImageReaderOptions.CmykConversion) does the same for
[streaming readers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md).

## Convert sample buffers

[IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform) is the conversion between two profiles that [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) uses, for your own buffers of
interleaved samples without alpha: 8-bit, 16-bit or floating-point. It is the only way to reach CMYK:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertSamples;format=md-fenced-code-block;dedent=true -->
```csharp
public static byte[] ConvertSamplesToSrgb(IccProfile sourceProfile, ReadOnlySpan<byte> samples)
{
    // A transform is immutable and thread-safe: create it once for a pair of profiles and reuse it
    var options = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual };
    var transform = IccColorTransform.Create(sourceProfile, IccProfile.Srgb, options);

    // Interleaved samples without alpha: 1 (gray), 3 (RGB) or 4 (CMYK) per source color, 3 per sRGB color
    var converted = new byte[samples.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
    transform.Convert(samples, converted);
    return converted;
}
```
<!-- endref -->

- A transform is immutable and thread-safe: create it once for a pair of profiles and reuse it. Converting samples
  allocates nothing.
- Integer samples cover the whole range of their type, floating-point samples are in [0, 1], and colors outside the
  destination gamut are clipped per channel. CMYK samples are ink amounts: 0 is no ink.
- The source and the destination can be the same buffer when both profiles have the same number of channels.

## Choose a rendering intent

[IccColorTransformOptions](cref:Meziantou.Framework.Imaging.IccColorTransformOptions) select how colors that differ between two media are rendered:

| [Intent](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.Intent) | What it does |
| --- | --- |
| [RelativeColorimetric](cref:Meziantou.Framework.Imaging.Metadata.IccRenderingIntent.RelativeColorimetric) (the default) | Reproduces the colors in gamut, relative to the white of each medium |
| [Perceptual](cref:Meziantou.Framework.Imaging.Metadata.IccRenderingIntent.Perceptual) | Uses the perceptual tables of the profiles, which their creators made for that purpose |
| [Saturation](cref:Meziantou.Framework.Imaging.Metadata.IccRenderingIntent.Saturation) | Uses the saturation tables of the profiles |
| [AbsoluteColorimetric](cref:Meziantou.Framework.Imaging.Metadata.IccRenderingIntent.AbsoluteColorimetric) | Reproduces the white of the source medium instead of mapping it to the white of the destination |

- Matrix-based profiles, such as those of displays and of sRGB, give the same result for the first three intents.
  Table-based profiles, such as printer profiles, have a table per intent; a profile without the table of an intent
  uses its perceptual table.
- [BlackPointCompensation](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.BlackPointCompensation) (on by default) maps the darkest color of the source to the darkest color of the
  destination, so that shadow detail is neither clipped nor lifted. It changes nothing between two display profiles,
  and does not apply to the absolute colorimetric intent. Keep it on when a version 4 profile meets a version 2 or
  matrix-based one with the perceptual or saturation intent: the black of version 4 tables is that of the perceptual
  reference medium (L* = 3.1373), not 0, and without compensation nothing maps one black to the other.

## Supported profiles

ICC version 2 and version 4 profiles of input, display, output and color space classes, for gray, RGB and CMYK data:
matrix-based profiles (with sampled, gamma and parametric curves) and table-based ones. Conversions are evaluated in
double precision from the formulas of the ICC specification, so they give the same results on every platform.

A malformed profile is an [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException). A valid profile of another kind (version 5, device link,
abstract, named color, other color spaces), or one that cannot be a destination, is an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
