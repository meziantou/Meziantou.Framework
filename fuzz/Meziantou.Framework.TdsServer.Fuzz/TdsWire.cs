using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.TdsServer.Fuzz;

/// <summary>Builds what a TDS client sends: packets, PRELOGIN and LOGIN7 payloads, SQLBatch and RPC payloads.</summary>
internal static class TdsWire
{
    public const byte SqlBatch = 0x01;
    public const byte Rpc = 0x03;
    public const byte TabularResult = 0x04;
    public const byte Attention = 0x06;
    public const byte Login7 = 0x10;
    public const byte PreLogin = 0x12;

    public const byte EncryptionOff = 0x00;
    public const byte EncryptionNotSupported = 0x02;
    public const byte EncryptionRequired = 0x03;

    /// <summary>The collation SQL Server uses by default (SQL_Latin1_General_CP1_CI_AS: LCID 0x0409, sort id 52, code page 1252).</summary>
    public static ReadOnlySpan<byte> DefaultCollation => [0x09, 0x04, 0xD0, 0x00, 0x34];

    /// <summary>Splits a message into packets of at most <paramref name="chunkSize"/> payload bytes, the last one marked end of message.</summary>
    public static void WriteMessage(List<byte> output, byte packetType, ReadOnlySpan<byte> payload, int chunkSize = int.MaxValue, byte extraStatus = 0)
    {
        chunkSize = Math.Clamp(chunkSize, 1, ushort.MaxValue - 8);
        var packetId = (byte)1;
        var offset = 0;
        Span<byte> header = stackalloc byte[8];
        do
        {
            var count = Math.Min(chunkSize, payload.Length - offset);
            var isLast = offset + count >= payload.Length;
            header[0] = packetType;
            header[1] = (byte)((isLast ? 0x01 : 0x00) | extraStatus);
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(count + 8));
            header[6] = packetId++;
            output.AddRange(header);
            output.AddRange(payload.Slice(offset, count));
            offset += count;
        }
        while (offset < payload.Length);
    }

    public static byte[] Message(byte packetType, ReadOnlySpan<byte> payload, int chunkSize = int.MaxValue)
    {
        var output = new List<byte>();
        WriteMessage(output, packetType, payload, chunkSize);
        return [.. output];
    }

    /// <summary>A PRELOGIN payload with the VERSION and ENCRYPTION options.</summary>
    public static byte[] PreLoginPayload(byte encryption)
    {
        return
        [
            0x00, 0x00, 0x0B, 0x00, 0x06, // VERSION at 11, 6 bytes
            0x01, 0x00, 0x11, 0x00, 0x01, // ENCRYPTION at 17, 1 byte
            0xFF,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00,
            encryption,
        ];
    }

    /// <summary>A LOGIN7 payload: the 94-byte fixed part, then the variable-length fields it points to.</summary>
    public static byte[] Login7Payload(string? userName, string? password, string? applicationName, string? database, byte[]? sspi = null)
    {
        var fixedPart = new byte[94];
        var data = new List<byte>();
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(4), 0x74000004); // TDS 7.4
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(8), 4096); // packet size
        AddString(40, userName, obfuscate: false);
        AddString(44, password, obfuscate: true);
        AddString(48, applicationName, obfuscate: false);
        AddString(68, database, obfuscate: false);
        if (sspi is { Length: > 0 })
        {
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(78), (ushort)(94 + data.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(80), (ushort)sspi.Length);
            data.AddRange(sspi);
        }

        var result = (byte[])[.. fixedPart, .. data];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)result.Length);
        return result;

        void AddString(int offsetIndex, string? value, bool obfuscate)
        {
            if (value is null)
                return;

            var bytes = Encoding.Unicode.GetBytes(value);
            if (obfuscate)
            {
                // LOGIN7 swaps the nibbles of every byte of the password, then XORs it with 0xA5
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)((((bytes[i] << 4) | (bytes[i] >> 4)) & 0xFF) ^ 0xA5);
                }
            }

            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(offsetIndex), (ushort)(94 + data.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(offsetIndex + 2), (ushort)value.Length);
            data.AddRange(bytes);
        }
    }

    /// <summary>The ALL_HEADERS block of TDS 7.2+: a single transaction descriptor header.</summary>
    public static byte[] AllHeaders()
    {
        var result = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 22);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 18);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(18), 1);
        return result;
    }

    public static byte[] SqlBatchPayload(string text, bool withAllHeaders = true)
    {
        return [.. withAllHeaders ? AllHeaders() : [], .. Encoding.Unicode.GetBytes(text)];
    }

    /// <summary>An RPC payload calling a procedure by its well-known id (sp_executesql is 10).</summary>
    public static byte[] RpcPayload(ushort procedureId, params byte[][] parameters)
    {
        var result = new List<byte>(AllHeaders());
        result.AddRange([0xFF, 0xFF]);
        AddUInt16(result, procedureId);
        result.AddRange([0x00, 0x00]); // option flags
        foreach (var parameter in parameters)
        {
            result.AddRange(parameter);
        }

        return [.. result];
    }

    /// <summary>An RPC payload calling a procedure by its name, without ALL_HEADERS (as TDS 7.1 clients do).</summary>
    public static byte[] RpcPayload(string procedureName, params byte[][] parameters)
    {
        var result = new List<byte>();
        AddUInt16(result, (ushort)procedureName.Length);
        result.AddRange(Encoding.Unicode.GetBytes(procedureName));
        result.AddRange([0x00, 0x00]);
        foreach (var parameter in parameters)
        {
            result.AddRange(parameter);
        }

        return [.. result];
    }

    /// <summary>A parameter: its name, its status flags (0x01 is an output parameter), then its TYPE_INFO and value.</summary>
    public static byte[] Parameter(string name, ReadOnlySpan<byte> typeAndValue, byte status = 0)
    {
        return [(byte)name.Length, .. Encoding.Unicode.GetBytes(name), status, .. typeAndValue];
    }

    public static byte[] IntN(int maxLength, long? value)
    {
        if (value is null)
            return [0x26, (byte)maxLength, 0];

        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.Value);
        return [0x26, (byte)maxLength, (byte)maxLength, .. bytes.AsSpan(0, maxLength)];
    }

    public static byte[] BitN(bool? value) => value is null ? [0x68, 1, 0] : [0x68, 1, 1, value.Value ? (byte)1 : (byte)0];

    public static byte[] FloatN(double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        return [0x6D, 8, 8, .. bytes];
    }

    public static byte[] RealN(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return [0x6D, 4, 4, .. bytes];
    }

    public static byte[] GuidN(Guid value) => [0x24, 16, 16, .. value.ToByteArray()];

    public static byte[] DecimalN(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (byte)((bits[3] >> 16) & 0xFF);
        var magnitude = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(magnitude, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(magnitude.AsSpan(4), bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(magnitude.AsSpan(8), bits[2]);
        return [0x6A, 17, 38, scale, 17, value < 0 ? (byte)0 : (byte)1, .. magnitude];
    }

    public static byte[] MoneyN(decimal value)
    {
        var units = (long)(value * 10000m);
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)(units >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)units);
        return [0x6E, 8, 8, .. bytes];
    }

    public static byte[] SmallMoneyN(decimal value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)(value * 10000m));
        return [0x6E, 4, 4, .. bytes];
    }

    public static byte[] DateTimeN(int days, uint ticks300)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, days);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), ticks300);
        return [0x6F, 8, 8, .. bytes];
    }

    public static byte[] SmallDateTimeN(ushort days, ushort minutes)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, days);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), minutes);
        return [0x6F, 4, 4, .. bytes];
    }

    public static byte[] DateN(DateOnly value) => [0x28, 3, .. UInt24(value.DayNumber)];

    public static byte[] TimeN(TimeOnly value) => [0x29, 7, 5, .. Time(value.Ticks)];

    public static byte[] DateTime2N(DateTime value) => [0x2A, 7, 8, .. Time(value.TimeOfDay.Ticks), .. UInt24(value.Date.Subtract(DateTime.MinValue).Days)];

    public static byte[] DateTimeOffsetN(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var offset = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(offset, (short)value.Offset.TotalMinutes);
        return [0x2B, 7, 10, .. Time(utc.TimeOfDay.Ticks), .. UInt24(utc.Date.Subtract(DateTime.MinValue).Days), .. offset];
    }

    public static byte[] NVarChar(string? value)
    {
        var result = new List<byte> { 0xE7 };
        AddUInt16(result, 8000);
        result.AddRange(DefaultCollation);
        if (value is null)
        {
            AddUInt16(result, 0xFFFF);
        }
        else
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            AddUInt16(result, (ushort)bytes.Length);
            result.AddRange(bytes);
        }

        return [.. result];
    }

    /// <summary>An nvarchar(max) value, sent as a PLP stream split in chunks.</summary>
    public static byte[] NVarCharMax(string value, int chunkSize = 7)
    {
        var result = new List<byte> { 0xE7, 0xFF, 0xFF };
        result.AddRange(DefaultCollation);
        AddPlp(result, Encoding.Unicode.GetBytes(value), chunkSize);
        return [.. result];
    }

    public static byte[] VarChar(ReadOnlySpan<byte> collation, ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA7 };
        AddUInt16(result, 8000);
        result.AddRange(collation);
        AddUInt16(result, (ushort)value.Length);
        result.AddRange(value);
        return [.. result];
    }

    public static byte[] VarBinary(ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA5 };
        AddUInt16(result, 8000);
        AddUInt16(result, (ushort)value.Length);
        result.AddRange(value);
        return [.. result];
    }

    public static byte[] VarBinaryMax(ReadOnlySpan<byte> value)
    {
        var result = new List<byte> { 0xA5, 0xFF, 0xFF };
        AddPlp(result, value, chunkSize: 3);
        return [.. result];
    }

    public static byte[] Json(string value)
    {
        var result = new List<byte> { 0xF4 };
        AddPlp(result, Encoding.UTF8.GetBytes(value), chunkSize: 16);
        return [.. result];
    }

    private static void AddPlp(List<byte> result, ReadOnlySpan<byte> value, int chunkSize)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value.Length);
        result.AddRange(buffer);
        for (var offset = 0; offset < value.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, value.Length - offset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)count);
            result.AddRange(buffer[..4]);
            result.AddRange(value.Slice(offset, count));
        }

        result.AddRange([0, 0, 0, 0]);
    }

    private static byte[] UInt24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    private static byte[] Time(long ticks) => [(byte)ticks, (byte)(ticks >> 8), (byte)(ticks >> 16), (byte)(ticks >> 24), (byte)(ticks >> 32)];

    private static void AddUInt16(List<byte> result, ushort value)
    {
        result.Add((byte)value);
        result.Add((byte)(value >> 8));
    }
}
