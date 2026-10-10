using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

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
