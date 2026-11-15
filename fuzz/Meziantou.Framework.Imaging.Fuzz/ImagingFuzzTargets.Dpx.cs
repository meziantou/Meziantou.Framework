using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A DPX or Cineon file must only throw the exceptions the library documents for invalid input. An identification and a
/// load must agree (the same size and pixel format, the same rejections, but for the 64-bit components single precision
/// does not hold, which only the pixels reveal), a sequential reader must give the pixels of the load, and the decoded
/// pixels must encode without error to a DPX file of the default depths that the decoder reads back exactly, and that
/// encodes again to the same bytes.
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The magic numbers and versions, the codes of the descriptors, bit depths, packings and encodings of the image element
    // structure, and the packing codes of Cineon. A property: All, in another file of the class, reads it
    private static string[] DpxTokens =>
    [
        "SDPX", "XPDS", "\x80\x2a\x5f\xd7", "\xd7\x5f\x2a\x80", "V1.0", "V2.0", "V4.5", "\xff\xff\xff\xff", "\0\0\0\0",
        "\x01", "\x02", "\x03", "\x04", "\x06", "\x32", "\x33", "\x34", "\x64", "\x65", "\x66", "\x67",
        "\x08", "\x0a", "\x0c", "\x10", "\x20", "\x40", "\0\x01", "\0\x02", "\x05", "\x85", "\x06",
        "\0\0\x06\x80", "\0\0\x08\0", "\0\0\x20\0",
    ];

    // Small images: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration DpxConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 20, MaxMetadataBytes = 1 << 20 },
    };

    private static void DpxTarget(ReadOnlySpan<byte> data) => FilmScanTarget(data, ImageFormat.Dpx);

    private static void CineonTarget(ReadOnlySpan<byte> data) => FilmScanTarget(data, ImageFormat.Cineon);

    private static void FilmScanTarget(ReadOnlySpan<byte> data, ImageFormat format)
    {
        var input = data.ToArray();
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = DpxConfiguration, Mode = ImageIdentifyMode.FullScan }));
        Image? image = null;
        var loaded = Catch(() => image = Image.Load(input, new ImageDecodeOptions { Configuration = DpxConfiguration }));
        try
        {
            if (identified.Info is not { } info || info.Format != format)
                return;

            FuzzAssert.True(identified.Exception is null, "The full scan failed: " + identified.Exception?.Message);
            if (image is null)
            {
                FuzzAssert.True(loaded.Exception is UnsupportedImageFeatureException { Feature: "64-bit floating-point components that single precision does not hold" }, "A load fails where a full scan succeeds: " + loaded.Exception?.Message);
                return;
            }

            FuzzAssert.Equal(info.Size, image.Size, "The identified size");
            FuzzAssert.Equal(info.PixelFormat, image.PixelFormat, "The identified pixel format");

            var isFloat = PixelFormats.IsFloatingPoint(image.PixelFormat);
            using (var reader = isFloat
                ? (IDisposable)Image.OpenReader<RgbaFloat>(new MemoryStream(input), new ImageReaderOptions { Configuration = DpxConfiguration })
                : Image.OpenReader<Rgba64>(new MemoryStream(input), new ImageReaderOptions { Configuration = DpxConfiguration }))
            {
                using var frame = reader switch
                {
                    ImageReader<RgbaFloat> floats => (Image?)floats.ReadFrame(),
                    ImageReader<Rgba64> integers => integers.ReadFrame(),
                    _ => null,
                };
                FuzzAssert.True(frame is not null && GetPixels(frame, isFloat).AsSpan().SequenceEqual(GetPixels(image, isFloat)), "The reader gives other pixels than the load");
            }

            // The default depths of the encoder hold every decoded pixel format without loss
            var encoder = new DpxEncoder { MetadataHandling = MetadataHandling.Strip };
            var first = Save(image, encoder);
            using var decoded = Image.Load(first);
            FuzzAssert.Equal(image.Size, decoded.Size, "The size of the encoded image");
            FuzzAssert.True(GetPixels(decoded, isFloat).AsSpan().SequenceEqual(GetPixels(image, isFloat)), "The encoded image decodes to other pixels");
            FuzzAssert.True(first.AsSpan().SequenceEqual(Save(decoded, encoder)), "A decoded image does not encode to the same bytes again");
        }
        finally
        {
            image?.Dispose();
        }
    }

    /// <summary>Gets the pixels of an image as 16-bit RGBA samples, or as floats for floating-point images.</summary>
    private static byte[] GetPixels(Image image, bool isFloat)
    {
        if (isFloat)
        {
            using var floats = image.CloneAs<RgbaFloat>();
            return GetBytes(floats);
        }

        using var integers = image.CloneAs<Rgba64>();
        var bytes = new byte[image.Width * image.Height * 8];
        integers.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetDpxSeeds() => GetEmbeddedSeeds("Seeds/dpx/");

    private static IEnumerable<FuzzSeed> GetCineonSeeds() => GetEmbeddedSeeds("Seeds/cineon/");

    private static IEnumerable<FuzzSeed> GetEmbeddedSeeds(string prefix)
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(Path.GetFileNameWithoutExtension(name[prefix.Length..]), buffer.ToArray());
        }
    }
}
