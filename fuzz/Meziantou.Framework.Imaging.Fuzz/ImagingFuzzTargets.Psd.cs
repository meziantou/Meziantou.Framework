using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A PSD file must only throw the exceptions the library documents for invalid input. A header identification, a full
/// scan and a load must agree (the same size, pixel format, color model and transparency, the same rejections: a load also
/// rejects the files that only a load examines, a composite that is not there, ZIP data, CMYK and Lab samples without a
/// conversion), the pixels of an image without transparency must be opaque, a sequential reader must give the pixels of
/// the load, and the CMYK and Lab samples must load with their conversions.
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The signatures, the versions and color modes of the header, the identifiers of the image resources the decoder reads,
    // the keys of the tagged blocks of the layers, and the PackBits headers. A property: All, in another file of the class,
    // reads it, and the initializers of the files of a partial class run in no defined order
    private static string[] PsdTokens =>
    [
        "8BPS", "8BIM", "8B64", "MeSa", "\0\x01", "\0\x02", "\0\0\0\0\0\0", "\0\x03", "\0\x04", "\0\x05", "\0\x38",
        "\0\x01\0\0", "\0\x08", "\0\x10", "\0\x20", "\0\0", "\0\x03\0\0", "\0\x09", "\0\x07",
        "\x03\xed", "\x04\x0f", "\x04\x17", "\x04\x21", "\x04\x22", "\x04\x24", "\x04\x0c", "\x04\x04",
        "Layr", "Lr16", "Lr32", "LMsk", "\xff\xff", "\0\0\0\x02", "\0\0\x03\0", "\x7f", "\x80", "\x81", "\xff",
    ];

    private static readonly ImageConfiguration PsdConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 20, MaxMetadataBytes = 1 << 20 },
    };

    private static void PsdTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var header = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = PsdConfiguration }));
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = PsdConfiguration, Mode = ImageIdentifyMode.FullScan }));
        Image? image = null;
        var loaded = Catch(() => image = Image.Load(input, new ImageDecodeOptions { Configuration = PsdConfiguration }));
        try
        {
            if (identified.Info is not { Format: ImageFormat.Psd } info)
            {
                FuzzAssert.True(image is null, "A load succeeds where a full scan fails: " + identified.Exception?.Message);
                return;
            }

            FuzzAssert.True(header.Info is { } headerInfo && headerInfo.Size == info.Size && headerInfo.PixelFormat == info.PixelFormat && headerInfo.ColorModel == info.ColorModel && headerInfo.MayHaveTransparency == info.MayHaveTransparency,
                "A header identification describes another image than the full scan: " + header.Exception?.Message);
            if (image is null)
            {
                FuzzAssert.True(loaded.Exception is UnsupportedImageFeatureException { Feature: "No composite image" or "ZIP-compressed image data" or "CMYK samples without color conversion" or "Lab samples without color conversion" },
                    "A load fails where a full scan succeeds: " + loaded.Exception?.Message);
                if (info.ColorModel is ImageColorModel.Cmyk or ImageColorModel.CieLab)
                {
                    // The samples load with a conversion, unless the file has no composite or ZIP data
                    var converted = Catch(() => Image.Load(input, new ImageDecodeOptions { Configuration = PsdConfiguration, CmykConversion = CmykConversionOptions.Default, CieLabConversion = CieLabConversionOptions.Default }).Dispose());
                    FuzzAssert.True(converted.Exception is null or UnsupportedImageFeatureException { Feature: "No composite image" or "ZIP-compressed image data" or "CMYK samples without color profile" }, "A conversion fails: " + converted.Exception?.Message);
                }

                return;
            }

            FuzzAssert.Equal(info.Size, image.Size, "The identified size");
            FuzzAssert.Equal(info.PixelFormat, image.PixelFormat, "The identified pixel format");
            var pixels = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
            image.Frames[0].CopyPixelBytesTo(pixels);
            if (info.MayHaveTransparency == false && image.PixelFormat == PixelFormat.RgbaFloat)
            {
                for (var i = 12; i < pixels.Length; i += 16)
                {
                    FuzzAssert.True(BitConverter.ToSingle(pixels, i) == 1f, "An image without transparency has a transparent pixel");
                }
            }

            // Integer samples convert to floats exactly; a grayscale profile cannot label RGBA floats, the pixels are compared
            var conversion = new PixelConversionOptions { DiscardIncompatibleColorProfile = true };
            using var expected = image.CloneAs<RgbaFloat>(conversion);
            using var reader = Image.OpenReader<RgbaFloat>(new MemoryStream(input), new ImageReaderOptions { Configuration = PsdConfiguration, Conversion = conversion });
            using var frame = reader.ReadFrame();
            FuzzAssert.True(frame is not null && GetBytes(frame).AsSpan().SequenceEqual(GetBytes(expected)), "The reader gives other pixels than the load");
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static IEnumerable<FuzzSeed> GetPsdSeeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/psd/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/psd/".Length..^".psd".Length], buffer.ToArray());
        }
    }
}
