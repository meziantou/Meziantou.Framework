using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (custom formats, custom operations, wrapped memory, buffer pool), run by DocumentationExampleTests.</summary>
internal static class ExtensibilityExamples
{
    /// <summary>Loads, edits and saves a format the library does not know through its usual entry points.</summary>
    #region Imaging_RegisterCustomCodec
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
    #endregion

    // The codec and the encoder of farbfeld (https://tools.suckless.org/farbfeld/)
    #region Imaging_CustomCodec
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
    #endregion

    /// <summary>A custom per-pixel filter that works on every pixel format.</summary>
    #region Imaging_RowOperation
    public static void Solarize(Image image)
    {
        // Rgba64 rows: the rows of any integer pixel format are converted to them without loss, and back after the call
        image.ApplyRowOperation<Rgba64, SolarizeFilter>(new SolarizeFilter(threshold: 32768));
    }

    /// <summary>Inverts the samples above a threshold. A struct: the processing loop is compiled for it.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct SolarizeFilter(ushort threshold) : IPixelRowOperation<Rgba64>
    {
        // Called by several threads at once when MaxDegreeOfParallelism > 1: it only reads its fields
        public void Invoke(int y, Span<Rgba64> row)
        {
            foreach (ref var pixel in row)
            {
                pixel = new Rgba64(Solarize(pixel.R), Solarize(pixel.G), Solarize(pixel.B), pixel.A);
            }
        }

        private ushort Solarize(ushort sample) => sample > threshold ? (ushort)(ushort.MaxValue - sample) : sample;
    }
    #endregion

    /// <summary>A custom filter that reads the neighbors of each pixel.</summary>
    #region Imaging_NeighborhoodOperation
    public static void Emboss(Image image)
    {
        // RgbaFloat rows hold the samples of every pixel format; results outside [0, 1] are clipped when they are stored
        image.ApplyNeighborhoodOperation<RgbaFloat, EmbossFilter>(default);
    }

    public readonly struct EmbossFilter : IPixelNeighborhoodOperation<RgbaFloat>
    {
        public void Invoke(int y, PixelRowSource<RgbaFloat> source, Span<RgbaFloat> destination)
        {
            // The source holds the pixels before the operation, even those of the rows already written
            var above = source.GetRowClamped(y - 1);
            var below = source.GetRowClamped(y + 1);
            for (var x = 0; x < destination.Length; x++)
            {
                var left = above[Math.Max(x - 1, 0)];
                var right = below[Math.Min(x + 1, destination.Length - 1)];
                destination[x] = new RgbaFloat(
                    0.5f + right.R - left.R,
                    0.5f + right.G - left.G,
                    0.5f + right.B - left.B,
                    source.GetRow(y)[x].A);
            }
        }
    }
    #endregion

    /// <summary>Processes a buffer owned by another component without copying it.</summary>
    #region Imaging_WrapMemory
    public static void BlurCameraFrame(byte[] bgraPixels, int width, int height, int stride)
    {
        // The image reads and writes the array: the blur changes the pixels of the caller in place
        using var image = Image.WrapMemory<Bgra32>(bgraPixels, width, height, stride);
        image.GaussianBlur(1.5);
    }
    #endregion

    /// <summary>Processes native memory and frees it when the image no longer uses it.</summary>
    #region Imaging_WrapNativeMemory
    public static void GrayscaleNativeBuffer(int width, int height)
    {
        nuint length = (nuint)(width * height * 4);
        unsafe
        {
            var pixels = NativeMemory.AllocZeroed(length);

            // The release callback frees the memory once the image no longer uses it: when it is disposed, or when an
            // operation that changes the size moves the frame to memory of the library
            using var image = Image.WrapMemory<Rgba32>(pixels, width, height, release: () => NativeMemory.Free(pixels));
            image.Grayscale();
        }
    }

    public static void ProcessRentedBuffer(int width, int height)
    {
        // The owner of pooled memory is disposed once the image no longer uses it
        var owner = MemoryPool<byte>.Shared.Rent(width * height * 3);
        using var image = Image.WrapMemory<Rgb24>(owner.Memory, width, height, release: owner.Dispose);
        image.Flip(FlipMode.Horizontal);
    }
    #endregion

    /// <summary>Limits the memory the library keeps between operations.</summary>
    #region Imaging_BufferPool
    public static void ConfigureForSmallContainer()
    {
        // At startup: keep at most 4 MiB of returned buffers for reuse (0 disables retention)
        ImageBufferPool.MaxRetainedBytes = 4 * 1024 * 1024;
    }
    #endregion
}
