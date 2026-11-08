---
title: Custom formats
description: Read and write a format the library does not support through Image.Load, Identify and Save, with a codec of your own.
---

A [CustomImageCodec](cref:Meziantou.Framework.Imaging.CustomImageCodec) adds a format to the library. Register it in
[ImageConfiguration.Codecs](cref:Meziantou.Framework.Imaging.ImageConfiguration.Codecs), and the entry points of the
built-in formats read it: [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load),
[Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) and
[Image.DetectFormatInfo](cref:Meziantou.Framework.Imaging.Image.DetectFormatInfo), from a path, a stream or bytes,
synchronously or not:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ExtensibilityExamples.cs;region=Imaging_RegisterCustomCodec;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertFarbfeldToPng(string farbfeldPath, string pngPath, string copyPath)
{
    // Every operation that receives this configuration reads farbfeld files, detected from their first bytes
    var configuration = new ImageConfiguration { Codecs = [new FarbfeldCodec()] };

    var info = Image.Identify(farbfeldPath, new ImageIdentifyOptions { Configuration = configuration });
    Console.WriteLine($"{info.FormatInfo.Name}: {info.Width}x{info.Height}"); // farbfeld: 64x48

    using var image = Image.Load(farbfeldPath, new ImageDecodeOptions { Configuration = configuration });
    image.Save(pngPath);

    // The image keeps its configuration: the ".ff" extension selects the encoder of the codec
    image.Save(copyPath);
}
```
<!-- endref -->

The format of such a file is [ImageFormat.Custom](cref:Meziantou.Framework.Imaging.ImageFormat.Custom):
[ImageInfo.FormatInfo](cref:Meziantou.Framework.Imaging.ImageInfo.FormatInfo) tells which codec read it.

## Write a codec

This codec reads and writes [farbfeld](https://tools.suckless.org/farbfeld/) files, a format of 16-bit RGBA samples:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ExtensibilityExamples.cs;region=Imaging_CustomCodec;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Reads farbfeld files: "farbfeld", the width and the height (32-bit big-endian), then 16-bit big-endian RGBA samples.</summary>
public sealed class FarbfeldCodec() : CustomImageCodec(Format)
{
    public static readonly ImageFormatInfo Format = new("farbfeld", ["image/x-farbfeld"], [".ff"]);

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => prefix.StartsWith("farbfeld"u8);

    // Reads the header only; without this override, Identify decodes the whole file
    public override ImageInfo Identify(ReadOnlySpan<byte> data, CustomImageDecodeContext context)
    {
        var (width, height) = ReadHeader(data);
        return CreateImageInfo(width, height, PixelFormat.Rgba64);
    }

    public override Image Decode(ReadOnlySpan<byte> data, CustomImageDecodeContext context)
    {
        var (width, height) = ReadHeader(data);
        if (data.Length - 16 < (long)width * height * 8)
            throw new InvalidImageContentException("The farbfeld pixels are truncated.", ImageFormat.Custom);

        // The context applies the resource limits and charges the pixels to the load. A typed load
        // (Image.Load<Rgba32>) converts the returned image
        var image = context.CreateImage<Rgba64>(width, height);
        image.Frames[0].ProcessPixelRows(data[16..], static (pixels, samples) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                var line = samples.Slice(y * row.Length * 8, row.Length * 8);
                for (var x = 0; x < row.Length; x++)
                {
                    var sample = line.Slice(x * 8, 8);
                    row[x] = new Rgba64(
                        BinaryPrimitives.ReadUInt16BigEndian(sample),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[2..]),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[4..]),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[6..]));
                }
            }
        });

        return image;
    }

    // Save(path) uses this encoder for the ".ff" extension
    public override CustomImageEncoder CreateEncoder() => new FarbfeldEncoder();

    private static (int Width, int Height) ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            throw new InvalidImageContentException("The farbfeld header is truncated.", ImageFormat.Custom);

        var width = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw new InvalidImageContentException("The farbfeld size is not valid.", ImageFormat.Custom);

        return ((int)width, (int)height);
    }
}

/// <summary>Writes farbfeld files. The format stores one still image, without metadata.</summary>
public sealed class FarbfeldEncoder() : CustomImageEncoder(FarbfeldCodec.Format)
{
    public override void Encode(Image image, Stream destination)
    {
        // Like the built-in encoders, never drop what the format cannot store without being told to
        if (image.IsAnimated)
            throw new UnsupportedImageFeatureException("A farbfeld file stores one still image.", ImageFormat.Custom, "Animation");

        if (image.Metadata.IccProfile is not null && MetadataHandling == MetadataHandling.Strict)
            throw new UnsupportedImageFeatureException("A farbfeld file cannot store a color profile.", ImageFormat.Custom, "ICC profile");

        Span<byte> header = stackalloc byte[16];
        "farbfeld"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[12..], (uint)image.Height);
        destination.Write(header);

        using var samples = image.CloneAs<Rgba64>();
        var line = new byte[image.Width * 8];
        samples.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var sample = line.AsSpan(x * 8, 8);
                    BinaryPrimitives.WriteUInt16BigEndian(sample, row[x].R);
                    BinaryPrimitives.WriteUInt16BigEndian(sample[2..], row[x].G);
                    BinaryPrimitives.WriteUInt16BigEndian(sample[4..], row[x].B);
                    BinaryPrimitives.WriteUInt16BigEndian(sample[6..], row[x].A);
                }

                destination.Write(line);
            }
        });
    }
}
```
<!-- endref -->

- **Describe the format** with the public constructor of [ImageFormatInfo](cref:Meziantou.Framework.Imaging.ImageFormatInfo):
  its name, its media types and its file extensions.
- **Recognize the files** in [IsMatch](cref:Meziantou.Framework.Imaging.CustomImageCodec.IsMatch), from their first
  bytes only: it receives the first 18 bytes ([Image.FormatDetectionPrefixLength](cref:Meziantou.Framework.Imaging.Image.FormatDetectionPrefixLength)),
  or the whole file when it is shorter. File names are never used.
- **Decode** in [Decode](cref:Meziantou.Framework.Imaging.CustomImageCodec.Decode). It receives the whole file: the library
  reads it, within [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes), so the codec
  never performs I/O and serves every overload of `Load`. Create the image with
  [CreateImage](cref:Meziantou.Framework.Imaging.CustomImageDecodeContext.CreateImage): it checks the canvas limits and
  charges the pixels to the load, like a built-in decoder. Add frames with
  [AppendFrame](cref:Meziantou.Framework.Imaging.Image.AppendFrame).
- **Identify** reads the header in [Identify](cref:Meziantou.Framework.Imaging.CustomImageCodec.Identify), with
  [CreateImageInfo](cref:Meziantou.Framework.Imaging.CustomImageCodec.CreateImageInfo). Without this override, it decodes
  the file and describes the image.
- **Write** with a [CustomImageEncoder](cref:Meziantou.Framework.Imaging.Formats.CustomImageEncoder), returned by
  [CreateEncoder](cref:Meziantou.Framework.Imaging.CustomImageCodec.CreateEncoder) for the file extensions of the format.

Throw an [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException) for malformed data
and an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) for what
your codec does not support: they reach the caller unchanged. A codec is shared by every operation of its configurations,
so it must be immutable and thread-safe.

## What the library does for the codec

- **Pixel format.** Produce the pixel format that holds your samples best. A typed load
  ([Image.Load\<TPixel\>](cref:Meziantou.Framework.Imaging.Image.Load)) converts the image, with the rules of every load:
  the conversion never loses alpha or precision silently.
  [RequestedPixelFormat](cref:Meziantou.Framework.Imaging.CustomImageDecodeContext.RequestedPixelFormat) lets a codec
  produce that format directly.
- **Frame limit.** The frames beyond [ImageDecodeOptions.FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit)
  are removed; [FrameLimit](cref:Meziantou.Framework.Imaging.CustomImageDecodeContext.FrameLimit) lets a codec stop
  decoding there.
- **Cleanup.** The images the codec created and does not return are disposed, and all of them when it throws. The
  returned image must come from [CreateImage](cref:Meziantou.Framework.Imaging.CustomImageDecodeContext.CreateImage).
- **Atomic files.** [Save](cref:Meziantou.Framework.Imaging.Image.Save) to a path publishes the file only once the
  encoder returns: a failure leaves an existing file unchanged.

## Detection order

The registered codecs are consulted first, in the order of the list, then the built-in ones. A codec that recognizes the
files of a built-in format therefore reads them instead of the library, and a codec whose extension is one of a
built-in format selects its encoder in `Save(path)`.

## Limitations

- Sequential readers ([Image.OpenReader](cref:Meziantou.Framework.Imaging.Image.OpenReader)) reject the files of a custom
  codec with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException),
  and sequential writers ([Image.CreateWriter](cref:Meziantou.Framework.Imaging.Image.CreateWriter)) reject custom
  encoders. [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) and
  [AuxiliaryImageCollection](cref:Meziantou.Framework.Imaging.AuxiliaryImageCollection) only read the built-in formats.
- [ImageEncoder.MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.MetadataHandling) and
  [FloatingPointConversion](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.FloatingPointConversion) are settings
  your encoder reads: the library does not apply them to a custom format.
- The default [EncodeAsync](cref:Meziantou.Framework.Imaging.Formats.CustomImageEncoder.EncodeAsync) writes
  synchronously: override it when the destination does not accept synchronous writes, such as an HTTP response.
