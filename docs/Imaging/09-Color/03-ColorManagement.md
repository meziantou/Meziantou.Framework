---
title: Color management
description: Convert images to sRGB, tone map HDR images, keep, convert and write CMYK images, convert sample buffers, choose a rendering intent, map colors into the gamut and use device link and abstract profiles.
---

The samples of an image only mean a color together with a description of their color space: an ICC profile, a CICP
color description, or nothing, which means sRGB. The library treats these descriptions as labels. It keeps them when it
loads and saves an image, and it converts colors only when you ask for it.

## Profiles are labels

- Decoders keep the ICC profile of a file in `image.Metadata.IccProfile`, and encoders write it. A conversion of the
  pixel format keeps it when it can still describe the pixels (a gray profile for gray formats, a CMYK profile for the
  CMYK formats, an RGB profile for the other color formats).
- `Metadata.Cicp` holds the CICP color description of AVIF, HEIC, PNG (`cICP`) and JPEG XL files: color primaries,
  transfer characteristics, matrix coefficients and range. The decoder applies the matrix and the range to produce RGB;
  the primaries and the transfer characteristics stay a label, so the samples of an HDR image are returned as they are
  stored.
- `Metadata.TransferFunction` is [Linear](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TransferFunction) for the linear-light samples of QOI and JPEG XL files, [Srgb](cref:Meziantou.Framework.Imaging.Metadata.ColorTransferFunction.Srgb) otherwise.
- [Metadata.ContentLightLevel](cref:Meziantou.Framework.Imaging.Metadata.ContentLightLevel) and `Metadata.MasteringDisplay` describe how HDR samples were mastered.

Only [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile), [IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform), a load with [CmykConversionOptions](cref:Meziantou.Framework.Imaging.CmykConversionOptions) and a conversion with
[PixelConversionOptions.CmykConversion](cref:Meziantou.Framework.Imaging.PixelConversionOptions.CmykConversion) convert colors, and the pixels and their label always change together. To draw sRGB colors on an image of another color space, see
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
  outside the destination gamut are clipped per channel, unless [GamutMapping](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.GamutMapping) maps them (see
  [Map colors into the gamut](#map-colors-into-the-gamut)).
- The conversion is all or nothing: on failure or cancellation, the image keeps its pixels and its profile.
- After the conversion, the profile describes the pixels: a color description that is not sRGB, the light levels and
  the mastering display are removed, and so is the EXIF thumbnail.
- When both profiles have the same bytes, nothing is converted: converting an untagged image to [IccProfile.Srgb](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Srgb) only
  labels it.

Convert to sRGB before saving to a format that cannot store a profile, or before showing an image where color
management is not available: its colors then look right everywhere. [IccProfile.DisplayP3](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.DisplayP3) is the wide-gamut color
space of the screens of phones and computers: convert to it, with 16-bit pixels, to keep the colors of a wider profile
that sRGB would clip.

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

## Keep CMYK images

Print workflows keep the inks of an image: load a CMYK JPEG file or TIFF page as [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) or [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) pixels and
save it as JPEG or TIFF, and nothing is converted:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadCmykPixels;format=md-fenced-code-block;dedent=true -->
```csharp
public static void KeepCmykInks(string jpegPath, string tiffPath, string previewPath)
{
    // The ink amounts as they are (0 is no ink), labeled with the CMYK profile of the file
    using var inks = Image.Load<Cmyk32>(jpegPath);

    // Crop, Flip, Rotate by quarter turns and AutoOrient keep CMYK pixels; JPEG and TIFF store them
    inks.Crop(new Rectangle(0, 0, inks.Width / 2, inks.Height));
    inks.Save(tiffPath, new TiffEncoder());

    // Colors of light are an explicit conversion: from the CMYK profile of the image when it has one,
    // otherwise with the device CMYK formulas
    var conversion = new PixelConversionOptions { CmykConversion = new CmykPixelConversionOptions() };
    using var preview = inks.CloneAs<Rgb24>(conversion);
    preview.Save(previewPath);
}
```
<!-- endref -->

- A component is the amount of an ink: 0 is no ink, so paper white is `(0, 0, 0, 0)`. The decoder complements the
  samples of Adobe JPEG files (255 is no ink there) and converts YCCK files to CMYK; TIFF pages store the amounts.
- The CMYK profile of the file labels the pixels ([IccProfile](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.IccProfile)), and the encoders write it back.
- 12-bit and lossless JPEG files and 16-bit TIFF pages keep their precision as [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64). [CloneAs](cref:Meziantou.Framework.Imaging.Image.CloneAs) converts
  between [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) and [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) like any change of precision.
- CMYK pixels have no alpha: a TIFF page of inks with alpha is only loaded as RGBA, with [CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion).
- Inks are not colors of light. [Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop), [Flip](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flip), [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate) by quarter turns and [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) move the pixels;
  the other operations (resizing, filters, drawing...) and the encoders other than JPEG and TIFF throw an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): convert the image to RGB first.
- JPEG output is the layout of Adobe Photoshop: four components without subsampling, the complements of the inks and an
  Adobe segment without color transform, baseline only. TIFF output stores separated samples (`PhotometricInterpretation`
  5, `InkSet` 1) with a lossless compression.

## Convert between CMYK and RGB pixels

[PixelConversionOptions.CmykConversion](cref:Meziantou.Framework.Imaging.PixelConversionOptions.CmykConversion) lets [CloneAs](cref:Meziantou.Framework.Imaging.Image.CloneAs) and the typed loads convert between CMYK pixels and the other formats.
To prepare an image for a press, give the profile of its printing condition:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertToCmyk;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveForPrint(Image image, IccProfile pressProfile, string path)
{
    // The press profile (a CMYK output profile) describes the inks and labels them; the colors of the image are
    // those of its own profile, or sRGB when it has none. CMYK has no alpha: flatten transparent pixels on white
    var conversion = new PixelConversionOptions
    {
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        CmykConversion = new CmykPixelConversionOptions
        {
            CmykProfile = pressProfile,
            Transform = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual },
        },
    };

    using var inks = image.CloneAs<Cmyk32>(conversion);
    inks.Save(path, new JpegEncoder { Quality = 90 });
}
```
<!-- endref -->

| [CmykPixelConversionOptions](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions) | Default | What it does |
| --- | --- | --- |
| [CmykProfile](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions.CmykProfile) | `null` | The CMYK profile of the inks when the image has none: the source of CMYK pixels, the destination of a conversion to CMYK (it labels the inks). `null` uses the device CMYK formulas |
| [RgbProfile](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions.RgbProfile) | [IccProfile.Srgb](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Srgb) | The RGB profile of the colors converted from CMYK pixels (it labels them), and of the RGB pixels without profile converted to CMYK |
| [Transform](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions.Transform) | [IccColorTransformOptions.Default](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.Default) | The rendering intent and the black point compensation |

- From CMYK pixels, the colors are converted from the CMYK profile of the image, or from [CmykProfile](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions.CmykProfile). Without
  any, the device CMYK formulas of [Cmyk](cref:Meziantou.Framework.Imaging.Cmyk) are used and the colors have no profile: a quick preview, not
  what a press prints.
- To CMYK pixels, the colors are converted from the profile of the image (sRGB when it has none) to [CmykProfile](cref:Meziantou.Framework.Imaging.CmykPixelConversionOptions.CmykProfile),
  or with the device formulas without it. The profile must have the tables from the connection space (`B2A`): an output
  profile of a printing condition. [IccProfile.Cgats001CompatibleCmyk](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.Cgats001CompatibleCmyk) only describes inks, it cannot be a destination.
- CMYK has no alpha: transparent pixels need [BackgroundColor](cref:Meziantou.Framework.Imaging.PixelConversionOptions.BackgroundColor), and floating-point pixels follow the rules of the
  other conversions to integers. Gray pixels are converted like the RGB color of their gray level.
- The ICC conversion of 8-bit pixels uses 8-bit samples, and 16-bit samples as soon as one side has 16 bits. The device
  formulas are computed in double precision and rounded once.
- Without the option, a conversion between CMYK and other pixels throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException):
  inks are never turned into colors of light by accident.

## Load CMYK files as RGB

A CMYK image (a JPEG with four components, a TIFF page of separated data or a JPEG 2000 file) can also be converted to
RGB while it is loaded. Without a conversion, the untyped [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) (load the JPEG and TIFF files as [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) to keep their inks); [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) always works and reports [ImageColorModel.Cmyk](cref:Meziantou.Framework.Imaging.ImageColorModel.Cmyk) or [Ycck](cref:Meziantou.Framework.Imaging.ImageColorModel.Ycck):

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
interleaved samples without alpha: 8-bit, 16-bit or floating-point, CMYK samples included:

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
  destination gamut are clipped per channel unless [GamutMapping](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.GamutMapping) maps them. CMYK samples are ink
  amounts: 0 is no ink, the layout of [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) and [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) pixels.
- With [ClipFloatingPointSamples](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.ClipFloatingPointSamples) set to `false`, floating-point samples keep their values outside [0, 1]: the light
  above the white of HDR colors, and the negative components of wide-gamut colors. The tone curves are extended beyond
  [0, 1] (mirrored below 0, as in CSS Color 4). Only matrix-based profiles can do this: a conversion through a lookup
  table, or with a gamut mapping, is refused.
- The source and the destination can be the same buffer when both profiles have the same number of channels.

## Render the HDR version of a phone photo

Ultra HDR JPEG files, and the HEIC and AVIF photos of recent phones, store a standard dynamic range image and a gain
map: a second image that gives, pixel by pixel, how much brighter the high dynamic range version is. Loading the photo
gives the standard image, as before. [AuxiliaryImageCollection](cref:Meziantou.Framework.Imaging.AuxiliaryImageCollection) lists the gain map, and the thumbnails, depth maps and mattes
of the file, and decodes them on request:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs;region=Imaging_HdrGainMap;format=md-fenced-code-block;dedent=true -->
```csharp
public static IReadOnlyList<AuxiliaryImageKind> SaveHdrRendition(string photoPath, string exrPath)
{
    // Image.Load reads the main image only. The other images of the file are listed here, from its structure, and
    // each one is decoded on request: the gain map of an Ultra HDR JPEG, the thumbnails, depth maps, mattes and
    // gain maps of a HEIC or AVIF photo
    var images = AuxiliaryImageCollection.Load(photoPath);
    var kinds = images.Select(image => image.Kind).ToList();

    // The gain map turns the standard dynamic range photo into its high dynamic range version. The headroom of the
    // display, in stops (log2 of its peak light over the white of SDR), selects how much of the gain applies: the
    // headroom of the alternate rendition gives all of it. The result is linear light, 1 being the white of SDR
    if (images.GainMapImage?.GainMapMetadata is { } metadata)
    {
        using var hdr = images.DecodeHdr(metadata.AlternateHdrHeadroom);

        // OpenEXR stores the floating-point samples, and the primaries as its chromaticities
        hdr.Save(exrPath, new OpenExrEncoder());
    }

    return kinds;
}
```
<!-- endref -->

- The headroom of a display is in stops: the log2 of its peak light over the white of SDR. 0 gives the standard
  image in linear light; the headroom of the alternate rendition (the [GainMapMetadata](cref:Meziantou.Framework.Imaging.Metadata.GainMapMetadata)) gives the whole HDR
  version.
- The result is linear light where 1 is the white of SDR, in the primaries of the photo, with nothing clipped. The
  parameters come from the ISO 21496-1 metadata (JPEG, HEIC, AVIF) or from the `hdrgm` XMP properties of Ultra HDR;
  the gain maps of Apple that have no ISO 21496-1 metadata are listed without parameters.

## Write a photo with its gain map

[JpegEncoder.GainMap](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.GainMap) and [AvifEncoder.GainMap](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.GainMap) write a gain map next to the image: an Ultra HDR JPEG file, or an
AVIF file with a tone map item. A [GainMap](cref:Meziantou.Framework.Imaging.Formats.GainMap) is the gain map image and its [GainMapMetadata](cref:Meziantou.Framework.Imaging.Metadata.GainMapMetadata), for example the
ones of a photo that was read, after the same edit:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs;region=Imaging_WriteGainMap;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeHdrPhoto(string photoPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    var images = AuxiliaryImageCollection.Load(photoPath);
    if (images.GainMapImage is { GainMapMetadata: { } metadata } gainMapImage)
    {
        // The samples of the gain map are its coded values, often at a fraction of the size of the photo: it gets the
        // same geometry (the same resize, crop or rotation), scaled to its own size
        using var gainMap = gainMapImage.Decode();
        var scaleX = (double)gainMap.Width / photo.Width;
        var scaleY = (double)gainMap.Height / photo.Height;
        photo.Resize(new ResizeOptions(1024, 1024) { AllowUpscaling = false });
        gainMap.Resize(new ResizeOptions(Math.Max(1, (int)Math.Round(photo.Width * scaleX, MidpointRounding.AwayFromZero)), Math.Max(1, (int)Math.Round(photo.Height * scaleY, MidpointRounding.AwayFromZero))) { Mode = ResizeMode.Stretch });

        // An Ultra HDR file: the gain map image follows the photo, with its parameters (ISO 21496-1 and XMP). An
        // AvifEncoder with a GainMap writes the same photo as an AVIF file
        photo.Save(outputPath, new JpegEncoder { Quality = 90, GainMap = new GainMap(gainMap, metadata) });
    }
    else
    {
        photo.Resize(new ResizeOptions(1024, 1024) { AllowUpscaling = false });
        photo.Save(outputPath, new JpegEncoder { Quality = 90 });
    }
}
```
<!-- endref -->

- The samples of the gain map image are its coded values, as [AuxiliaryImage.Decode](cref:Meziantou.Framework.Imaging.AuxiliaryImage.Decode) returns them: a grayscale image gives
  one gain for the three channels. Its color description (ICC profile or CICP) is the one of the alternate rendition,
  which a gain map that does not use the color space of the base image applies in.
- The gain map image is encoded with the settings of the encoder, but with full chroma resolution: the gain multiplies
  its errors. Both images are kept in memory until the file is written.
- The parameters are written as the metadata of ISO 21496-1 and, in a JPEG file, as the `hdrgm` XMP properties of
  Ultra HDR 1.0, merged with the XMP packet of each image. XMP cannot describe a gain map in the color space of the
  alternate rendition: the readers of Ultra HDR 1.0, which only read XMP, apply such a gain map in the color space of
  the base image.
- Only a still image has a gain map: an animation, or an animated gain map image, throws an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). The gain map image of an AVIF file must be opaque.

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

## Map colors into the gamut

A color that the destination cannot reproduce is clipped per channel by default
([GamutMapping.Clip](cref:Meziantou.Framework.Imaging.GamutMapping.Clip)), which can change the hue and the lightness of a saturated color.
[GamutMapping.ReduceChroma](cref:Meziantou.Framework.Imaging.GamutMapping.ReduceChroma) reduces its chroma toward gray instead, at constant lightness and hue in Oklab, with the
gamut mapping algorithm of CSS Color 4: it stops once the clipped color is within a just noticeable difference of the
reduced one.

- In-gamut colors are unchanged, and so is every conversion that does not ask for the mapping.
- It applies to a matrix-based destination (displays, sRGB) whatever the intent, and to a table-based destination
  (printers) with the colorimetric intents; the destination then needs a table to the profile connection space. The
  perceptual and saturation tables of a printer profile map the gamut as their creator chose, and a gray destination
  has no chroma: they are used as they are.
- It applies to [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) and to [IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform) alike.

## Convert through a sequence of profiles

[IccColorTransform.Create](cref:Meziantou.Framework.Imaging.IccColorTransform.Create) also takes a list of profiles, in the order the colors go through them. It
accepts device link profiles, which convert device values to other device values, and abstract profiles, which
convert colors of the profile connection space (a color correction, a look):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs;region=Imaging_SoftProof;format=md-fenced-code-block;dedent=true -->
```csharp
public static ushort[] SoftProof(IccProfile display, IccProfile printer, ReadOnlySpan<ushort> rgb)
{
    // The printer profile appears twice: the colors enter it from the profile connection space to become inks, then
    // leave it as the colors of these inks. The colors the printer cannot reproduce lose chroma at constant
    // lightness and hue, instead of being clipped channel by channel.
    var proof = IccColorTransform.Create([display, printer, printer, display], new IccColorTransformOptions
    {
        Intent = IccRenderingIntent.RelativeColorimetric,
        GamutMapping = GamutMapping.ReduceChroma,
    });

    // A transform is immutable and thread-safe: create it once and reuse it
    var preview = new ushort[rgb.Length];
    proof.Convert(rgb, preview);
    return preview;
}
```
<!-- endref -->

- Device values that enter a device profile go to the profile connection space; colors of the connection space that
  enter a device profile become its device values. To go through the device values of a profile, as a soft proof goes
  through the inks of a printer, the profile appears twice.
- A device link profile uses its own table whatever the rendering intent, which was chosen when the link was made.
- Abstract profiles convert the colors after the black point compensation of the device profiles around them.
- A sequence that does not connect, such as an abstract profile first or a device link profile after a device
  profile, is an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).

## Supported profiles

ICC version 2 and version 4 profiles of input, display, output and color space classes, for gray, RGB and CMYK data:
matrix-based profiles (with sampled, gamma and parametric curves) and table-based ones; device link and abstract
profiles in a sequence of profiles. Conversions are evaluated in
double precision from the formulas of the ICC specification, so they give the same results on every platform.

A malformed profile is an [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException). A valid profile of another kind (version 5, named color,
other color spaces, a device link or abstract profile outside a sequence), or one that cannot be a destination, is an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
