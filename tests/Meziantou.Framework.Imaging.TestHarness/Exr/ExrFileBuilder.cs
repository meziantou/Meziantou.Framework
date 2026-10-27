using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Exr;

/// <summary>
/// Assembles OpenEXR files from a list of attributes and chunks, for the tests that need a file the encoders do not write
/// and for malformed files. It is written from the OpenEXR File Layout document and never uses the library under test. It
/// writes what it is told to: nothing is validated.
/// </summary>
/// <remarks>
/// The chunks are given in the order of the offset table; <see cref="FileOrder"/> stores them in another order,
/// <see cref="GapBefore"/> leaves bytes before a chunk, and <see cref="Offsets"/> replaces the computed table.
/// </remarks>
public sealed class ExrFileBuilder
{
    /// <summary>Gets or sets the version field: the version number 2 and the flags.</summary>
    public int VersionField { get; set; } = 2;

    /// <summary>Gets the attributes of the header, in file order.</summary>
    public IList<ExrAttribute> Attributes { get; } = [];

    /// <summary>Gets the chunks, in the order of the offset table.</summary>
    public IList<ExrChunk> Chunks { get; } = [];

    /// <summary>Gets or sets the order in which the chunks are stored (indexes of <see cref="Chunks"/>), or <see langword="null"/> for the table order.</summary>
    public IReadOnlyList<int>? FileOrder { get; set; }

    /// <summary>Gets the number of zero bytes written before a chunk (keyed by its index in <see cref="Chunks"/>).</summary>
    public IDictionary<int, int> GapBefore { get; } = new Dictionary<int, int>();

    /// <summary>Gets or sets the offset table to write instead of the offsets of the chunks.</summary>
    public IReadOnlyList<long>? Offsets { get; set; }

    /// <summary>Gets or sets the number of entries of the offset table, when it differs from the number of chunks.</summary>
    public int? OffsetCount { get; set; }

    /// <summary>Gets or sets bytes written after the last chunk.</summary>
    public ReadOnlyMemory<byte> Trailer { get; set; }

    /// <summary>Creates the builder of a scan-line image with the required attributes.</summary>
    /// <param name="width">The width of the data window and of the display window.</param>
    /// <param name="height">The height of the data window and of the display window.</param>
    /// <param name="compression">The compression attribute.</param>
    /// <param name="channels">The channels, in the order of the list.</param>
    /// <returns>The builder, without chunks.</returns>
    public static ExrFileBuilder ScanLines(int width, int height, int compression, params ExrChannelDescription[] channels)
    {
        var builder = new ExrFileBuilder();
        builder.Attributes.Add(new ExrAttribute("channels", "chlist", ChannelList(channels)));
        builder.Attributes.Add(new ExrAttribute("compression", "compression", [(byte)compression]));
        builder.Attributes.Add(new ExrAttribute("dataWindow", "box2i", Box(0, 0, width - 1, height - 1)));
        builder.Attributes.Add(new ExrAttribute("displayWindow", "box2i", Box(0, 0, width - 1, height - 1)));
        builder.Attributes.Add(new ExrAttribute("lineOrder", "lineOrder", [0]));
        builder.Attributes.Add(new ExrAttribute("pixelAspectRatio", "float", Floats(1f)));
        builder.Attributes.Add(new ExrAttribute("screenWindowCenter", "v2f", Floats(0f, 0f)));
        builder.Attributes.Add(new ExrAttribute("screenWindowWidth", "float", Floats(1f)));
        return builder;
    }

    /// <summary>Replaces the value of an attribute, or adds it.</summary>
    public ExrFileBuilder Set(string name, string type, byte[] value)
    {
        for (var i = 0; i < Attributes.Count; i++)
        {
            if (Attributes[i].Name == name)
            {
                Attributes[i] = new ExrAttribute(name, type, value);
                return this;
            }
        }

        Attributes.Add(new ExrAttribute(name, type, value));
        return this;
    }

    /// <summary>Removes an attribute.</summary>
    public ExrFileBuilder Remove(string name)
    {
        for (var i = Attributes.Count - 1; i >= 0; i--)
        {
            if (Attributes[i].Name == name)
            {
                Attributes.RemoveAt(i);
            }
        }

        return this;
    }

    /// <summary>Adds a scan-line chunk.</summary>
    public ExrFileBuilder AddScanLineChunk(int y, byte[] data)
    {
        Chunks.Add(new ExrChunk([y], data));
        return this;
    }

    /// <summary>Adds a tile.</summary>
    public ExrFileBuilder AddTile(int tileX, int tileY, int levelX, int levelY, byte[] data)
    {
        Chunks.Add(new ExrChunk([tileX, tileY, levelX, levelY], data));
        return this;
    }

    /// <summary>Writes the file.</summary>
    public byte[] Build()
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[8];
        stream.Write([0x76, 0x2F, 0x31, 0x01]);
        BinaryPrimitives.WriteInt32LittleEndian(number, VersionField);
        stream.Write(number[..4]);
        foreach (var attribute in Attributes)
        {
            stream.Write(Encoding.UTF8.GetBytes(attribute.Name));
            stream.WriteByte(0);
            stream.Write(Encoding.UTF8.GetBytes(attribute.Type));
            stream.WriteByte(0);
            BinaryPrimitives.WriteInt32LittleEndian(number, attribute.Value.Length);
            stream.Write(number[..4]);
            stream.Write(attribute.Value);
        }

        stream.WriteByte(0);
        var tableStart = stream.Position;
        var entries = OffsetCount ?? Chunks.Count;
        var order = FileOrder ?? [.. Enumerable.Range(0, Chunks.Count)];
        var offsets = new long[Chunks.Count];
        var position = tableStart + (8L * entries);
        foreach (var index in order)
        {
            position += GapBefore.TryGetValue(index, out var gap) ? gap : 0;
            offsets[index] = position;
            position += (4L * Chunks[index].Coordinates.Count) + 4 + Chunks[index].Data.Length;
        }

        for (var i = 0; i < entries; i++)
        {
            var offset = Offsets is { } table ? table[i] : i < offsets.Length ? offsets[i] : 0;
            BinaryPrimitives.WriteInt64LittleEndian(number, offset);
            stream.Write(number);
        }

        foreach (var index in order)
        {
            if (GapBefore.TryGetValue(index, out var gap))
            {
                stream.Write(new byte[gap]);
            }

            var chunk = Chunks[index];
            foreach (var coordinate in chunk.Coordinates)
            {
                BinaryPrimitives.WriteInt32LittleEndian(number, coordinate);
                stream.Write(number[..4]);
            }

            BinaryPrimitives.WriteInt32LittleEndian(number, chunk.Data.Length);
            stream.Write(number[..4]);
            stream.Write(chunk.Data);
        }

        stream.Write(Trailer.Span);
        return stream.ToArray();
    }

    /// <summary>Encodes a <c>chlist</c> value.</summary>
    public static byte[] ChannelList(params ExrChannelDescription[] channels)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        foreach (var channel in channels)
        {
            stream.Write(Encoding.UTF8.GetBytes(channel.Name));
            stream.WriteByte(0);
            BinaryPrimitives.WriteInt32LittleEndian(number, channel.PixelType);
            stream.Write(number);
            stream.Write([channel.PLinear ? (byte)1 : (byte)0, 0, 0, 0]);
            BinaryPrimitives.WriteInt32LittleEndian(number, channel.XSampling);
            stream.Write(number);
            BinaryPrimitives.WriteInt32LittleEndian(number, channel.YSampling);
            stream.Write(number);
        }

        stream.WriteByte(0);
        return stream.ToArray();
    }

    /// <summary>Encodes a <c>box2i</c> value.</summary>
    public static byte[] Box(int minX, int minY, int maxX, int maxY) => Ints(minX, minY, maxX, maxY);

    /// <summary>Encodes a <c>tiledesc</c> value.</summary>
    public static byte[] TileDescription(int width, int height, int levelMode, int roundingMode)
    {
        var value = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(value, width);
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(4), height);
        value[8] = (byte)(levelMode + (roundingMode * 16));
        return value;
    }

    /// <summary>Encodes little-endian <c>int</c> values.</summary>
    public static byte[] Ints(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 * i), values[i]);
        }

        return bytes;
    }

    /// <summary>Encodes little-endian <c>float</c> values.</summary>
    public static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 * i), values[i]);
        }

        return bytes;
    }

    /// <summary>Encodes little-endian <c>half</c> values.</summary>
    public static byte[] Halves(params float[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(2 * i), (Half)values[i]);
        }

        return bytes;
    }

    /// <summary>Encodes little-endian <c>unsigned int</c> values.</summary>
    public static byte[] UInts(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * i), values[i]);
        }

        return bytes;
    }

    /// <summary>Encodes a <c>string</c> value.</summary>
    public static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
}
