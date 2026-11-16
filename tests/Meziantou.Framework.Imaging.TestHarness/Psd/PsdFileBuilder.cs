using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Psd;

/// <summary>
/// Assembles Adobe Photoshop files from the Adobe Photoshop File Formats Specification ("Photoshop File Format"), byte by
/// byte, without the library under test: the file header, the color mode data, the image resources, the layer and mask
/// information (a layer count, in the layer info or in an <c>Lr16</c>/<c>Lr32</c> tagged block) and the image data, raw or
/// compressed with PackBits. The channels are given as their stored samples: big-endian, CMYK inverted, 1-bit samples
/// packed most significant bit first with 1 for black. Shared by the unit tests and the corpus generator.
/// </summary>
public sealed class PsdFileBuilder
{
    public PsdFileBuilder(int width, int height, int depth, int mode, IReadOnlyList<byte[]> channels)
    {
        Width = width;
        Height = height;
        Depth = depth;
        Mode = mode;
        Channels = [.. channels];
        ChannelCount = channels.Count;
    }

    /// <summary>Gets or sets the version: 1 (PSD) or 2 (PSB).</summary>
    public int Version { get; set; } = 1;

    public int Width { get; set; }

    public int Height { get; set; }

    public int Depth { get; set; }

    public int Mode { get; set; }

    /// <summary>Gets or sets the channel count of the header (the number of <see cref="Channels"/> by default).</summary>
    public int ChannelCount { get; set; }

    /// <summary>Gets the stored samples of each channel: <c>Height</c> rows of <c>ceil(Width × Depth / 8)</c> bytes.</summary>
    public IList<byte[]> Channels { get; }

    /// <summary>Gets or sets the color mode data (the color table of an indexed image).</summary>
    public ReadOnlyMemory<byte> ColorModeData { get; set; }

    /// <summary>Gets the image resources: identifier, name and data (the signature is <c>8BIM</c> unless given).</summary>
    public IList<(int Id, string Name, byte[] Data, string Signature)> Resources { get; } = [];

    /// <summary>Gets or sets the compression of the image data: 0 raw, 1 PackBits; another value writes <see cref="CompressedData"/> after it.</summary>
    public int Compression { get; set; }

    /// <summary>Gets or sets the bytes written after a compression that is not 0 or 1.</summary>
    public ReadOnlyMemory<byte> CompressedData { get; set; }

    /// <summary>Gets or sets the layer count written in the layer info (a negative count: the composite has a transparency channel), or <see langword="null"/> for an empty layer and mask information section.</summary>
    public short? LayerCount { get; set; }

    /// <summary>Gets or sets the key of the tagged block (<c>Lr16</c>, <c>Lr32</c>) that holds the layer count instead of the layer info, as Photoshop writes 16 and 32-bit documents.</summary>
    public string? LayerBlockKey { get; set; }

    /// <summary>Gets or sets the bytes of the layer and mask information section, written as they are instead of the one <see cref="LayerCount"/> describes.</summary>
    public ReadOnlyMemory<byte>? LayerSection { get; set; }

    /// <summary>Gets or sets a function that replaces the compressed rows (channel, row, PackBits data).</summary>
    public Func<int, int, byte[], byte[]>? EditPackedRow { get; set; }

    public int RowLength => ((Width * Depth) + 7) / 8;

    private bool IsLarge => Version == 2;

    public PsdFileBuilder AddResource(int id, byte[] data, string name = "", string signature = "8BIM")
    {
        Resources.Add((id, name, data, signature));
        return this;
    }

    /// <summary>Adds the version information resource (1057) that says whether the file has a real composite.</summary>
    public PsdFileBuilder AddVersionInfo(bool hasRealMergedData)
    {
        var data = new List<byte>();
        data.AddRange(U32(1));
        data.Add(hasRealMergedData ? (byte)1 : (byte)0);
        data.AddRange(UnicodeString("Adobe Photoshop"));
        data.AddRange(UnicodeString("Adobe Photoshop 2026"));
        data.AddRange(U32(1));
        return AddResource(1057, [.. data]);
    }

    /// <summary>Adds the resolution resource (1005): pixels per inch as 16.16 fixed-point numbers.</summary>
    public PsdFileBuilder AddResolution(int horizontalFixed, int verticalFixed, int unit = 1)
    {
        var data = new List<byte>();
        data.AddRange(U32((uint)horizontalFixed));
        data.AddRange(U16(unit));
        data.AddRange(U16(1));
        data.AddRange(U32((uint)verticalFixed));
        data.AddRange(U16(unit));
        data.AddRange(U16(1));
        return AddResource(1005, [.. data]);
    }

    public byte[] Build()
    {
        var output = new List<byte>();
        output.AddRange("8BPS"u8.ToArray());
        output.AddRange(U16(Version));
        output.AddRange(new byte[6]);
        output.AddRange(U16(ChannelCount));
        output.AddRange(U32((uint)Height));
        output.AddRange(U32((uint)Width));
        output.AddRange(U16(Depth));
        output.AddRange(U16(Mode));

        output.AddRange(U32((uint)ColorModeData.Length));
        output.AddRange(ColorModeData.ToArray());

        var resources = new List<byte>();
        foreach (var (id, name, data, signature) in Resources)
        {
            resources.AddRange(System.Text.Encoding.ASCII.GetBytes(signature));
            resources.AddRange(U16(id));
            resources.Add((byte)name.Length);
            resources.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
            if ((name.Length + 1) % 2 != 0)
            {
                resources.Add(0);
            }

            resources.AddRange(U32((uint)data.Length));
            resources.AddRange(data);
            if (data.Length % 2 != 0)
            {
                resources.Add(0);
            }
        }

        output.AddRange(U32((uint)resources.Count));
        output.AddRange(resources);

        var section = LayerSection?.ToArray() ?? BuildLayerSection();
        output.AddRange(Length(section.Length));
        output.AddRange(section);

        output.AddRange(U16(Compression));
        switch (Compression)
        {
            case 0:
                foreach (var channel in Channels)
                {
                    output.AddRange(channel);
                }

                break;

            case 1:
            {
                var rows = new List<byte[]>();
                for (var c = 0; c < Channels.Count; c++)
                {
                    for (var y = 0; y < Height; y++)
                    {
                        var packed = PackBits(Channels[c].AsSpan(y * RowLength, RowLength));
                        rows.Add(EditPackedRow?.Invoke(c, y, packed) ?? packed);
                    }
                }

                foreach (var row in rows)
                {
                    output.AddRange(IsLarge ? U32((uint)row.Length) : U16(row.Length));
                }

                foreach (var row in rows)
                {
                    output.AddRange(row);
                }

                break;
            }

            default:
                output.AddRange(CompressedData.ToArray());
                break;
        }

        return [.. output];
    }

    /// <summary>
    /// Compresses a row with PackBits (Apple Technical Note TN1023): runs of 2 to 128 equal bytes as <c>1 - n</c> and the
    /// byte, the other bytes as literals of 1 to 128 bytes after <c>n - 1</c>.
    /// </summary>
    public static byte[] PackBits(ReadOnlySpan<byte> row)
    {
        var output = new List<byte>();
        var i = 0;
        while (i < row.Length)
        {
            var run = 1;
            while (i + run < row.Length && run < 128 && row[i + run] == row[i])
            {
                run++;
            }

            if (run >= 2)
            {
                output.Add((byte)(sbyte)(1 - run));
                output.Add(row[i]);
                i += run;
                continue;
            }

            var literal = 1;
            while (i + literal < row.Length && literal < 128 && !(i + literal + 1 < row.Length && row[i + literal] == row[i + literal + 1]))
            {
                literal++;
            }

            output.Add((byte)(literal - 1));
            output.AddRange(row.Slice(i, literal).ToArray());
            i += literal;
        }

        return [.. output];
    }

    /// <summary>Gets the planes of interleaved 8-bit samples.</summary>
    public static byte[][] Planes8(ReadOnlySpan<byte> interleaved, int channels)
    {
        var count = interleaved.Length / channels;
        var planes = new List<byte[]>();
        for (var c = 0; c < channels; c++)
        {
            var plane = new byte[count];
            for (var i = 0; i < count; i++)
            {
                plane[i] = interleaved[(i * channels) + c];
            }

            planes.Add(plane);
        }

        return [.. planes];
    }

    /// <summary>Gets the planes of interleaved 16-bit samples, stored big-endian.</summary>
    public static byte[][] Planes16(ReadOnlySpan<ushort> interleaved, int channels)
    {
        var count = interleaved.Length / channels;
        var planes = new List<byte[]>();
        for (var c = 0; c < channels; c++)
        {
            var plane = new byte[count * 2];
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(plane.AsSpan(i * 2), interleaved[(i * channels) + c]);
            }

            planes.Add(plane);
        }

        return [.. planes];
    }

    /// <summary>Gets the planes of interleaved 32-bit floating-point samples, stored big-endian.</summary>
    public static byte[][] Planes32(ReadOnlySpan<float> interleaved, int channels)
    {
        var count = interleaved.Length / channels;
        var planes = new List<byte[]>();
        for (var c = 0; c < channels; c++)
        {
            var plane = new byte[count * 4];
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteSingleBigEndian(plane.AsSpan(i * 4), interleaved[(i * channels) + c]);
            }

            planes.Add(plane);
        }

        return [.. planes];
    }

    /// <summary>Packs 1-bit samples (1 is black), row by row, most significant bit first, rows padded with 0 bits.</summary>
    public static byte[] PackBitmap(ReadOnlySpan<byte> bits, int width, int height)
    {
        var rowLength = (width + 7) / 8;
        var plane = new byte[rowLength * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (bits[(y * width) + x] != 0)
                {
                    plane[(y * rowLength) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return plane;
    }

    public static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    public static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] UnicodeString(string value)
    {
        var bytes = new List<byte>(U32((uint)value.Length));
        foreach (var c in value)
        {
            bytes.AddRange(U16(c));
        }

        return [.. bytes];
    }

    private byte[] Length(long length)
    {
        if (!IsLarge)
            return U32((uint)length);

        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)length);
        return bytes;
    }

    private byte[] BuildLayerSection()
    {
        if (LayerCount is not { } count)
            return [];

        var section = new List<byte>();
        if (LayerBlockKey is null)
        {
            // The layer info: its length, then the count; no layer record follows (a count of 0 layers with the transparency flag)
            section.AddRange(Length(2));
            section.AddRange(U16((ushort)count));
            section.AddRange(U32(0));
            return [.. section];
        }

        // An empty layer info, an empty global layer mask info, then the tagged block of the layers
        section.AddRange(Length(0));
        section.AddRange(U32(0));
        section.AddRange("8BIM"u8.ToArray());
        section.AddRange(System.Text.Encoding.ASCII.GetBytes(LayerBlockKey));
        section.AddRange(IsLarge ? Length(4) : U32(4));
        section.AddRange(U16((ushort)count));
        section.AddRange(U16(0));
        return [.. section];
    }
}
