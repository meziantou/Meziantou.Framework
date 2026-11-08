using System.Buffers.Binary;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsSspiMessage
{
    private const byte SspiToken = 0xED;

    /// <summary>Creates an SSPI token (MS-TDS 2.2.7.22): the token type followed by the SSPI data as a US_VARBYTE.</summary>
    public static byte[] CreateSspiToken(ReadOnlySpan<byte> sspiData)
    {
        if (sspiData.Length > ushort.MaxValue)
        {
            throw new ArgumentException("An SSPI token cannot exceed 65535 bytes.", nameof(sspiData));
        }

        var token = new byte[3 + sspiData.Length];
        token[0] = SspiToken;
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(1, 2), (ushort)sspiData.Length);
        sspiData.CopyTo(token.AsSpan(3));
        return token;
    }
}
