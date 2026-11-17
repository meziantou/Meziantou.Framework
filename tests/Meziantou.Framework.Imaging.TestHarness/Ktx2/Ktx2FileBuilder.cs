using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Ktx2;

/// <summary>
/// Assembles KTX 2.0 files (KTX File Format Specification 2.0, revision 4) from a header, a basic data format descriptor
/// (Khronos Data Format Specification 1.4, section 5), key/value pairs, supercompression global data and the bytes of each
/// level, for the tests and the fuzz seeds that need files the reference tools do not write, and for malformed files. It
/// lays the sections out as section 2 does (the level index, the descriptor, the key/value data, the global data on an
/// 8-byte boundary, then the levels from the smallest one), and never uses the library under test. It writes what it is
/// told to: nothing is validated, and every computed field can be overridden.
/// </summary>
public sealed class Ktx2FileBuilder
{
    /// <summary>The 12 bytes of the identifier (section 3.1).</summary>
    public static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public uint VkFormat { get; set; }

    public uint TypeSize { get; set; } = 1;

    public uint PixelWidth { get; set; }

    public uint PixelHeight { get; set; }

    public uint PixelDepth { get; set; }

    public uint LayerCount { get; set; }

    public uint FaceCount { get; set; } = 1;

    /// <summary>Gets or sets the stored level count; <see langword="null"/> writes the number of <see cref="Levels"/>.</summary>
    public uint? LevelCount { get; set; }

    public uint SupercompressionScheme { get; set; }

    /// <summary>Gets or sets the data format descriptor, its total size included (see <see cref="BasicDfd"/>).</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The tests edit the bytes of the descriptor in place to break a field.")]
    public byte[] Dfd { get; set; } = [];

    /// <summary>Gets the key/value pairs, in the order they are written: a key and its value bytes (a string value includes its NUL).</summary>
    public IList<KeyValuePair<string, byte[]>> KeyValues { get; } = [];

    /// <summary>Gets or sets the supercompression global data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The tests edit the bytes of the global data in place to break a field.")]
    public byte[] GlobalData { get; set; } = [];

    /// <summary>Gets the stored bytes of each level, the base level first.</summary>
    public IList<byte[]> Levels { get; } = [];

    /// <summary>Gets the uncompressed lengths of the levels; a missing entry writes the stored length (or 0 with BasisLZ).</summary>
    public IList<ulong> UncompressedLengths { get; } = [];

    /// <summary>Gets or sets the alignment of the levels: <see langword="null"/> uses lcm(block size, 4) without supercompression, 1 otherwise.</summary>
    public int? LevelAlignment { get; set; }

    /// <summary>Gets or sets the block size used for the alignment of the levels when <see cref="LevelAlignment"/> is <see langword="null"/>.</summary>
    public int BlockSize { get; set; } = 4;

    /// <summary>Gets or sets an action that edits the bytes of the file once it is assembled (to break a field).</summary>
    public Action<byte[]>? Edit { get; set; }

    /// <summary>Adds a string value with its NUL terminator.</summary>
    public Ktx2FileBuilder AddString(string key, string value)
    {
        KeyValues.Add(new(key, [.. Encoding.UTF8.GetBytes(value), 0]));
        return this;
    }

    /// <summary>
    /// Creates a basic descriptor block (section 5) with its total size: a color model, primaries, transfer function,
    /// flags, texel block dimensions, the bytes of the first planes, and samples (channel with qualifiers, bit offset, bit
    /// length, lower and upper bounds).
    /// </summary>
    public static byte[] BasicDfd(int colorModel, int primaries, int transfer, int flags, (int Width, int Height) block, int[] bytesPlane, params (int ChannelType, int BitOffset, int BitLength, uint Lower, uint Upper)[] samples)
    {
        var blockSize = 24 + (16 * samples.Length);
        var data = new byte[4 + blockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2u | ((uint)blockSize << 16));
        data[12] = (byte)colorModel;
        data[13] = (byte)primaries;
        data[14] = (byte)transfer;
        data[15] = (byte)flags;
        data[16] = (byte)(block.Width - 1);
        data[17] = (byte)(block.Height - 1);
        for (var i = 0; i < bytesPlane.Length; i++)
        {
            data[20 + i] = (byte)bytesPlane[i];
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = data.AsSpan(28 + (16 * i), 16);
            var (channelType, bitOffset, bitLength, lower, upper) = samples[i];
            BinaryPrimitives.WriteUInt32LittleEndian(sample, (uint)bitOffset | ((uint)(bitLength - 1) << 16) | ((uint)channelType << 24));
            BinaryPrimitives.WriteUInt32LittleEndian(sample[8..], lower);
            BinaryPrimitives.WriteUInt32LittleEndian(sample[12..], upper);
        }

        return data;
    }

    /// <summary>The descriptor of an uncompressed RGBSDA format whose samples are 8-bit arrays, in the order of <paramref name="channels"/> (0 R, 1 G, 2 B, 15 A).</summary>
    public static byte[] Rgba8Dfd(bool srgb, params int[] channels)
    {
        var samples = new (int, int, int, uint, uint)[channels.Length];
        for (var i = 0; i < channels.Length; i++)
        {
            samples[i] = (channels[i] | (srgb && channels[i] == 15 ? 0x10 : 0), i * 8, 8, 0, 255);
        }

        return BasicDfd(1, 1, srgb ? 2 : 1, 0, (1, 1), [channels.Length], samples);
    }

    /// <summary>Compresses a level with ZLIB (RFC 1950), as supercompression scheme 3 stores it.</summary>
    public static byte[] Zlib(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Assembles the file.</summary>
    public byte[] Build()
    {
        var levelCount = LevelCount ?? (uint)Levels.Count;
        var indexCount = Math.Max(1, Levels.Count);
        var levelIndexOffset = 80;
        var dfdOffset = levelIndexOffset + (24 * indexCount);
        var kvd = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var (key, value) in KeyValues)
        {
            var pair = new byte[Encoding.UTF8.GetByteCount(key) + 1 + value.Length];
            Encoding.UTF8.GetBytes(key, pair);
            value.CopyTo(pair, pair.Length - value.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)pair.Length);
            kvd.Write(length);
            kvd.Write(pair);
            while (kvd.Length % 4 != 0)
            {
                kvd.WriteByte(0);
            }
        }

        var kvdOffset = dfdOffset + Dfd.Length;
        var kvdBytes = kvd.ToArray();
        var metadataEnd = kvdOffset + kvdBytes.Length;
        var sgdOffset = GlobalData.Length == 0 ? 0 : Align(metadataEnd, 8);
        if (GlobalData.Length != 0)
        {
            metadataEnd = sgdOffset + GlobalData.Length;
        }

        var alignment = LevelAlignment ?? (SupercompressionScheme == 0 ? Lcm(BlockSize, 4) : 1);
        var offsets = new long[Levels.Count];
        var position = (long)metadataEnd;
        for (var level = Levels.Count - 1; level >= 0; level--)
        {
            position = Align(position, alignment);
            offsets[level] = position;
            position += Levels[level].Length;
        }

        var file = new byte[position];
        Identifier.CopyTo(file);
        var header = file.AsSpan(12);
        BinaryPrimitives.WriteUInt32LittleEndian(header, VkFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], TypeSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], PixelWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], PixelHeight);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], PixelDepth);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], LayerCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], FaceCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], levelCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[32..], SupercompressionScheme);
        BinaryPrimitives.WriteUInt32LittleEndian(header[36..], (uint)dfdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)Dfd.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], kvdBytes.Length == 0 ? 0 : (uint)kvdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], (uint)kvdBytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[52..], (ulong)sgdOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header[60..], (ulong)GlobalData.Length);
        for (var level = 0; level < Levels.Count; level++)
        {
            var entry = file.AsSpan(levelIndexOffset + (24 * level), 24);
            BinaryPrimitives.WriteUInt64LittleEndian(entry, (ulong)offsets[level]);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], (ulong)Levels[level].Length);
            var uncompressed = level < UncompressedLengths.Count ? UncompressedLengths[level] : SupercompressionScheme == 1 ? 0 : (ulong)Levels[level].Length;
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], uncompressed);
            Levels[level].CopyTo(file, offsets[level]);
        }

        Dfd.CopyTo(file, dfdOffset);
        kvdBytes.CopyTo(file, kvdOffset);
        GlobalData.CopyTo(file, sgdOffset);
        Edit?.Invoke(file);
        return file;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static long Align(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static int Lcm(int a, int b)
    {
        var (x, y) = (a, b);
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return a / x * b;
    }
}
