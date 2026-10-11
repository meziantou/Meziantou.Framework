namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A test-only GIF LZW encoder (GIF89a Appendix F), independent of the library decoder: a dictionary of (prefix code, index)
/// pairs, codes packed least-significant bit first, the code size growing when the next table code exceeds the current code
/// space, up to 12 bits.
/// </summary>
internal static class GifLzwTestEncoder
{
    public static byte[] Encode(ReadOnlySpan<byte> indices, int minimumCodeSize, GifLzwTestOptions options)
    {
        var clear = 1 << minimumCodeSize;
        var end = clear + 1;
        var codes = new List<(int Code, int Size)>();
        var table = new Dictionary<(int Prefix, byte Index), int>();
        var size = minimumCodeSize + 1;
        var next = end + 1;
        for (var i = 0; i < options.LeadingClearCodes; i++)
        {
            codes.Add((clear, size));
        }

        if (indices.IsEmpty)
        {
            codes.Add((end, size));
            return Pack([.. codes]);
        }

        int prefix = indices[0];
        var sinceClear = 0;
        for (var i = 1; i < indices.Length; i++)
        {
            var index = indices[i];
            if (table.TryGetValue((prefix, index), out var existing))
            {
                prefix = existing;
                continue;
            }

            codes.Add((prefix, size));
            if (next < 4096)
            {
                table[(prefix, index)] = next++;
                sinceClear++;
                if (next > 1 << size && size < 12)
                {
                    size++;
                }
            }

            if ((next == 4096 && options.ClearWhenFull) || (options.ClearInterval > 0 && sinceClear == options.ClearInterval))
            {
                codes.Add((clear, size));
                table.Clear();
                size = minimumCodeSize + 1;
                next = end + 1;
                sinceClear = 0;
            }

            prefix = index;
        }

        codes.Add((prefix, size));
        if (next >= 1 << size && size < 12)
        {
            size++; // the decoder adds an entry for the last data code too
        }

        if (options.EndCode)
        {
            codes.Add((end, size));
        }

        foreach (var code in options.CodesAfterEnd)
        {
            codes.Add((code, size));
        }

        return Pack([.. codes]);
    }

    public static byte[] Pack(params (int Code, int Size)[] codes)
    {
        var output = new List<byte>();
        ulong accumulator = 0;
        var count = 0;
        foreach (var (code, size) in codes)
        {
            accumulator |= (ulong)code << count;
            count += size;
            while (count >= 8)
            {
                output.Add((byte)accumulator);
                accumulator >>= 8;
                count -= 8;
            }
        }

        if (count > 0)
        {
            output.Add((byte)accumulator);
        }

        return [.. output];
    }
}
