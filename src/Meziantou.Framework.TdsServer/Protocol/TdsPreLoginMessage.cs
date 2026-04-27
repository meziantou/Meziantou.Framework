using System.Buffers.Binary;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsPreLoginMessage
{
    private const byte VersionOption = 0x00;
    private const byte EncryptionOption = 0x01;
    private const byte TerminatorOption = 0xFF;

    public static byte[] CreateResponse(ReadOnlySpan<byte> requestPayload, TdsPreLoginEncryptionMode encryptionMode)
    {
        if (requestPayload.IsEmpty)
        {
            return CreateMinimalResponse(encryptionMode);
        }

        var payload = requestPayload.ToArray();
        payload[GetEncryptionValueOffset(requestPayload)] = (byte)encryptionMode;
        return payload;
    }

    public static TdsPreLoginEncryptionMode ParseEncryptionMode(ReadOnlySpan<byte> payload)
    {
        return payload[GetEncryptionValueOffset(payload)] switch
        {
            (byte)TdsPreLoginEncryptionMode.Off => TdsPreLoginEncryptionMode.Off,
            (byte)TdsPreLoginEncryptionMode.On => TdsPreLoginEncryptionMode.On,
            (byte)TdsPreLoginEncryptionMode.NotSupported => TdsPreLoginEncryptionMode.NotSupported,
            (byte)TdsPreLoginEncryptionMode.Required => TdsPreLoginEncryptionMode.Required,
            _ => throw new InvalidDataException("Unsupported PRELOGIN encryption mode."),
        };
    }

    private static int GetEncryptionValueOffset(ReadOnlySpan<byte> payload)
    {
        var position = 0;
        while (position < payload.Length)
        {
            var option = payload[position];
            if (option == TerminatorOption)
            {
                break;
            }

            if (position + 5 > payload.Length)
            {
                throw new InvalidDataException("Invalid PRELOGIN payload header.");
            }

            var offset = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(position + 1, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(position + 3, 2));
            if (offset + length > payload.Length)
            {
                throw new InvalidDataException("Invalid PRELOGIN payload segment.");
            }

            if (option == EncryptionOption)
            {
                if (length != 1)
                {
                    throw new InvalidDataException("Invalid PRELOGIN encryption option length.");
                }

                return offset;
            }

            position += 5;
        }

        throw new InvalidDataException("PRELOGIN encryption option is missing.");
    }

    private static byte[] CreateMinimalResponse(TdsPreLoginEncryptionMode encryptionMode)
    {
        var payload = new byte[18];
        var headerLength = 11;
        payload[0] = VersionOption;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3, 2), 6);
        payload[5] = EncryptionOption;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), (ushort)(headerLength + 6));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8, 2), 1);
        payload[10] = TerminatorOption;
        payload[17] = (byte)encryptionMode;
        return payload;
    }
}
