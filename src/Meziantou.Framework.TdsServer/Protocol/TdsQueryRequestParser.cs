using System.Buffers.Binary;
using System.Net;
using System.Text;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

internal static class TdsQueryRequestParser
{
    public static TdsQueryContext Parse(TdsPacket packet, EndPoint remoteEndPoint)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        return packet.Type switch
        {
            TdsPacketType.SqlBatch => new TdsQueryContext
            {
                RemoteEndPoint = remoteEndPoint,
                RequestType = TdsQueryRequestType.SqlBatch,
                CommandText = DecodeUnicode(packet.Payload),
            },
            TdsPacketType.Rpc => CreateRpcContext(packet.Payload, remoteEndPoint),
            _ => throw new InvalidOperationException($"Unsupported query packet type '{packet.Type}'."),
        };
    }

    private static TdsQueryContext CreateRpcContext(byte[] payload, EndPoint remoteEndPoint)
    {
        var request = TryParseRpc(payload) ?? new TdsRpcRequest
        {
            Parameters = [],
        };

        return new TdsQueryContext
        {
            RemoteEndPoint = remoteEndPoint,
            RequestType = TdsQueryRequestType.Rpc,
            ProcedureName = request.ProcedureName,
            Parameters = request.Parameters,
        };
    }

    private static TdsRpcRequest? TryParseRpc(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return null;
        }

        var position = 0;
        var allHeadersLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(position, 4));
        if (allHeadersLength >= 4 && allHeadersLength <= payload.Length)
        {
            position = (int)allHeadersLength;
        }

        if (position + 4 > payload.Length)
        {
            return null;
        }

        var procedureNameOrMarker = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        string? procedureName;
        if (procedureNameOrMarker == 0xFFFF)
        {
            var procedureId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
            position += 2;

            procedureName = procedureId switch
            {
                10 => "sp_executesql",
                11 => "sp_prepare",
                12 => "sp_execute",
                13 => "sp_prepexec",
                14 => "sp_unprepare",
                _ => $"proc_{procedureId}",
            };
        }
        else
        {
            var byteLength = checked(procedureNameOrMarker * 2);
            if (position + byteLength > payload.Length)
            {
                return null;
            }

            procedureName = Encoding.Unicode.GetString(payload.Slice(position, byteLength));
            position += byteLength;
        }

        if (position + 2 > payload.Length)
        {
            return null;
        }

        position += 2; // Option flags

        var parameters = new List<TdsQueryParameter>();
        while (position < payload.Length)
        {
            var parameter = TryParseParameter(payload, ref position);
            if (parameter is null)
            {
                break;
            }

            parameters.Add(parameter);
        }

        return new TdsRpcRequest
        {
            ProcedureName = procedureName,
            Parameters = parameters,
        };
    }

    private static TdsQueryParameter? TryParseParameter(ReadOnlySpan<byte> payload, ref int position)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var nameLength = payload[position++];
        var nameByteLength = checked(nameLength * 2);
        if (position + nameByteLength + 2 > payload.Length)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(payload.Slice(position, nameByteLength));
        position += nameByteLength;

        position += 1; // status
        var typeToken = payload[position++];

        return typeToken switch
        {
            0x26 => ParseIntNParameter(payload, ref position, name),
            0x68 => ParseBitNParameter(payload, ref position, name),
            0x6D => ParseFloatNParameter(payload, ref position, name),
            0xA7 => ParseVarCharParameter(payload, ref position, name),
            0xE7 => ParseNVarCharParameter(payload, ref position, name),
            0xA5 => ParseVarBinaryParameter(payload, ref position, name),
            0xF4 => ParseJsonParameter(payload, ref position, name),
            _ => null,
        };
    }

    private static TdsQueryParameter? ParseIntNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        var maxLength = payload[position++];
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        object value = valueLength switch
        {
            1 => payload[position],
            2 => BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(position, 2)),
            4 => BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(position, 4)),
            8 => BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(position, 8)),
            _ => payload.Slice(position, valueLength).ToArray(),
        };

        position += valueLength;
        _ = maxLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(value) };
    }

    private static TdsQueryParameter? ParseBitNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = payload[position] != 0;
        position += valueLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(value) };
    }

    private static TdsQueryParameter? ParseFloatNParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 2 > payload.Length)
        {
            return null;
        }

        _ = payload[position++]; // max length
        var valueLength = payload[position++];
        if (valueLength == 0)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        object value = valueLength switch
        {
            4 => BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(position, 4)),
            8 => BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(position, 8)),
            _ => payload.Slice(position, valueLength).ToArray(),
        };

        position += valueLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(value) };
    }

    private static TdsQueryParameter? ParseNVarCharParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 9 > payload.Length)
        {
            return null;
        }

        var maxLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;
        position += 5; // collation

        if (maxLength == 0xFFFF)
        {
            var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
            if (isNull)
            {
                return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
            }

            if (plpPayload is null)
            {
                return null;
            }

            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(Encoding.Unicode.GetString(plpPayload)) };
        }

        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(payload.Slice(position, valueLength));
        position += valueLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(value) };
    }

    private static TdsQueryParameter? ParseVarCharParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 9 > payload.Length)
        {
            return null;
        }

        var maxLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;
        position += 5; // collation

        if (maxLength == 0xFFFF)
        {
            var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
            if (isNull)
            {
                return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
            }

            if (plpPayload is null)
            {
                return null;
            }

            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(Encoding.UTF8.GetString(plpPayload)) };
        }

        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var value = Encoding.UTF8.GetString(payload.Slice(position, valueLength));
        position += valueLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(value) };
    }

    private static TdsQueryParameter? ParseVarBinaryParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        if (position + 4 > payload.Length)
        {
            return null;
        }

        _ = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;
        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(position, 2));
        position += 2;

        if (valueLength == 0xFFFF)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (position + valueLength > payload.Length)
        {
            return null;
        }

        var bytes = payload.Slice(position, valueLength).ToArray();
        position += valueLength;
        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(bytes) };
    }

    private static TdsQueryParameter? ParseJsonParameter(ReadOnlySpan<byte> payload, ref int position, string name)
    {
        var plpPayload = TryReadPlpPayload(payload, ref position, out var isNull);
        if (isNull)
        {
            return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(rawValue: null) };
        }

        if (plpPayload is null)
        {
            return null;
        }

        return new TdsQueryParameter { Name = name, Value = new TdsParameterValue(Encoding.UTF8.GetString(plpPayload)) };
    }

    private static byte[]? TryReadPlpPayload(ReadOnlySpan<byte> payload, ref int position, out bool isNull)
    {
        isNull = false;

        if (position + 8 > payload.Length)
        {
            return null;
        }

        var totalLength = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(position, 8));
        position += 8;

        if (totalLength == ulong.MaxValue)
        {
            isNull = true;
            return null;
        }

        using var stream = new MemoryStream();
        while (true)
        {
            if (position + 4 > payload.Length)
            {
                return null;
            }

            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(position, 4));
            position += 4;

            if (chunkLength == 0)
            {
                break;
            }

            if (position + chunkLength > payload.Length)
            {
                return null;
            }

            stream.Write(payload.Slice(position, (int)chunkLength));
            position += (int)chunkLength;
        }

        if (totalLength != ulong.MaxValue - 1 && totalLength != (ulong)stream.Length)
        {
            return null;
        }

        return stream.ToArray();
    }

    private static string DecodeUnicode(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(payload);
    }
}
