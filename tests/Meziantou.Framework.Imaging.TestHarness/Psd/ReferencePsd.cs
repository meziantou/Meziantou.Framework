using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Psd;

/// <summary>
/// An independent, strict reader of Adobe Photoshop files (Adobe Photoshop File Formats Specification, "Photoshop File
/// Format"), written for the tests without the library under test: the header, the color mode data, the image resources,
/// the layer count, and the composite image data, raw or PackBits. Any deviation is an <see cref="InvalidDataException"/>.
/// </summary>
public static class ReferencePsd
{
    public static PsdPicture Read(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        if (!reader.Bytes(4).SequenceEqual("8BPS"u8))
            throw new InvalidDataException("The signature is not 8BPS.");

        var version = reader.U16();
        if (version is not (1 or 2))
            throw new InvalidDataException("The version is not 1 or 2.");

        var large = version == 2;
        if (reader.Bytes(6).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("The reserved bytes are not zero.");

        var channels = reader.U16();
        var height = (int)reader.U32();
        var width = (int)reader.U32();
        var depth = reader.U16();
        var mode = reader.U16();
        var maxSize = large ? 300_000 : 30_000;
        if (channels is < 1 or > 56 || width is < 1 || width > maxSize || height is < 1 || height > maxSize || depth is not (1 or 8 or 16 or 32) || mode is not (0 or 1 or 2 or 3 or 4 or 7 or 8 or 9))
            throw new InvalidDataException("The header is not valid.");

        if ((mode == 0) != (depth == 1) || (mode == 2 && depth != 8))
            throw new InvalidDataException("The depth does not fit the color mode.");

        if (channels < (mode switch { 3 or 9 => 3, 4 => 4, _ => 1 }))
            throw new InvalidDataException("The file has fewer channels than the color mode has colors.");

        var colorModeData = reader.Bytes(checked((int)reader.U32())).ToArray();
        if (mode == 2 && colorModeData.Length != 768)
            throw new InvalidDataException("The color table does not have 768 bytes.");

        // Image resources
        var resources = new Dictionary<int, byte[]>();
        var resourcesLength = checked((int)reader.U32());
        var resourcesEnd = reader.Position + resourcesLength;
        while (reader.Position < resourcesEnd)
        {
            var signature = reader.Bytes(4);
            var id = reader.U16();
            var nameLength = reader.Bytes(1)[0];
            reader.Bytes(nameLength + ((nameLength + 1) % 2));
            var size = checked((int)reader.U32());
            var resource = reader.Bytes(size).ToArray();
            if (size % 2 != 0 && reader.Position < resourcesEnd)
            {
                reader.Bytes(1);
            }

            if (signature.SequenceEqual("8BIM"u8))
            {
                resources.TryAdd(id, resource);
            }
            else if (!(signature.SequenceEqual("MeSa"u8) || signature.SequenceEqual("AgHg"u8) || signature.SequenceEqual("PHUT"u8) || signature.SequenceEqual("DCSR"u8)))
            {
                throw new InvalidDataException("An image resource has another signature than 8BIM.");
            }

            if (reader.Position > resourcesEnd)
                throw new InvalidDataException("An image resource extends past its section.");
        }

        // Layer and mask information: only the layer count
        var sectionLength = large ? reader.U64() : reader.U32();
        var sectionEnd = reader.Position + checked((int)sectionLength);
        int? layerCount = null;
        if (sectionLength > 0)
        {
            var layerInfoLength = checked((int)(large ? reader.U64() : reader.U32()));
            if (layerInfoLength > sectionEnd - reader.Position)
                throw new InvalidDataException("The layer info extends past its section.");

            if (layerInfoLength > 0)
            {
                layerCount = reader.I16();
            }
            else if (reader.Position < sectionEnd)
            {
                reader.Bytes(checked((int)reader.U32()));
                while (sectionEnd - reader.Position >= 12)
                {
                    var signature = reader.Bytes(4);
                    if (!signature.SequenceEqual("8BIM"u8) && !signature.SequenceEqual("8B64"u8))
                        throw new InvalidDataException("A tagged block has another signature than 8BIM or 8B64.");

                    var key = System.Text.Encoding.ASCII.GetString(reader.Bytes(4));
                    var longLength = large && key is "LMsk" or "Lr16" or "Lr32" or "Layr" or "Mt16" or "Mt32" or "Mtrn" or "Alph" or "FMsk" or "lnk2" or "FEid" or "FXid" or "PxSD";
                    var length = checked((int)(longLength ? reader.U64() : reader.U32()));
                    var block = reader.Bytes(length);
                    if (key is "Layr" or "Lr16" or "Lr32" && length >= 2)
                    {
                        layerCount = BinaryPrimitives.ReadInt16BigEndian(block);
                        break;
                    }
                }
            }

            if (reader.Position > sectionEnd)
                throw new InvalidDataException("The layer and mask information extends past its section.");

            reader.Position = sectionEnd;
        }

        var compression = reader.U16();
        var rowLength = ((width * depth) + 7) / 8;
        var planes = new List<byte[]>();
        switch (compression)
        {
            case 0:
                for (var c = 0; c < channels; c++)
                {
                    planes.Add(reader.Bytes(rowLength * height).ToArray());
                }

                break;

            case 1:
            {
                var counts = new int[channels * height];
                for (var i = 0; i < counts.Length; i++)
                {
                    counts[i] = large ? checked((int)reader.U32()) : reader.U16();
                }

                for (var c = 0; c < channels; c++)
                {
                    var plane = new byte[rowLength * height];
                    for (var y = 0; y < height; y++)
                    {
                        UnpackBits(reader.Bytes(counts[(c * height) + y]), plane.AsSpan(y * rowLength, rowLength));
                    }

                    planes.Add(plane);
                }

                break;
            }

            case 2 or 3:
                break;

            default:
                throw new InvalidDataException("The compression is not defined.");
        }

        return new PsdPicture
        {
            Version = version,
            Width = width,
            Height = height,
            Depth = depth,
            Mode = mode,
            ChannelCount = channels,
            ColorModeData = colorModeData,
            Resources = resources,
            LayerCount = layerCount,
            Compression = compression,
            Channels = planes,
        };
    }

    private static void UnpackBits(ReadOnlySpan<byte> source, Span<byte> row)
    {
        var i = 0;
        var o = 0;
        while (i < source.Length)
        {
            var n = (sbyte)source[i++];
            if (n >= 0)
            {
                if (i + n + 1 > source.Length || o + n + 1 > row.Length)
                    throw new InvalidDataException("A PackBits literal extends past its row.");

                source.Slice(i, n + 1).CopyTo(row[o..]);
                i += n + 1;
                o += n + 1;
            }
            else if (n != -128)
            {
                if (i >= source.Length || o + 1 - n > row.Length)
                    throw new InvalidDataException("A PackBits run extends past its row.");

                row.Slice(o, 1 - n).Fill(source[i++]);
                o += 1 - n;
            }
        }

        if (o != row.Length)
            throw new InvalidDataException("A PackBits row does not have the length of a row.");
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; set; }

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || Position + count > _data.Length)
                throw new InvalidDataException("The file is truncated.");

            var result = _data.Slice(Position, count);
            Position += count;
            return result;
        }

        public int U16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));

        public short I16() => BinaryPrimitives.ReadInt16BigEndian(Bytes(2));

        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Bytes(4));

        public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Bytes(8));
    }
}
