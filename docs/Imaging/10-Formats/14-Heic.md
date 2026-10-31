---
title: HEIC
description: Read the photos of phones and cameras, and write HEIC images and image sequences that other devices open.
---

HEIC stores HEVC (H.265) images in the HEIF container: it is the format of the photos of Apple devices and of many
cameras. The library has its own HEVC decoder and encoder. [HeicEncoder](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder) is **lossless by default**, and lossless files
are large and opened by fewer readers: choose lossy 4:2:0 output for files that other devices must open.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Images | Still images and image sequences, grids (the tiles of phone photos) | Still images and image sequences, grids |
| Samples | 8 to 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome | 8, 10 or 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome |
| Alpha | Straight alpha | Lossless alpha |
| Geometry | Clean aperture applied; orientation reported | Orientation of still images |
| Metadata | ICC profile, EXIF, XMP, CICP color description, content light level, mastering display, pixel aspect ratio | The same |

8-bit files load as [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) with alpha, or [Gray8](cref:Meziantou.Framework.Imaging.Gray8); deeper files load as [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48), [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) or [Gray16](cref:Meziantou.Framework.Imaging.Gray16), their
samples scaled to 16 bits exactly.

## Load a phone photo

A camera stores the image as the sensor reads it, and the rotation as a property of the file: it is reported in
`Metadata.Orientation`, and [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) applies it. The ICC profile (Display P3 on recent phones) and the EXIF block
are kept:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadHeic;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ConvertHeic(string heicPath, string jpegPath)
{
    using var image = Image.Load(heicPath);
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, orientation: {image.Metadata.Orientation}";

    // A camera stores the image as its sensor reads it, and the rotation to apply as a property of the file: the
    // decoder reports it as the orientation of the image and moves no pixel. AutoOrient applies it, so that the
    // JPEG file needs no orientation.
    image.AutoOrient();

    // The ICC profile of the photo (Display P3 for a recent phone) and its EXIF block are stored in the JPEG file
    image.Save(jpegPath, new JpegEncoder { Quality = 90 });
    return description;
}
```
<!-- endref -->

See [Handling phone photos](../03-Guides/05-HandlePhonePhotos.md). Thumbnails, gain maps, depth maps and the other photos
of a burst are not read.

## Save a HEIC file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveHeic;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveHeic(string photoPath, string heicPath, string losslessPath, string animatedPath, string animatedHeicPath)
{
    using (var photo = Image.Load(photoPath))
    {
        // Lossy 4:2:0 with 8 bits is the Main Still Picture profile of HEVC, which every HEIC reader decodes.
        // The alpha plane, when the image has one, is always stored losslessly.
        photo.Save(heicPath, new HeicEncoder
        {
            Compression = HeicCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 4,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
        });

        // The default is lossless: every sample of an 8-bit image is decoded back exactly. The R'G'B' samples are
        // coded as they are (4:4:4), which is a profile of the range extensions of HEVC: fewer readers decode it.
        photo.Save(losslessPath, new HeicEncoder { AllowBitDepthReduction = true });
    }

    // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
    // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedHeicPath, new HeicEncoder { Compression = HeicCompression.Lossy });
}
```
<!-- endref -->

Lossy 4:2:0 output of 8 or 10 bits uses the profiles of HEVC that every HEIC reader decodes. Everything else (lossless
output, 4:4:4 and 4:2:2, 12 bits, gray images and alpha planes) uses the range extensions profiles, which fewer readers
decode.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.Compression) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.HeicCompression.Lossless) or [Lossy](cref:Meziantou.Framework.Imaging.Formats.HeicCompression.Lossy) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.HeicCompression.Lossless) |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.Quality) | 0 (smallest) to 100 (best) for lossy output | 75 |
| [OptimizeQuantization](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.OptimizeQuantization) | Searches several quantization rounding offsets per lossy transform block, balancing reconstructed error against estimated coded size; costs more encoding time | `false` |
| [Effort](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.Effort) | 0 (fastest, largest) to 9 (slowest, smallest): trades encoding time for size, never changes lossless pixels | 5 |
| [ChromaSubsampling](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.ChromaSubsampling) | [Auto](cref:Meziantou.Framework.Imaging.Formats.HeicChromaSubsampling.Auto) (4:2:0), [Ratio420](cref:Meziantou.Framework.Imaging.Formats.HeicChromaSubsampling.Ratio420), [Ratio422](cref:Meziantou.Framework.Imaging.Formats.HeicChromaSubsampling.Ratio422) or [Ratio444](cref:Meziantou.Framework.Imaging.Formats.HeicChromaSubsampling.Ratio444), for lossy output | [Auto](cref:Meziantou.Framework.Imaging.Formats.HeicChromaSubsampling.Auto) |
| [BitDepth](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.BitDepth) | 8, 10 or 12; `null` keeps the depth of the pixel format. A value for a 16-bit pixel format allows rounding to it | `null` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.AllowBitDepthReduction) | Lets 16-bit pixel formats be rounded to 12 bits when [BitDepth](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.BitDepth) is not set | `false` |
| [ClearTransparentColors](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.ClearTransparentColors) | Replaces the color of fully transparent pixels with black, which makes the file smaller | `false` |
| [GridTileSize](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.GridTileSize) | Cuts a still image larger than a tile into tiles coded separately (64 to 65,528 pixels per side); cameras use 512 x 512 | `null` |
| [KeyFrameInterval](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.KeyFrameInterval) | The number of images from a key frame to the next in a sequence; 0 makes the first image the only key frame | 0 |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.DurationRounding) | [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) stores durations exactly or throws; [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds them to microseconds when they need it | [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata HEIC cannot store (text, resolution) | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

The quality scale is not the one of [AvifEncoder](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder): at the same number, a HEIC file is closer to its source and larger.

`OptimizeQuantization` is opt-in and does not change the quality-to-quantizer scale. It can change decoded lossy pixels,
but never lossless pixels or alpha values. It searches block rounding offsets, not a coefficient trellis, and does not
provide a target bit rate or file size. Image sequences still use I and P pictures, without B pictures.

The HEVC profile follows the sample format and coding tools. The level is the lowest one of the Main tier that accommodates
the coded dimensions and the largest coded picture, subject to the profile's coded-picture-buffer and minimum-compression
constraints. Only if no Main-tier level suffices does the encoder try the High tier, then unrestricted level 8.5.
A high level can therefore be necessary even for small dimensions, especially for lossless noise. A still-image grid
bounds the dimensions of each coded picture; changing the level label alone would not make a constrained decoder compatible.
Sequence playback rates come from frame durations in the container; this picture-based level selection does not impose
a bit-rate or luma-sample-rate limit on playback.

## Pitfalls

- `Save("photo.heic")` is lossless: pass a [HeicEncoder](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder) with `Compression = HeicCompression.Lossy` for photos.
- HEIC stores at most 12 bits: [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) and [Gray16](cref:Meziantou.Framework.Imaging.Gray16) images throw unless [BitDepth](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.BitDepth) or [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.AllowBitDepthReduction) is set.
- An image sequence needs a seekable destination and a known frame count, and stores no orientation. A still image
  goes to any stream.
- Encoding a large image takes time: a lower [Effort](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.Effort) encodes faster. With [GridTileSize](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder.GridTileSize) and
  [ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) above 1, the tiles are coded concurrently, and the file is identical.
- A side is at most 65,528 pixels, and a still image at most 4 GiB.
- Premultiplied alpha, bit depths above 12 bits and the screen content, multilayer and 3D extensions of HEVC are
  rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). Only the base layer of a layered stream is decoded.
- HEVC is covered by patents in many countries: check the licensing terms that apply to what you distribute.
