namespace Meziantou.Framework.Pdf.Security;

/// <summary>The RC4 stream cipher of the security handlers of revisions 2 to 4 (ISO 32000-2, 7.6.3): not in the base class library.</summary>
internal static class Rc4
{
    public static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var output = new byte[data.Length];
        Transform(key, data, output);
        return output;
    }

    public static void Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, Span<byte> output)
    {
        Span<byte> state = stackalloc byte[256];
        for (var i = 0; i < 256; i++)
        {
            state[i] = (byte)i;
        }

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var x = 0;
        var y = 0;
        for (var k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[k] = (byte)(data[k] ^ state[(state[x] + state[y]) & 0xFF]);
        }
    }
}
