using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

// The sources of the HEIC suite and the comparison of pixels.
public sealed partial class HeicDecoderInteropTests
{
    public enum SourceKind
    {
        Rgb8,
        Rgba8,
        Rgb16,
        Rgba16,
        Gray8,
        Gray16,
    }

    /// <summary>
    /// Compares straight RGBA 16-bit buffers: alpha exactly, each color sample within <paramref name="maxColorError"/>.
    /// </summary>
    /// <returns>The largest and the mean absolute difference of the color samples.</returns>
    internal static (int Max, double Mean) AssertPixels(RawPixelBuffer expected, RawPixelBuffer actual, int maxColorError, string context)
    {
        Assert.Equal(RawPixelLayout.Rgba16Le, expected.Layout);
        Assert.Equal(RawPixelLayout.Rgba16Le, actual.Layout);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height), message: $"{context}: size.");
        var measured = MeasurePixels(expected, actual);
        if (measured.FirstAlphaMismatch is var (alphaX, alphaY))
            Assert.Fail($"{context}: alpha of pixel ({alphaX}, {alphaY}) is {actual.GetSample(alphaX, alphaY, 3)}, expected {expected.GetSample(alphaX, alphaY, 3)}.");

        if (measured.Max > maxColorError)
        {
            var (x, y, channel) = measured.Worst;
            Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{context}: the largest color difference is {measured.Max} ({measured.Max / 257.0:F3}/255), over the bound of {maxColorError} ({maxColorError / 257.0:F3}/255): pixel ({x}, {y}) channel {"RGB"[channel]} is {actual.GetSample(x, y, channel)}, expected {expected.GetSample(x, y, channel)} (16-bit scale); mean difference {measured.Mean:F2}."));
        }

        return (measured.Max, measured.Mean);
    }

    /// <summary>Measures the differences of two straight RGBA 16-bit buffers of the same size.</summary>
    /// <returns>The largest and the mean absolute difference of the color samples, the first sample with the largest difference, and the first pixel whose alpha differs.</returns>
    internal static (int Max, double Mean, (int X, int Y, int Channel) Worst, (int X, int Y)? FirstAlphaMismatch) MeasurePixels(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        var max = 0;
        long sum = 0;
        var worst = (0, 0, 0);
        (int X, int Y)? alphaMismatch = null;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                if (alphaMismatch is null && expected.GetSample(x, y, 3) != actual.GetSample(x, y, 3))
                {
                    alphaMismatch = (x, y);
                }

                for (var channel = 0; channel < 3; channel++)
                {
                    var error = Math.Abs(expected.GetSample(x, y, channel) - actual.GetSample(x, y, channel));
                    sum += error;
                    if (error > max)
                    {
                        max = error;
                        worst = (x, y, channel);
                    }
                }
            }
        }

        return (max, sum / (double)(expected.Width * expected.Height * 3), worst, alphaMismatch);
    }

    /// <summary>Widens a captured frame to straight RGBA, 16 bits per sample: 8-bit samples are multiplied by 257, gray is replicated, a missing alpha is opaque.</summary>
    internal static RawPixelBuffer ToRgba16(RawPixelBuffer source)
    {
        var layout = source.Layout;
        var result = new byte[RawPixelLayout.Rgba16Le.GetByteLength(source.Width, source.Height)];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var target = result.AsSpan(((y * source.Width) + x) * 8, 8);
                for (var channel = 0; channel < 4; channel++)
                {
                    int value;
                    if (channel == 3)
                    {
                        value = layout.HasAlpha ? Widen(source.GetSample(x, y, layout.AlphaChannel)) : ushort.MaxValue;
                    }
                    else
                    {
                        value = Widen(source.GetSample(x, y, layout.IsColor ? channel : 0));
                    }

                    BinaryPrimitives.WriteUInt16LittleEndian(target[(channel * 2)..], (ushort)value);
                }
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, result);

        int Widen(int sample) => layout.BytesPerSample == 1 ? sample * 257 : sample;
    }

    /// <summary>
    /// Reduces the 16-bit samples that the library gives to an image of <paramref name="bitDepth"/> bits to the 8 most
    /// significant bits of the samples of the image, multiplied by 257: the library scales a sample <c>v</c> to
    /// <c>round(v * 65535 / max)</c>, which <c>round(s * max / 65535)</c> reverses.
    /// </summary>
    internal static RawPixelBuffer ReduceToEightBits(RawPixelBuffer pixels, int bitDepth)
    {
        var maximum = (1 << bitDepth) - 1;
        var result = pixels.Span.ToArray();
        for (var i = 0; i < result.Length; i += 2)
        {
            var sample = ((BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(i)) * (long)maximum) + 32767) / 65535;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i), (ushort)((sample >> (bitDepth - 8)) * 257));
        }

        return RawPixelBuffer.Create(pixels.Width, pixels.Height, RawPixelLayout.Rgba16Le, result);
    }

    /// <summary>
    /// The pixels of a source that heif-enc stores without loss in samples of <paramref name="bitDepth"/> bits, as the library
    /// returns them: an 8-bit sample is multiplied by 257; heif-enc keeps the most significant bits of a 16-bit sample, and
    /// the library scales the sample <c>v</c> it decodes to <c>round(v * 65535 / max)</c>.
    /// </summary>
    private static RawPixelBuffer ToStoredPixels(RawPixelBuffer source, int bitDepth)
    {
        var widened = ToRgba16(source);
        if (source.Layout.BytesPerSample == 1)
        {
            Assert.Equal(8, bitDepth);
            return widened;
        }

        var maximum = (1 << bitDepth) - 1;
        var result = widened.Span.ToArray();
        for (var i = 0; i < result.Length; i += 2)
        {
            var stored = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(i)) >> (16 - bitDepth);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i), (ushort)(((stored * 65535L) + (maximum / 2)) / maximum));
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, result);
    }

    /// <summary>
    /// A deterministic image: smooth gradients (chroma that subsampling changes), seeded noise, saturated rectangles with
    /// sharp edges, and, with alpha, opaque, translucent and fully transparent areas whose colors are kept. A frame number
    /// moves the content, so that the frames of a sequence predict each other.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    internal static RawPixelBuffer CreateSource(SourceKind kind, int width, int height, int seed, int frame = 0)
    {
        var layout = kind switch
        {
            SourceKind.Rgb8 => RawPixelLayout.Rgb8,
            SourceKind.Rgba8 => RawPixelLayout.Rgba8,
            SourceKind.Gray8 => RawPixelLayout.Gray8,
            SourceKind.Gray16 => RawPixelLayout.Gray16Le,
            _ => RawPixelLayout.Rgba16Le,
        };

        var max = layout.MaxSampleValue;
        var random = new Random(seed);
        var bytes = new byte[layout.GetByteLength(width, height)];
        Span<int> pixel = stackalloc int[4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = (x + (3 * frame)) % Math.Max(width, 1);
                var v = (y + frame) % Math.Max(height, 1);
                pixel[0] = (int)((long)u * max / Math.Max(width - 1, 1));
                pixel[1] = (int)((long)v * max / Math.Max(height - 1, 1));
                pixel[2] = (int)((long)(u + v) * max / Math.Max(width + height - 2, 1));
                pixel[3] = max;
                if (u >= width / 4 && u < width / 2 && v >= height / 4 && v < (3 * height) / 4)
                {
                    // A saturated rectangle
                    (pixel[0], pixel[1], pixel[2]) = (max, v % 2 == 0 ? 0 : max, 0);
                }
                else if (u >= (5 * width) / 8 && v < height / 3)
                {
                    (pixel[0], pixel[1], pixel[2]) = (0, 0, max);
                }

                for (var channel = 0; channel < 3; channel++)
                {
                    pixel[channel] = Math.Clamp(pixel[channel] + random.Next(-max / 24, (max / 24) + 1), 0, max);
                }

                if (kind is SourceKind.Rgba8 or SourceKind.Rgba16)
                {
                    pixel[3] = ((u + (2 * v)) % 9) switch
                    {
                        0 => 0,
                        1 => max / 3,
                        2 => (int)((long)u * max / Math.Max(width - 1, 1)),
                        _ => max,
                    };
                }

                var target = bytes.AsSpan(((y * width) + x) * layout.BytesPerPixel);
                for (var channel = 0; channel < layout.ChannelCount; channel++)
                {
                    var value = pixel[channel];
                    if (layout.BytesPerSample == 1)
                    {
                        target[channel] = (byte)value;
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(target[(channel * 2)..], (ushort)value);
                    }
                }
            }
        }

        return RawPixelBuffer.Create(width, height, layout, bytes);
    }

    /// <summary>Writes a source as a PNG file with the library; heif-enc reads it with libpng.</summary>
    private static async Task<FullPath> SaveSourceAsync(string name, RawPixelBuffer pixels, FullPath? directory = null)
    {
        var format = pixels.Layout.Name switch
        {
            "rgb8" => PixelFormat.Rgb24,
            "rgba8" => PixelFormat.Rgba32,
            "gray8" => PixelFormat.Gray8,
            "gray16le" => PixelFormat.Gray16,
            _ => PixelFormat.Rgba64,
        };

        using var image = EncoderSources.CreateImage(pixels, format);
        var path = directory is { } folder ? folder / (name + ".png") : ArtifactPath(name + ".png");
        await image.SaveAsync(path, new PngEncoder(), XunitCancellationToken);
        return path;
    }

    /// <summary>
    /// Adds chunks to a PNG file, after its header: an ICC profile (<c>iCCP</c>), an EXIF block (<c>eXIf</c>) and an XMP
    /// packet (an <c>iTXt</c> chunk of keyword <c>XML:com.adobe.xmp</c>), which heif-enc copies to the file it writes.
    /// </summary>
    private static byte[] AddPngMetadata(ReadOnlySpan<byte> png, byte[]? icc, byte[]? exif, byte[]? xmp)
    {
        // The signature, then the IHDR chunk: length, type, 13 bytes, CRC
        const int HeaderEnd = 8 + 12 + 13;
        using var output = new MemoryStream();
        output.Write(png[..HeaderEnd]);
        if (icc is not null)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(icc);
            }

            // The name of the profile, a null separator, the compression method (0: zlib), the compressed profile
            WriteChunk(output, "iCCP", [.. "profile"u8, 0, 0, .. compressed.ToArray()]);
        }

        if (exif is not null)
        {
            WriteChunk(output, "eXIf", exif);
        }

        if (xmp is not null)
        {
            // The keyword, a null separator, no compression (flag and method), an empty language tag and translated keyword
            WriteChunk(output, "iTXt", [.. "XML:com.adobe.xmp"u8, 0, 0, 0, 0, 0, .. xmp]);
        }

        output.Write(png[HeaderEnd..]);
        return output.ToArray();

        static void WriteChunk(MemoryStream output, string type, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            output.Write(length);
            byte[] body = [.. Encoding.ASCII.GetBytes(type), .. data];
            output.Write(body);

            // CRC-32 of the type and of the data (PNG specification, section 5.5)
            var crc = 0xFFFFFFFFu;
            foreach (var value in body)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
            output.Write(length);
        }
    }

    private static FullPath ArtifactPath(string fileName) => InteropSettings.GetArtifactsDirectory(nameof(HeicDecoderInteropTests)) / fileName;

    /// <summary>A seed derived from the name of a case, identical on every run and platform.</summary>
    internal static int StableSeed(string name)
    {
        var hash = 17;
        foreach (var c in name)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash & 0x7FFFFFFF;
    }

    /// <summary>The pixel format the documentation gives to a file: 8-bit files load at 8 bits, deeper files at 16; gray only without alpha.</summary>
    internal static PixelFormat ExpectedPixelFormat(int bitDepth, bool isMonochrome, bool hasAlpha) => (bitDepth, isMonochrome, hasAlpha) switch
    {
        (8, true, false) => PixelFormat.Gray8,
        (8, false, false) => PixelFormat.Rgb24,
        (8, true, true) => PixelFormat.La16,
        (8, false, true) => PixelFormat.Rgba32,
        (_, true, false) => PixelFormat.Gray16,
        (_, false, false) => PixelFormat.Rgb48,
        (_, true, true) => PixelFormat.La32,
        _ => PixelFormat.Rgba64,
    };

    internal static void WriteOutput(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);

    /// <summary>A read-only stream that cannot seek and returns its content in small pieces.</summary>
    internal sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, 997), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
