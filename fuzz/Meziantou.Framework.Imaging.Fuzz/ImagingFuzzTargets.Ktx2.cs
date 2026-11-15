using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A KTX 2.0 file must only throw the exceptions the library documents for invalid input. An identification (header and
/// full scan) and a load must agree (the same size, pixel format and frames, the same rejections); the collection must
/// have the number of entries the identification reports, its first entry must decode to the pixels of the load (unless
/// the load is an animation), and every entry must decode to the size and the pixel format its description gives, or be
/// rejected with a documented exception.
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The identifier, the reserved keys of the key/value data and their values, the supercompression schemes, the magic
    // numbers of Zstandard and ZLIB, and the bytes of the formats and color models. A property: All reads it, and the
    // initializers of the files of a partial class run in no defined order
    private static string[] Ktx2Tokens =>
    [
        "«KTX 20»\r\n\u001A\n", "KTXorientation\0", "KTXswizzle\0", "KTXwriter\0", "KTXwriterScParams\0", "KTXanimData\0",
        "KTXcubemapIncomplete\0", "KTXastcDecodeMode\0", "rd\0", "ru\0", "rdi\0", "rrr1\0", "rrrg\0", "000r\0", "unorm8\0",
        "\u0000\u0000\u0000\u0000", "\u0001\u0000\u0000\u0000", "\u0002\u0000\u0000\u0000", "\u0003\u0000\u0000\u0000", "\u0006\u0000\u0000\u0000",
        "(µ/ý", "x\u009C", "%\u0000\u0000\u0000", "+\u0000\u0000\u0000", "\u0093\u0000\u0000\u0000", "\u009D\u0000\u0000\u0000",
        "\u0083\u0000\u0000\u0000", "\u0087\u0000\u0000\u0000", "\u008B\u0000\u0000\u0000", "\u008F\u0000\u0000\u0000", "\u0091\u0000\u0000\u0000",
        "\u0080", "\u0082", "\u0083", "\u0085", "\u0086", "¡", "¢", "£", "¦", "ü\u0001", "\u0017", "ÿÿÿÿ",
    ];

    // Small textures: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration Ktx2Configuration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 18, MaxTotalPixels = 1 << 20, MaxFrames = 64, MaxMetadataBytes = 1 << 18 },
    };

    private static void Ktx2Target(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var header = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = Ktx2Configuration }));
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = Ktx2Configuration, Mode = ImageIdentifyMode.FullScan }));
        Image? image = null;
        var loaded = Catch(() => image = Image.Load(input, new ImageDecodeOptions { Configuration = Ktx2Configuration }));
        try
        {
            if (header.Info is not { Format: ImageFormat.Ktx2 } info)
            {
                FuzzAssert.True(image is null || image.Metadata.SourceFormat != ImageFormat.Ktx2, "A load succeeds where the identification fails: " + header.Exception?.Message);
                return;
            }

            FuzzAssert.True(identified.Info is not null || identified.Exception is ImageResourceLimitException, "A full scan fails where the header identification succeeds: " + identified.Exception?.Message);
            if (image is null)
            {
                // The texels are only read by a load: a block or a supercompressed level may be malformed
                FuzzAssert.True(loaded.Exception is InvalidImageContentException or ImageResourceLimitException, "A load fails with an unexpected exception: " + loaded.Exception);
                return;
            }

            FuzzAssert.Equal(info.Size, image.Size, "The identified size");
            FuzzAssert.Equal(info.PixelFormat, image.PixelFormat, "The identified pixel format");
            FuzzAssert.Equal(info.FrameCount, image.Frames.Count, "The identified frame count");

            // The same file read from a stream
            using (var fromStream = Image.Load(new MemoryStream(input), new ImageDecodeOptions { Configuration = Ktx2Configuration }))
            {
                FuzzAssert.True(GetPixelBytes(fromStream).AsSpan().SequenceEqual(GetPixelBytes(image)), "A stream gives other pixels than a span");
            }

            ImageCollection collection;
            try
            {
                collection = ImageCollection.Load(input, Ktx2Configuration);
            }
            catch (ImageResourceLimitException)
            {
                return;
            }

            using (collection)
            {
                FuzzAssert.Equal(ImageCollectionKind.TextureSurfaces, collection.Kind, "The kind of the collection");
                FuzzAssert.Equal(info.CollectionEntryCount, collection.Count, "The identified number of entries");
                for (var i = 0; i < collection.Count && i < 16; i++)
                {
                    var entry = collection[i];
                    Image? decoded = null;
                    var result = Catch(() => decoded = entry.Decode());
                    using (decoded)
                    {
                        if (decoded is null)
                        {
                            FuzzAssert.True(result.Exception is InvalidImageContentException or ImageResourceLimitException, "An entry fails with an unexpected exception: " + result.Exception);
                            continue;
                        }

                        FuzzAssert.Equal(entry.Size, decoded.Size, "The size of an entry");
                        FuzzAssert.Equal(entry.PixelFormat, decoded.PixelFormat, "The pixel format of an entry");
                        if (i == 0 && !image.IsAnimated)
                        {
                            FuzzAssert.True(GetPixelBytes(decoded).AsSpan().SequenceEqual(GetPixelBytes(image)), "The first entry is not the image of a load");
                        }
                    }
                }
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static byte[] GetPixelBytes(Image image)
    {
        var length = image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat);
        var bytes = new byte[length * image.Frames.Count];
        for (var i = 0; i < image.Frames.Count; i++)
        {
            image.Frames[i].CopyPixelBytesTo(bytes.AsSpan(i * length, length));
        }

        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetKtx2Seeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/ktx2/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/ktx2/".Length..^".ktx2".Length], buffer.ToArray());
        }
    }
}
