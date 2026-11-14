using System.Runtime.InteropServices;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The processing operations of <c>Meziantou.Framework.Imaging</c> that read pixels or points they do not control: custom
/// processors, seam carving, inpainting, line and corner detection, skew estimation and homography estimation. The inputs
/// are small images whose pixel format, size, frames and bytes come from the fuzzer, followed by the options of the
/// operation.
/// </summary>
public static partial class ImagingFuzzTargets
{
    /// <summary>The bytes that select the formats, the sizes and the options, and the extreme samples.</summary>
    private static readonly string[] Tokens =
    [
        "\0", "\u0001", "\u0002", "\u0003", "\u0004", "\u0007", "\u0008", "\u000F", "\u0010", "\u001F", "\u007F",
        "\0\0\0\0", "\u007F\u007F\u007F\u007F", "\0\u007F\0\u007F",
    ];

    /// <summary>The pixel formats of the images of the inputs.</summary>
    private static readonly PixelFormat[] Formats =
    [
        PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.Rgba64, PixelFormat.La16,
        PixelFormat.RgbaFloat, PixelFormat.RgbaHalf, PixelFormat.Bgr565, PixelFormat.A8, PixelFormat.Bgra32, PixelFormat.Rgb48,
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("imaging-processor", ProcessorTarget) { Seeds = GetProcessorSeeds, Dictionary = Tokens },
        new("imaging-seam-carving", SeamCarvingTarget) { Seeds = GetSeamCarvingSeeds, Dictionary = Tokens },
        new("imaging-inpainting", InpaintingTarget) { Seeds = GetInpaintingSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Reads an image from the input: a byte of format, a byte of width and one of height (1 to <paramref name="maxSide"/>),
    /// a byte of frame count (1 to 3, and a poster frame when its high bit is set), then the bytes of the pixels, repeated
    /// when the input is shorter.
    /// </summary>
    private static Image CreateImage(ref ReadOnlySpan<byte> data, int maxSide, ReadOnlySpan<byte> pixels = default)
    {
        var format = Formats[FuzzInput.ReadByte(ref data) % Formats.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % maxSide);
        var height = 1 + (FuzzInput.ReadByte(ref data) % maxSide);
        var frameByte = FuzzInput.ReadByte(ref data);
        var frames = 1 + (frameByte & 0x7F) % 3;
        var source = pixels.IsEmpty ? data : pixels;
        Image image = format switch
        {
            PixelFormat.Rgba32 => Create<Rgba32>(source, width, height),
            PixelFormat.Rgb24 => Create<Rgb24>(source, width, height),
            PixelFormat.Gray8 => Create<Gray8>(source, width, height),
            PixelFormat.Gray16 => Create<Gray16>(source, width, height),
            PixelFormat.Rgba64 => Create<Rgba64>(source, width, height),
            PixelFormat.La16 => Create<La16>(source, width, height),
            PixelFormat.RgbaFloat => Create<RgbaFloat>(source, width, height),
            PixelFormat.RgbaHalf => Create<RgbaHalf>(source, width, height),
            PixelFormat.Bgr565 => Create<Bgr565>(source, width, height),
            PixelFormat.A8 => Create<A8>(source, width, height),
            PixelFormat.Bgra32 => Create<Bgra32>(source, width, height),
            PixelFormat.Rgb48 => Create<Rgb48>(source, width, height),
            _ => throw new InvalidOperationException("The format is not listed."),
        };

        try
        {
            for (var i = 1; i < frames; i++)
            {
                // The next frames are the first one with its rows rotated, so that they differ
                var frame = image.InsertFrame(i, image.Frames[0]);
                RotateRows(frame, i);
            }

            if ((frameByte & 0x80) != 0)
            {
                RotateRows(image.SetPosterFrame(image.Frames[0]), frames);
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    private static Image<TPixel> Create<TPixel>(ReadOnlySpan<byte> source, int width, int height)
        where TPixel : unmanaged
    {
        var length = width * height * Marshal.SizeOf<TPixel>();
        var bytes = new byte[length];
        for (var i = 0; i < length && !source.IsEmpty; i++)
        {
            bytes[i] = source[i % source.Length];
        }

        return Image.ImportPixelBytes<TPixel>(bytes, width, height);
    }

    private static void RotateRows(ImageFrame frame, int shift)
    {
        var rowLength = frame.Width * PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var bytes = new byte[rowLength * frame.Height];
        frame.CopyPixelBytesTo(bytes);
        var rotated = new byte[bytes.Length];
        for (var y = 0; y < frame.Height; y++)
        {
            bytes.AsSpan(((y + shift) % frame.Height) * rowLength, rowLength).CopyTo(rotated.AsSpan(y * rowLength));
        }

        frame.ProcessPixelBytes(rotated, static (pixels, rotated) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                rotated.AsSpan(y * pixels.RowLength, pixels.RowLength).CopyTo(pixels.GetRowSpan(y));
            }
        });
    }

    /// <summary>Gets the size, the frame count and the bytes of every frame and of the poster: what a failed transactional operation must leave unchanged.</summary>
    private static string Snapshot(Image image)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(image.Width).Append('x').Append(image.Height).Append(' ').Append(image.Frames.Count).Append(image.PosterFrame is null ? "" : "+poster");
        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        foreach (var frame in frames)
        {
            var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
            frame.CopyPixelBytesTo(bytes);
            builder.Append(' ').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            builder.Append(' ').Append(frame.Metadata.Hotspot?.ToString() ?? "-");
        }

        return builder.ToString();
    }
}
