using System.Diagnostics;

namespace Meziantou.Framework;

internal static class EncodingExtensions
{
    public unsafe static byte[] GetBytes(this Encoding encoding, ReadOnlySpan<char> s)
    {
        var count = encoding.GetByteCount(s);
        var buffer = new byte[count];
        fixed (byte* ptr = buffer)
        fixed (char* ptr2 = s)
        {
            var result = encoding.GetBytes(ptr2, s.Length, ptr, count);
            Debug.Assert(result == count);
        }

        return buffer;
    }
}
