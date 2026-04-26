using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsLoginParser
{
    private const int FixedLength = 94;

    public static TdsLoginRequest Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FixedLength)
        {
            throw new InvalidDataException("Invalid LOGIN7 payload.");
        }

        var userName = ReadUnicodeString(payload, 40);
        var password = ReadPassword(payload, 44);
        var applicationName = ReadUnicodeString(payload, 48);
        var database = ReadUnicodeString(payload, 68);
        var sspi = ReadSspi(payload);

        return new TdsLoginRequest
        {
            UserName = userName,
            Password = password,
            ApplicationName = applicationName,
            Database = database,
            Sspi = sspi,
        };
    }

    private static string? ReadUnicodeString(ReadOnlySpan<byte> payload, int offsetIndex)
    {
        if (offsetIndex + 4 > payload.Length)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offsetIndex, 2));
        var charLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offsetIndex + 2, 2));
        var byteLength = checked(charLength * 2);
        if (offset + byteLength > payload.Length || byteLength == 0)
        {
            return null;
        }

        return Encoding.Unicode.GetString(payload.Slice(offset, byteLength));
    }

    private static string? ReadPassword(ReadOnlySpan<byte> payload, int offsetIndex)
    {
        if (offsetIndex + 4 > payload.Length)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offsetIndex, 2));
        var charLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offsetIndex + 2, 2));
        var byteLength = checked(charLength * 2);
        if (offset + byteLength > payload.Length || byteLength == 0)
        {
            return null;
        }

        var encoded = payload.Slice(offset, byteLength);
        var decoded = new byte[byteLength];
        for (var i = 0; i < encoded.Length; i++)
        {
            var value = encoded[i];
            var swapped = (byte)(((value & 0x0F) << 4) | ((value & 0xF0) >> 4));
            decoded[i] = (byte)(swapped ^ 0xA5);
        }

        return Encoding.Unicode.GetString(decoded);
    }

    private static byte[] ReadSspi(ReadOnlySpan<byte> payload)
    {
        if (78 + 4 > payload.Length)
        {
            return [];
        }

        var offset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(78, 2));
        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(80, 2));
        if (length == 0)
        {
            return [];
        }

        if (offset + length > payload.Length)
        {
            return [];
        }

        return payload.Slice(offset, length).ToArray();
    }
}
