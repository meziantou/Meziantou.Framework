namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>RunLengthDecode</c> filter (ISO 32000-2, 7.4.5): a length byte from 0 to 127 is followed by that many plus one
/// bytes to copy, from 129 to 255 by one byte to repeat 257 minus that many times, and 128 ends the data.
/// </summary>
internal static class RunLengthFilter
{
    public static byte[] Decode(ReadOnlySpan<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, input.Length * 2);
        var position = 0;
        while (position < input.Length)
        {
            var length = input[position++];
            if (length == 128)
                break;

            if (length < 128)
            {
                var count = Math.Min(length + 1, input.Length - position);
                output.Write(input.Slice(position, count));
                position += count;
            }
            else
            {
                if (position >= input.Length)
                    break;

                output.WriteRepeated(input[position++], 257 - length);
            }
        }

        return output.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length + (input.Length / 128) + 2);
        var position = 0;
        while (position < input.Length)
        {
            var run = 1;
            while (position + run < input.Length && run < 128 && input[position + run] == input[position])
            {
                run++;
            }

            if (run >= 2)
            {
                output.Add((byte)(257 - run));
                output.Add(input[position]);
                position += run;
                continue;
            }

            // A literal sequence up to the next run of at least two equal bytes
            var literal = 1;
            while (position + literal < input.Length && literal < 128 && (position + literal + 1 >= input.Length || input[position + literal] != input[position + literal + 1]))
            {
                literal++;
            }

            output.Add((byte)(literal - 1));
            for (var i = 0; i < literal; i++)
            {
                output.Add(input[position + i]);
            }

            position += literal;
        }

        output.Add(128);
        return [.. output];
    }
}
