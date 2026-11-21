namespace Meziantou.Framework.Email.Internals;

/// <summary>Decodes the uuencode transfer encoding that some old clients use (<c>x-uuencode</c>).</summary>
internal static class MimeUuencode
{
    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length * 3 / 4);
        var started = false;
        while (!data.IsEmpty)
        {
            var end = data.IndexOf((byte)'\n');
            var line = end < 0 ? data : data[..end];
            data = end < 0 ? [] : data[(end + 1)..];
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            if (!started)
            {
                started = line.StartsWith("begin "u8);
                continue;
            }

            if (line.SequenceEqual("end"u8))
            {
                break;
            }

            if (line.IsEmpty)
            {
                continue;
            }

            var count = (line[0] - 0x20) & 0x3F;
            for (var i = 1; i + 3 < line.Length + 3 && count > 0; i += 4)
            {
                var c0 = Char(line, i);
                var c1 = Char(line, i + 1);
                var c2 = Char(line, i + 2);
                var c3 = Char(line, i + 3);
                var bytes = new[] { (byte)((c0 << 2) | (c1 >> 4)), (byte)((c1 << 4) | (c2 >> 2)), (byte)((c2 << 6) | c3) };
                for (var j = 0; j < 3 && count > 0; j++, count--)
                {
                    output.Add(bytes[j]);
                }
            }
        }

        return [.. output];

        static int Char(ReadOnlySpan<byte> line, int index) => index < line.Length ? (line[index] - 0x20) & 0x3F : 0;
    }
}
