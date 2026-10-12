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
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate })
        {
            foreach (var bigTiff in new[] { false, true })
            {
                var data = Encode(still, new TiffEncoder { Compression = compression, BigTiff = bigTiff, BigEndian = bigTiff });
                using var decoded = Image.Load(data);
                CheckEqual(still, decoded, string.Create(CultureInfo.InvariantCulture, $"TIFF {compression} bigtiff={bigTiff}"));
            }
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
