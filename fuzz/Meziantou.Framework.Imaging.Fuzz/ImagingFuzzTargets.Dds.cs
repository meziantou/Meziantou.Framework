using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// A DDS file must only throw the exceptions the library documents for invalid input. An identification and a load must
/// agree (the same size, pixel format and rejections), and a collection that loads must list the surfaces the identification counts, its first entry decoding to the pixels of the
/// load. The block target decodes one 4×4 block of every block-compressed format from the bytes of the fuzzer: the values
/// of each format are in its range (unsigned normalized in [0, 1], signed in [-1, 1], unsigned BC6H halves in [0, 65504]).
/// </summary>
public static partial class ImagingFuzzTargets
{
    // The magic number, the FourCCs, the sizes of the structures and the flags. A property: All, in another file of the
    // class, reads it, and the initializers of the files of a partial class run in no defined order
    private static string[] DdsTokens =>
    [
        "DDS ", "DX10", "DXT1", "DXT2", "DXT3", "DXT4", "DXT5", "ATI1", "ATI2", "BC4U", "BC4S", "BC5U", "BC5S", "YUY2", "RGBG",
        "|\0\0\0", " \0\0\0", "\u0007\u0010\u0000\u0000", "\u0007\u0010\u0002\u0000", "\u0004\0\0\0", "@\0\0\0", "A\0\0\0", "\0\0\u0002\0",
        "\0\u0002\0\0", "\0þ\0\0", "\0\0 \0", "\u0003\0\0\0", "\u0004\0\0\0", "\u0002\0\0\0", "\u001C\0\0\0", "G\0\0\0",
        "b\0\0\0", "_\0\0\0", "`\0\0\0", "P\0\0\0", "S\0\0\0", "q\0\0\0", "t\0\0\0",
        "ÿÿÿÿ", "\0\0\0ÿ", "ÿ\0\0\0",
    ];

    // Small textures: a file that declares a large one fails at the limits instead of allocating
    private static readonly ImageConfiguration DdsConfiguration = new()
    {
        Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 20, MaxFrames = 64 },
    };

    /// <summary>The DXGI formats of the block target, selected by the first byte of its input.</summary>
    private static readonly uint[] DdsBlockFormats = [71, 74, 77, 80, 81, 83, 84, 95, 96, 98];

    private static void DdsTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var identified = Catch(() => Image.Identify(input, new ImageIdentifyOptions { Configuration = DdsConfiguration, Mode = ImageIdentifyMode.FullScan }));
        Image? image = null;
        var loaded = Catch(() => image = Image.Load(input, new ImageDecodeOptions { Configuration = DdsConfiguration }));
        try
        {
            if (identified.Info is not { Format: ImageFormat.Dds } info)
            {
                FuzzAssert.True(image is null || Image.DetectFormat(input) != ImageFormat.Dds, "A load succeeds where a full scan fails: " + identified.Exception?.Message);
                return;
            }

            FuzzAssert.True(image is not null, "A load fails where a full scan succeeds: " + loaded.Exception?.Message);
            FuzzAssert.Equal(info.Size, image!.Size, "The identified size");
            FuzzAssert.Equal(info.PixelFormat, image.PixelFormat, "The identified pixel format");
            var pixels = GetDdsBytes(image);

            ImageCollection? collection = null;
            try
            {
                collection = ImageCollection.Load(input, DdsConfiguration);
            }
            catch (Exception exception) when (IsDocumented(exception))
            {
                // A file that does not hold every surface, or holds too many, loads its first one only
                return;
            }

            using (collection)
            {
                FuzzAssert.Equal(info.CollectionEntryCount, (int?)collection.Count, "The identified number of surfaces");
                var first = collection[0];
                FuzzAssert.True(first.TextureSurface is { ArrayIndex: 0, MipLevel: 0, DepthSlice: 0 }, "The first surface");
                using (var decoded = first.Decode())
                {
                    FuzzAssert.True(GetDdsBytes(decoded).AsSpan().SequenceEqual(pixels), "The first entry gives other pixels than the load");
                }

                // The other surfaces decode, each to the size of its level
                for (var i = 1; i < collection.Count; i++)
                {
                    var entry = collection[i];
                    var surface = entry.TextureSurface!;
                    FuzzAssert.Equal(new Size(Math.Max(1, info.Width >> surface.MipLevel), Math.Max(1, info.Height >> surface.MipLevel)), entry.Size, "The size of a level");
                    using var decoded = entry.Decode();
                    FuzzAssert.Equal(entry.Size, decoded.Size, "The decoded size of a surface");
                }
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static void DdsBlockTarget(ReadOnlySpan<byte> data)
    {
        var format = DdsBlockFormats[FuzzInput.ReadByte(ref data) % DdsBlockFormats.Length];
        var blockLength = format is 71 or 80 or 81 ? 8 : 16;
        var block = new byte[blockLength];
        data[..Math.Min(data.Length, blockLength)].CopyTo(block);
        var file = CreateDds(format, block);

        using var image = Image.Load(file);
        var (min, max) = format switch
        {
            81 or 84 => (-1f, 1f),
            95 => (0f, 65504f),
            96 => (float.NegativeInfinity, 65504f),
            _ => (0f, 1f),
        };

        // The unsigned normalized formats are integers, in range by construction; the others are floats or halves
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var (r, g, b, a) = image switch
                {
                    Image<RgbaFloat> floats => (floats.Frames[0][x, y].R, floats.Frames[0][x, y].G, floats.Frames[0][x, y].B, floats.Frames[0][x, y].A),
                    Image<RgbaHalf> halves => ((float)halves.Frames[0][x, y].R, (float)halves.Frames[0][x, y].G, (float)halves.Frames[0][x, y].B, (float)halves.Frames[0][x, y].A),
                    Image<GrayFloat> grays => (grays.Frames[0][x, y].Value, grays.Frames[0][x, y].Value, grays.Frames[0][x, y].Value, 1f),
                    _ => (0f, 0f, 0f, 1f),
                };

                foreach (var value in (ReadOnlySpan<float>)[r, g, b])
                {
                    FuzzAssert.True(!float.IsNaN(value) && value >= min && value <= max, "A value outside the range of the format");
                }

                FuzzAssert.True(a == 1, "The alpha of a format without alpha");
            }
        }

        // A collection decodes the same block
        using var collection = ImageCollection.Load(file);
        using var entry = collection[0].Decode();
        FuzzAssert.True(GetDdsBytes(entry).AsSpan().SequenceEqual(GetDdsBytes(image)), "The collection gives other pixels than the load");
    }

    /// <summary>A 4×4 texture of one block of a DXGI format, with a DX10 header.</summary>
    private static byte[] CreateDds(uint dxgiFormat, ReadOnlySpan<byte> block)
    {
        var file = new byte[148 + block.Length];
        var header = file.AsSpan();
        "DDS "u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 0x1007);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], 4);
        "DX10"u8.CopyTo(header[84..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[108..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[128..], dxgiFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(header[132..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(header[140..], 1);
        block.CopyTo(header[148..]);
        return file;
    }

    private static byte[] GetDdsBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetDdsSeeds()
    {
        var assembly = typeof(ImagingFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Seeds/dds/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new FuzzSeed(name["Seeds/dds/".Length..^".dds".Length], buffer.ToArray());
        }
    }

    /// <summary>The first block of the random blocks of the corpus, after the byte that selects their format.</summary>
    private static IEnumerable<FuzzSeed> GetDdsBlockSeeds()
    {
        foreach (var seed in GetDdsSeeds().Where(seed => seed.Name.StartsWith("random-", StringComparison.Ordinal) && seed.Data.Length > 148 && seed.Data.Span.Slice(84, 4).SequenceEqual("DX10"u8)))
        {
            var index = Array.IndexOf(DdsBlockFormats, BinaryPrimitives.ReadUInt32LittleEndian(seed.Data.Span[128..]));
            if (index < 0)
                continue;

            var blockLength = DdsBlockFormats[index] is 71 or 80 or 81 ? 8 : 16;
            yield return new FuzzSeed(seed.Name, (byte[])[(byte)index, .. seed.Data.Span.Slice(148, blockLength)]);
        }
    }
}
