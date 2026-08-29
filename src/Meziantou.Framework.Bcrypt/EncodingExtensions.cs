using System.Diagnostics;

namespace Meziantou.Framework;

internal static class EncodingExtensions
{
    public static byte[] GetBytes(this Encoding encoding, ReadOnlySpan<char> s)
    {
        var count = encoding.GetByteCount(s);
        if (count is 0)
            return [];

        var buffer = new byte[count];
        var written = encoding.GetBytes(s, buffer);
        Debug.Assert(written == count);

        return buffer;
    }
}
