namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>The PalmDOC compression of the text records of a book: a variant of LZ77 where each record is compressed on its own.</summary>
internal static class PalmDocCompression
{
    /// <summary>Decompresses a record and appends its text to the output.</summary>
    /// <exception cref="InvalidDataException">A back-reference points before the start of the record, or the text is too long.</exception>
    public static void Decompress(ReadOnlySpan<byte> input, MobiBuffer output)
    {
        var start = output.Length;
        var position = 0;
        while (position < input.Length)
        {
            var value = input[position++];
            switch (value)
            {
                // The next 1 to 8 bytes are copied as they are
                case >= 1 and <= 8:
                    var count = Math.Min(value, input.Length - position);
                    output.Append(input.Slice(position, count));
                    position += count;
                    break;

                case < 0x80:
                    output.Append(value);
                    break;

                // A space followed by a character from 0x40 to 0x7F
                case >= 0xC0:
                    output.Append((byte)' ');
                    output.Append((byte)(value ^ 0x80));
                    break;

                // A back-reference: 11 bits of distance and 3 bits of length (3 to 10 bytes)
                default:
                    if (position >= input.Length)
                        return;

                    var pair = ((value << 8) | input[position++]) & 0x3FFF;
                    var distance = pair >> 3;
                    var length = (pair & 0x7) + 3;
                    if (distance is 0 || distance > output.Length - start)
                        throw new InvalidDataException("A back-reference of the compressed text points before the start of its record");

                    output.AppendBackReference(distance, length);
                    break;
            }
        }
    }
}
