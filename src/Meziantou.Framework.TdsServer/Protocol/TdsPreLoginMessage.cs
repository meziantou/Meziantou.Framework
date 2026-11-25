using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsPreLoginMessage
{
    private const byte VersionOption = 0x00;
    private const byte EncryptionOption = 0x01;
    private const byte InstanceOption = 0x02;
    private const byte TerminatorOption = 0xFF;
    private const int OptionEntryLength = 5;

    // The client sends the name of the instance it connects to in INSTOPT, and the server answers with one byte:
    // 0 when the name matches. The server answers for any instance name.
    private const byte InstanceMatches = 0x00;

    public static byte[] CreateResponse(ReadOnlySpan<byte> requestPayload, TdsPreLoginEncryptionMode encryptionMode)
    {
        if (requestPayload.IsEmpty)
        {
            return CreateMinimalResponse(encryptionMode);
        }

        // The response has the options of the request, in the same order. ENCRYPTION and INSTOPT carry the answer of
        // the server, and the other options repeat the data of the request. The data of the options follows the
        // option table without gaps: SqlClient rejects a response whose option lengths are not the ones of the
        // specification, or whose options do not describe every byte of the payload.
        var options = ReadOptions(requestPayload);
        var optionTableLength = (options.Length * OptionEntryLength) + 1;
        var responseLength = (long)optionTableLength;
        foreach (var option in options)
        {
            responseLength += GetResponseDataLength(option);
        }

        // Several options can describe the same bytes of the request, so the response can be longer than the request.
        // The offset of each option is a 16-bit value, which bounds the length of the response.
        if (responseLength > ushort.MaxValue)
        {
            throw new InvalidDataException("The PRELOGIN response would be too long.");
        }

        var response = new byte[responseLength];
        var dataOffset = optionTableLength;
        for (var i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var dataLength = GetResponseDataLength(option);
            var entry = response.AsSpan(i * OptionEntryLength, OptionEntryLength);
            entry[0] = option.Token;
            BinaryPrimitives.WriteUInt16BigEndian(entry.Slice(1, 2), (ushort)dataOffset);
            BinaryPrimitives.WriteUInt16BigEndian(entry.Slice(3, 2), (ushort)dataLength);

            var data = response.AsSpan(dataOffset, dataLength);
            switch (option.Token)
            {
                case EncryptionOption:
                    data[0] = (byte)encryptionMode;
                    break;

                case InstanceOption:
                    data[0] = InstanceMatches;
                    break;

                default:
                    requestPayload.Slice(option.Offset, option.Length).CopyTo(data);
                    break;
            }

            dataOffset += dataLength;
        }

        response[optionTableLength - 1] = TerminatorOption;
        return response;
    }

    public static TdsPreLoginEncryptionMode ParseEncryptionMode(ReadOnlySpan<byte> payload)
    {
        foreach (var option in ReadOptions(payload))
        {
            if (option.Token == EncryptionOption)
            {
                return payload[option.Offset] switch
                {
                    (byte)TdsPreLoginEncryptionMode.Off => TdsPreLoginEncryptionMode.Off,
                    (byte)TdsPreLoginEncryptionMode.On => TdsPreLoginEncryptionMode.On,
                    (byte)TdsPreLoginEncryptionMode.NotSupported => TdsPreLoginEncryptionMode.NotSupported,
                    (byte)TdsPreLoginEncryptionMode.Required => TdsPreLoginEncryptionMode.Required,
                    _ => throw new InvalidDataException("Unsupported PRELOGIN encryption mode."),
                };
            }
        }

        throw new UnreachableException();
    }

    private static int GetResponseDataLength(PreLoginOption option)
    {
        return option.Token is EncryptionOption or InstanceOption ? 1 : option.Length;
    }

    /// <summary>Reads and validates the option table. The ENCRYPTION option is required, and the first one is 1 byte long.</summary>
    private static PreLoginOption[] ReadOptions(ReadOnlySpan<byte> payload)
    {
        var optionTableLength = GetOptionTableLength(payload);
        var options = new List<PreLoginOption>();
        var hasEncryption = false;
        var position = 0;
        while (position < payload.Length)
        {
            var token = payload[position];
            if (token == TerminatorOption)
            {
                break;
            }

            if (position + OptionEntryLength > payload.Length)
            {
                throw new InvalidDataException("Invalid PRELOGIN payload header.");
            }

            // The option data follows the option table. A value inside the table would be one of the table bytes,
            // and the response would overwrite that byte with the encryption mode.
            var offset = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(position + 1, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(position + 3, 2));
            if (offset < optionTableLength || offset + length > payload.Length)
            {
                throw new InvalidDataException("Invalid PRELOGIN payload segment.");
            }

            if (token == EncryptionOption && !hasEncryption)
            {
                if (length != 1)
                {
                    throw new InvalidDataException("Invalid PRELOGIN encryption option length.");
                }

                hasEncryption = true;
            }

            options.Add(new PreLoginOption(token, offset, length));
            position += OptionEntryLength;
        }

        if (!hasEncryption)
        {
            throw new InvalidDataException("PRELOGIN encryption option is missing.");
        }

        return [.. options];
    }

    /// <summary>Returns the length of the option table, terminator included, or at least the payload length when it has no terminator.</summary>
    private static int GetOptionTableLength(ReadOnlySpan<byte> payload)
    {
        var position = 0;
        while (position < payload.Length)
        {
            if (payload[position] == TerminatorOption)
            {
                return position + 1;
            }

            position += OptionEntryLength;
        }

        return position;
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PreLoginOption(byte Token, int Offset, int Length);
}
