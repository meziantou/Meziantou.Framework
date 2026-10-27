using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of the B44 and B44A compressions (compression notes, section 7): the HALF samples of each channel in blocks
/// of 4x4 samples, each stored as 14 bytes (a base value, a shift and fifteen 6-bit differences of monotonic keys) or, for
/// a block of one value, 3 bytes; the UINT and FLOAT samples stored as they are.
/// </summary>
internal static class ExrB44
{
    private static ushort[]? s_fromPerceptual;

    /// <summary>Gets the size of the B44 data of a chunk whose blocks all have 14 bytes.</summary>
    public static long GetMaxStoredLength(in ExrChunkLayout layout)
    {
        long length = 0;
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var nx = layout.GetSampleCount(c);
            var ny = layout.GetLineCount(c);
            length += layout.Channels[c].PixelType == ExrPixelType.Half ? 14L * ((nx + 3) / 4) * ((ny + 3) / 4) : 4L * nx * ny;
        }

        return length;
    }

    /// <summary>Decodes a chunk into planes: for each channel in name order, its sample lines from top to bottom.</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="planes">Receives the planes (as many bytes as the uncompressed data of the chunk).</param>
    /// <param name="layout">The geometry of the chunk.</param>
    public static void Decode(ReadOnlySpan<byte> stored, Span<byte> planes, in ExrChunkLayout layout)
    {
        var input = 0;
        var output = 0;
        Span<ushort> block = stackalloc ushort[16];
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var channel = layout.Channels[c];
            var nx = layout.GetSampleCount(c);
            var ny = layout.GetLineCount(c);
            if (channel.PixelType != ExrPixelType.Half)
            {
                var length = 4 * nx * ny;
                if (length > stored.Length - input)
                    throw Truncated();

                stored.Slice(input, length).CopyTo(planes[output..]);
                input += length;
                output += length;
                continue;
            }

            var table = channel.PLinear ? GetFromPerceptualTable() : null;
            for (var by = 0; by < ny; by += 4)
            {
                for (var bx = 0; bx < nx; bx += 4)
                {
                    if (stored.Length - input < 3)
                        throw Truncated();

                    if (stored[input + 2] >= 52)
                    {
                        // A block of one value (13 << 2 or more is not a shift of a 14-byte block)
                        block.Fill(ToHalf((ushort)((stored[input] << 8) | stored[input + 1])));
                        input += 3;
                    }
                    else
                    {
                        if (stored.Length - input < 14)
                            throw Truncated();

                        DecodeBlock(stored.Slice(input, 14), block);
                        input += 14;
                    }

                    if (table is not null)
                    {
                        for (var i = 0; i < block.Length; i++)
                        {
                            block[i] = table[block[i]];
                        }
                    }

                    // Only the samples of the plane are written
                    for (var r = 0; r < 4 && by + r < ny; r++)
                    {
                        var row = output + (2 * (((by + r) * nx) + bx));
                        for (var x = 0; x < 4 && bx + x < nx; x++)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(planes[(row + (2 * x))..], block[(4 * r) + x]);
                        }
                    }
                }
            }

            output += 2 * nx * ny;
        }
    }

    /// <summary>Gets the table that converts the decoded values of a pLinear channel back (section 7.6, table L).</summary>
    internal static ushort[] GetFromPerceptualTable()
    {
        var table = Volatile.Read(ref s_fromPerceptual);
        if (table is not null)
            return table;

        table = new ushort[65536];
        for (var x = 0; x < table.Length; x++)
        {
            if ((x & 0x7C00) == 0x7C00 || x > 0x8000)
            {
                table[x] = 0;
                continue;
            }

            // In single precision: the logarithm rounded to a float, then the product by 8, then the nearest half
            var value = (float)BitConverter.UInt16BitsToHalf((ushort)x);
            var logarithm = (float)Math.Log(value);
            table[x] = BitConverter.HalfToUInt16Bits((Half)(8f * logarithm));
        }

        Interlocked.CompareExchange(ref s_fromPerceptual, table, null);
        return s_fromPerceptual;
    }

    /// <summary>Decodes the 16 keys of a 14-byte block and maps them back to half values (sections 7.5 and 7.2).</summary>
    private static void DecodeBlock(ReadOnlySpan<byte> data, Span<ushort> block)
    {
        var t0 = (data[0] << 8) | data[1];

        // 96 bits: the shift, then r0 to r14, 6 bits each, most significant first
        var bits = ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(data[2..]) << 32) | BinaryPrimitives.ReadUInt32BigEndian(data[10..]);
        Span<int> fields = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            fields[i] = (int)(uint)((bits >> (90 - (6 * i))) & 0x3F);
        }

        var shift = fields[0];
        int Delta(int r) => ((r - 32) << shift) & 0xFFFF;
        Span<int> t = stackalloc int[16];
        t[0] = t0;
        t[4] = (t[0] + Delta(fields[1])) & 0xFFFF;
        t[8] = (t[4] + Delta(fields[2])) & 0xFFFF;
        t[12] = (t[8] + Delta(fields[3])) & 0xFFFF;
        t[1] = (t[0] + Delta(fields[4])) & 0xFFFF;
        t[5] = (t[4] + Delta(fields[5])) & 0xFFFF;
        t[9] = (t[8] + Delta(fields[6])) & 0xFFFF;
        t[13] = (t[12] + Delta(fields[7])) & 0xFFFF;
        t[2] = (t[1] + Delta(fields[8])) & 0xFFFF;
        t[6] = (t[5] + Delta(fields[9])) & 0xFFFF;
        t[10] = (t[9] + Delta(fields[10])) & 0xFFFF;
        t[14] = (t[13] + Delta(fields[11])) & 0xFFFF;
        t[3] = (t[2] + Delta(fields[12])) & 0xFFFF;
        t[7] = (t[6] + Delta(fields[13])) & 0xFFFF;
        t[11] = (t[10] + Delta(fields[14])) & 0xFFFF;
        t[15] = (t[14] + Delta(fields[15])) & 0xFFFF;
        for (var i = 0; i < 16; i++)
        {
            block[i] = ToHalf((ushort)t[i]);
        }
    }

    /// <summary>Maps a monotonic key back to a half bit pattern (section 7.2).</summary>
    private static ushort ToHalf(ushort key) => (key & 0x8000) != 0 ? (ushort)(key & 0x7FFF) : (ushort)~key;

    private static InvalidImageContentException Truncated() => ExrFormat.Invalid("the B44 data of a chunk are truncated.");
}
