---
title: Create thumbnails
description: Create small previews of photos and animations, upright, fitted, cropped or letterboxed, in a compact format.
---

A thumbnail is a small copy of an image, shown in a list or a gallery. It must look like the original (upright, with
the right colors), be small on disk, and leak nothing that the original holds, such as the GPS position of a photo.

## Create the thumbnail of a photo

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_CreateThumbnail;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CreateThumbnail(string inputPath, string outputPath)
{
    // A large JPEG photo is decoded at 1/2, 1/4 or 1/8 of its size, as long as it stays at least as large as the box:
    // much faster than decoding every pixel to throw most of them away. The box is square, so the orientation of the
    // photo does not matter. Other formats are decoded at full size
    using var image = Image.Load(inputPath, new ImageDecodeOptions { TargetSize = new Size(200, 200) });

    // A camera stores the rotation of the photo as metadata: turn the pixels upright before resizing
    image.AutoOrient();

    // Fits inside 200x200 and keeps the aspect ratio; an image that is already smaller keeps its size
    image.Resize(new ResizeOptions(200, 200) { Mode = ResizeMode.Contain, AllowUpscaling = false });

    // Stripping the metadata also removes the color profile: convert the colors to sRGB first, which is what
    // viewers assume for an image without profile. Nothing is converted when the image is already sRGB
    image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb);

    image.Save(outputPath, new JpegEncoder
    {
        Quality = 80,
        BackgroundColor = new Rgba32(255, 255, 255), // transparent pixels become white
        AllowBitDepthReduction = true,               // 16-bit images are written with 8 bits
        MetadataHandling = MetadataHandling.Strip,   // no EXIF (camera, GPS position), XMP or text
    });
}
```
<!-- endref -->

Each step matters:

1. **[TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize)** lets the decoder of a JPEG file produce a smaller image, at 1/2, 1/4 or 1/8 of its size, as
   long as it stays at least as large as the box: a 4000x3000 photo is decoded as a 500x375 image, which costs a fraction
   of the memory and the time of the full photo. Other formats are decoded at full size. See
   [Decode a smaller image](../05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).
2. **[AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient)** applies the orientation that the camera wrote in the metadata. Decoders report it
   (`Metadata.Orientation`) and never move a pixel: without this step, a portrait photo becomes a sideways thumbnail
   once the metadata is removed. Turning the image first also makes the box apply to the upright image.
3. **[Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize)** with [ResizeMode.Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain) fits the image inside 200x200 and keeps its aspect ratio: a 4000x3000 photo
   becomes 200x150. With `AllowUpscaling = false`, an image that is already smaller than the box keeps its size instead
   of being enlarged and blurred.
4. **[ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile)** converts the colors to sRGB. A photo labeled with a wider color space, such as the Display
   P3 profile of a phone, would look dull once its profile is removed. An image without profile is already sRGB, and
   nothing is converted.
5. **[Save](cref:Meziantou.Framework.Imaging.Image.Save)** with [MetadataHandling.Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) writes no EXIF, XMP, text or ICC profile: the file is smaller, and the camera
   model, the date and the GPS position stay in the original.

The example writes a JPEG file, which has no transparency and only 8-bit samples: [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BackgroundColor) and
`AllowBitDepthReduction` say how to store an image that has them. Without these settings, [Save](cref:Meziantou.Framework.Imaging.Image.Save) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) for such an image instead of losing information.

## Fit, fill or letterbox

The [Mode](cref:Meziantou.Framework.Imaging.ResizeOptions.Mode) of [ResizeOptions](cref:Meziantou.Framework.Imaging.ResizeOptions) decides how the image meets the box:

| Mode | Result | Typical use |
| --- | --- | --- |
| [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain) (default) | Fits inside the box, keeps the aspect ratio; the result may be smaller than the box on one side | Previews of any shape |
| [Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) | Fills the box exactly and cuts what overflows, around [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) | Square tiles of a grid |
| [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad) | Fits inside the box, then pads to exactly its size with [BackgroundColor](cref:Meziantou.Framework.Imaging.ResizeOptions.BackgroundColor), the image placed at [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) | Tiles of one size that show the whole image |
| [BoxPad](cref:Meziantou.Framework.Imaging.ResizeMode.BoxPad) | Like [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad), but an image that already fits is only placed on the canvas, never enlarged | The same, for small images |
| [Stretch](cref:Meziantou.Framework.Imaging.ResizeMode.Stretch) | Fills the box exactly and distorts the image | Rarely wanted |

A letterboxed thumbnail, with black bars and a white frame:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Letterbox;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Letterbox(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Fits inside 320x240, then pads to exactly 320x240 with black bars, the image centered (ResizeOptions.Anchor).
    // The padding is transparent by default, which a pixel format without alpha (an RGB JPEG) cannot store
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Pad, BackgroundColor = new Rgba64(0, 0, 0) });

    // A 4-pixel white frame on every side: 328x248
    image.Pad(4, 4, 4, 4, new Rgba64(65535, 65535, 65535));
    image.Save(outputPath);
}
```
<!-- endref -->

The padding is transparent by default, which a pixel format without alpha (the RGB pixels of most JPEG images) cannot
store: set an opaque [BackgroundColor](cref:Meziantou.Framework.Imaging.ResizeOptions.BackgroundColor).

`AllowUpscaling = false` keeps [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain), [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad) and [BoxPad](cref:Meziantou.Framework.Imaging.ResizeMode.BoxPad) within the size of the original (the padding modes still
produce the size of the box). [Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) and [Stretch](cref:Meziantou.Framework.Imaging.ResizeMode.Stretch) cannot honor it: they throw an `ArgumentException` when the image
would have to be enlarged, so use [BoxPad](cref:Meziantou.Framework.Imaging.ResizeMode.BoxPad) for tiles of one size that never enlarge a small image. See
[Resizing](../06-Processing/03-Resizing.md) for the anchors and the resampling filters.

## Thumbnails of animations

[Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) resizes every frame of an animation in one operation, and saving to a format that stores animations keeps the
frames, their durations and the loop count:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ResizeAnimation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

    // Same format as the extension; APNG/GIF timing and play count come from the decoded image
    image.Save(outputPath);
}
```
<!-- endref -->

GIF, PNG (APNG), WebP, AVIF, HEIC and JPEG XL files store animations. For a still preview of an animation, decode its
first frame only and export it as a still image:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_FirstFramePreview;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePreview(string animationPath, string previewPath)
{
    // Decoding stops after the first displayed frame: the rest of the file is not read
    using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

    image.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });

    // The image keeps its animation settings: save its frame as a still image
    image.Frames[0].Save(previewPath, new PngEncoder());
}
```
<!-- endref -->

The thumbnail of a photo above throws on an animation, since JPEG stores one picture: load the input with
`FrameLimit = 1` and save `image.Frames[0]` when the inputs can be animated. See
[Process animated images](04-ProcessAnimatedImages.md).

## Choose the format and the quality

| Format | Encoder settings for a thumbnail | Transparency | Animation |
| --- | --- | --- | --- |
| JPEG | `Quality` (1 to 100, default 90); [BackgroundColor](cref:Meziantou.Framework.Imaging.ResizeOptions.BackgroundColor) for transparent images, `AllowBitDepthReduction` for 16-bit ones | No | No |
| WebP | `Compression = WebPCompression.Lossy` and `Quality` (0 to 100); `AllowBitDepthReduction` for 16-bit images | Yes, kept losslessly | Yes |
| PNG | Lossless; `PixelLayout = PngPixelLayout.Indexed` with [AllowColorReduction](cref:Meziantou.Framework.Imaging.Formats.PngEncoder.AllowColorReduction) for a palette of at most 256 colors | Yes | Yes (APNG) |
| GIF | A palette of at most 256 colors per frame | On or off per pixel | Yes |

Saving to a `.webp`, `.avif` or `.heic` path without an encoder writes a **lossless** file, which is much larger than a
lossy one for a photo: pass an encoder with a lossy `Compression` for thumbnails. JPEG and lossy WebP suit photos; PNG
suits drawings, icons and screenshots. See [Saving images](../05-LoadingAndSaving/04-SavingImages.md) and the
[format pages](../10-Formats/01-Overview.md) for every setting.

## Keep or strip the metadata

| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What the thumbnail keeps |
| --- | --- |
| [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) (default) | All the metadata; [Save](cref:Meziantou.Framework.Imaging.Image.Save) throws when the format cannot store some of it |
| [DiscardUnsupported](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.DiscardUnsupported) | The metadata the format can store; the rest is dropped |
| [Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) | No optional metadata: no EXIF, XMP, text, resolution, ICC profile or orientation |

[Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) also drops the orientation and the color profile, which change how viewers display the pixels: apply them to the
pixels first with [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) and [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile), as the example does. To keep the color profile and remove only
the private data, see [Strip or edit metadata](08-StripOrEditMetadata.md).

## Many thumbnails

- An encoder is immutable: create it once and use it for every file, from any thread.
- One image supports one operation at a time; different images can be processed in parallel.
- A large photo can be resized with several threads: see [Performance](../11-Performance.md).
