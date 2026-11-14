using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A Radiance HDR file must only throw the exceptions the library documents for invalid input. An identification and a
/// load must agree (the same size, the same rejections), a sequential reader must give the pixels of the load, and the
/// decoded pixels, which are always colors RGBE can store, must encode without error to a file that the decoder reads
/// back and the encoder writes again byte for byte (a decoded mantissa is the middle of its interval, which the encoder
/// truncates back into it).
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The tokens of the information header and of the resolution string, and the markers of the run-length encodings. A
    // property: All, in another file of the class, reads it, and the initializers of the files of a partial class run in
    // no defined order
    private static string[] RadianceTokens =>
    [
        "#?RADIANCE\n", "#?RGBE\n", "\n", "\n\n", "FORMAT=", "32-bit_rle_rgbe", "32-bit_rle_xyze", "EXPOSURE=", "COLORCORR=", "PRIMARIES=",
        "PIXASPECT=", "GAMMA=", "VIEW=", "SOFTWARE=", "-Y ", "+Y ", "-X ", "+X ", " ", "\t", "1", "8", "0.5", "1e-3", "inf", "nan", "-1",
        "0.640 0.330 0.290 0.600 0.150 0.060 0.333 0.333", "\x02\x02\x00\x08", "\x02\x02\x7f\xff", "\x01\x01\x01\x08", "\x80", "\x81", "\xff",
    ];

    // Small images: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration RadianceConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 20, MaxMetadataBytes = 1 << 20 },
    };

    private static void RadianceTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = RadianceConfiguration, Mode = ImageIdentifyMode.FullScan }));
        Image<RgbaFloat>? image = null;
        var loaded = Catch(() => image = Image.Load<RgbaFloat>(input, new ImageDecodeOptions { Configuration = RadianceConfiguration }));
        try
        {
            if (identified.Info is not { Format: ImageFormat.Radiance } info)
                return;

            // A load rejects what a full scan rejects; it rejects XYZE samples, which an identification reports
            if (image is null)
            {
                FuzzAssert.True(loaded.Exception is UnsupportedImageFeatureException { Feature: "XYZE samples" } && info.Metadata.RadianceHeader?.IsXyz == true, "A load fails where a full scan succeeds: " + loaded.Exception?.Message);
                return;
            }

            FuzzAssert.Equal(info.Size, image.Size, "The identified size");
            FuzzAssert.True(identified.Exception is null, "The full scan failed: " + identified.Exception?.Message);

            using (var reader = Image.OpenReader<RgbaFloat>(new MemoryStream(input), new ImageReaderOptions { Configuration = RadianceConfiguration }))
            using (var frame = reader.ReadFrame())
            {
                FuzzAssert.True(frame is not null && GetBytes(frame).AsSpan().SequenceEqual(GetBytes(image)), "The reader gives other pixels than the load");
            }

            foreach (var compression in Enum.GetValues<RadianceCompression>())
            {
                // A picture with other primaries is labeled with an ICC profile, which the format cannot store: the round trip is
                // about the pixels
                var encoder = new RadianceEncoder { Compression = compression, MetadataHandling = MetadataHandling.Strip };
                var first = Save(image, encoder);
                using var decoded = Image.Load<RgbaFloat>(first);
                FuzzAssert.Equal(image.Size, decoded.Size, "The size of the encoded image");
                var second = Save(decoded, encoder);
                FuzzAssert.True(first.AsSpan().SequenceEqual(second), "A decoded image does not encode to the same bytes again");
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static (ImageInfo? Info, Exception? Exception) Catch(Func<ImageInfo> action)
    {
        try
        {
            return (action(), null);
        }
        catch (Exception exception) when (IsDocumented(exception))
        {
            return (null, exception);
        }
    }

    private static (ImageInfo? Info, Exception? Exception) Catch(Action action)
    {
        try
        {
            action();
            return (null, null);
        }
        catch (Exception exception) when (IsDocumented(exception))
        {
            return (null, exception);
        }
    }

    private static bool IsDocumented(Exception exception) => exception is UnknownImageFormatException or InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException;

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * 16];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetRadianceSeeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/radiance/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/radiance/".Length..^".hdr".Length], buffer.ToArray());
        }
    }
}
