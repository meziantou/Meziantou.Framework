using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>
/// Representative usage of the public API, shown by the package README (snippets), and run by
/// the unit tests (<c>DocumentationExampleTests</c>).
/// </summary>
internal static class Examples
{
    /// <summary>Eager animated resize: every frame and the poster frame are resized atomically; timing is preserved.</summary>
    // begin-snippet: resize-animation
    public static void ResizeAnimation(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

        // Same format as the extension; APNG/GIF timing and play count come from the decoded image
        image.Save(outputPath);
    }
    // end-snippet

    /// <summary>Auto-crop: trims the uniform background around the content and keeps a margin; returns whether the image changed.</summary>
    // begin-snippet: auto-crop
    public static bool TrimBackground(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The detection works on the stored pixels: apply the EXIF orientation first
        image.AutoOrient();

        // Read-only: the background color and the bounding box of everything else, in every frame
        var options = new AutoCropOptions { PaddingX = 8, PaddingY = 8, BucketThreshold = 0.945 };
        var analysis = image.AnalyzeAutoCrop(options);
        if (!analysis.Success)
            return false; // no uniform border, or no content of at least 3x3 pixels

        // Keeps 8 pixels around the content. Where the canvas is too small for the margin, it is enlarged and filled with
        // the background color (AutoCropPaddingMode.Contain clamps the margin instead)
        var changed = image.AutoCrop(analysis, options);
        image.Save(outputPath);
        return changed;
    }
    // end-snippet

    /// <summary>Convolution matrices: a sharpening matrix on a copy, then edge detection with the alpha of the pixels kept.</summary>
    // begin-snippet: convolve
    public static void SharpenAndDetectEdges(string inputPath, string sharpenedPath, string edgesPath)
    {
        using var image = Image.Load(inputPath);

        // The matrix is applied as written, row by row; weights that add up to 1 keep the overall brightness
        var sharpen = new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);
        using (var sharpened = image.Clone())
        {
            sharpened.Convolve(new ConvolutionOptions(sharpen));
            sharpened.Save(sharpenedPath);
        }

        // Weights that add up to 0 would also filter alpha down to 0 (transparent black): keep the alpha instead
        var laplacian = new ConvolutionKernel(3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);
        image.Convolve(new ConvolutionOptions(laplacian) { PreserveAlpha = true, EdgeMode = ConvolutionEdgeMode.Mirror });
        image.Save(edgesPath);
    }
    // end-snippet

    /// <summary>Typed row access with a static callback (no closure allocation).</summary>
    // begin-snippet: typed-rows
    public static void InvertColors(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        foreach (var frame in image.Frames)
        {
            frame.ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    foreach (ref var pixel in pixels.GetRowSpan(y))
                    {
                        pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
                    }
                }
            });
        }

        image.Save(path);
    }
    // end-snippet

    /// <summary>Typed row access passing state explicitly.</summary>
    // begin-snippet: typed-rows-state
    public static long ComputeLuminanceSum(Image<Gray16> image)
    {
        var total = new StrongBox<long>();
        image.Frames[0].ProcessPixelRows(total, static (pixels, state) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    state.Value += pixel.Value;
                }
            }
        });

        return total.Value;
    }
    // end-snippet

    /// <summary>Builds an animation from scratch with exact rational timing and saves it as APNG and GIF.</summary>
    // begin-snippet: build-animation
    public static void BuildAnimation(string apngPath, string gifPath)
    {
        using var animation = new Image<Rgba32>(64, 64, new Rgba32(255, 0, 0));
        animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);

        var second = animation.AppendFrame(); // creates default animation settings (infinite loop)
        second.Metadata.Duration = new FrameDuration(1, 30); // exactly 1/30 s
        second.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                pixels.GetRowSpan(y).Fill(new Rgba32(0, 0, 255));
            }
        });

        animation.Animation!.TotalPlays = 3; // played three times in total

        animation.Save(apngPath, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        animation.Save(gifPath, new GifEncoder { Dithering = GifDithering.FloydSteinberg });
    }
    // end-snippet

    /// <summary>JPEG stores a single frame: export one frame explicitly and choose how to remove alpha.</summary>
    // begin-snippet: export-jpeg-frame
    public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
    {
        using var animation = Image.Load(animatedPath);
        using var frame = animation.CloneFrame(frameIndex);
        frame.Save(jpegPath, new JpegEncoder
        {
            Quality = 85,
            BackgroundColor = new Rgba32(255, 255, 255),
            AllowBitDepthReduction = true,
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });
    }
    // end-snippet

    /// <summary>WebP output: a lossy photo with its alpha kept losslessly, and a lossless animation published atomically.</summary>
    // begin-snippet: save-webp
    public static void SaveWebP(string photoPath, string webpPath, string animatedPath, string animatedWebPPath)
    {
        using (var photo = Image.Load(photoPath))
        {
            photo.Save(webpPath, new WebPEncoder
            {
                Compression = WebPCompression.Lossy,
                Quality = 80, // 0-100: size versus fidelity
                Effort = 6,   // 0-9: encoding time versus size
                AllowBitDepthReduction = true, // 16-bit sources are written with 8 bits
            });
        }

        // Every sample is kept, including the colors of fully transparent pixels. An animation needs a seekable
        // destination (the file size is patched at the end): a path, a FileStream or a MemoryStream.
        using var animation = Image.Load(animatedPath);
        animation.Save(animatedWebPPath, new WebPEncoder { Compression = WebPCompression.Lossless });
    }
    // end-snippet

    /// <summary>QOI output: a lossless still image, and linear-light samples whose label must survive the conversion.</summary>
    // begin-snippet: save-qoi
    public static void SaveQoi(string inputPath, string qoiPath, string linearQoiPath, string pngPath)
    {
        using var image = Image.Load(inputPath);
        using var frame = image.CloneFrame(0); // QOI stores one still image
        frame.Save(qoiPath, new QoiEncoder { AllowBitDepthReduction = true });

        // A QOI colorspace of 1 (linear samples) is reported as a label, never applied: other formats cannot store it, so
        // saving the pixels elsewhere requires an explicit decision instead of silently relabeling them as sRGB
        using var linear = Image.Load(linearQoiPath);
        if (linear.Metadata.TransferFunction == ColorTransferFunction.Linear)
        {
            linear.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        }
    }
    // end-snippet

    /// <summary>Asset-pipeline output: the BMP, TGA and Netpbm formats, with their explicit choices.</summary>
    // begin-snippet: save-asset-formats
    public static void SaveAssetFormats(string inputPath, string bmpPath, string tgaPath, string pnmPath)
    {
        using var image = Image.Load(inputPath);
        using var frame = image.CloneFrame(0); // BMP, TGA and Netpbm store one still image

        // BMP: 32-bit with an explicit alpha mask for pixel formats with alpha, 24-bit otherwise. Forcing 24-bit discards
        // alpha, so it needs a background color.
        frame.Save(bmpPath, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24, BackgroundColor = new Rgba64(65535, 65535, 65535), AllowBitDepthReduction = true });

        // TGA: run-length packets are lossless and usually smaller on graphic content
        frame.Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

        // Netpbm: the variant follows the pixel format (PGM, PPM, or PAM when the pixels have alpha), and MAXVAL follows
        // the precision, so 16-bit images keep every bit
        frame.Save(pnmPath, new PnmEncoder());
    }
    // end-snippet

    /// <summary>A multi-page TIFF document: pages may differ in size and pixel format, and are read one at a time.</summary>
    // begin-snippet: tiff-pages
    public static Size WriteAndReadTiffDocument(string inputPath, string documentPath)
    {
        using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            using var source = Image.Load(inputPath);
            using var first = source.CloneFrame(0); // a page is a still image, never an animation frame
            using var second = first.Clone();
            second.Resize(new ResizeOptions(first.Width / 2, first.Height / 2));

            // Add copies the image: the caller keeps and disposes its own
            pages.Add(first);
            pages.Add(second);
            pages.Save(documentPath, new TiffEncoder { Compression = TiffCompression.Deflate });
        }

        // Loading reads the directory structure only: the pixels of a page are read when that page is decoded
        using var document = ImageCollection.Load(documentPath);
        using var page = document[1].Decode();
        return page.Size; // the second page keeps its own size, never the first one's
    }
    // end-snippet

    /// <summary>A multi-size icon and a cursor: representations are alternatives, never animation frames.</summary>
    // begin-snippet: ico-representations
    public static Point WriteAndReadIcon(string inputPath, string iconPath, string cursorPath)
    {
        using var loaded = Image.Load(inputPath);
        using var source = loaded.CloneFrame(0); // a representation is a still image, never an animation frame
        using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
        foreach (var side in (int[])[16, 32, 256])
        {
            using var sized = source.Clone();
            sized.Resize(new ResizeOptions(side, side) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
            representations.Add(sized, hotspot: new Point(side / 2, side / 2));
        }

        // An icon cannot store a hotspot, so the entry hotspots are unsupported metadata there
        representations.Save(iconPath, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        representations.Save(cursorPath, new IcoEncoder { Kind = IconKind.Cursor });

        using var icon = ImageCollection.Load(iconPath);
        using var best = icon.SelectBySize(new Size(24, 24)).Decode(); // the 32x32 representation: nothing is resized
        using var cursor = ImageCollection.Load(cursorPath);
        return cursor.SelectBySize(new Size(16, 16)).Hotspot!.Value;
    }
    // end-snippet

    /// <summary>Bounded-memory streaming transform: decode, resize and re-encode one frame at a time.</summary>
    // begin-snippet: streaming-transform
    public static async Task StreamingResizeAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        await using var reader = await Image.OpenReaderAsync<Rgba32>(input, cancellationToken: cancellationToken);
        var info = reader.Info;
        var size = new Size(info.Width / 2, info.Height / 2);

        await using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
        {
            Encoder = new GifEncoder(),
            Animation = info.Animation, // e.g. preserve the number of plays
            Metadata = info.Metadata,
        });

        while (await reader.ReadFrameAsync(cancellationToken) is { } frame)
        {
            using (frame)
            {
                frame.Resize(new ResizeOptions(size) { Mode = ResizeMode.Stretch }, cancellationToken);
                await writer.WriteFrameAsync(frame.Frames[0], cancellationToken);
            }
        }

        await writer.CompleteAsync(cancellationToken); // mandatory: disposing without Complete aborts the output
    }
    // end-snippet

    /// <summary>Streaming with a reused destination: ReadFrameInto overwrites the pixels of one caller-owned image.</summary>
    // begin-snippet: read-frame-into
    public static void StreamingWithReusedBuffer(string inputPath, string outputPath)
    {
        using var reader = Image.OpenReader<Rgba32>(inputPath);
        var info = reader.Info;
        var frameCount = info.FrameCount ?? throw new InvalidOperationException("PNG output requires a known frame count.");

        using var writer = Image.CreateWriter<Rgba32>(outputPath, new ImageWriterOptions(info.Size)
        {
            Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated },
            ExpectedFrameCount = frameCount,
            Animation = info.Animation,
        });

        if (reader.ReadPosterFrame() is { } poster)
        {
            using (poster)
            {
                writer.WritePosterFrame(poster.Frames[0]);
            }
        }

        using var buffer = new Image<Rgba32>(info.Width, info.Height);
        while (reader.ReadFrameInto(buffer))
        {
            buffer.Grayscale();
            writer.WriteFrame(buffer.Frames[0]);
        }

        writer.Complete(); // publishes outputPath atomically
    }
    // end-snippet

    /// <summary>Identify without decoding pixels; unknown values stay null.</summary>
    // begin-snippet: identify
    public static string Describe(Stream stream)
    {
        var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        var frames = info.FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        return $"{info.Format} {info.Size} {info.PixelFormat}, frames: {frames}, orientation: {info.Metadata.Orientation}";
    }
    // end-snippet

    /// <summary>Precision-preserving conversion with an explicit alpha policy.</summary>
    // begin-snippet: convert-precision
    public static void ConvertToGray16(string inputPath, string outputPath)
    {
        using var image = Image.Load<Rgba64>(inputPath);
        image.AutoOrient();
        using var gray = image.CloneAs<Gray16>(new PixelConversionOptions
        {
            BackgroundColor = new Rgba64(65535, 65535, 65535),
            DiscardIncompatibleColorProfile = true,
        });
        gray.Save(outputPath, new PngEncoder());
    }
    // end-snippet

    /// <summary>Copies raw pixels in and out; the library never wraps caller memory.</summary>
    // begin-snippet: raw-pixels
    public static byte[] RoundTripRawPixels(ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var image = Image.ImportPixelBytes<Rgba32>(rgba, width, height);
        image.Crop(new Rectangle(0, 0, width / 2, height));
        var result = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(result);
        return result;
    }
    // end-snippet

    /// <summary>Handling the error taxonomy.</summary>
    // begin-snippet: error-handling
    public static Image? TryLoad(string path, ImageConfiguration configuration)
    {
        try
        {
            return Image.Load(path, new ImageDecodeOptions { Configuration = configuration });
        }
        catch (UnknownImageFormatException)
        {
            return null; // not an image
        }
        catch (UnsupportedImageFeatureException ex)
        {
            Console.Error.WriteLine($"Unsupported {ex.Format} feature: {ex.Feature}");
            return null;
        }
        catch (ImageResourceLimitException ex)
        {
            Console.Error.WriteLine($"Limit {ex.Kind} exceeded ({ex.Requested} > {ex.Limit})");
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null; // corrupted or truncated file
        }
    }
    // end-snippet

    /// <summary>Metadata editing with explicit save policies.</summary>
    // begin-snippet: edit-metadata
    public static void EditMetadata(string path)
    {
        using var image = Image.Load(path);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
        image.Metadata.Resolution = new ImageResolution(300, 300);
        image.Save(path, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    }
    // end-snippet
}
