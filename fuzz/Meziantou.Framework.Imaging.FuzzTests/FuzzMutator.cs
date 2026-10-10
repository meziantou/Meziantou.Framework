using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Structure-agnostic mutations (bit flips, interesting values in 8/16/32-bit fields of either endianness, insertions,
/// deletions, duplicated or swapped ranges, truncation, splicing with another seed) plus format-aware fix-ups: PNG chunk CRCs
/// are recomputed after most mutations so that inputs reach the chunk payload parsers (IHDR fields, fcTL/acTL counts and
/// offsets, zlib/deflate streams, text and ICC decompression) instead of stopping at the checksum, and JPEG/GIF mutations
/// often target marker segments and block headers.
/// </summary>
internal static class FuzzMutator
{
    private static readonly uint[] InterestingValues =
    [
        0, 1, 2, 3, 7, 8, 9, 15, 16, 17, 31, 32, 63, 64, 127, 128, 255, 256, 257, 511, 512, 1000, 1023, 1024, 4095, 4096,
        32767, 32768, 65535, 65536, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF,
    ];

    public static byte[] Mutate(FuzzRandom random, byte[] seed, IReadOnlyList<byte[]> spliceSources)
    {
        // PNG: a quarter of the cases mutate the decompressed payload of a zlib chunk instead (filter bytes, scanlines, text,
        // ICC profiles), so that the unfiltering, Adam7, palette and metadata code is reached with well-formed deflate data
        if (IsPng(seed) && random.Chance(25) && TryMutatePngPayload(random, seed) is { } payloadMutation)
            return payloadMutation;

        var data = new List<byte>(seed);
        var count = 1 + random.Next(random.Chance(70) ? 2 : 8);
        for (var i = 0; i < count; i++)
        {
            MutateOnce(random, data, spliceSources);
        }

        var result = data.ToArray();
        if (IsPng(result) && random.Chance(85))
        {
            FixPngCrcs(result);
        }

        return result;
    }

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    /// <summary>
    /// Decompresses the zlib payload of one chunk (the IDAT stream, merged into the first IDAT chunk, an fdAT frame, iCCP, zTXt
    /// or a compressed iTXt), mutates the decompressed bytes, recompresses them and rebuilds the chunk with a valid CRC.
    /// </summary>
    private static byte[]? TryMutatePngPayload(FuzzRandom random, byte[] seed)
    {
        var chunks = new List<(string Type, byte[] Data)>();
        var offset = 8;
        while (offset + 12 <= seed.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(seed.AsSpan(offset));
            if (length > (uint)(seed.Length - offset - 12))
                return null;

            chunks.Add((Encoding.ASCII.GetString(seed, offset + 4, 4), seed.AsSpan(offset + 8, (int)length).ToArray()));
            offset += 12 + (int)length;
        }

        // Merge consecutive IDAT chunks so that the image datastream is one zlib stream
        var merged = new List<(string Type, byte[] Data)>();
        foreach (var chunk in chunks)
        {
            if (chunk.Type == "IDAT" && merged.Count > 0 && merged[^1].Type == "IDAT")
            {
                merged[^1] = ("IDAT", [.. merged[^1].Data, .. chunk.Data]);
            }
            else
            {
                merged.Add(chunk);
            }
        }

        var candidates = Enumerable.Range(0, merged.Count).Where(i => GetZlibOffset(merged[i].Type, merged[i].Data) >= 0).ToList();
        if (candidates.Count == 0)
            return null;

        var index = random.Pick(candidates);
        var (type, data) = merged[index];
        var zlibOffset = GetZlibOffset(type, data);
        byte[] inflated;
        try
        {
            using var input = new MemoryStream(data, zlibOffset, data.Length - zlibOffset);
            using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            inflated = output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var payload = new List<byte>(inflated);
        var count = 1 + random.Next(4);
        for (var i = 0; i < count; i++)
        {
            MutateOnce(random, payload, [inflated]);
        }

        using (var compressed = new MemoryStream())
        {
            compressed.Write(data, 0, zlibOffset);
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write([.. payload]);
            }

            merged[index] = (type, compressed.ToArray());
        }

        using var result = new MemoryStream();
        result.Write(seed, 0, 8);
        var header = new byte[8];
        var crc = new byte[4];
        foreach (var (chunkType, chunkData) in merged)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)chunkData.Length);
            Encoding.ASCII.GetBytes(chunkType, header.AsSpan(4));
            result.Write(header);
            result.Write(chunkData);
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. header.AsSpan(4), .. chunkData]));
            result.Write(crc);
        }

        return result.ToArray();
    }

    /// <summary>Gets the offset of the zlib stream inside a chunk payload, or -1 when the chunk carries none.</summary>
    private static int GetZlibOffset(string type, byte[] data)
    {
        switch (type)
        {
            case "IDAT":
                return 0;
            case "fdAT":
                return data.Length > 4 ? 4 : -1;
            case "iCCP":
            case "zTXt":
            {
                var separator = Array.IndexOf(data, (byte)0);
                return separator >= 0 && separator + 2 < data.Length ? separator + 2 : -1;
            }

            case "iTXt":
            {
                // keyword \0 compression-flag method language \0 translated \0 text
                var keyword = Array.IndexOf(data, (byte)0);
                if (keyword < 0 || keyword + 3 >= data.Length || data[keyword + 1] != 1)
                    return -1;

                var language = Array.IndexOf(data, (byte)0, keyword + 3);
                var translated = language < 0 ? -1 : Array.IndexOf(data, (byte)0, language + 1);
                return translated >= 0 && translated + 1 < data.Length ? translated + 1 : -1;
            }

            default:
                return -1;
        }
    }

    /// <summary>Recomputes the CRC of every complete chunk (lengths are trusted as they are).</summary>
    public static void FixPngCrcs(byte[] data)
    {
        var offset = 8;
        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            if (length > (uint)(data.Length - offset - 12))
                return;

            var crcOffset = offset + 8 + (int)length;
            var crc = Crc32(data.AsSpan(offset + 4, 4 + (int)length));
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(crcOffset), crc);
            offset = crcOffset + 4;
        }
    }

    private static void MutateOnce(FuzzRandom random, List<byte> data, IReadOnlyList<byte[]> spliceSources)
    {
        if (data.Count == 0)
        {
            data.Add(random.NextByte());
            return;
        }

        var position = PickPosition(random, data);
        switch (random.Next(12))
        {
            case 0: // bit flip
            case 1:
                data[position] ^= (byte)(1 << random.Next(8));
                break;

            case 2: // random byte
                data[position] = random.NextByte();
                break;

            case 3: // interesting 8-bit value
                data[position] = (byte)random.Pick(InterestingValues);
                break;

            case 4: // interesting 16-bit value
            case 5: // interesting 32-bit value
            {
                var size = random.Chance(50) ? 2 : 4;
                var value = random.Pick(InterestingValues);
                if (random.Chance(30))
                {
                    value += (uint)random.Next(-2, 3);
                }

                var bytes = new byte[4];
                if (random.Chance(75))
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
                }

                var source = size == 2 ? (random.Chance(75) ? bytes.AsSpan(2, 2) : bytes.AsSpan(0, 2)) : bytes;
                for (var i = 0; i < source.Length && position + i < data.Count; i++)
                {
                    data[position + i] = source[i];
                }

                break;
            }

            case 6: // insert bytes
            {
                var length = 1 + random.Next(random.Chance(80) ? 4 : 64);
                var fill = random.Chance(50) ? (byte?)random.NextByte() : null;
                data.InsertRange(position, Enumerable.Range(0, length).Select(_ => fill ?? random.NextByte()));
                break;
            }

            case 7: // delete bytes
            {
                var length = Math.Min(data.Count - position, 1 + random.Next(random.Chance(80) ? 4 : 128));
                data.RemoveRange(position, length);
                break;
            }

            case 8: // duplicate a range
            {
                var length = Math.Min(data.Count - position, 1 + random.Next(64));
                var copy = data.GetRange(position, length);
                data.InsertRange(Math.Min(data.Count, position + length + random.Next(16)), copy);
                break;
            }

            case 9: // truncate
                if (data.Count > 1)
                {
                    var keep = 1 + random.Next(data.Count - 1);
                    data.RemoveRange(keep, data.Count - keep);
                }

                break;

            case 10: // splice the tail of another seed
            {
                var other = random.Pick(spliceSources);
                if (other.Length > 0)
                {
                    var start = random.Next(other.Length);
                    var length = Math.Min(other.Length - start, 1 + random.Next(256));
                    data.RemoveRange(position, Math.Min(data.Count - position, random.Next(256)));
                    data.InsertRange(position, other.Skip(start).Take(length));
                }

                break;
            }

            default: // overwrite a range with a repeated byte
            {
                var length = Math.Min(data.Count - position, 1 + random.Next(32));
                var value = random.Chance(50) ? (byte)0 : random.Chance(50) ? (byte)0xFF : random.NextByte();
                for (var i = 0; i < length; i++)
                {
                    data[position + i] = value;
                }

                break;
            }
        }
    }

    /// <summary>Prefers positions near structure headers: PNG chunk headers and IHDR/fcTL fields, JPEG marker segments, GIF headers and block introducers.</summary>
    private static int PickPosition(FuzzRandom random, List<byte> data)
    {
        if (random.Chance(40))
            return random.Next(data.Count);

        var candidates = new List<int>();
        for (var i = 0; i + 1 < data.Count && candidates.Count < 4096; i++)
        {
            var b = data[i];
            var next = data[i + 1];
            if ((b == 0xFF && next is not 0x00 and not 0xFF) // JPEG markers
                || b is 0x21 or 0x2C or 0x3B // GIF extension, image descriptor, trailer
                || (i >= 4 && IsAsciiLetter(data[i]) && IsAsciiLetter(data[i - 1]) && IsAsciiLetter(data[i - 2]) && IsAsciiLetter(data[i - 3]))) // PNG chunk types
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count == 0)
            return random.Next(data.Count);

        var anchor = random.Pick(candidates);
        return Math.Clamp(anchor + random.Next(-4, 24), 0, data.Count - 1);
    }

    /// <summary>Bitwise CRC-32 (ISO 3309, as used by PNG).</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static bool IsAsciiLetter(byte value) => value is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z');
}
