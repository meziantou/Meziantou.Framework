using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coder of the B44 and B44A compressions (compression notes, section 7): the HALF samples of each channel in blocks
/// of 4x4 samples, each stored as 14 bytes (a base value, a shift and fifteen 6-bit differences of monotonic keys) or, for
/// a block of one value, 3 bytes; the UINT and FLOAT samples stored as they are.
/// </summary>
internal static class ExrB44
{
    private static ushort[]? s_fromPerceptual;
    private static ushort[]? s_toPerceptual;

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

    /// <summary>Encodes the planes of a chunk (section 7.1): the blocks of the half channels, the other channels as they are.</summary>
    /// <param name="planes">For each channel in name order, its sample lines from top to bottom.</param>
    /// <param name="destination">Receives the stored data.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <param name="flatBlocks">Whether blocks of a single value are stored in 3 bytes (B44A).</param>
    /// <returns>The length of the stored data, or -1 when it does not fit <paramref name="destination"/>.</returns>
    public static int Encode(ReadOnlySpan<byte> planes, Span<byte> destination, in ExrChunkLayout layout, bool flatBlocks)
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
                if (length > destination.Length - output)
                    return -1;

                planes.Slice(input, length).CopyTo(destination[output..]);
                input += length;
                output += length;
                continue;
            }

            var table = channel.PLinear ? GetToPerceptualTable() : null;
            for (var by = 0; by < ny; by += 4)
            {
                for (var bx = 0; bx < nx; bx += 4)
                {
                    // The samples past the edge repeat the last column and the last row of the plane
                    for (var r = 0; r < 4; r++)
                    {
                        var row = input + (2 * (Math.Min(by + r, ny - 1) * nx));
                        for (var x = 0; x < 4; x++)
                        {
                            var value = BinaryPrimitives.ReadUInt16LittleEndian(planes[(row + (2 * Math.Min(bx + x, nx - 1)))..]);
                            block[(4 * r) + x] = table is null ? value : table[value];
                        }
                    }

                    if (destination.Length - output < 14)
                        return -1;

                    output += EncodeBlock(block, destination[output..], flatBlocks, adjustBase: table is null);
                }
            }

            input += 2 * nx * ny;
        }

        return output;
    }

    /// <summary>
    /// Encodes the 16 half values of a block (sections 7.2 to 7.4): 3 bytes for a block of one value when
    /// <paramref name="flatBlocks"/>, 14 bytes otherwise.
    /// </summary>
    internal static int EncodeBlock(ReadOnlySpan<ushort> block, Span<byte> destination, bool flatBlocks, bool adjustBase)
    {
        Span<int> t = stackalloc int[16];
        var max = 0;
        for (var i = 0; i < 16; i++)
        {
            t[i] = ToKey(block[i]);
            max = Math.Max(max, t[i]);
        }

        Span<int> d = stackalloc int[16];
        Span<int> r = stackalloc int[15];
        var shift = -1;
        while (true)
        {
            shift++;
            var a = (1 << shift) - 1;
            for (var i = 0; i < 16; i++)
            {
                var x = max - t[i];
                d[i] = shift == 0 ? x : ((2 * x) + a + ((x >> shift) & 1)) >> (shift + 1);
            }

            r[0] = d[0] - d[4] + 32;
            r[1] = d[4] - d[8] + 32;
            r[2] = d[8] - d[12] + 32;
            r[3] = d[0] - d[1] + 32;
            r[4] = d[4] - d[5] + 32;
            r[5] = d[8] - d[9] + 32;
            r[6] = d[12] - d[13] + 32;
            r[7] = d[1] - d[2] + 32;
            r[8] = d[5] - d[6] + 32;
            r[9] = d[9] - d[10] + 32;
            r[10] = d[13] - d[14] + 32;
            r[11] = d[2] - d[3] + 32;
            r[12] = d[6] - d[7] + 32;
            r[13] = d[10] - d[11] + 32;
            r[14] = d[14] - d[15] + 32;
            var valid = true;
            foreach (var value in r)
            {
                if (value is < 0 or > 63)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
                break;
        }

        if (flatBlocks && r.IndexOfAnyExcept(32) < 0)
        {
            destination[0] = (byte)(t[0] >> 8);
            destination[1] = (byte)t[0];
            destination[2] = 0xFC;
            return 3;
        }

        var t0 = adjustBase ? (max - (d[0] << shift)) & 0xFFFF : t[0];
        destination[0] = (byte)(t0 >> 8);
        destination[1] = (byte)t0;

        // 96 bits: the shift, then r0 to r14, 6 bits each, most significant first
        UInt128 bits = (uint)shift;
        foreach (var value in r)
        {
            bits = (bits << 6) | (uint)value;
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[2..], (ulong)(bits >> 32));
        BinaryPrimitives.WriteUInt32BigEndian(destination[10..], (uint)bits);
        return 14;
    }

    /// <summary>Gets the table that converts the values of a pLinear channel before they are packed (section 7.6, table E).</summary>
    internal static ushort[] GetToPerceptualTable()
    {
        var table = Volatile.Read(ref s_toPerceptual);
        if (table is not null)
            return table;

        table = new ushort[65536];
        for (var x = 0; x < table.Length; x++)
        {
            if ((x & 0x7C00) == 0x7C00)
            {
                table[x] = 0;
                continue;
            }

            if (x is >= 0x558C and < 0x8000)
            {
                table[x] = 0x7BFF;
                continue;
            }

            // In single precision: the quotient by 8 rounded to a float, then the exponential, then the nearest half
            var value = (float)BitConverter.UInt16BitsToHalf((ushort)x);
            var exponential = (float)Math.Exp(value / 8f);
            table[x] = BitConverter.HalfToUInt16Bits((Half)exponential);
        }

        Interlocked.CompareExchange(ref s_toPerceptual, table, null);
        return s_toPerceptual;
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

    /// <summary>Maps a half bit pattern to a key that is monotonic in its value (section 7.2): infinities and values that are not numbers become the key of +0.</summary>
    private static int ToKey(ushort value)
    {
        if ((value & 0x7C00) == 0x7C00)
            return 0x8000;

        return (value & 0x8000) != 0 ? (ushort)~value : value | 0x8000;
    }

    /// <summary>Maps a monotonic key back to a half bit pattern (section 7.2).</summary>
    private static ushort ToHalf(ushort key) => (key & 0x8000) != 0 ? (ushort)(key & 0x7FFF) : (ushort)~key;

    private static InvalidImageContentException Truncated() => ExrFormat.Invalid("the B44 data of a chunk are truncated.");
}
