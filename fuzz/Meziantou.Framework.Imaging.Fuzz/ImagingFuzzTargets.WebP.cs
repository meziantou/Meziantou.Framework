using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The integer formats, whose 8-bit and 16-bit conversions the output is compared with.</summary>
    private static readonly PixelFormat[] WebPExactFormats =
    [
        PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.Rgba64, PixelFormat.La16,
        PixelFormat.Bgr565, PixelFormat.A8, PixelFormat.Bgra32, PixelFormat.Rgb48,
    ];

    /// <summary>
    /// The WebP encoder options of the bit-depth reduction (<see cref="WebPEncoder.DitherBitDepthReduction"/>), of the lossy
    /// color conversion (<see cref="WebPEncoder.SharpYuv"/>) and of the output held for non-seekable streams. The input is an
    /// option byte (bit 0: lossy, bit 1: sharp YUV, bit 2: dithering, bits 4-7: quality), a byte of effort, then an image
    /// (see <see cref="CreateImage"/>). The output is the same on a seekable and a non-seekable stream, and the same when
    /// encoded twice; it has the frames and the size of the image, and its alpha is the nearest 8-bit alpha. Lossless output
    /// is the nearest 8-bit image without dithering; with dithering, every visible color component is within one 8-bit step
    /// of its 16-bit value, gray stays gray, and fully transparent pixels keep their nearest colors.
    /// </summary>
    private static void WebPEncoderTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var effort = FuzzInput.ReadByte(ref data) % 10;
        var lossy = (options & 1) != 0;
        var encoder = new WebPEncoder
        {
            Compression = lossy ? WebPCompression.Lossy : WebPCompression.Lossless,
            SharpYuv = (options & 2) != 0,
            DitherBitDepthReduction = (options & 4) != 0,
            AllowBitDepthReduction = true,
            Quality = (options >> 4) * 100 / 15,
            Effort = effort,
            MetadataHandling = MetadataHandling.Strip,
            FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
        };

        using var image = CreateImage(ref data, maxSide: 24);
        if (image.PosterFrame is not null)
        {
            // WebP has no poster frame
            FuzzAssert.True(Catch(() => Save(image, encoder)).Exception is UnsupportedImageFeatureException or ArgumentException, "A poster frame is rejected");
            return;
        }

        byte[]? output = null;
        var result = Catch(() => output = Save(image, encoder));
        if (output is null)
        {
            // Floating-point samples that are not numbers have no 16-bit value: documented, and the same for every destination
            FuzzAssert.True(result.Exception is UnsupportedImageFeatureException, $"Unexpected {result.Exception?.GetType()}");
            using var stream = new NonSeekableStream();
            FuzzAssert.Equal(result.Exception?.GetType(), Catch(() => image.Save(stream, encoder)).Exception?.GetType(), "The non-seekable output fails differently");
            return;
        }

        var seekable = output;
        FuzzAssert.True(Save(image, encoder).AsSpan().SequenceEqual(seekable), "The encoding is not deterministic");
        using (var stream = new NonSeekableStream())
        {
            image.Save(stream, encoder);
            FuzzAssert.True(stream.Written.ToArray().AsSpan().SequenceEqual(seekable), "The output differs on a non-seekable stream");
        }

        using var decoded = Image.Load<Rgba32>(seekable);
        FuzzAssert.Equal(image.Frames.Count, decoded.Frames.Count, "The number of frames");
        FuzzAssert.Equal((image.Width, image.Height), (decoded.Width, decoded.Height), "The size");
        if (!WebPExactFormats.Contains(image.PixelFormat))
            return;

        using var nearest = image.CloneAs<Rgba32>();
        using var wide = image.CloneAs<Rgba64>();
        var gray = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.La16 or PixelFormat.A8;
        var dithered = encoder.DitherBitDepthReduction && PixelFormats.GetBitsPerComponent(image.PixelFormat) > 8;
        for (var i = 0; i < decoded.Frames.Count; i++)
        {
            var expected = GetRgbaBytes(nearest.Frames[i]);
            var actual = GetRgbaBytes(decoded.Frames[i]);
            var wideBytes = new byte[wide.Width * wide.Height * 8];
            wide.Frames[i].CopyPixelBytesTo(wideBytes);
            var samples = new ushort[wideBytes.Length / 2];
            for (var s = 0; s < samples.Length; s++)
            {
                samples[s] = BitConverter.ToUInt16(wideBytes, s * 2);
            }

            for (var p = 0; p < expected.Length; p += 4)
            {
                FuzzAssert.Equal(expected[p + 3], actual[p + 3], $"The alpha of pixel {p / 4} of frame {i}");
                if (gray && !lossy)
                {
                    FuzzAssert.True(actual[p] == actual[p + 1] && actual[p] == actual[p + 2], $"Pixel {p / 4} of frame {i} is not gray");
                }

                if (lossy)
                    continue;

                for (var c = 0; c < 3; c++)
                {
                    if (!dithered || expected[p + 3] == 0)
                    {
                        FuzzAssert.Equal(expected[p + c], actual[p + c], $"Component {c} of pixel {p / 4} of frame {i}");
                    }
                    else
                    {
                        var error = Math.Abs((actual[p + c] * 257) - samples[p + c]);
                        FuzzAssert.True(error <= 257, $"Component {c} of pixel {p / 4} of frame {i} is {actual[p + c]}, more than one step from {samples[p + c]}");
                    }
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetWebPEncoderSeeds()
    {
        // The cases of the unit tests: a 16-bit ramp dithered losslessly, 16-bit gray, colored stripes with sharp YUV,
        // an animation for the non-seekable output, and 8-bit pixels that dithering leaves unchanged
        yield return new FuzzSeed("dithered-ramp", (byte[])[0x04, 5, 4, 23, 7, 0, 0x30, 0x75, 0x34, 0x77, 0xCF, 0x8A, 0xFF, 0xFF, 0x38, 0x75, 0x3C, 0x77, 0xC7, 0x8A, 0xFF, 0xFF]);
        yield return new FuzzSeed("dithered-gray", (byte[])[0x04, 5, 3, 15, 3, 0, 0xE8, 0x03, 0x00, 0x00, 0xCD, 0x07, 0x41, 0x9C, 0xB2, 0x0B, 0x42, 0x9C]);
        yield return new FuzzSeed("sharp-yuv-stripes", (byte[])[0xE3, 5, 0, 20, 15, 0, 255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 0, 255]);
        yield return new FuzzSeed("lossy-dithered-sharp", (byte[])[0x77, 3, 4, 17, 11, 0, 0x00, 0x10, 0x00, 0x80, 0xFF, 0xFF, 0x7F, 0x7F, 0x12, 0x34, 0x56, 0x78]);
        yield return new FuzzSeed("animation", (byte[])[0x00, 4, 0, 6, 5, 2, 10, 20, 30, 255, 40, 50, 60, 0, 70, 80, 90, 128]);
        yield return new FuzzSeed("eight-bit-dithering", (byte[])[0x04, 5, 0, 12, 9, 0, 1, 2, 3, 4, 250, 128, 64, 32]);
    }

    /// <summary>A write-only stream that cannot seek, like a network stream.</summary>
    private sealed class NonSeekableStream : Stream
    {
        public MemoryStream Written { get; } = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => Written.Write(buffer);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Written.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
