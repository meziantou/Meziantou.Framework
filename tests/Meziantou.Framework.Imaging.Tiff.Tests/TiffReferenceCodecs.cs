using System.Text;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Reference encoders for the decoder tests, independent of the encoders of the library and written for clarity, not
/// speed: the LZW algorithm of TIFF 6.0 (section 13) as the specification words it, with a table of strings; PackBits
/// (section 9) with a different run policy than the library; a packer of bit strings for the CCITT code words the
/// tests spell out from the tables of ITU-T T.4 and T.6; and the floating-point predictor of TIFF Technical Note 3, in
/// both directions.
/// </summary>
internal static class TiffReferenceCodecs
{
    /// <summary>
    /// Compresses a strip as the pseudo-code of TIFF 6.0 does: the table maps strings to codes, a code is written when
    /// the next character makes a string the table does not have, the code size grows after the entries 511, 1023 and
    /// 2047 are added, and the table is cleared when the entry 4093 is added.
    /// </summary>
    public static byte[] EncodeLzw(ReadOnlySpan<byte> data)
    {
        var bits = new StringBuilder();
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        int nextCode;
        int codeSize;
        void Initialize()
        {
            table.Clear();
            for (var i = 0; i < 256; i++)
            {
                table[((char)i).ToString()] = i;
            }

            nextCode = 258;
            codeSize = 9;
        }

        void Write(int code) => bits.Append(Convert.ToString(code, 2).PadLeft(codeSize, '0'));

        void AfterEntry()
        {
            nextCode++;
            if (nextCode == 4094)
            {
                Write(256);
                Initialize();
            }
            else if (nextCode is 512 or 1024 or 2048)
            {
                codeSize++;
            }
        }

        Initialize();
        Write(256);
        var omega = "";
        foreach (var value in data)
        {
            var k = (char)value;
            if (table.ContainsKey(omega + k))
            {
                omega += k;
                continue;
            }

            Write(table[omega]);
            table[omega + k] = nextCode;
            AfterEntry();
            omega = k.ToString();
        }

        if (omega.Length > 0)
        {
            // A decoder adds an entry after each code it reads, the last one included
            Write(table[omega]);
            AfterEntry();
        }

        Write(257);
        return PackBitString(bits.ToString());
    }

    /// <summary>Compresses a block row by row: every run of two or more equal bytes is a repeated run, everything else a literal run of one byte.</summary>
    public static byte[] EncodePackBits(ReadOnlySpan<byte> block, int rowLength)
    {
        var output = new List<byte>();
        for (var start = 0; start < block.Length; start += rowLength)
        {
            var row = block.Slice(start, Math.Min(rowLength, block.Length - start));
            var position = 0;
            while (position < row.Length)
            {
                var run = 1;
                while (run < 128 && position + run < row.Length && row[position + run] == row[position])
                {
                    run++;
                }

                if (run >= 2)
                {
                    output.Add(unchecked((byte)(sbyte)(1 - run)));
                    output.Add(row[position]);
                }
                else
                {
                    output.Add(0);
                    output.Add(row[position]);
                }

                position += run;
            }
        }

        return [.. output];
    }

    /// <summary>Packs a string of 0 and 1 (spaces are ignored) most significant bit first, padding the last byte with zeros.</summary>
    public static byte[] PackBitString(string bits)
    {
        var digits = bits.Replace(" ", "", StringComparison.Ordinal);
        var result = new byte[(digits.Length + 7) / 8];
        for (var i = 0; i < digits.Length; i++)
        {
            if (digits[i] == '1')
            {
                result[i / 8] |= (byte)(0x80 >> (i % 8));
            }
            else if (digits[i] != '0')
            {
                throw new ArgumentException("A bit string holds 0, 1 and spaces only.", nameof(bits));
            }
        }

        return result;
    }

    /// <summary>
    /// Applies the floating-point predictor of TIFF Technical Note 3 to one row: the bytes of the samples are regrouped by
    /// significance (the most significant byte of every sample, then the next one of every sample, and so on, whatever the
    /// byte order of the file), then each byte is replaced by its difference from the byte one pixel of samples before it.
    /// </summary>
    /// <param name="row">The samples of the row, in the byte order of the file.</param>
    /// <param name="samplesPerPixel">The number of samples of a pixel.</param>
    /// <param name="bytesPerSample">The number of bytes of a sample.</param>
    /// <param name="bigEndian">Whether the samples are big-endian.</param>
    /// <returns>The stored row.</returns>
    public static byte[] ApplyFloatingPointPredictor(ReadOnlySpan<byte> row, int samplesPerPixel, int bytesPerSample, bool bigEndian)
    {
        var count = row.Length / bytesPerSample;
        var planes = new byte[row.Length];
        for (var sample = 0; sample < count; sample++)
        {
            for (var significance = 0; significance < bytesPerSample; significance++)
            {
                var source = (sample * bytesPerSample) + (bigEndian ? significance : bytesPerSample - 1 - significance);
                planes[(significance * count) + sample] = row[source];
            }
        }

        var result = new byte[row.Length];
        for (var i = 0; i < planes.Length; i++)
        {
            result[i] = i < samplesPerPixel ? planes[i] : (byte)(planes[i] - planes[i - samplesPerPixel]);
        }

        return result;
    }

    /// <summary>Undoes <see cref="ApplyFloatingPointPredictor"/>: the samples of a stored row, in the byte order of the file.</summary>
    /// <param name="stored">The stored row.</param>
    /// <param name="samplesPerPixel">The number of samples of a pixel.</param>
    /// <param name="bytesPerSample">The number of bytes of a sample.</param>
    /// <param name="bigEndian">Whether the samples are big-endian.</param>
    /// <returns>The samples.</returns>
    public static byte[] UndoFloatingPointPredictor(ReadOnlySpan<byte> stored, int samplesPerPixel, int bytesPerSample, bool bigEndian)
    {
        var planes = new byte[stored.Length];
        for (var i = 0; i < stored.Length; i++)
        {
            planes[i] = i < samplesPerPixel ? stored[i] : (byte)(stored[i] + planes[i - samplesPerPixel]);
        }

        var count = stored.Length / bytesPerSample;
        var result = new byte[stored.Length];
        for (var sample = 0; sample < count; sample++)
        {
            for (var significance = 0; significance < bytesPerSample; significance++)
            {
                var target = (sample * bytesPerSample) + (bigEndian ? significance : bytesPerSample - 1 - significance);
                result[target] = planes[(significance * count) + sample];
            }
        }

        return result;
    }

    /// <summary>Reverses the bits of every byte: the stored form of data whose <c>FillOrder</c> is 2.</summary>
    public static byte[] ReverseBits(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                value |= ((data[i] >> bit) & 1) << (7 - bit);
            }

            result[i] = (byte)value;
        }

        return result;
    }
}
