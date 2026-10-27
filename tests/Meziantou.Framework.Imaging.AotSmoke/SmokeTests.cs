using System.Globalization;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.AotSmoke;

/// <summary>Public-API scenarios run by the NativeAOT smoke test; each throws on a mismatch.</summary>
internal static class SmokeTests
{
    private const int Width = 37;
    private const int Height = 23;

    public static void Png()
    {
        foreach (var interlaced in new[] { false, true })
        {
            using var source = CreatePattern(seed: 1);
            source.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "smoke"));
            var encoded = Encode(source, new PngEncoder { Interlaced = interlaced });
            Check(Image.DetectFormat(encoded) == ImageFormat.Png, "PNG detection");
            var info = Image.Identify(encoded, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Check(info.Width == Width && info.Height == Height && info.FrameCount == 1 && info.PixelFormat == PixelFormat.Rgba32, "PNG identification");
            using var decoded = Image.Load(encoded);
            CheckEqual(source, decoded, "PNG round trip");
            Check(decoded.Metadata.TextEntries.Count == 1 && decoded.Metadata.TextEntries[0].Value == "smoke", "PNG text metadata");
        }

        using var gray = Image.ImportPixelData<Gray16>([.. Enumerable.Range(0, 64).Select(i => new Gray16((ushort)(i * 1031)))], 8, 8);
        using var gray16 = Image.Load<Gray16>(Encode(gray, new PngEncoder()));
        CheckEqual(gray, gray16, "16-bit gray PNG round trip");

        // The indexed layout: sixteen colors are stored exactly as 4-bit indexes into a palette
        using var fewColors = CreatePattern(seed: 3, colors: 16);
        var indexed = Encode(fewColors, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = true });
        var indexedInfo = Image.Identify(indexed);
        Check(indexedInfo.ColorModel == ImageColorModel.Indexed && indexedInfo.BitsPerComponent == 4, "PNG palette identification");
        using (var decoded = Image.Load<Rgba32>(indexed))
        {
            CheckEqual(fewColors, decoded, "PNG palette round trip");
        }

        // It never reduces colors: more than 256 of them are rejected
        using var manyColors = CreatePattern(seed: 1);
        Expect<UnsupportedImageFeatureException>(() => Encode(manyColors, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));

        // The smallest layout, filters and zlib level, chosen by compressing the candidates: the pixels are stored exactly
        var smallest = Encode(fewColors, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        Check(smallest.Length <= Encode(fewColors, new PngEncoder()).Length, "PNG minimum size");
        using (var decoded = Image.Load<Rgba32>(smallest))
        {
            CheckEqual(fewColors, decoded, "PNG minimum size round trip");
        }
    }

    public static void Apng()
    {
        using var animation = CreateAnimation(frames: 3);
        animation.Animation = new AnimationMetadata { TotalPlays = 2 };
        var encoded = Encode(animation, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        using var decoded = Image.Load<Rgba32>(encoded);
        Check(decoded.Frames.Count == 3 && decoded.Animation?.TotalPlays == 2, "APNG frames and plays");
        CheckEqual(animation, decoded, "APNG round trip");
        Check(decoded.Frames[1].Metadata.Duration == new FrameDuration(1, 10), "APNG duration");

        // The indexed layout: one palette holds the colors of every frame
        using var fewColors = CreateAnimation(frames: 3, colors: 16);
        var indexed = Encode(fewColors, new PngEncoder { AnimationMode = PngAnimationMode.Animated, PixelLayout = PngPixelLayout.Indexed });
        Check(Image.Identify(indexed).ColorModel == ImageColorModel.Indexed, "APNG palette identification");
        using var indexedDecoded = Image.Load<Rgba32>(indexed);
        CheckEqual(fewColors, indexedDecoded, "APNG palette round trip");

        // Minimum size: frame regions, blending and disposals chosen by compressing the candidates
        var smallest = Encode(animation, new PngEncoder { AnimationMode = PngAnimationMode.Animated, Optimization = PngOptimization.MinimumSize });
        using var smallestDecoded = Image.Load<Rgba32>(smallest);
        Check(smallestDecoded.Frames.Count == 3 && smallestDecoded.Animation?.TotalPlays == 2, "APNG minimum size frames and plays");
        CheckEqual(animation, smallestDecoded, "APNG minimum size round trip");
    }

    public static void Gif()
    {
        using var animation = CreateAnimation(frames: 3, colors: 16);
        var encoded = Encode(animation, new GifEncoder());
        var info = Image.Identify(encoded, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Format == ImageFormat.Gif && info.FrameCount == 3, "GIF identification");
        using var decoded = Image.Load<Rgba32>(encoded);
        CheckEqual(animation, decoded, "GIF round trip (exact palette)");

        using var dithered = CreatePattern(seed: 5);
        using var quantized = Image.Load<Rgba32>(Encode(dithered, new GifEncoder { MaxColors = 32, Dithering = GifDithering.FloydSteinberg }));
        Check(quantized.Width == Width && quantized.Height == Height, "GIF quantized output");
    }

    public static void Jpeg()
    {
        foreach (var subsampling in new[] { JpegChromaSubsampling.Auto, JpegChromaSubsampling.Ratio444, JpegChromaSubsampling.Ratio422, JpegChromaSubsampling.Ratio420 })
        {
            using var source = Image.ImportPixelData<Rgb24>(CreateSmoothRgb(), Width, Height);
            var encoded = Encode(source, new JpegEncoder { Quality = 95, ChromaSubsampling = subsampling });
            using var decoded = Image.Load<Rgb24>(encoded);
            var error = MaxError(source, decoded);
            Check(error <= 24, string.Create(CultureInfo.InvariantCulture, $"JPEG {subsampling} reconstruction (max error {error})"));

            // The progressive process codes the same coefficients in several scans
            using var progressive = Image.Load<Rgb24>(Encode(source, new JpegEncoder { Quality = 95, ChromaSubsampling = subsampling, Process = JpegEncodingProcess.Progressive }));
            Check(MaxError(decoded, progressive) == 0, $"JPEG {subsampling} progressive output");
        }

        using var opaque = CreatePattern(seed: 3);
        using var flattened = Image.Load(Encode(opaque, new JpegEncoder { BackgroundColor = new Rgba64(0, 0, 0, 65535) }));
        Check(flattened.PixelFormat == PixelFormat.Rgb24, "JPEG default pixel format");
    }

    // A 17x9 JPEG file with four components (CMYK inks, no Adobe APP14 segment, no profile, restart intervals): the
    // fixture invalid/jpeg/cmyk-without-adobe-restart of the test corpus
    // (tools/Meziantou.Framework.Imaging.CorpusGenerator, BuildInkJpeg). The library has no encoder for inks.
    private const string CmykJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/" +
        "2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAAUCAAJABEEAREAAhEBAxEBBBEA" +
        "/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2Jy" +
        "ggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLD" +
        "xMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3" +
        "AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6" +
        "goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/90ABAAB/9oADgQBAAIRAxEE" +
        "AAA/APKvDfgTV/FvijT7jTTbxwjTRe3DysSiwvs2yGVcp1kIwCejgH5Hx93mXF+F8PMGs8xV6lbGzdqfNeWkb6a2UVKLt7rfLOOsrWP3Wfipl2Y14cQ4NP6l" +
        "QdSlOMtK0pxajeNP4VFOcLOpOFRqcn7NR5ebyrwl+0N4j8S+I49PtrttP1m5j+1tLLK1zesSFO5psLsPnxuM5WQJPgvIAa//0PGPC/wi1LWfGvizTrW5jXW4" +
        "ry2tbqC4HlhEnLeQ4OeVZXI527gqsAA2a/R58XYPDYT/AF2jO/s4884Rte2/Lf3tVHmi01D342lGElzH0+M8S8Nl8sTgsX77zdf7O6UruEXzfxpq8YyjGrGP" +
        "7qNW0qc04v3JVOA8I/GLUr3xDeaVo98+nSaNdRreQ2NyHu5Ig5wBcbeN8MssZVVALRKZIlZiR//Rxf8Ahly5/wCgof8Awn9T/wDia9j/AImkwf8A0CS/8Dof" +
        "/LD4H/V7in/n5hv/AAOl/wDKCT/hL/EP/PWb/vxJ/wDHK//S9F8Sf8lNsP8Adj/9J9Tr8p46/wB2wX/YLD/0/QPxjw6/5NtxP/jl/wCnsEfGfw8/5KHb/wDY" +
        "NX+b1//T6X4ff8nS+N/+uOi/+k1vX5xT/wCTdZz6z/8AT2JP5nzP4uGv8UP/AEmmfMHwY/5Cfxd/6/LX/wBGXFf/1PtWv8zDxzyyv//Z";

    // A 3x2 lossless JPEG file (SOF3) of 8-bit grayscale samples written by hand, predictor 7: the samples 100, 101, 99
    // and 98, 100, 102 (the unit tests explain its codes). The library has no lossless JPEG encoder.
    private static ReadOnlySpan<byte> LosslessJpegFile =>
    [
        0xFF, 0xD8, 0xFF, 0xC3, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x03, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x17, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x05, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x07, 0x00, 0x00, 0xC3, 0x73,
        0x2E, 0xFF, 0x00, 0xFF, 0xD9,
    ];

    public static void LosslessJpeg()
    {
        var info = Image.Identify(LosslessJpegFile);
        Check(info is { Format: ImageFormat.Jpeg, Width: 3, Height: 2, BitsPerComponent: 8, PixelFormat: PixelFormat.Gray8 }, "Lossless JPEG identification");
        using var image = Image.Load<Gray8>(LosslessJpegFile);
        var frame = image.Frames[0];
        Check(frame[0, 0] == new Gray8(100) && frame[1, 0] == new Gray8(101) && frame[2, 0] == new Gray8(99), "Lossless JPEG first line");
        Check(frame[0, 1] == new Gray8(98) && frame[1, 1] == new Gray8(100) && frame[2, 1] == new Gray8(102), "Lossless JPEG second line");
    }

    public static void CmykJpegConversion()
    {
        var data = Convert.FromBase64String(CmykJpeg);
        var info = Image.Identify(data);
        Check(info.ColorModel == ImageColorModel.Cmyk && info.PixelFormat == PixelFormat.Rgb24 && info.Width == 17 && info.Height == 9, "CMYK JPEG identification");

        // No pixel format holds inks: a load without color conversion is rejected
        Expect<UnsupportedImageFeatureException>(() => Image.Load(data));

        // The profile the library assumes for inks without profile, converted to sRGB (pixels of the same load on the
        // just-in-time runtime: the corpus compares them with independent references)
        using var image = Image.Load<Rgb24>(data, new ImageDecodeOptions { CmykConversion = new CmykConversionOptions() });
        Check(image.Size == new Size(17, 9) && ReferenceEquals(image.Metadata.IccProfile, IccProfile.Srgb), "CMYK JPEG conversion label");
        Check(image.Frames[0][0, 0] == new Rgb24(88, 160, 195) && image.Frames[0][8, 4] == new Rgb24(24, 24, 49) && image.Frames[0][16, 8] == new Rgb24(101, 2, 37), "CMYK JPEG converted pixels");
        Check(IccProfile.Cgats001CompatibleCmyk is { ColorSpace: IccProfileColorSpace.Cmyk, ProfileClass: IccProfileClass.Input } && IccProfile.Cgats001CompatibleCmyk.Data.Length == 8464, "CMYK profile of the library");
    }

    public static void WebP()
    {
        using var still = CreatePattern(seed: 7);
        using (var decoded = Image.Load(Encode(still, new WebPEncoder())))
        {
            CheckEqual(still, decoded, "WebP lossless");
        }

        using var smooth = Image.ImportPixelData<Rgb24>(CreateSmoothRgb(), Width, Height);
        using (var decoded = Image.Load<Rgb24>(Encode(smooth, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 95 })))
        {
            var error = MaxError(smooth, decoded);
            Check(error <= 24, string.Create(CultureInfo.InvariantCulture, $"WebP lossy reconstruction (max error {error})"));
        }

        using var animation = CreateAnimation(frames: 3);
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(Encode(animation, new WebPEncoder { Effort = 0 })));
        var count = 0;
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                Check(Pixels(animation.Frames[count]).AsSpan().SequenceEqual(Pixels(frame.Frames[0])), "WebP animation frame");
            }

            count++;
        }

        Check(count == 3, "WebP animation frame count");
    }

    // The inputs of the decoder are files written by another encoder, avifenc 1.4.2 (lossless, 8x6, with alpha), from
    // the pattern of GetAvifPixel:
    //   ffmpeg -f lavfi -i "color=black:size=8x6:rate=10,format=rgba,geq=r='X*36':g='Y*50':b='N*100+X+Y':a='255-X*Y*7'" -frames:v 3 f%d.png
    //   avifenc -l -s 6 f1.png still.avif
    //   avifenc -l -s 6 --timescale 25 --duration 2 f1.png --duration 5 f2.png f3.png --repetition-count 4 animation.avif
    private const string AvifStill =
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUEAAAGGbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAAAAAOcGl0bQAAAAAA" +
        "AQAAACxpbG9jAAAAAEQAAAIAAQAAAAEAAAH6AAAATgACAAAAAQAAAa4AAABMAAAAQmlpbmYAAAAAAAIAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAABpp" +
        "bmZlAgAAAAACAABhdjAxQWxwaGEAAAAAGmlyZWYAAAAAAAAADmF1eGwAAgABAAEAAADDaXBycAAAAJ1pcGNvAAAAFGlzcGUAAAAAAAAACAAAAAYAAAAQcGl4" +
        "aQAAAAADCAgIAAAADGF2MUOBIAAAAAAAE2NvbHJuY2x4AAEADQAAgAAAAA5waXhpAAAAAAEIAAAADGF2MUOBABwAAAAAOGF1eEMAAAAAdXJuOm1wZWc6bXBl" +
        "Z0I6Y2ljcDpzeXN0ZW1zOmF1eGlsaWFyeTphbHBoYQAAAAAeaXBtYQAAAAAAAAACAAEEAQKDBAACBAEFhgcAAACibWRhdBIACgUYCL1hUDJBEAD81KVEBJOg" +
        "P6voWPECMvxA6kUWAATK/W90hyMf4pOeddePwHUjFXE9SwbZy5ZNW7JFLSu/DUcAwqILr08JfQgSAAoIOAi9YQENACAyQBAAAIvW+KBmlaTxhNHeGqy0X+BH" +
        "DhjhlTPbuLwOfENChQtnTMv6koNw75d5j830a/MlmtHzQaeA8gVlKkIK5kA=";

    private const string AvifAnimation =
        "AAAALGZ0eXBhdmlzAAAAAGF2aWZhdmlzbXNmMWlzbzhtaWYxbWlhZk1BMUEAAAGGbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAA" +
        "AAAOcGl0bQAAAAAAAQAAACxpbG9jAAAAAEQAAAIAAQAAAAEAAAeKAAAAUwACAAAAAQAABxcAAABTAAAAQmlpbmYAAAAAAAIAAAAaaW5mZQIAAAAAAQAAYXYw" +
        "MUNvbG9yAAAAABppbmZlAgAAAAACAABhdjAxQWxwaGEAAAAAGmlyZWYAAAAAAAAADmF1eGwAAgABAAEAAADDaXBycAAAAJ1pcGNvAAAAFGlzcGUAAAAAAAAA" +
        "CAAAAAYAAAAQcGl4aQAAAAADCAgIAAAADGF2MUOBIAAAAAAAE2NvbHJuY2x4AAEADQAAgAAAAA5waXhpAAAAAAEIAAAADGF2MUOBABwAAAAAOGF1eEMAAAAA" +
        "dXJuOm1wZWc6bXBlZ0I6Y2ljcDpzeXN0ZW1zOmF1eGlsaWFyeTphbHBoYQAAAAAeaXBtYQAAAAAAAAACAAEEAQKDBAACBAEFhgcAAAVdbW9vdgAAAHhtdmhk" +
        "AQAAAAAAAADm8DIJAAAAAObwMgkAAAAZAAAAAAAAADwAAQAAAQAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAlJ0cmFrAAAAaHRraGQBAAABAAAAAObwMgkAAAAA5vAyCQAAAAEAAAAAAAAAAAAAADwAAAAAAAAAAAAAAAAAAAAA" +
        "AAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAgAAAAGAAAAAAAsZWR0cwAAACRlbHN0AQAAAQAAAAEAAAAAAAAADAAAAAAAAAAAAAEAAAAA" +
        "AbZtZGlhAAAALG1kaGQBAAAAAAAAAObwMgkAAAAA5vAyCQAAABkAAAAAAAAADFXEAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAAAAFhbWlu" +
        "ZgAAABR2bWhkAAAAAQAAAAAAAAAAAAAAJGRpbmYAAAAcZHJlZgAAAAAAAAABAAAADHVybCAAAAABAAABIXN0YmwAAACVc3RzZAAAAAAAAAABAAAAhWF2MDEA" +
        "AAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAACAAGAEgAAABIAAAAAAAAAAEKQU9NIENvZGluZwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY//8AAAAMYXYxQ4EgAAAA" +
        "AAATY29scm5jbHgAAQANAACAAAAAEGNjc3QAAAAAfAAAAAAAACBzdHRzAAAAAAAAAAIAAAABAAAAAgAAAAIAAAAFAAAAHHN0c2MAAAAAAAAAAQAAAAEAAAAD" +
        "AAAAAQAAACBzdHN6AAAAAAAAAAAAAAADAAAAUwAAAB8AAAAcAAAAFHN0Y28AAAAAAAAAAQAAB4oAAAAUc3RzcwAAAAAAAAABAAAAAQAAAot0cmFrAAAAaHRr" +
        "aGQBAAABAAAAAObwMgkAAAAA5vAyCQAAAAIAAAAAAAAAAAAAADwAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAA" +
        "AAgAAAAGAAAAAAAUdHJlZgAAAAxhdXhsAAAAAQAAACxlZHRzAAAAJGVsc3QBAAABAAAAAQAAAAAAAAAMAAAAAAAAAAAAAQAAAAAB221kaWEAAAAsbWRoZAEA" +
        "AAAAAAAA5vAyCQAAAADm8DIJAAAAGQAAAAAAAAAMVcQAAAAAACFoZGxyAAAAAAAAAABhdXh2AAAAAAAAAAAAAAAAAAAAAYZtaW5mAAAAFHZtaGQAAAABAAAA" +
        "AAAAAAAAAAAkZGluZgAAABxkcmVmAAAAAAAAAAEAAAAMdXJsIAAAAAEAAAFGc3RibAAAALpzdHNkAAAAAAAAAAEAAACqYXYwMQAAAAAAAAABAAAAAAAAAAAA" +
        "AAAAAAAAAAAIAAYASAAAAEgAAAAAAAAAAQpBT00gQ29kaW5nAAAAAAAAAAAAAAAAAAAAAAAAAAAAABj//wAAAAxhdjFDgQAcAAAAABBjY3N0AAAAAHwAAAAA" +
        "AAA4YXV4aQAAAAB1cm46bXBlZzptcGVnQjpjaWNwOnN5c3RlbXM6YXV4aWxpYXJ5OmFscGhhAAAAACBzdHRzAAAAAAAAAAIAAAABAAAAAgAAAAIAAAAFAAAA" +
        "HHN0c2MAAAAAAAAAAQAAAAEAAAADAAAAAQAAACBzdHN6AAAAAAAAAAAAAAADAAAAUwAAABAAAAAQAAAAFHN0Y28AAAAAAAAAAQAABxcAAAAUc3RzcwAAAAAA" +
        "AAABAAAAAQAAAQltZGF0EgAKCQAAAAEXptfNUDJEEACAAIhoKOsQOHPH2WsZ1A+sKBe3WI9ST9/EKe/IC6tYJgFDsOxT+qa4vIpzNjw+RgjSjJfYvXPxoEgs" +
        "PLytVycUXzASADIMMAPAgAAARoAEAJGQEgAyDDAEAQQAAEaABACNwBIACgwgAAABF6bXzQENACAyQRAAgACL1vigZpWk8YTR3hqstF/gRw4Y4ZUz27i8DnxD" +
        "QoULZ0zL+pKDcO+XeY/N9GvzJZrR80GngPIFZSpCCuZAEgAyGzADwIAAAEaAAQAgKt+T+n2mzlWGu7ZgUgODwBIAMhgwBAEEAABGgAEAInEUpn+E93lt023a" +
        "wXA=";

    // The inputs of the HEIC decoder are files written by another encoder, heif-enc 1.23.6 with x265 (lossless, 64x64, with
    // alpha; the images of the sequence that follow the first one are P pictures), from the pattern of CheckHeicFrame:
    //   ffmpeg -f lavfi -i "color=black:size=64x64:rate=10,format=rgba,geq=r='floor(X/16)*60':g='floor(Y/16)*60':b='N*60+floor((X+Y)/32)*40':a='255-floor(X/32)*100'" -frames:v 3 f%d.png
    //   heif-enc -L f1.png -o still.heic
    //   heif-enc -L -S --timebase 25 --duration 2 --repetitions 4 --gop-structure low-delay f1.png f2.png f3.png -o animation.heics
    private const string HeicStill =
        "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";

    private const string HeicAnimation =
        "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" +
        "DAIB0Al8IMFuSq4J6AAAAAsCAdAR/6iBgtSBQw==";

    // The fixture jxl/lossy-rgb8 of the corpus: a VarDCT frame of 32x16 pixels, stored in XYB
    private const string JpegXlLossy =
        "/wrDQUJxIQATiAIAFAe1nyAAABUqo4wbvJzr+fJDh8W0jesMbbVtYQljs70wSEg4IxqHB33Zmh+4+yYKvg1vJj9vVs0UFw4zrJmC/gMchhoYWPPRAcDRttgWQIUUyAFgt+3d3E0yLyZhBeLUQM8nf9li9APiyWOXh5tB5lpj53vi+kQYY00WJwHHyrDNOqUXXL9YiLTFuZB2ilSkbzSF7UoGNffJVs7vtySNE5/JxsQ8q1b4l29G9Y1mkpbuENJUHbaOqiHK9N9pP/3rDRz14FgLwnWTnbZIAjSX5YSpgJMC27fmp9OxCkhCDTYWWDcil7D4Vyw9iEm2JGdZbA6U2bWnn30QqzvSTjGvf+dZxpdA1wU0REK7e+1TNgKjbiO8rtPIGKaN9SqLe091Tyc/O9QPeb0vXiX0BsnzGBNqXbofwrevpLCa+Z7M86d/XR8MocxnE9K5nj7/K1tDVO7L+ajXsr5lbtTXOn825DbGuIJ6xMJkzUx/I79SvdJ9pEMRP9rqjEm7/ccotsSl4BEbtg3tiU2LN4NmjgKIFox5nlMr6dNKJO18/H8NAt99EnhFU3QZ7YJIqJY9FK4/1GY96zPNhVPQlDLVwrFdBnWdSDwUKis=";

    public static void JpegXl()
    {
        // Lossless: 8-bit and 16-bit samples, the colors of transparent pixels included, at two efforts
        using var still = CreatePattern(seed: 9);
        foreach (var effort in new[] { 1, 7 })
        {
            var encoded = Encode(still, new JpegXlEncoder { Effort = effort });
            Check(Image.DetectFormat(encoded) == ImageFormat.JpegXl, "JPEG XL detection");
            var info = Image.Identify(encoded);
            Check(info.Width == Width && info.Height == Height && info.FrameCount == 1 && info.PixelFormat == PixelFormat.Rgba32 && info.MayHaveTransparency == true, "JPEG XL identification");
            using var decoded = Image.Load(encoded);
            CheckEqual(still, decoded, "JPEG XL lossless");
        }

        using (var deep = still.CloneAs<Rgba64>())
        using (var decoded = Image.Load(Encode(deep, new JpegXlEncoder())))
        {
            Check(decoded.PixelFormat == PixelFormat.Rgba64, "JPEG XL 16-bit pixel format");
            CheckEqual(deep, decoded, "JPEG XL 16-bit lossless");
        }

        // An animation, frame by frame
        using var animation = CreateAnimation(frames: 3);
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(Encode(animation, new JpegXlEncoder { Effort = 3 }))))
        {
            var count = 0;
            while (reader.ReadFrame() is { } frame)
            {
                using (frame)
                {
                    Check(Pixels(animation.Frames[count]).AsSpan().SequenceEqual(Pixels(frame.Frames[0])), "JPEG XL animation frame");
                }

                count++;
            }

            Check(count == 3, "JPEG XL animation frame count");
        }

        // A lossy file: the floating-point path (inverse transforms, filters, XYB). The reference decoder gives these samples
        using var lossy = Image.Load<Rgb24>(Convert.FromBase64String(JpegXlLossy));
        Check(lossy.Width == 32 && lossy.Height == 16 && lossy.Frames.Count == 1, "JPEG XL lossy size");
        var sum = 0;
        foreach (var sample in Pixels(lossy.Frames[0]))
        {
            sum += sample;
        }

        Check(Math.Abs(sum - 181036) <= 16, string.Create(CultureInfo.InvariantCulture, $"JPEG XL lossy samples (sum {sum})"));
        foreach (var (x, y, r, g, b) in new[] { (0, 0, 0, 255, 119), (15, 7, 0, 128, 154), (31, 15, 206, 45, 108), (8, 12, 56, 88, 52) })
        {
            var pixel = lossy.Frames[0][x, y];
            Check(Math.Abs(pixel.R - r) <= 1 && Math.Abs(pixel.G - g) <= 1 && Math.Abs(pixel.B - b) <= 1, string.Create(CultureInfo.InvariantCulture, $"JPEG XL lossy sample at ({x}, {y}): {pixel}"));
        }

        // Lossy output in passes, read refinement after refinement: the last one is the image that is loaded at once, and
        // the alpha channel of lossy output is exact
        var progressive = Encode(still, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 95, Progressive = true });
        using (var expected = Image.Load<Rgba32>(progressive))
        using (var destination = new Image<Rgba32>(Width, Height))
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(progressive)))
        {
            var refinements = 0;
            var isFinal = false;
            while (!isFinal && reader.ReadFrameRefinementInto(destination) is { } refinement)
            {
                refinements++;
                isFinal = refinement.IsFinal;
            }

            Check(isFinal && refinements >= 1, "JPEG XL refinements");
            Check(Pixels(expected.Frames[0]).AsSpan().SequenceEqual(Pixels(destination.Frames[0])), "JPEG XL final refinement");
            var source = Pixels(still.Frames[0]);
            var decoded = Pixels(expected.Frames[0]);
            for (var i = 3; i < source.Length; i += 4)
            {
                Check(source[i] == decoded[i], "JPEG XL lossy alpha");
            }
        }

        // Lossless output in passes: the squeeze transform, exactly undone
        using (var decoded = Image.Load(Encode(still, new JpegXlEncoder { Progressive = true })))
        {
            CheckEqual(still, decoded, "JPEG XL progressive lossless");
        }

        // A JPEG file recompressed without decoding its pixels, and rebuilt byte for byte
        var jpeg = Encode(still, new JpegEncoder { Quality = 90, BackgroundColor = new Rgba64(0, 0, 0, 65535) });
        using var recompressed = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        Check(JpegXlTranscoder.HasJpegReconstructionData(recompressed.ToArray()), "JPEG XL reconstruction data");
        using var rebuilt = new MemoryStream();
        Check(JpegXlTranscoder.TryReconstructJpeg(recompressed.ToArray(), rebuilt), "JPEG XL reconstruction");
        Check(rebuilt.ToArray().AsSpan().SequenceEqual(jpeg), "JPEG XL rebuilt JPEG bytes");
    }

    public static void Avif()
    {
        var still = Convert.FromBase64String(AvifStill);
        Check(Image.DetectFormat(still) == ImageFormat.Avif, "AVIF detection");
        var info = Image.Identify(still, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Width == 8 && info.Height == 6 && info.FrameCount == 1 && info.PixelFormat == PixelFormat.Rgba32 && info.MayHaveTransparency == true, "AVIF identification");
        using (var decoded = Image.Load<Rgba32>(still))
        {
            CheckAvifFrame(decoded.Frames[0], index: 0, "AVIF still");
            Check(new CicpColorDescription(1, 13, 0, fullRange: true).Equals(decoded.Metadata.Cicp), "AVIF color description");
        }

        var animation = Convert.FromBase64String(AvifAnimation);
        using (var decoded = Image.Load<Rgba32>(animation))
        {
            Check(decoded.Frames.Count == 3 && decoded.Animation?.TotalPlays == 5, "AVIF animation frames and plays");
            Check(decoded.Frames[0].Metadata.Duration == new FrameDuration(2, 25) && decoded.Frames[2].Metadata.Duration == new FrameDuration(1, 5), "AVIF durations");
            CheckAvifFrame(decoded.Frames[2], index: 2, "AVIF animation frame");
        }

        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(animation));
        var count = 0;
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                CheckAvifFrame(frame.Frames[0], count, "AVIF reader frame");
                count++;
            }
        }

        Check(count == 3, "AVIF reader frame count");

        // The encoder: a lossless still image, a lossy one and an image sequence, read back by the decoder
        using var source = Image.Load<Rgba32>(animation);
        using (var first = source.CloneFrame(0))
        {
            using var lossless = new MemoryStream();
            first.Save(lossless, new AvifEncoder());
            using (var decoded = Image.Load<Rgba32>(lossless.ToArray()))
            {
                CheckAvifFrame(decoded.Frames[0], index: 0, "AVIF lossless encoding");
            }

            using var lossy = new MemoryStream();
            first.Save(lossy, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 60, Effort = 2 });
            var lossyInfo = Image.Identify(lossy.ToArray());
            Check(lossyInfo.Format == ImageFormat.Avif && lossyInfo.Width == 8 && lossyInfo.Height == 6 && lossyInfo.ColorModel == ImageColorModel.YCbCr, "AVIF lossy encoding");
        }

        using var sequence = new MemoryStream();
        source.Save(sequence, new AvifEncoder { Effort = 0 });
        using (var decoded = Image.Load<Rgba32>(sequence.ToArray()))
        {
            Check(decoded.Frames.Count == 3 && decoded.Animation?.TotalPlays == 5, "AVIF sequence encoding frames and plays");
            Check(decoded.Frames[0].Metadata.Duration == new FrameDuration(2, 25) && decoded.Frames[2].Metadata.Duration == new FrameDuration(1, 5), "AVIF sequence encoding durations");
            CheckAvifFrame(decoded.Frames[2], index: 2, "AVIF sequence encoding frame");
        }
    }

    public static void Heic()
    {
        var still = Convert.FromBase64String(HeicStill);
        Check(Image.DetectFormat(still) == ImageFormat.Heic, "HEIC detection");
        var info = Image.Identify(still, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Width == 64 && info.Height == 64 && info.FrameCount == 1 && info.PixelFormat == PixelFormat.Rgba32 && info.MayHaveTransparency == true, "HEIC identification");
        using (var decoded = Image.Load<Rgba32>(still))
        {
            CheckHeicFrame(decoded.Frames[0], index: 0, "HEIC still");
            Check(new CicpColorDescription(1, 13, 0, fullRange: true).Equals(decoded.Metadata.Cicp), "HEIC color description");
        }

        // An image sequence whose images are predicted from one another
        var animation = Convert.FromBase64String(HeicAnimation);
        using (var decoded = Image.Load<Rgba32>(animation))
        {
            Check(decoded.Frames.Count == 3 && decoded.Animation?.TotalPlays == 4, "HEIC animation frames and plays");
            Check(decoded.Frames[0].Metadata.Duration == new FrameDuration(2, 25) && decoded.Frames[2].Metadata.Duration == new FrameDuration(2, 25), "HEIC durations");
            CheckHeicFrame(decoded.Frames[2], index: 2, "HEIC animation frame");
        }

        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(animation));
        var count = 0;
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                CheckHeicFrame(frame.Frames[0], count, "HEIC reader frame");
                count++;
            }
        }

        Check(count == 3, "HEIC reader frame count");

        // The encoder: a lossless still image, a lossy one and an image sequence, read back by the decoder
        using var source = Image.Load<Rgba32>(animation);
        using (var first = source.CloneFrame(0))
        {
            using var lossless = new MemoryStream();
            first.Save(lossless, new HeicEncoder());
            using (var decoded = Image.Load<Rgba32>(lossless.ToArray()))
            {
                CheckHeicFrame(decoded.Frames[0], index: 0, "HEIC lossless encoding");
            }

            using var lossy = new MemoryStream();
            first.Save(lossy, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 60, Effort = 2 });
            var lossyInfo = Image.Identify(lossy.ToArray());
            Check(lossyInfo.Format == ImageFormat.Heic && lossyInfo.Width == 64 && lossyInfo.Height == 64 && lossyInfo.ColorModel == ImageColorModel.YCbCr, "HEIC lossy encoding");
        }

        using var sequence = new MemoryStream();
        source.Save(sequence, new HeicEncoder { Effort = 0 });
        using (var decoded = Image.Load<Rgba32>(sequence.ToArray()))
        {
            Check(decoded.Frames.Count == 3 && decoded.Animation?.TotalPlays == 4, "HEIC sequence encoding frames and plays");
            Check(decoded.Frames[0].Metadata.Duration == new FrameDuration(2, 25) && decoded.Frames[2].Metadata.Duration == new FrameDuration(2, 25), "HEIC sequence encoding durations");
            CheckHeicFrame(decoded.Frames[2], index: 2, "HEIC sequence encoding frame");
        }
    }

    private static void CheckHeicFrame(ImageFrame<Rgba32> frame, int index, string context)
    {
        Check(frame.Width == 64 && frame.Height == 64, context + " size");
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var expected = new Rgba32((byte)(x / 16 * 60), (byte)(y / 16 * 60), (byte)((index * 60) + ((x + y) / 32 * 40)), (byte)(255 - (x / 32 * 100)));
                Check(frame[x, y] == expected, string.Create(CultureInfo.InvariantCulture, $"{context} pixel at ({x}, {y}): {frame[x, y]}, {expected} expected"));
            }
        }
    }

    private static void CheckAvifFrame(ImageFrame<Rgba32> frame, int index, string context)
    {
        Check(frame.Width == 8 && frame.Height == 6, context + " size");
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var expected = new Rgba32((byte)(x * 36), (byte)(y * 50), (byte)((index * 100) + x + y), (byte)(255 - (x * y * 7)));
                Check(frame[x, y] == expected, string.Create(CultureInfo.InvariantCulture, $"{context} pixel ({x}, {y})"));
            }
        }
    }

    public static void Qoi()
    {
        using var still = CreatePattern(seed: 11);
        var data = Encode(still, new QoiEncoder());
        using (var decoded = Image.Load(data))
        {
            CheckEqual(still, decoded, "QOI");
        }

        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Format == ImageFormat.Qoi && info.FrameCount == 1, "QOI identification");

        still.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(Encode(still, new QoiEncoder())));
        using (var frame = reader.ReadFrame())
        {
            Check(frame is not null && Pixels(still.Frames[0]).AsSpan().SequenceEqual(Pixels(frame.Frames[0])), "QOI reader frame");
            Check(frame?.Metadata.TransferFunction == ColorTransferFunction.Linear, "QOI linear transfer function");
        }

        Check(reader.ReadFrame() is null, "QOI single frame");
    }

    public static void Bmp()
    {
        using var still = CreatePattern(seed: 13);
        foreach (var layout in new[] { BmpPixelLayout.Auto, BmpPixelLayout.Bgra32 })
        {
            var data = Encode(still, new BmpEncoder { PixelLayout = layout });
            using var decoded = Image.Load(data);
            CheckEqual(still, decoded, "BMP " + layout);
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Check(info.Format == ImageFormat.Bmp && info.FrameCount == 1, "BMP identification");
        }

        // The indexed layouts (histogram, palette, index plane) and the run-length reader and writer: whatever the row
        // order and the compression, the decoded pixels are the same
        var flatten = new Rgba64(0, 0, 0);
        using var indexed = Image.Load(Encode(still, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, BackgroundColor = flatten }));
        foreach (var encoder in new[]
        {
            new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, BackgroundColor = flatten, Compression = BmpCompression.RunLength },
            new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, BackgroundColor = flatten, RowOrder = BmpRowOrder.TopDown },
        })
        {
            using var decoded = Image.Load(Encode(still, encoder));
            CheckEqual(indexed, decoded, "BMP indexed " + encoder.Compression + " " + encoder.RowOrder);
        }

        using var highColor = Image.Load(Encode(still, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr565, AllowBitDepthReduction = true, BackgroundColor = flatten }));
        Check(highColor.PixelFormat == PixelFormat.Rgb24 && highColor.Size == still.Size, "BMP 5-6-5");

        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(Encode(still, new BmpEncoder())));
        using (var frame = reader.ReadFrame())
        {
            Check(frame is not null && Pixels(still.Frames[0]).AsSpan().SequenceEqual(Pixels(frame.Frames[0])), "BMP reader frame");
        }

        Check(reader.ReadFrame() is null, "BMP single frame");
    }

    public static void Tga()
    {
        using var still = CreatePattern(seed: 17);
        foreach (var compression in new[] { TgaCompression.None, TgaCompression.RunLength })
        {
            var data = Encode(still, new TgaEncoder { Compression = compression });
            using var decoded = Image.Load(data);
            CheckEqual(still, decoded, "TGA " + compression);
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Check(info.Format == ImageFormat.Tga && info.FrameCount == 1, "TGA identification");
        }

        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(Encode(still, new TgaEncoder { Compression = TgaCompression.RunLength })));
        using (var frame = reader.ReadFrame())
        {
            Check(frame is not null && Pixels(still.Frames[0]).AsSpan().SequenceEqual(Pixels(frame.Frames[0])), "TGA reader frame");
        }

        Check(reader.ReadFrame() is null, "TGA single frame");
    }

    public static void Pnm()
    {
        using var still = CreatePattern(seed: 19);
        var data = Encode(still, new PnmEncoder());
        using (var decoded = Image.Load(data))
        {
            CheckEqual(still, decoded, "PAM");
        }

        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Format == ImageFormat.Pnm && info.FrameCount == 1, "PNM identification");

        using var opaque = still.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        using var plain = Image.Load<Rgb24>(Encode(opaque, new PnmEncoder { Encoding = PnmEncoding.Plain }));
        using var binary = Image.Load<Rgb24>(Encode(opaque, new PnmEncoder()));
        var plainPixels = new Rgb24[plain.Width * plain.Height];
        var binaryPixels = new Rgb24[binary.Width * binary.Height];
        plain.Frames[0].CopyPixelDataTo(plainPixels);
        binary.Frames[0].CopyPixelDataTo(binaryPixels);
        Check(plainPixels.AsSpan().SequenceEqual(binaryPixels), "PNM plain and binary agree");

        // A portable bitmap is written on request, from black and white pixels only
        Gray8[] bits = [new(0), new(255), new(255), new(0), new(0), new(255)];
        using var bitmap = Image.ImportPixelData<Gray8>(bits, 3, 2);
        foreach (var encoding in (PnmEncoding[])[PnmEncoding.Binary, PnmEncoding.Plain])
        {
            var encoded = Encode(bitmap, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap, Encoding = encoding });
            using var decoded = Image.Load<Gray8>(encoded);
            var decodedBits = new Gray8[bits.Length];
            decoded.Frames[0].CopyPixelDataTo(decodedBits);
            Check(Image.Identify(encoded).BitsPerComponent == 1 && decodedBits.AsSpan().SequenceEqual(bits), $"PBM {encoding}");
        }

        Check(Expect<UnsupportedImageFeatureException>(() => Encode(opaque, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap })).Format == ImageFormat.Pnm, "PBM rejects colors");
    }

    public static void Tiff()
    {
        using var still = CreatePattern(seed: 23);
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits })
        {
            foreach (var bigTiff in new[] { false, true })
            {
                var data = Encode(still, new TiffEncoder { Compression = compression, BigTiff = bigTiff, BigEndian = bigTiff });
                using var decoded = Image.Load(data);
                CheckEqual(still, decoded, string.Create(CultureInfo.InvariantCulture, $"TIFF {compression} bigtiff={bigTiff}"));
            }
        }

        // The bilevel layout with the CCITT compressions, from black and white pixels only
        var bits = new Gray8[Width * Height];
        for (var i = 0; i < bits.Length; i++)
        {
            bits[i] = new Gray8(((i % Width) / 3) % 2 == ((i / Width) / 2) % 2 ? (byte)0 : (byte)255);
        }

        using var bilevel = Image.ImportPixelData<Gray8>(bits, Width, Height);
        foreach (var compression in new[] { TiffCompression.CcittModifiedHuffman, TiffCompression.CcittGroup3, TiffCompression.CcittGroup4, TiffCompression.Lzw })
        {
            var data = Encode(bilevel, new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Bilevel });
            using var decoded = Image.Load(data);
            Check(Image.Identify(data).BitsPerComponent == 1, string.Create(CultureInfo.InvariantCulture, $"TIFF bilevel {compression} depth"));
            CheckEqual(bilevel, decoded, string.Create(CultureInfo.InvariantCulture, $"TIFF bilevel {compression}"));
        }

        Check(Expect<UnsupportedImageFeatureException>(() => Encode(still, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel })).Format == ImageFormat.Tiff, "TIFF bilevel rejects colors");

        // The palette layout stores the colors of the page exactly; a JPEG page holds what the JPEG encoder writes
        using var opaque = still.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        using var fewColors = CreatePattern(seed: 23, colors: 16);
        using var palette = fewColors.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        var indexed = Encode(palette, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed, Compression = TiffCompression.PackBits });
        using (var decoded = Image.Load(indexed))
        {
            Check(Image.Identify(indexed).ColorModel == ImageColorModel.Indexed, "TIFF palette identification");
            CheckEqual(palette, decoded, "TIFF palette");
        }

        var jpegPage = Encode(opaque, new TiffEncoder { Compression = TiffCompression.Jpeg, JpegQuality = 85 });
        using (var decoded = Image.Load(jpegPage))
        using (var expected = Image.Load(Encode(opaque, new JpegEncoder { Quality = 85 })))
        {
            Check(Image.Identify(jpegPage).ColorModel == ImageColorModel.YCbCr, "TIFF JPEG identification");
            CheckEqual(expected, decoded, "TIFF JPEG page and JPEG file");
        }

        // A page of 16-bit inks (two pixels, one uncompressed strip, assembled here: the library writes no CMYK data) is
        // only loaded with a color conversion, and converted like the public transform converts the same samples
        ushort[] inks = [0, 0, 0, 0, 60000, 20000, 5000, 1000];
        byte[] cmyk =
        [
            (byte)'I', (byte)'I', 42, 0, 32, 0, 0, 0,
            .. inks.SelectMany(static ink => (byte[])[(byte)ink, (byte)(ink >> 8)]),
            16, 0, 16, 0, 16, 0, 16, 0,
            9, 0,
            0, 1, 4, 0, 1, 0, 0, 0, 2, 0, 0, 0,
            1, 1, 4, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            2, 1, 3, 0, 4, 0, 0, 0, 24, 0, 0, 0,
            3, 1, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            6, 1, 3, 0, 1, 0, 0, 0, 5, 0, 0, 0,
            17, 1, 4, 0, 1, 0, 0, 0, 8, 0, 0, 0,
            21, 1, 3, 0, 1, 0, 0, 0, 4, 0, 0, 0,
            22, 1, 4, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            23, 1, 4, 0, 1, 0, 0, 0, 16, 0, 0, 0,
            0, 0, 0, 0,
        ];
        Check(Image.Identify(cmyk).ColorModel == ImageColorModel.Cmyk, "CMYK TIFF identification");
        Check(Expect<UnsupportedImageFeatureException>(() => Image.Load(cmyk)).Format == ImageFormat.Tiff, "CMYK TIFF needs a conversion");
        using (var converted = Image.Load<Rgba64>(cmyk, new ImageDecodeOptions { CmykConversion = new CmykConversionOptions() }))
        {
            var colors = new ushort[6];
            IccColorTransform.Create(IccProfile.Cgats001CompatibleCmyk, IccProfile.Srgb).Convert(inks, colors);
            var first = converted.Frames[0][0, 0];
            var second = converted.Frames[0][1, 0];
            Check(ReferenceEquals(converted.Metadata.IccProfile, IccProfile.Srgb), "CMYK TIFF conversion label");
            Check(first == new Rgba64(colors[0], colors[1], colors[2], ushort.MaxValue) && second == new Rgba64(colors[3], colors[4], colors[5], ushort.MaxValue), "CMYK TIFF converted pixels");
        }

        // A multi-page document: the pages keep their own size and are read one at a time
        using var stream = new MemoryStream();
        using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            using var smaller = still.Clone();
            smaller.Resize(new ResizeOptions(Width / 2, Height / 2) { Mode = ResizeMode.Stretch });
            pages.Add(still);
            pages.Add(smaller);
            pages.Save(stream, new TiffEncoder());
        }

        stream.Position = 0;
        using var document = ImageCollection.Load(stream);
        Check(document.Kind == ImageCollectionKind.Pages && document.Count == 2, "TIFF page count");
        using (var first = document[0].Decode())
        {
            CheckEqual(still, first, "TIFF page 0");
        }

        Check(document[1].Size.Width == Width / 2, "TIFF page 1 size");
        var info = Image.Identify(stream.ToArray(), new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Check(info.Format == ImageFormat.Tiff && info.FrameCount == 1 && info.CollectionEntryCount == 2, "TIFF identification");
    }

    public static void Icon()
    {
        using var still = CreatePattern(seed: 29);
        foreach (var payloadFormat in new[] { IconPayloadFormat.Dib, IconPayloadFormat.Png })
        {
            var data = Encode(still, new IcoEncoder { PayloadFormat = payloadFormat });
            using var decoded = Image.Load(data);
            CheckEqual(still, decoded, string.Create(CultureInfo.InvariantCulture, $"ICO {payloadFormat}"));
        }

        using var stream = new MemoryStream();
        using (var representations = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            using var smaller = still.Clone();
            smaller.Resize(new ResizeOptions(Width / 2, Height / 2) { Mode = ResizeMode.Stretch });
            representations.Add(still, new Point(1, 2));
            representations.Add(smaller, new Point(0, 0));
            representations.Save(stream, new IcoEncoder { Kind = IconKind.Cursor });
        }

        stream.Position = 0;
        Check(Image.DetectFormat(stream.ToArray()) == ImageFormat.Cur, "CUR detection");
        using var cursor = ImageCollection.Load(stream);
        Check(cursor.Kind == ImageCollectionKind.Representations && cursor.Count == 2, "CUR representation count");
        Check(cursor.SelectBySize(new Size(Width, Height)).Hotspot == new Point(1, 2), "CUR hotspot");
        using var selected = cursor.SelectBySize(new Size(Width, Height)).Decode();
        CheckEqual(still, selected, "CUR representation");
    }

    public static void Ani()
    {
        using var animation = CreateAnimation(frames: 3);
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            animation.Frames[i].Metadata.Duration = new FrameDuration(i + 1, 60);
            animation.Frames[i].Metadata.Hotspot = new Point(i, 1);
        }

        // The last frame repeats the first one: it is stored once and replayed through the sequence table
        var repeated = animation.AppendFrame(animation.Frames[0]);
        repeated.Metadata.Duration = new FrameDuration(7, 60);
        animation.Animation = new AnimationMetadata();
        animation.Metadata.TextEntries.Clear();
        animation.Metadata.TextEntries.Add(new ImageTextEntry("Title", "smoke"));
        foreach (var payloadFormat in new[] { IconPayloadFormat.Dib, IconPayloadFormat.Png })
        {
            var data = Encode(animation, new AniEncoder { PayloadFormat = payloadFormat });
            Check(Image.DetectFormat(data) == ImageFormat.Ani, "ANI detection");
            using var decoded = Image.Load(data);
            CheckEqual(animation, decoded, string.Create(CultureInfo.InvariantCulture, $"ANI {payloadFormat}"));
            Check(decoded.Animation is { TotalPlays: null }, "ANI always loops");
            for (var i = 0; i < animation.Frames.Count; i++)
            {
                Check(decoded.Frames[i].Metadata.Hotspot == animation.Frames[i].Metadata.Hotspot, "ANI hotspot");
            }

            Check(decoded.Metadata.TextEntries is [{ Keyword: "Title", Value: "smoke" }], "ANI title");
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Check(info.Format == ImageFormat.Ani && info.FrameCount == 4 && info.IsAnimated == true, "ANI identification");
        }
    }

    public static void Processing()
    {
        using var image = CreateAnimation(frames: 2);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.AutoOrient();
        Check(image.Width == Height && image.Height == Width, "auto-orient");
        image.Resize(new ResizeOptions(10, 10) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3 });
        image.Rotate(RotateMode.Rotate90);
        image.Flip(FlipMode.Horizontal);
        image.Crop(new Rectangle(1, 1, 8, 8));
        image.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0])) { EdgeMode = ConvolutionEdgeMode.Wrap, WorkingSpace = ConvolutionWorkingSpace.LinearSrgb });
        image.Frames[1].Convolve(new ConvolutionOptions(new ConvolutionKernel(1, 3, [0.25, 0.5, 0.25])) { PreserveAlpha = true });
        image.Grayscale();
        Check(image.Width == 8 && image.Height == 8 && image.Frames.Count == 2, "processing geometry");
        using var clone = image.CloneAs<Rgba64>();
        Check(clone.Frames.Count == 2, "CloneAs");

        // A 4x3 block at (5, 2) of a white 12x10 canvas, right of and above the center
        using var bordered = new Image<Rgba32>(12, 10, new Rgba32(255, 255, 255));
        for (var y = 2; y < 5; y++)
        {
            for (var x = 5; x < 9; x++)
            {
                bordered.Frames[0][x, y] = new Rgba32(10, 20, 30);
            }
        }

        var analysis = bordered.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true });
        Check(analysis.Success && analysis.Bounds == new Rectangle(5, 2, 4, 3) && analysis.WeightX > 0 && analysis.WeightY < 0, "auto-crop analysis");
        Check(analysis.BackgroundColor == new Rgba32(255, 255, 255) && ((Image)bordered).AnalyzeAutoCrop().BackgroundColor == new Rgba64(65535, 65535, 65535), "auto-crop background");
        using var known = bordered.Clone();
        Check(known.AutoCrop(analysis, new AutoCropOptions { PaddingMode = AutoCropPaddingMode.Contain }) && known.Size == new Size(4, 3), "auto-crop with a known analysis");

        // (5 - 6, 2 - 1, 4 + 12, 3 + 2): the canvas is enlarged on the left and on the right
        Check(bordered.AutoCrop(new AutoCropOptions { PaddingX = 6, PaddingY = 1 }) && bordered.Size == new Size(16, 5), "auto-crop");
        Check(bordered.Frames[0][0, 0] == new Rgba32(255, 255, 255) && bordered.Frames[0][6, 1] == new Rgba32(10, 20, 30), "auto-crop pixels");
    }

    public static void Drawing()
    {
        // Every brush, both fill rules, strokes with every join and cap, on a typed frame and on a whole animation
        using var image = new Image<Rgba32>(32, 24, new Rgba32(255, 255, 255));
        image.AppendFrame();
        var star = new VectorPathBuilder().AddPolygon([new PointD(16, 2), new PointD(20, 20), new PointD(4, 8), new PointD(28, 8), new PointD(12, 20)]).Build();
        var curve = new VectorPathBuilder().MoveTo(2, 20).CubicBezierTo(10, -6, 20, 30, 30, 4).QuadraticBezierTo(24, 16, 16, 22).Build();
        var linear = new LinearGradientBrush(new PointD(0, 0), new PointD(32, 0), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255, 64))]) { SpreadMode = GradientSpreadMode.Reflect };
        var radial = new RadialGradientBrush(new PointD(16, 12), 10, [new GradientStop(0, new Rgba32(255, 255, 0)), new GradientStop(1, new Rgba32(0, 128, 0))]) { SpreadMode = GradientSpreadMode.Repeat };
        image.Draw((star, curve, linear, radial), static (canvas, state) =>
        {
            canvas.Fill(state.linear);
            canvas.Fill(state.radial, state.star, FillRule.EvenOdd);
            canvas.Stroke(new Pen(new Rgba32(0, 0, 0, 200), 2.5) { LineJoin = LineJoin.Round, LineCap = LineCap.Round }, state.curve);
            canvas.StrokeRectangle(new Pen(state.linear, 1) { LineJoin = LineJoin.Bevel }, new Rectangle(1, 1, 30, 22));
            canvas.FillCircle(new Rgba32(0, 0, 0), new PointD(4, 4), 2);
            canvas.StrokeEllipse(new Pen(new Rgba32(255, 255, 255), 1.5) { LineCap = LineCap.Square, MiterLimit = 2 }, new PointD(24, 16), 5, 3);
        });
        image.Frames[1].Draw(canvas => canvas.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1)));
        Check(image.Frames[0][4, 4] == new Rgba32(0, 0, 0) && image.Frames[1][4, 4] == new Rgba32(0, 0, 0), "drawing on every frame");
        Check(image.Frames[1][0, 0] == new Rgba32(0, 0, 0) && image.Frames[0][0, 0] != new Rgba32(0, 0, 0), "drawing on one frame");
        Check(image.Frames[0][0, 12] != new Rgba32(255, 255, 255), "gradient fill");

        using var gray = new Image<Gray16>(4, 1, new Gray16(0));
        ((Image)gray).Draw(canvas => canvas.FillRectangle(new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))]), new RectangleD(0, 0, 3.5, 1)));

        // The centers are at 1/8, 3/8, 5/8 and 7/8 of 65535 (ties upward); the last pixel is half covered
        Check(gray.Frames[0][0, 0] == new Gray16(8192) && gray.Frames[0][1, 0] == new Gray16(24576) && gray.Frames[0][2, 0] == new Gray16(40959) && gray.Frames[0][3, 0] == new Gray16(28672), "gradient samples");

        // Arcs, a transformed path, dashes, a sweep gradient, a clip, an image, an operator that erases
        using var sprite = new Image<Rgba64>(2, 2, new Rgba64(65535, 0, 0));
        using var extra = new Image<Rgba32>(16, 16, Colors.White);
        var ring = new VectorPathBuilder().AddArc(new PointD(8, 8), 6, 6, -90, 270).Build().Transform(AffineTransform.CreateRotation(90, new PointD(8, 8)));
        extra.Draw((ring, frame: sprite.Frames[0]), static (canvas, state) =>
        {
            canvas.FillRoundedRectangle(new SweepGradientBrush(new PointD(8, 8), [new GradientStop(0, Colors.Gold), new GradientStop(1, Colors.Navy)]), new Rectangle(0, 0, 16, 16), 4);
            canvas.Stroke(new Pen(Colors.Black, 1.5) { DashPattern = [2, 1] }, state.ring);
            canvas.Clip(new Rectangle(4, 4, 8, 8), state.frame, static (inner, frame) => inner.DrawImage(frame, new Rectangle(0, 0, 16, 16)));
            canvas.FillPie(new SolidBrush(Colors.Black) { CompositeMode = CompositeMode.DestinationOut }, new PointD(8, 8), 2, 0, 360);
            canvas.Fill(new ImageBrush(state.frame) { Transform = AffineTransform.CreateTranslation(14, 14) });
        });
        Check(extra.Frames[0][0, 0] == Colors.White && extra.Frames[0][13, 13] != Colors.White, "rounded rectangle");
        Check(extra.Frames[0][5, 5] == new Rgba32(255, 0, 0), "image in a clip");
        Check(extra.Frames[0][8, 8] == Colors.Transparent, "erasing");
        Check(extra.Frames[0][14, 14] == new Rgba32(255, 0, 0) && extra.Frames[0][13, 13] != new Rgba32(255, 0, 0), "image brush");

        // Three quarters of a circle of radius 6; turned by a quarter turn, the quarter that is missing is the top right one
        Check(ring.GetLength() is > 27.5 and < 28.3, "path length");
        Check(ring.Widen(new Pen(Colors.Black, 2)).Contains(new PointD(8, 14)) && !ring.Widen(new Pen(Colors.Black, 2)).Contains(new PointD(12.2, 3.8)), "stroke as a path");
        Check(ring.GetTightBounds() == RectangleD.FromLTRB(2, 2, 14, 14), "tight bounds");
        Check(Rgba32.ParseHex("#FF000080") == new Rgba32(255, 0, 0, 128) && Colors.Gold.ToHexString() == "#FFD700", "colors");
    }

    public static void Text()
    {
        // The same font from two containers: A is 12 by 12 pixels at 16 pixels per em, 2 pixels from its origin and 1
        // pixel under the top of the line; B follows it 2 pixels closer than its advance, by kerning
        foreach (var data in (byte[][])[SmokeFonts.BoxesWoff2, SmokeFonts.BoxesWoff])
        {
            var font = Font.Load(data);
            Check(font.FamilyName == "Meziantou Boxes" && font.GlyphCount == 13 && font.ContainsGlyph(new Rune('A')), "text: font");
            var layout = TextLayout.Create("AB\nA", font, 16);
            Check(layout.Width == 22 && layout.Height == 32 && layout.LineCount == 2 && layout.Baseline == 13 && !layout.HasMissingGlyphs, "text: layout");
            Check(layout.InkBounds == RectangleD.FromLTRB(2, 1, 21, 29), "text: ink bounds");

            using var image = new Image<Gray8>(24, 32, new Gray8(0));
            image.Draw(layout, static (canvas, layout) => canvas.FillText(new Rgba32(255, 255, 255), layout, new PointD(0, 0)));
            var frame = image.Frames[0];
            Check(frame[2, 1] == new Gray8(255) && frame[13, 12] == new Gray8(255) && frame[14, 12] == new Gray8(0) && frame[15, 5] == new Gray8(255) && frame[2, 17] == new Gray8(255) && frame[0, 0] == new Gray8(0), "text: fill");

            using var fromPath = new Image<Gray8>(24, 32, new Gray8(0));
            fromPath.Draw(layout.ToPath(new PointD(0, 0)), static (canvas, path) => canvas.Fill(new Rgba32(255, 255, 255), path));
            CheckEqual(fromPath, image, "text: path");
        }

        // A wrapped paragraph of two paints with a decoration and a stack of fonts. "AA AB" in 40 pixels: two lines,
        // aligned on the right; the B of the second line is red and underlined, one pixel thick, 2 pixels under the baseline
        var boxes = Font.Load(SmokeFonts.BoxesWoff);
        var rich = new RichTextBuilder()
            .Append("AA A", new TextStyle(new FontStack(boxes, Font.Load(SmokeFonts.CharstringsOtf)), 16) { Features = [new FontFeature("kern", 0)] })
            .Append("B", new TextStyle(boxes, 16) { Brush = new SolidBrush(new Rgba32(255, 0, 0)), Decorations = TextDecorations.Underline })
            .Build();
        var paragraph = TextLayout.Create(rich, new TextLayoutOptions { MaxWidth = 40, Alignment = TextAlignment.End, Hyphenation = TextHyphenation.Automatic });
        Check(paragraph.LineCount == 2 && paragraph.Lines[1].Start == 3 && !paragraph.IsTruncated && paragraph.Height == 32, "text: paragraph");
        using (var wrapped = new Image<Rgba32>(48, 40, new Rgba32(255, 255, 255)))
        {
            wrapped.Draw(paragraph, static (canvas, layout) => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(0, 0)));
            Check(Pixels(wrapped.Frames[0]).Any(static value => value == 0) && wrapped.Frames[0][36, 31] == new Rgba32(255, 0, 0), "text: paragraph pixels");
        }

        // Along a path that goes down: the letters are turned, their top on the right. A is 12 by 12 pixels, 2 pixels
        // after the start of the path and from the path to 12 pixels on its left
        using (var turned = new Image<Gray8>(24, 24, new Gray8(0)))
        {
            var down = new VectorPathBuilder().MoveTo(4, 2).LineTo(4, 22).Build();
            turned.Draw((Layout: TextLayout.Create("A", boxes, 16), Path: down), static (canvas, state) => canvas.FillText(new Rgba32(255, 255, 255), state.Layout, state.Path, new TextPathOptions { Overflow = TextPathOverflow.Clip }));
            using var expected = new Image<Gray8>(24, 24, new Gray8(0));
            expected.Draw(canvas => canvas.FillRectangle(new Rgba32(255, 255, 255), new RectangleD(4, 4, 12, 12)));
            CheckEqual(turned, expected, "text: path of the text");
        }

        // An instance of a variable font: at the heaviest weight, A is the rectangle (80, 0)-(560, 700) of 1000 units
        // per em and advances by 640 units; at 20 pixels per em, it is drawn from 1.6 to 11.2 and the text is 12.8 wide
        var variable = Font.Load(SmokeFonts.VariableTtf);
        var heavy = variable.WithVariations(new FontVariationSetting("wght", 900));
        Check(variable.VariationAxes is [{ Tag: "wght", Minimum: 100, Default: 400, Maximum: 900 }, { Tag: "wdth" }] && variable.NamedInstances.Count == 3, "text: axes");
        Check(heavy.Weight == 900 && heavy.Variations[0].Value == 900 && heavy.Metrics.Ascent == 850, "text: instance");
        var heavyLayout = TextLayout.Create("A", heavy, 20);
        Check(TextLayout.Create("A", variable, 20).Width == 12 && Math.Abs(heavyLayout.Width - 12.8) < 1e-9, "text: instance advance");
        Check(Math.Abs(heavyLayout.InkBounds.Left - 1.6) < 1e-9 && Math.Abs(heavyLayout.InkBounds.Right - 11.2) < 1e-9, "text: instance outline");
        Check(variable.WithVariations(variable.NamedInstances[2]).Variations[1].Value == 200, "text: named instance");

        // A color glyph: at 16 pixels per em, the three layers of A are squares of 12, 8 and 4 pixels, red, green and
        // the color of the text
        var color = Font.Load(SmokeFonts.ColorBoxesTtf);
        Check(color.HasColorGlyphs && color.ColorPalettes is [{ Colors: [{ R: 255, G: 0, B: 0, A: 255 }, ..] }, _], "text: palettes");
        using (var emoji = new Image<Rgba32>(16, 16, new Rgba32(255, 255, 255)))
        {
            emoji.Draw(color, static (canvas, font) => canvas.FillText(new Rgba32(0, 0, 255), "A", font, 16, new PointD(0, 0)));
            var pixels = emoji.Frames[0];
            Check(pixels[2, 1] == new Rgba32(255, 0, 0) && pixels[4, 3] == new Rgba32(0, 255, 0) && pixels[6, 5] == new Rgba32(0, 0, 255) && pixels[1, 1] == new Rgba32(255, 255, 255), "text: color glyph");
        }

        // A glyph that is an image: at the size of its strike, 16 pixels per em, its pixels are those of the canvas
        using (var bitmap = new Image<Rgba32>(16, 16, new Rgba32(255, 255, 255)))
        {
            bitmap.Draw(Font.Load(SmokeFonts.BitmapBoxesTtf), static (canvas, font) => canvas.FillText(new Rgba32(0, 0, 0), "A", font, 16, new PointD(0, 0)));
            var pixels = bitmap.Frames[0];
            Check(pixels[2, 1] == new Rgba32(255, 0, 0) && pixels[6, 5] == new Rgba32(0, 255, 0) && pixels[5, 12] == new Rgba32(127, 127, 255) && pixels[1, 1] == new Rgba32(255, 255, 255), "text: bitmap glyph");
        }

        var hebrew = TextLayout.Create("\u05D0\u05D1 AB", new TextStyle(boxes, 16), new TextLayoutOptions { Direction = TextDirection.RightToLeft });
        Check(hebrew.Lines[0].IsRightToLeft && hebrew.HasMissingGlyphs, "text: direction");

        // The shaping models of Arabic, Hebrew and Syriac: joining forms, the order of the marks, a precomposed letter,
        // the forms of alaph. The font has none of these characters: each of the nine letters and two spaces is 8 pixels
        // wide (the missing glyph, the space), and the marks do not move the pen.
        var joined = TextLayout.Create("\u0628\u064E\u0651\u0644\u0627 \u05E9\u05BC\u05C1\uFB2C \u070F\u0712\u0710\u0710", boxes, 16);
        Check(joined.Lines[0].IsRightToLeft && joined.HasMissingGlyphs && joined.Width == 11 * 8, "text: shaping models");

        // Thai and Hangul: a consonant, a tone mark and a sara am (decomposed into a ring, which is a mark, and a vowel),
        // then the letters of a syllable, which the font has neither as letters nor as a syllable.
        var syllables = TextLayout.Create("\u0E01\u0E48\u0E33 \u1100\u1161\u11A8", boxes, 16);
        Check(!syllables.Lines[0].IsRightToLeft && syllables.HasMissingGlyphs && syllables.Width == 6 * 8, "text: syllables");

        // Khmer and Myanmar: a consonant with a subscript ro and a vowel with two parts, a vowel without consonant, then
        // a consonant and the vowel drawn before it. The marks of these scripts keep their advance: nine times 8 pixels.
        var stacked = TextLayout.Create("\u1780\u17D2\u179A\u17BE \u17C1 \u1000\u1031", boxes, 16);
        Check(!stacked.Lines[0].IsRightToLeft && stacked.HasMissingGlyphs && stacked.Width == 9 * 8, "text: stacked syllables");

        // The universal model: a Tibetan letter with a vowel sign, which is a mark and does not move the pen, a Sinhala
        // letter with the vowel sign drawn before it, and two Mongolian letters that join. Seven times 8 pixels.
        var clusters = TextLayout.Create("\u0F40\u0F72 \u0D9A\u0DD9 \u1820\u1820", boxes, 16);
        Check(!clusters.Lines[0].IsRightToLeft && clusters.HasMissingGlyphs && clusters.Width == 7 * 8, "text: clusters");

        // A language, a synthetic bold and oblique, and a dictionary for the scripts without spaces: at 48 pixels per
        // em, the bold adds 2 pixels to the advance of each letter, and the dictionary lets the line end between two
        // words, each of one letter here.
        var styled = new TextStyle(boxes, 48) { Language = "zh-Hant", Synthesis = FontSynthesis.Bold | FontSynthesis.Oblique };
        var synthesized = TextLayout.Create("AA", styled);
        Check(synthesized.Width == 100, "text: synthetic bold");
        using (var bold = new Image<Rgba32>(110, 50))
        {
            bold.Draw(synthesized, static (canvas, layout) => canvas.FillText(new Rgba32(255, 255, 255), layout, new PointD(0, 0)));
            Check(Pixels(bold.Frames[0]).Any(static value => value != 0), "text: synthetic bold drawn");
        }

        var dictionary = LineBreakDictionary.Parse("\u0E01\n");
        var thai = TextLayout.Create("\u0E01\u0E01\u0E01", new TextStyle(boxes, 16), new TextLayoutOptions { MaxWidth = 10, LineBreakDictionary = dictionary });
        Check(thai.LineCount == 3, "text: dictionary line breaks");

        // CFF outlines, stroked, through the overload that lays out the text
        var cff = Font.Load(SmokeFonts.CharstringsOtf);
        using var stroked = new Image<Rgba32>(64, 16);
        stroked.Draw(cff, static (canvas, font) =>
        {
            canvas.FillText(new Rgba32(255, 0, 0), "BCDE", font, 12, new PointD(1, 1));
            canvas.StrokeText(new Pen(new Rgba32(0, 0, 255), 1), TextLayout.Create("BCDE", font, 12), new PointD(1, 1));
        });
        Check(Pixels(stroked.Frames[0]).Any(static value => value != 0), "text: CFF");
        Check(Expect<UnknownImageFormatException>(() => Font.Load([1, 2, 3, 4])) is not null, "text: not a font");
    }

    public static void ColorConversion()
    {
        // The built-in profiles go through the same parser and conversion as any profile. sRGB to sGray keeps the
        // luminance: white stays white, and a mid gray stays the same gray
        var toGray = IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray);
        Check(toGray.SourceChannelCount == 3 && toGray.DestinationChannelCount == 1, "color: channel counts");
        var gray = new byte[3];
        toGray.Convert([255, 255, 255, 0, 0, 0, 128, 128, 128], gray);
        Check(gray is [255, 0, 128], "color: sRGB to sGray");

        var toRgb = IccColorTransform.Create(IccProfile.SrgbGray, IccProfile.Srgb, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual });
        var words = new ushort[6];
        toRgb.Convert([(ushort)0, 65535], words);
        Check(words is [0, 0, 0, 65535, 65535, 65535], "color: sGray to sRGB");

        // Linear-light pixels converted to sRGB: 128 / 255 encodes to 188 / 255; alpha is kept
        using var image = new Image<Rgba32>(Width, Height, new Rgba32(128, 255, 0, 77));
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        image.ConvertColorProfile(IccProfile.Srgb);
        Check(image.Frames[0][Width - 1, Height - 1] == new Rgba32(188, 255, 0, 77), "color: linear to sRGB");
        Check(ReferenceEquals(image.Metadata.IccProfile, IccProfile.Srgb), "color: profile label");
        Check(image.Metadata.TransferFunction == ColorTransferFunction.Srgb, "color: transfer function label");

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var reloaded = Image.Load<Rgba32>(stream);
        Check(reloaded.Metadata.IccProfile is { ColorSpace: IccProfileColorSpace.Rgb, ProfileClass: IccProfileClass.Display, Version.Major: 4 }, "color: profile round trip");
        Check(reloaded.Metadata.IccProfile!.Data.Equals(IccProfile.Srgb.Data), "color: profile bytes");
    }

    public static void Streaming()
    {
        using var animation = CreateAnimation(frames: 4, colors: 16);
        var directory = Directory.CreateTempSubdirectory("meziantou-image-aot-");
        try
        {
            foreach (var extension in new[] { ".apng", ".gif" })
            {
                var path = Path.Combine(directory.FullName, "animation" + extension);
                using (var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(Width, Height) { ExpectedFrameCount = extension == ".gif" ? null : 4 }))
                {
                    foreach (var frame in animation.Frames)
                    {
                        writer.WriteFrame(frame);
                    }

                    writer.Complete();
                }

                using var reader = Image.OpenReader<Rgba32>(path);
                var count = 0;
                using var destination = reader.ReadFrame() ?? throw new InvalidOperationException("No frame");
                count++;
                while (reader.ReadFrameInto(destination))
                {
                    count++;
                }

                Check(count == 4, "reader frame count " + extension);
                using var loaded = Image.Load<Rgba32>(path);
                CheckEqual(animation, loaded, "path writer round trip " + extension);
            }

            using (var still = CreatePattern(seed: 9))
            {
                var path = Path.Combine(directory.FullName, "still.png");
                still.SaveAsync(path).GetAwaiter().GetResult();
                using var loaded = Image.LoadAsync<Rgba32>(path).GetAwaiter().GetResult();
                CheckEqual(still, loaded, "async path save and load");
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    public static void Errors()
    {
        Expect<UnknownImageFormatException>(() => Image.Load("not an image"u8));
        using var pattern = CreatePattern(seed: 2);
        var png = Encode(pattern, new PngEncoder());
        var corrupted = (byte[])png.Clone();
        corrupted[^5] ^= 0xFF; // IEND CRC
        Expect<InvalidImageContentException>(() => Image.Load(corrupted));
        var limits = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = Width - 1 } };
        var exception = Expect<ImageResourceLimitException>(() => Image.Load(png, new ImageDecodeOptions { Configuration = limits }));
        Check(exception.Kind == ImageResourceLimitKind.Width && exception.Limit == Width - 1 && exception.Requested == Width, "limit exception fields");
        using var animation = CreateAnimation(frames: 2);
        Expect<UnsupportedImageFeatureException>(() => Encode(animation, new JpegEncoder()));
    }

    private static T Expect<T>(Func<object> action)
        where T : Exception
    {
        try
        {
            (action() as IDisposable)?.Dispose();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"{typeof(T).Name} expected.");
    }

    // The fixture jp2/ht-lossless-rgb8 of the corpus: a codestream of the high-throughput block coder, 13x9 pixels whose
    // samples are (37x + 11) mod 256, (59y + 101) mod 256 and (13xy + 29) mod 256
    private const string Jpeg2000HighThroughput =
        "/0//UQAvQAAAAAANAAAACQAAAAAAAAAAAAAADQAAAAkAAAAAAAAAAAADBwEBBwEBBwEB/1AACAACAAAAA/9SAAwAAgABAQMEBEAB/1wADSBQWFhYWFhYUFBQ" +
        "/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAAc4AAf+TwBOAoUgKACGUAMAVAHOsMeYAgbQAwBUAB9PN/gCDNADACeAEsAJgXtDCAEc0AIz+MjMA" +
        "RPwAAXQAwAqIATQAmIYTdf0AQbQAMOsAIjQAGMYBAXQAwAqQNjZt2vwAwbQAwArYAV0AKoADZGAlKvkCkLz4/pcAahyCXIIRM/4AAAEvNgCJJqoR/gDIv5UA" +
        "wAr4AV0AKwCT4ruzicNpAIQvf5N+uADz6YmVUf0RKwAIQFuWAClojq5F4gAAgL+WAMAKyAFeXq+71wigLjBD0ngA3x/fPr4/vj/+AAhAFrYAwBbkAXRQBaoH" +
        "1h+wGwIrDb0zZR0n6A/QGXwC0Y0nYBCEUdwAwL4PGCshdsYesAO71XfGnlJt62t3tgAmY5R4IF5AREScAICAAwAe4IEe4O/9/gIVwt89hBIZAMAW5AF0UAWs" +
        "ns+39/P3ez8+H9//Z/9/gQ/FB+O9pRxD0I99AP5+Ly8Pj/7+/v5e/n9efl7ffv4+f38XA1MhCBveCAAnXAB+vp8fv37/f/3/fYAYpjhfGEHxj3sAwBWQBbT/" +
        "f/9/sAhGU4ECeAD+fr/f7/f7+/v7+/v7CGOnptAAB2kAAKddAP/Z";

    public static void Jpeg2000()
    {
        // Lossless: 8-bit and 16-bit samples, the colors of transparent pixels included, as a file and as a codestream
        using var still = CreatePattern(seed: 11);
        foreach (var container in new[] { Jpeg2000Container.Jp2, Jpeg2000Container.Codestream })
        {
            var encoded = Encode(still, new Jpeg2000Encoder { Container = container });
            Check(Image.DetectFormat(encoded) == ImageFormat.Jpeg2000, "JPEG 2000 detection");
            var info = Image.Identify(encoded, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Check(info.Width == Width && info.Height == Height && info.FrameCount == 1 && info.PixelFormat == PixelFormat.Rgba32 && info.MayHaveTransparency == true, "JPEG 2000 identification");
            using var decoded = Image.Load(encoded);
            CheckEqual(still, decoded, "JPEG 2000 lossless");
        }

        using (var deep = still.CloneAs<Rgba64>())
        using (var decoded = Image.Load(Encode(deep, new Jpeg2000Encoder())))
        {
            Check(decoded.PixelFormat == PixelFormat.Rgba64, "JPEG 2000 16-bit pixel format");
            CheckEqual(deep, decoded, "JPEG 2000 16-bit lossless");
        }

        // Lossy: the irreversible wavelet and component transformations, in floating point; the opacity is exact
        using (var decoded = Image.Load<Rgba32>(Encode(still, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = 95 })))
        {
            var error = MaxError(still, decoded);
            Check(error is > 0 and <= 48, string.Create(CultureInfo.InvariantCulture, $"JPEG 2000 lossy error ({error})"));
            var (source, lossy) = (Pixels(still.Frames[0]), Pixels(decoded.Frames[0]));
            for (var i = 3; i < source.Length; i += 4)
            {
                Check(source[i] == lossy[i], "JPEG 2000 lossy opacity");
            }
        }

        // A sequential reader, and a codestream of the high-throughput block coder written by OpenJPH
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(Convert.FromBase64String(Jpeg2000HighThroughput)));
        using var frame = reader.ReadFrame();
        Check(frame is { Width: 13, Height: 9 } && reader.ReadFrame() is null, "JPEG 2000 reader");
        var pixels = Pixels(frame!.Frames[0]);
        for (var y = 0; y < 9; y++)
        {
            for (var x = 0; x < 13; x++)
            {
                var offset = 3 * ((y * 13) + x);
                Check(pixels[offset] == (byte)((x * 37) + 11) && pixels[offset + 1] == (byte)((y * 59) + 101) && pixels[offset + 2] == (byte)((x * y * 13) + 29), "JPEG 2000 high-throughput samples");
            }
        }
    }

    public static void OpenExr()
    {
        // Floating-point pixels are stored as they are, values above 1 and the special values included, with every compression
        using var still = CreatePattern(seed: 13);
        // Alphas of 0, 1/2 and 1: the premultiplication of the colors, which OpenEXR stores, is then exact
        using var full = still.CloneAs<RgbaFloat>();
        full.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x].A = ((x + y) % 3) * 0.5f;
                }
            }
        });
        full.Frames[0][1, 2] = new RgbaFloat(42.5f, -1f, float.PositiveInfinity, 0.5f);
        full.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var half = full.CloneAs<RgbaHalf>();
        foreach (var compression in Enum.GetValues<OpenExrCompression>())
        {
            foreach (var image in new Image[] { full, half })
            {
                var encoded = Encode(image, new OpenExrEncoder { Compression = compression });
                Check(Image.DetectFormat(encoded) == ImageFormat.OpenExr, "OpenEXR detection");
                var info = Image.Identify(encoded, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
                Check(info.Width == Width && info.Height == Height && info.PixelFormat == PixelFormat.RgbaFloat, "OpenEXR identification");
                using var decoded = Image.Load(encoded);
                using var expected = image is Image<RgbaHalf> halfImage ? halfImage.CloneAs<RgbaFloat>() : ((Image<RgbaFloat>)image).CloneAs<RgbaFloat>();
                CheckEqual(expected, decoded, "OpenEXR " + compression.ToString());
                Check(decoded.Metadata.TransferFunction == ColorTransferFunction.Linear, "OpenEXR linear light");
            }
        }

        // A typed load clips or tone maps the highlights; 8-bit pixels are exact as half samples
        using (var clipped = Image.Load<Rgba32>(Encode(full, new OpenExrEncoder()), new ImageDecodeOptions { Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } }))
        {
            Check(clipped.Frames[0][1, 2].R == 255, "OpenEXR clipped highlight");
        }

        still.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using (var decoded = Image.Load<Rgba32>(Encode(still, new OpenExrEncoder { Compression = OpenExrCompression.Piz, PixelType = OpenExrPixelType.Float })))
        {
            CheckEqual(still, decoded, "OpenEXR 8-bit samples");
        }
    }

    private static byte[] Encode(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static Image<Rgba32> CreatePattern(int seed, int colors = 0)
    {
        var pixels = new Rgba32[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var value = (x * 7) + (y * 13) + (seed * 31);
                pixels[(y * Width) + x] = colors > 0
                    ? new Rgba32((byte)((value % colors) * 16), (byte)(255 - ((value % colors) * 16)), (byte)seed, 255)
                    : new Rgba32((byte)value, (byte)(x * 5), (byte)(y * 9 + seed), (byte)(x == 0 ? 0 : 255 - y));
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, Width, Height);
    }

    private static Image<Rgba32> CreateAnimation(int frames, int colors = 0)
    {
        var image = CreatePattern(seed: 0, colors: colors == 0 ? 0 : colors);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        for (var i = 1; i < frames; i++)
        {
            using var next = CreatePattern(seed: i, colors: colors);
            var frame = image.AppendFrame(next.Frames[0]);
            frame.Metadata.Duration = new FrameDuration(1, 10);
        }

        return image;
    }

    private static Rgb24[] CreateSmoothRgb()
    {
        var pixels = new Rgb24[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                pixels[(y * Width) + x] = new Rgb24((byte)(x * 255 / (Width - 1)), (byte)(y * 255 / (Height - 1)), (byte)((x + y) * 3));
            }
        }

        return pixels;
    }

    private static void CheckEqual(Image expected, Image actual, string context)
    {
        Check(expected.Frames.Count == actual.Frames.Count && expected.Size == actual.Size, context + ": structure");
        for (var i = 0; i < expected.Frames.Count; i++)
        {
            Check(Pixels(expected.Frames[i]).AsSpan().SequenceEqual(Pixels(actual.Frames[i])), context + ": pixels of frame " + i.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static int MaxError(Image expected, Image actual)
    {
        var a = Pixels(expected.Frames[0]);
        var b = Pixels(actual.Frames[0]);
        var max = 0;
        for (var i = 0; i < a.Length; i++)
        {
            max = Math.Max(max, Math.Abs(a[i] - b[i]));
        }

        return max;
    }

    private static byte[] Pixels(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Check failed: " + what);
    }
}
