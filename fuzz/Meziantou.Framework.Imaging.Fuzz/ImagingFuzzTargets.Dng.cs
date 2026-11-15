using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A DNG file must only throw the exceptions the library documents for invalid input. An identification and a load must
/// agree (the same size, a load fails only where a full scan fails, or in the samples), the rendering is finite and opaque,
/// the raw image API reads the file a load reads and gives linear values in [0, 1], a lenient load renders what a strict
/// load renders, and a region holds the pixels of the full rendering.
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The signatures, the tags of DNG and TIFF/EP in both byte orders, the field types and the opcode IDs. A property: All,
    // in another file of the class, reads it, and the initializers of the files of a partial class run in no defined order
    private static string[] DngTokens =>
    [
        "II*\0", "MM\0*", "\x08\0\0\0", "\x12\xc6", "\xc6\x12", "\x01\x04\0\0", "\x01\x07\x01\0", "\x2d\x83", "\x2e\x83", "\x23\x80", "\xcc\x88",
        "\x16\xc6", "\x17\xc6", "\x18\xc6", "\x19\xc6", "\x1a\xc6", "\x1b\xc6", "\x1c\xc6", "\x1d\xc6", "\x1f\xc6", "\x20\xc6", "\x21\xc6", "\x22\xc6",
        "\x28\xc6", "\x29\xc6", "\x2a\xc6", "\x5a\xc6", "\x5b\xc6", "\x8d\xc6", "\x14\xc7", "\x15\xc7", "\x40\xc7", "\x41\xc7", "\x4e\xc7", "\x1f\xc7",
        "\x43\xcd", "\x4a\x01", "\x01\0", "\x03\0", "\x04\0", "\x05\0", "\x07\0", "\x0a\0", "\x0b\0", "\x0c\0", "\x08\0", "\x42\xcd",
        "\x4c\x88", "\x4d\x88", "\x4e\x88", "\x4f\x88", "\0\0\0\x01", "\0\0\0\x09", "\0\0\0\x0c", "\x01\x03\0\0", "\0\0\0\0", "\xff\xff\xff\xff",
    ];

    // Small images: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration DngConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 18, MaxMetadataBytes = 1 << 20, MaxLiveAllocationBytes = 64 * 1024 * 1024 },
    };

    private static readonly ImageConfiguration LenientDngConfiguration = new()
    {
        Limits = DngConfiguration.Limits,
        DecodingStrictness = DecodingStrictness.Lenient,
    };

    private static void DngTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = DngConfiguration, Mode = ImageIdentifyMode.FullScan }));
        Image<RgbaFloat>? image = null;
        var loaded = Catch(() => image = Image.Load<RgbaFloat>(input, new ImageDecodeOptions { Configuration = DngConfiguration }));
        try
        {
            if (identified.Info is not { Format: ImageFormat.Dng } info)
            {
                FuzzAssert.True(image is null || image.Metadata.DngProperties is null, "A load renders a DNG file a full scan does not identify as one");
                return;
            }

            if (image is null)
            {
                // What the identification accepts fails in the samples or the allocations of the rendering only
                FuzzAssert.True(loaded.Exception is InvalidImageContentException or ImageResourceLimitException or UnsupportedImageFeatureException, "A load fails where a full scan succeeds: " + loaded.Exception?.Message);
                return;
            }

            FuzzAssert.Equal(info.Size, image.Size, "The identified size");
            var pixels = GetBytes(image);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var value = BitConverter.ToSingle(pixels, i);
                FuzzAssert.True(float.IsFinite(value), "A rendered sample is not finite");
                if ((i / 4) % 4 == 3)
                {
                    FuzzAssert.True(value == 1, "A rendered pixel is not opaque");
                }
            }

            using (var raw = DngRawImage.Load(input, DngConfiguration))
            {
                var row = new float[raw.ActiveArea.Width * raw.SamplesPerPixel];
                for (var y = 0; y < raw.ActiveArea.Height; y++)
                {
                    raw.CopyLinearRow(y, row);
                    foreach (var value in row)
                    {
                        FuzzAssert.True(value is >= 0 and <= 1, "A linear value is outside [0, 1]");
                    }
                }

                FuzzAssert.Equal(info.Size, raw.DefaultCrop.Size, "The default crop is the size of the rendering");
            }

            using (var lenient = Image.Load<RgbaFloat>(input, new ImageDecodeOptions { Configuration = LenientDngConfiguration }))
            {
                FuzzAssert.True(GetBytes(lenient).AsSpan().SequenceEqual(pixels), "A lenient load renders other pixels than a strict one");
            }

            if (image.Width > 2 && image.Height > 2)
            {
                var area = new Rectangle(1, 1, image.Width - 2, image.Height - 1);
                using var region = Image.Load<RgbaFloat>(input, new ImageDecodeOptions { Configuration = DngConfiguration, Region = area });
                var regionPixels = GetBytes(region);
                for (var y = 0; y < area.Height; y++)
                {
                    FuzzAssert.True(regionPixels.AsSpan(y * area.Width * 16, area.Width * 16).SequenceEqual(pixels.AsSpan((((area.Y + y) * image.Width) + area.X) * 16, area.Width * 16)), "A region holds other pixels than the full rendering");
                }
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static IEnumerable<FuzzSeed> GetDngSeeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/dng/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/dng/".Length..^".dng".Length], buffer.ToArray());
        }
    }
}
