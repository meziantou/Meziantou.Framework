using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// The adapter from the library image model to the pure-buffer comparison model. Every frame is copied row by
/// row, through the public pixel-access API only, into a <see cref="RawPixelBuffer"/> in the canonical layout of its pixel
/// format (<see cref="GetLayout"/>); the image is never encoded to compare it.
/// </summary>
/// <remarks>
/// Three independent copy paths are provided so that tests can cross-check them: untyped rows
/// (<see cref="ImageFrame.ProcessPixelBytes(PixelBytesAction)"/>: byte layout of the format, 16-bit components in native
/// endianness), typed rows (<see cref="ImageFrame{TPixel}.ProcessPixelRows(PixelRowsAction{TPixel})"/>: components read
/// from the pixel fields, independent of the byte layout) and strided copies
/// (<see cref="ImageFrame.CopyPixelBytesTo(Span{byte}, int)"/>, <see cref="ImageFrame{TPixel}.CopyPixelDataTo(Span{TPixel}, int)"/>).
/// </remarks>
public static class ImageSnapshots
{
    /// <summary>Gets the raw layout a frame of <paramref name="format"/> is captured in.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns><c>rgba8</c> for <c>Rgba32</c> and <c>Bgra32</c> (channels reordered), <c>rgb8</c>, <c>rgba16le</c>, <c>gray8</c> or <c>gray16le</c>.</returns>
    public static RawPixelLayout GetLayout(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 => RawPixelLayout.Rgba8,
        PixelFormat.Rgb24 => RawPixelLayout.Rgb8,
        PixelFormat.Rgba64 => RawPixelLayout.Rgba16Le,
        PixelFormat.Gray8 => RawPixelLayout.Gray8,
        PixelFormat.Gray16 => RawPixelLayout.Gray16Le,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format."),
    };

    /// <summary>Captures an image (frames, poster, timing, animation settings, orientation) through the untyped row API.</summary>
    /// <param name="image">The image.</param>
    /// <param name="includePixelFormat">Whether the snapshot carries the pixel format name (compare it only for default working formats).</param>
    /// <param name="includeMetadata">Whether the snapshot carries the metadata (resolution, profiles, text).</param>
    /// <returns>The snapshot.</returns>
    public static DecodedImageSnapshot Capture(Image image, bool includePixelFormat = true, bool includeMetadata = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CreateSnapshot(image, CaptureFrame, includePixelFormat, includeMetadata);
    }

    /// <summary>Captures an image through the typed row API (components read from the pixel fields).</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image.</param>
    /// <param name="includePixelFormat">Whether the snapshot carries the pixel format name.</param>
    /// <param name="includeMetadata">Whether the snapshot carries the metadata.</param>
    /// <returns>The snapshot.</returns>
    public static DecodedImageSnapshot CaptureTyped<TPixel>(Image<TPixel> image, bool includePixelFormat = true, bool includeMetadata = false)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(image);
        return CreateSnapshot(image, frame => CaptureFrameRows((ImageFrame<TPixel>)frame), includePixelFormat, includeMetadata);
    }

    /// <summary>Copies the visible rows of a frame through <see cref="ImageFrame.ProcessPixelBytes(PixelBytesAction)"/>.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The pixels in the layout of <see cref="GetLayout"/>.</returns>
    public static RawPixelBuffer CaptureFrame(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var builder = new RawPixelBufferBuilder(frame.Width, frame.Height, GetLayout(frame.PixelFormat));
        frame.ProcessPixelBytes(builder, static (pixels, builder) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                SetNativeRow(builder, y, pixels.PixelFormat, pixels.GetRowSpan(y));
            }
        });

        return builder.Build();
    }

    /// <summary>Copies the visible rows of a frame through <see cref="ImageFrame{TPixel}.ProcessPixelRows(PixelRowsAction{TPixel})"/>, reading each component from the pixel fields.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="frame">The frame.</param>
    /// <returns>The pixels in the layout of <see cref="GetLayout"/>.</returns>
    public static RawPixelBuffer CaptureFrameRows<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(frame);
        var builder = new RawPixelBufferBuilder(frame.Width, frame.Height, GetLayout(frame.PixelFormat));
        frame.ProcessPixelRows(builder, static (pixels, builder) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    SetPixel(builder, x, y, row[x]);
                }
            }
        });

        return builder.Build();
    }

    /// <summary>Copies a frame with <see cref="ImageFrame.CopyPixelBytesTo(Span{byte}, int)"/> into a strided buffer and extracts the visible rows.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="strideInBytes">The destination stride (0 = tightly packed).</param>
    /// <param name="padding">The value the destination is filled with first; every padding byte must still have it afterward.</param>
    /// <returns>The pixels in the layout of <see cref="GetLayout"/>.</returns>
    /// <exception cref="InvalidOperationException">The copy wrote outside the visible bytes of a row.</exception>
    public static RawPixelBuffer CaptureFrameCopy(ImageFrame frame, int strideInBytes = 0, byte padding = 0xA5)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var rowBytes = frame.Width * PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var stride = strideInBytes == 0 ? rowBytes : strideInBytes;
        var buffer = new byte[(stride * (frame.Height - 1)) + rowBytes + 7]; // trailing bytes must stay untouched too
        buffer.AsSpan().Fill(padding);
        frame.CopyPixelBytesTo(buffer, strideInBytes);
        EnsurePaddingUntouched(buffer, stride, rowBytes, frame.Height, padding);

        var builder = new RawPixelBufferBuilder(frame.Width, frame.Height, GetLayout(frame.PixelFormat));
        for (var y = 0; y < frame.Height; y++)
        {
            SetNativeRow(builder, y, frame.PixelFormat, buffer.AsSpan(y * stride, rowBytes));
        }

        return builder.Build();
    }

    /// <summary>Copies a frame with <see cref="ImageFrame{TPixel}.CopyPixelDataTo(Span{TPixel}, int)"/> into a strided buffer and extracts the visible rows.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="frame">The frame.</param>
    /// <param name="strideInPixels">The destination stride (0 = tightly packed).</param>
    /// <param name="padding">The byte value the destination is filled with first; every padding pixel must still have it afterward.</param>
    /// <returns>The pixels in the layout of <see cref="GetLayout"/>.</returns>
    /// <exception cref="InvalidOperationException">The copy wrote outside the visible pixels of a row.</exception>
    public static RawPixelBuffer CaptureFrameDataCopy<TPixel>(ImageFrame<TPixel> frame, int strideInPixels = 0, byte padding = 0xA5)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stride = strideInPixels == 0 ? frame.Width : strideInPixels;
        var buffer = new TPixel[(stride * (frame.Height - 1)) + frame.Width + 3];
        unsafe { MemoryMarshal.AsBytes(buffer.AsSpan()).Fill(padding); }
        frame.CopyPixelDataTo(buffer, strideInPixels);
        var size = Unsafe.SizeOf<TPixel>();
        EnsurePaddingUntouched(unsafe(MemoryMarshal.AsBytes(buffer.AsSpan())), stride * size, frame.Width * size, frame.Height, padding);

        var builder = new RawPixelBufferBuilder(frame.Width, frame.Height, GetLayout(frame.PixelFormat));
        for (var y = 0; y < frame.Height; y++)
        {
            var row = buffer.AsSpan(y * stride, frame.Width);
            for (var x = 0; x < row.Length; x++)
            {
                SetPixel(builder, x, y, row[x]);
            }
        }

        return builder.Build();
    }

    /// <summary>Captures the metadata of an image for <see cref="GoldenAssert.ImageMatches"/>.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <returns>The snapshot.</returns>
    public static DecodedMetadataSnapshot CaptureMetadata(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new DecodedMetadataSnapshot
        {
            HorizontalDpi = metadata.Resolution?.HorizontalDpi,
            VerticalDpi = metadata.Resolution?.VerticalDpi,
            TransferFunction = metadata.TransferFunction == ColorTransferFunction.Linear ? "linear" : "srgb",
            IccProfile = metadata.IccProfile?.Data.Memory,
            ExifProfile = metadata.ExifProfile?.Data.Memory,
            XmpProfile = metadata.XmpProfile?.Data.Memory,
            TextEntries = [.. metadata.TextEntries.Select(entry => new DecodedTextEntry(entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword))],
        };
    }

    /// <summary>Converts an exact library duration to the harness representation.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The same rational value.</returns>
    public static RationalDuration ToRational(FrameDuration duration) => RationalDuration.Create(duration.Numerator, duration.Denominator);

    private static DecodedImageSnapshot CreateSnapshot(Image image, Func<ImageFrame, RawPixelBuffer> capture, bool includePixelFormat, bool includeMetadata)
    {
        var frames = new List<DecodedFrameSnapshot>(image.Frames.Count);
        foreach (var frame in image.Frames)
        {
            frames.Add(new DecodedFrameSnapshot(capture(frame), ToRational(frame.Metadata.Duration)));
        }

        return new DecodedImageSnapshot
        {
            Frames = frames,
            Poster = image.PosterFrame is { } poster ? capture(poster) : null,
            HasAnimation = image.Animation is not null,
            TotalPlays = image.Animation?.TotalPlays,
            Orientation = (int)image.Metadata.Orientation,
            PixelFormat = includePixelFormat ? image.PixelFormat.ToString() : null,
            Metadata = includeMetadata ? CaptureMetadata(image.Metadata) : null,
        };
    }

    private static void SetNativeRow(RawPixelBufferBuilder builder, int y, PixelFormat format, ReadOnlySpan<byte> row)
    {
        switch (format)
        {
            case PixelFormat.Bgra32:
                var destination = builder.GetRowSpan(y);
                if (row.Length != destination.Length)
                    throw new ArgumentException($"Row {y.ToString(CultureInfo.InvariantCulture)} has {row.Length.ToString(CultureInfo.InvariantCulture)} bytes; {destination.Length.ToString(CultureInfo.InvariantCulture)} are required.", nameof(row));

                for (var i = 0; i < row.Length; i += 4)
                {
                    destination[i] = row[i + 2];
                    destination[i + 1] = row[i + 1];
                    destination[i + 2] = row[i];
                    destination[i + 3] = row[i + 3];
                }

                break;

            case PixelFormat.Rgba64 or PixelFormat.Gray16:
                builder.SetRow16(y, unsafe(MemoryMarshal.Cast<byte, ushort>(row)));
                break;

            default:
                builder.SetRow(y, row);
                break;
        }
    }

    private static void SetPixel<TPixel>(RawPixelBufferBuilder builder, int x, int y, TPixel pixel)
        where TPixel : unmanaged
    {
        switch (pixel)
        {
            case Rgba32 p:
                SetSamples(builder, x, y, p.R, p.G, p.B, p.A);
                break;
            case Bgra32 p:
                SetSamples(builder, x, y, p.R, p.G, p.B, p.A);
                break;
            case Rgb24 p:
                SetSamples(builder, x, y, p.R, p.G, p.B);
                break;
            case Rgba64 p:
                SetSamples(builder, x, y, p.R, p.G, p.B, p.A);
                break;
            case Gray8 p:
                builder.SetSample(x, y, 0, p.Value);
                break;
            case Gray16 p:
                builder.SetSample(x, y, 0, p.Value);
                break;
            default:
                throw new NotSupportedException($"Unsupported pixel type {typeof(TPixel)}.");
        }
    }

    private static void SetSamples(RawPixelBufferBuilder builder, int x, int y, params ReadOnlySpan<int> samples)
    {
        for (var channel = 0; channel < samples.Length; channel++)
        {
            builder.SetSample(x, y, channel, samples[channel]);
        }
    }

    private static void EnsurePaddingUntouched(ReadOnlySpan<byte> buffer, int stride, int rowBytes, int height, byte padding)
    {
        for (var offset = 0; offset < buffer.Length; offset++)
        {
            var row = offset / stride;
            var isVisible = row < height && offset - (row * stride) < rowBytes;
            if (!isVisible && buffer[offset] != padding)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The copy modified byte {offset} outside the visible rows (stride {stride}, row length {rowBytes}): expected the padding value 0x{padding:X2}, actual 0x{buffer[offset]:X2}."));
        }
    }
}
