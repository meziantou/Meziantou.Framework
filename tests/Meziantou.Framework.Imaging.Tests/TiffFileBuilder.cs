using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Assembles TIFF and BigTIFF files byte by byte for the decoder tests, independently of the encoder: the
/// header, the strip or tile data, and one image file directory per page, with full control over the tags, the byte
/// order, the offset size and the links between directories (so that malformed chains can be built too).
/// </summary>
internal sealed class TiffFileBuilder
{
    private readonly List<TiffPageSpec> _pages = [];

    /// <summary>Gets a value indicating whether the file uses the <c>MM</c> byte order.</summary>
    public bool BigEndian { get; init; }

    /// <summary>Gets a value indicating whether the file is a BigTIFF (magic 43, 64-bit offsets).</summary>
    public bool BigTiff { get; init; }

    /// <summary>Gets the number of bytes inserted between the header and the first directory (to exercise non-trivial offsets).</summary>
    public int Gap { get; init; }

    /// <summary>Gets an override of the first-directory offset written in the header, for malformed files.</summary>
    public ulong? FirstDirectoryOffsetOverride { get; init; }

    public TiffFileBuilder AddPage(TiffPageSpec page)
    {
        _pages.Add(page);
        return this;
    }

    public byte[] Build()
    {
        var buffer = new List<byte>();
        var headerLength = BigTiff ? 16 : 8;
        buffer.AddRange(BigEndian ? "MM"u8 : "II"u8);
        AddUInt16(buffer, (ushort)(BigTiff ? 43 : 42));
        if (BigTiff)
        {
            AddUInt16(buffer, 8);
            AddUInt16(buffer, 0);
        }

        for (var i = 0; i < OffsetSize; i++)
        {
            buffer.Add(0);
        }

        buffer.AddRange(new byte[Gap]);

        var pointerOffset = (long)(BigTiff ? 8 : 4);

        foreach (var page in _pages)
        {
            Align(buffer);
            var blocks = page.BuildBlocks();
            var offsets = new List<ulong>();
            var byteCounts = new List<ulong>();
            foreach (var block in blocks)
            {
                Align(buffer);
                offsets.Add((ulong)buffer.Count);
                byteCounts.Add((ulong)block.Length);
                buffer.AddRange(block);
            }

            Align(buffer);
            var directoryOffset = buffer.Count;
            if (_firstDirectoryOffset == 0)
            {
                _firstDirectoryOffset = directoryOffset;
            }

            WriteDirectory(buffer, page, offsets, byteCounts);
            PatchOffset(buffer, pointerOffset, (ulong)directoryOffset);
            pointerOffset = _lastNextPointerOffset;
        }

        if (FirstDirectoryOffsetOverride is { } overridden)
        {
            PatchOffset(buffer, headerLength - OffsetSize, overridden);
        }

        if (NextDirectoryOverride is { } nextOverride)
        {
            PatchOffset(buffer, _lastNextPointerOffset, nextOverride);
        }
        else if (CycleToFirstDirectory)
        {
            PatchOffset(buffer, _lastNextPointerOffset, (ulong)_firstDirectoryOffset);
        }

        return [.. buffer];
    }

    /// <summary>Gets an override of the next-directory pointer of the last page (to build a malformed chain).</summary>
    public ulong? NextDirectoryOverride { get; init; }

    /// <summary>Gets a value indicating whether the last page points back at the first directory, making the chain cyclic.</summary>
    public bool CycleToFirstDirectory { get; init; }

    private int OffsetSize => BigTiff ? 8 : 4;

    private long _lastNextPointerOffset;
    private long _firstDirectoryOffset;

    private void WriteDirectory(List<byte> buffer, TiffPageSpec page, List<ulong> offsets, List<ulong> byteCounts)
    {
        var offsetType = BigTiff ? (ushort)16 : (ushort)4;
        var fields = new List<TiffFieldSpec>
        {
            TiffFieldSpec.Long(256, (uint)page.Width),
            TiffFieldSpec.Long(257, (uint)page.Height),
            TiffFieldSpec.Shorts(258, Enumerable.Repeat((ushort)page.BitsPerSample, page.SamplesPerPixel).ToArray()),
            TiffFieldSpec.Short(259, (ushort)page.Compression),
            TiffFieldSpec.Short(262, (ushort)page.Photometric),
            TiffFieldSpec.Short(277, (ushort)page.SamplesPerPixel),
        };

        if (page.IsTiled)
        {
            fields.Add(TiffFieldSpec.Long(322, (uint)page.TileWidth!.Value));
            fields.Add(TiffFieldSpec.Long(323, (uint)page.TileLength!.Value));
            fields.Add(new TiffFieldSpec(324, offsetType, [.. offsets]));
            if (!page.OmitByteCounts)
            {
                fields.Add(new TiffFieldSpec(325, offsetType, [.. byteCounts]));
            }
        }
        else
        {
            fields.Add(new TiffFieldSpec(273, offsetType, [.. offsets]));
            fields.Add(TiffFieldSpec.Long(278, (uint)page.RowsPerStrip));
            if (!page.OmitByteCounts)
            {
                fields.Add(new TiffFieldSpec(279, offsetType, [.. byteCounts]));
            }
        }

        if (page.Predictor is { } predictor)
        {
            fields.Add(TiffFieldSpec.Short(317, (ushort)predictor));
        }

        if (page.PlanarConfiguration is { } planar)
        {
            fields.Add(TiffFieldSpec.Short(284, (ushort)planar));
        }

        if (page.Orientation is { } orientation)
        {
            fields.Add(TiffFieldSpec.Short(274, (ushort)orientation));
        }

        if (page.ExtraSamples is { Length: > 0 } extra)
        {
            fields.Add(TiffFieldSpec.Shorts(338, [.. extra.Select(static value => (ushort)value)]));
        }

        if (page.SampleFormat is { } sampleFormat)
        {
            fields.Add(TiffFieldSpec.Shorts(339, [.. Enumerable.Repeat((ushort)sampleFormat, page.SamplesPerPixel)]));
        }

        if (page.Resolution is { } resolution)
        {
            fields.Add(TiffFieldSpec.Rational(282, resolution.XNumerator, resolution.Denominator));
            fields.Add(TiffFieldSpec.Rational(283, resolution.YNumerator, resolution.Denominator));
            fields.Add(TiffFieldSpec.Short(296, (ushort)resolution.Unit));
        }

        foreach (var field in page.ExtraFields)
        {
            fields.Add(field);
        }

        fields.Sort(static (left, right) => left.Tag.CompareTo(right.Tag));
        if (page.OmitPhotometric)
        {
            fields.RemoveAll(static field => field.Tag == 262);
        }

        var directoryOffset = buffer.Count;
        var entryLength = BigTiff ? 20 : 12;
        var countLength = BigTiff ? 8 : 2;
        var tableLength = countLength + (fields.Count * entryLength) + OffsetSize;
        var valuesStart = directoryOffset + tableLength;

        if (BigTiff)
        {
            AddUInt64(buffer, (ulong)fields.Count);
        }
        else
        {
            AddUInt16(buffer, (ushort)fields.Count);
        }

        var values = new List<byte>();
        foreach (var field in fields)
        {
            AddUInt16(buffer, field.Tag);
            AddUInt16(buffer, field.Type);
            var count = field.GetCount();
            if (BigTiff)
            {
                AddUInt64(buffer, count);
            }
            else
            {
                AddUInt32(buffer, (uint)count);
            }

            var payload = field.Serialize(BigEndian, BigTiff);
            if (payload.Length <= OffsetSize)
            {
                buffer.AddRange(payload);
                buffer.AddRange(new byte[OffsetSize - payload.Length]);
            }
            else
            {
                while (values.Count % 8 != 0)
                {
                    values.Add(0);
                }

                AddOffset(buffer, (ulong)(valuesStart + values.Count));
                values.AddRange(payload);
            }
        }

        _lastNextPointerOffset = buffer.Count;
        for (var i = 0; i < OffsetSize; i++)
        {
            buffer.Add(0);
        }

        buffer.AddRange(values);
    }

    private void PatchOffset(List<byte> buffer, long position, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (BigTiff)
        {
            if (BigEndian)
            {
                BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            }
        }
        else if (BigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        }

        for (var i = 0; i < OffsetSize; i++)
        {
            buffer[(int)position + i] = bytes[i];
        }
    }

    private static void Align(List<byte> buffer)
    {
        while (buffer.Count % 8 != 0)
        {
            buffer.Add(0);
        }
    }

    private void AddOffset(List<byte> buffer, ulong value)
    {
        if (BigTiff)
        {
            AddUInt64(buffer, value);
        }
        else
        {
            AddUInt32(buffer, (uint)value);
        }
    }

    private void AddUInt16(List<byte> buffer, ushort value)
    {
        var bytes = new byte[2];
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        }

        buffer.AddRange(bytes);
    }

    private void AddUInt32(List<byte> buffer, uint value)
    {
        var bytes = new byte[4];
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }

        buffer.AddRange(bytes);
    }

    private void AddUInt64(List<byte> buffer, ulong value)
    {
        var bytes = new byte[8];
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        }

        buffer.AddRange(bytes);
    }

    /// <summary>Compresses one block as a zlib datastream, like the Deflate compression of TIFF.</summary>
    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
