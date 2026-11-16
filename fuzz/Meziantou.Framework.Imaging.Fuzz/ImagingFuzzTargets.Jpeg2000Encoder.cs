using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The structure of the outputs of the JPEG 2000 encoder: precincts, tile-parts, TLM and PLT marker segments, quality
/// layers and the high-throughput block coder with its refinement passes. The input is four bytes of options, then a small
/// image. The encoder only refuses a tile cut into more than 255 tile-parts; its output is deterministic, the library
/// decodes it at every number of layers, a lossless output whose last layer is complete is the image, the tile-parts
/// follow each other and their lengths in the SOT, TLM and PLT marker segments add up, and neither the tile-parts nor the
/// length markers change the decoded samples.
/// </summary>
public static partial class ImagingFuzzTargets
{
    private static readonly PixelFormat[] Jpeg2000Formats = [PixelFormat.Gray8, PixelFormat.Rgb24, PixelFormat.Rgba32, PixelFormat.Gray16, PixelFormat.Rgba64];

    private static void Jpeg2000EncoderTarget(ReadOnlySpan<byte> data)
    {
        // The options: the compression, the block coder, the progression and the tile-part division; the precincts; the
        // tiles and the levels; the layers
        var options = FuzzInput.ReadByte(ref data);
        var precincts = FuzzInput.ReadByte(ref data);
        var tiles = FuzzInput.ReadByte(ref data);
        var layerByte = FuzzInput.ReadByte(ref data);
        var isLossy = (options & 1) != 0;
        var layerCount = layerByte & 3;
        var isComplete = (layerByte & 4) != 0;
        Jpeg2000QualityLayer[] layers = [.. Enumerable.Range(0, layerCount).Select(layer => Jpeg2000QualityLayer.FromBitsPerPixel(0.25 * (1 + layer) * (1 + (layerByte >> 3)))), .. isComplete && layerCount > 0 ? [Jpeg2000QualityLayer.Complete] : Array.Empty<Jpeg2000QualityLayer>()];
        Size[] precinctSizes = (precincts & 0x80) == 0 ? [] : [new Size(2 << (precincts & 7), 2 << ((precincts >> 3) & 7)), new Size(2 << ((precincts >> 4) & 3), 2 << ((precincts >> 5) & 3))];
        var encoder = new Jpeg2000Encoder
        {
            Compression = isLossy ? Jpeg2000Compression.Lossy : Jpeg2000Compression.Lossless,
            BlockCoder = (options & 2) != 0 ? Jpeg2000BlockCoder.HighThroughput : Jpeg2000BlockCoder.Standard,
            ProgressionOrder = (Jpeg2000ProgressionOrder)(((options >> 2) & 7) % 5),
            TilePartDivision = (Jpeg2000TilePartDivision)((options >> 5) & 3),
            WriteTilePartLengths = (options & 0x80) != 0,
            WritePacketLengths = (precincts & 0x40) != 0,
            PrecinctSizes = precinctSizes,
            TileWidth = (tiles & 0x0F) * 3,
            TileHeight = ((tiles >> 4) & 7) * 3,
            DecompositionLevels = (tiles & 0x80) != 0 ? 1 : 5,
            QualityLayers = layers,
            Container = Jpeg2000Container.Codestream,
        };

        using var image = CreateJpeg2000Image(ref data);
        byte[] output;
        try
        {
            output = Save(image, encoder);
        }
        catch (ArgumentException exception) when (exception.Message.Contains("255 tile-parts", StringComparison.Ordinal))
        {
            return;
        }

        FuzzAssert.True(Save(image, encoder).AsSpan().SequenceEqual(output), "The encoding is not deterministic");
        var tileParts = CheckJpeg2000TileParts(output, encoder.WriteTilePartLengths, encoder.WritePacketLengths);
        FuzzAssert.True(encoder.TilePartDivision != Jpeg2000TilePartDivision.None || tileParts.All(count => count == 1), "A tile has several tile-parts without a division");

        using var decoded = Image.Load(output);
        FuzzAssert.Equal(image.Size, decoded.Size, "The decoded size");
        var decodedBytes = GetJpeg2000Bytes(decoded);
        if (!isLossy && (layerCount == 0 || isComplete))
        {
            using Image expected = decoded.PixelFormat switch
            {
                PixelFormat.Gray8 => image.CloneAs<Gray8>(),
                PixelFormat.Gray16 => image.CloneAs<Gray16>(),
                PixelFormat.Rgb24 => image.CloneAs<Rgb24>(),
                PixelFormat.Rgb48 => image.CloneAs<Rgb48>(),
                PixelFormat.Rgba32 => image.CloneAs<Rgba32>(),
                PixelFormat.Rgba64 => image.CloneAs<Rgba64>(),
                _ => throw new InvalidOperationException("The decoder gives another pixel format: " + decoded.PixelFormat),
            };
            FuzzAssert.True(GetJpeg2000Bytes(expected).AsSpan().SequenceEqual(decodedBytes), "A lossless output is not the image");
        }

        for (var limit = 1; limit < layers.Length; limit++)
        {
            using var limited = Image.Load(output, new ImageDecodeOptions { Configuration = new ImageConfiguration { Jpeg2000Decoding = new Jpeg2000DecodingOptions { QualityLayerLimit = limit } } });
            FuzzAssert.Equal(image.Size, limited.Size, "The size of a decode of the first layers");
        }

        // Without targets, the tile-parts and the length markers do not change the coded data
        if (layerCount == 0)
        {
            var plain = new Jpeg2000Encoder
            {
                Compression = encoder.Compression,
                BlockCoder = encoder.BlockCoder,
                ProgressionOrder = encoder.ProgressionOrder,
                PrecinctSizes = encoder.PrecinctSizes,
                TileWidth = encoder.TileWidth,
                TileHeight = encoder.TileHeight,
                DecompositionLevels = encoder.DecompositionLevels,
                Container = Jpeg2000Container.Codestream,
            };
            using var reference = Image.Load(Save(image, plain));
            FuzzAssert.True(GetJpeg2000Bytes(reference).AsSpan().SequenceEqual(decodedBytes), "The tile-parts or the length markers change the decoded samples");
        }
    }

    /// <summary>
    /// Walks the tile-parts of a codestream (A.4.2): each one starts where the previous one ends, the number of each one in
    /// its tile follows the previous one, and the EOC marker follows the last one; the lengths of the TLM marker segments are
    /// those of the SOT marker segments, and the lengths of the PLT marker segments of a tile-part add up to its body.
    /// </summary>
    /// <returns>The number of tile-parts of each tile, in the order of the codestream.</returns>
    private static List<int> CheckJpeg2000TileParts(byte[] codestream, bool hasTilePartLengths, bool hasPacketLengths)
    {
        // The main header: the TLM marker segments, with a byte or two of tile index and 32-bit lengths
        var position = 2;
        var listed = new List<long>();
        while (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position)) != 0xFF90)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position + 2));
            if (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position)) == 0xFF55)
            {
                var indexLength = (codestream[position + 5] >> 4) & 3;
                FuzzAssert.True((codestream[position + 5] & 0x40) != 0 && indexLength is 1 or 2, "The TLM marker segment has 32-bit lengths and tile indices");
                for (var entry = position + 6; entry < position + 2 + length; entry += indexLength + 4)
                {
                    listed.Add(BinaryPrimitives.ReadUInt32BigEndian(codestream.AsSpan(entry + indexLength)));
                }
            }

            position += 2 + length;
        }

        var counts = new List<int>();
        var lengths = new List<long>();
        var parts = new Dictionary<int, int>();
        var declared = new Dictionary<int, int>();
        while (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position)) == 0xFF90)
        {
            var tile = BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position + 4));
            var tilePartLength = BinaryPrimitives.ReadUInt32BigEndian(codestream.AsSpan(position + 6));
            int part = codestream[position + 10];
            int partCount = codestream[position + 11];
            parts.TryGetValue(tile, out var expected);
            FuzzAssert.Equal(expected, part, "The index of a tile-part");
            FuzzAssert.True(part < partCount, "A tile-part beyond the number of tile-parts of its tile");
            parts[tile] = part + 1;
            FuzzAssert.Equal(declared.GetValueOrDefault(tile, partCount), partCount, "The number of tile-parts of a tile");
            declared[tile] = partCount;
            if (part == partCount - 1)
            {
                counts.Add(partCount);
            }

            // The PLT marker segments of the tile-part, up to the SOD marker
            var header = position + 12;
            var packets = 0L;
            var value = 0L;
            var hasPlt = false;
            while (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(header)) != 0xFF93)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(header + 2));
                if (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(header)) == 0xFF58)
                {
                    hasPlt = true;
                    foreach (var b in codestream.AsSpan(header + 5, length - 3))
                    {
                        value = (value << 7) | (b & 0x7FL);
                        if ((b & 0x80) == 0)
                        {
                            packets += value;
                            value = 0;
                        }
                    }
                }

                header += 2 + length;
            }

            FuzzAssert.Equal(hasPacketLengths, hasPlt, "The PLT marker segments");
            if (hasPlt)
            {
                FuzzAssert.Equal(0L, value, "A packet length split between PLT marker segments");
                FuzzAssert.Equal(position + tilePartLength - (header + 2), packets, "The packet lengths of a tile-part");
            }

            lengths.Add(tilePartLength);
            position += (int)tilePartLength;
        }

        FuzzAssert.Equal(codestream.Length - 2, position, "The end of the last tile-part");
        FuzzAssert.True(parts.All(pair => pair.Value == declared[pair.Key]), "A tile misses tile-parts");
        if (hasTilePartLengths)
        {
            FuzzAssert.True(listed.SequenceEqual(lengths), "The TLM marker segments list other lengths than the SOT marker segments");
        }
        else
        {
            FuzzAssert.Equal(0, listed.Count, "A TLM marker segment that was not asked for");
        }

        return counts;
    }

    /// <summary>Reads an image from the input: a byte of format, a byte of width and one of height (1 to 40), then the bytes of the pixels, repeated when the input is shorter.</summary>
    private static Image CreateJpeg2000Image(ref ReadOnlySpan<byte> data)
    {
        var format = Jpeg2000Formats[FuzzInput.ReadByte(ref data) % Jpeg2000Formats.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % 40);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 40);
        return format switch
        {
            PixelFormat.Gray8 => Create<Gray8>(data, width, height),
            PixelFormat.Rgb24 => Create<Rgb24>(data, width, height),
            PixelFormat.Rgba32 => Create<Rgba32>(data, width, height),
            PixelFormat.Gray16 => Create<Gray16>(data, width, height),
            _ => Create<Rgba64>(data, width, height),
        };
    }

    private static byte[] GetJpeg2000Bytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetJpeg2000EncoderSeeds()
    {
        // The cases of the unit tests: precincts in each progression order, small precincts, tile-parts with TLM and PLT
        // marker segments, and the layers of the high-throughput block coder, lossless and lossy
        byte[] pixels = [0, 40, 80, 120, 160, 200, 240, 17, 59, 101, 143, 185, 227, 13, 77, 141, 205, 33, 99, 165, 231, 7];
        yield return new FuzzSeed("precincts-lrcp", (byte[])[0x00, 0x80 | 0x05 | (0x04 << 3), 0x00, 0x00, 2, 37, 23, .. pixels]);
        yield return new FuzzSeed("precincts-pcrl-high-throughput", (byte[])[0x02 | (3 << 2), 0x80 | 0x04 | (0x04 << 3) | 0x10, 0x00, 0x00, 1, 30, 29, .. pixels]);
        yield return new FuzzSeed("small-precincts", (byte[])[0x00, 0x80, 0x80, 0x00, 2, 13, 11, .. pixels]);
        yield return new FuzzSeed("tile-parts-resolution-rpcl", (byte[])[(2 << 2) | (1 << 5) | 0x80, 0x80 | 0x40 | 0x05 | (0x05 << 3), 0x45, 0x06, 2, 40, 33, .. pixels]);
        yield return new FuzzSeed("tile-parts-layer-lrcp", (byte[])[(2 << 5) | 0x80, 0x40, 0x00, 0x07, 1, 25, 18, .. pixels]);
        yield return new FuzzSeed("tile-parts-component-cprl", (byte[])[(4 << 2) | (3 << 5), 0x40, 0x23, 0x02, 2, 31, 17, .. pixels]);
        yield return new FuzzSeed("high-throughput-layers", (byte[])[0x02, 0x00, 0x00, 0x07, 2, 40, 40, .. pixels]);
        yield return new FuzzSeed("high-throughput-lossy-target", (byte[])[0x03, 0x00, 0x00, 0x09, 4, 33, 21, .. pixels]);
        yield return new FuzzSeed("high-throughput-lossy-layers", (byte[])[0x03 | (1 << 5), 0x80 | 0x40 | 0x03, 0x00, 0x13, 3, 39, 27, .. pixels]);
    }
}
