using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.PostgreSql.Fuzz;

/// <summary>Builds the frontend messages a client sends and checks the backend messages the server answers.</summary>
internal static class PgWire
{
    public const int ProtocolVersion3 = 196608;
    public const int SslRequestCode = 80877103;
    public const int CancelRequestCode = 80877102;
    public const int GssEncRequestCode = 80877104;

    public const byte Query = (byte)'Q';
    public const byte Parse = (byte)'P';
    public const byte Bind = (byte)'B';
    public const byte Describe = (byte)'D';
    public const byte Execute = (byte)'E';
    public const byte Close = (byte)'C';
    public const byte Sync = (byte)'S';
    public const byte Flush = (byte)'H';
    public const byte Terminate = (byte)'X';
    public const byte PasswordMessage = (byte)'p';

    public const byte Authentication = (byte)'R';
    public const byte ParameterStatus = (byte)'S';
    public const byte BackendKeyData = (byte)'K';
    public const byte ReadyForQuery = (byte)'Z';
    public const byte ParseComplete = (byte)'1';
    public const byte BindComplete = (byte)'2';
    public const byte CloseComplete = (byte)'3';
    public const byte RowDescription = (byte)'T';
    public const byte DataRow = (byte)'D';
    public const byte CommandComplete = (byte)'C';
    public const byte ErrorResponse = (byte)'E';
    public const byte NoticeResponse = (byte)'N';
    public const byte ParameterDescription = (byte)'t';
    public const byte NoData = (byte)'n';
    public const byte EmptyQueryResponse = (byte)'I';
    public const byte PortalSuspended = (byte)'s';

    public const string ProtocolViolation = "08P01";

    public static byte[] CString(string value) => [.. Encoding.UTF8.GetBytes(value), 0];

    public static byte[] StartupPacket(int requestCode, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), requestCode);
        payload.CopyTo(packet.AsSpan(8));
        return packet;
    }

    public static byte[] Startup(params (string Key, string Value)[] parameters) => StartupPacket(ProtocolVersion3, StartupParameters(parameters));

    public static byte[] StartupParameters(IEnumerable<(string Key, string Value)> parameters)
    {
        var payload = new List<byte>();
        foreach (var (key, value) in parameters)
        {
            payload.AddRange(CString(key));
            payload.AddRange(CString(value));
        }

        payload.Add(0);
        return [.. payload];
    }

    public static byte[] Message(byte type, ReadOnlySpan<byte> payload)
    {
        var message = new byte[5 + payload.Length];
        message[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(1), payload.Length + 4);
        payload.CopyTo(message.AsSpan(5));
        return message;
    }

    public static byte[] QueryMessage(string sql) => Message(Query, CString(sql));

    public static byte[] ParsePayload(string statement, string query, params uint[] parameterTypes)
    {
        var payload = new List<byte>();
        payload.AddRange(CString(statement));
        payload.AddRange(CString(query));
        AddInt16(payload, (short)parameterTypes.Length);
        foreach (var type in parameterTypes)
        {
            AddInt32(payload, (int)type);
        }

        return [.. payload];
    }

    public static byte[] BindPayload(string portal, string statement, short[] parameterFormats, byte[]?[] values, short[] resultFormats)
    {
        var payload = new List<byte>();
        payload.AddRange(CString(portal));
        payload.AddRange(CString(statement));
        AddInt16(payload, (short)parameterFormats.Length);
        foreach (var format in parameterFormats)
        {
            AddInt16(payload, format);
        }

        AddInt16(payload, (short)values.Length);
        foreach (var value in values)
        {
            AddInt32(payload, value?.Length ?? -1);
            if (value is not null)
            {
                payload.AddRange(value);
            }
        }

        AddInt16(payload, (short)resultFormats.Length);
        foreach (var format in resultFormats)
        {
            AddInt16(payload, format);
        }

        return [.. payload];
    }

    public static byte[] TargetPayload(char target, string name) => [(byte)target, .. CString(name)];

    public static byte[] ExecutePayload(string portal, int maxRows)
    {
        var payload = new List<byte>(CString(portal));
        AddInt32(payload, maxRows);
        return [.. payload];
    }

    public static byte[] SaslInitialResponsePayload(string mechanism, ReadOnlySpan<byte> initialResponse)
    {
        var payload = new List<byte>(CString(mechanism));
        AddInt32(payload, initialResponse.Length);
        payload.AddRange(initialResponse);
        return [.. payload];
    }

    public static byte[] Int16(short value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] Int32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] Int64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    private static void AddInt16(List<byte> payload, short value) => payload.AddRange(Int16(value));

    private static void AddInt32(List<byte> payload, int value) => payload.AddRange(Int32(value));

    /// <summary>Splits the server output into backend messages, checking the framing and the layout of every message.</summary>
    public static List<BackendMessage> ReadBackendMessages(ReadOnlySpan<byte> output)
    {
        var messages = new List<BackendMessage>();
        while (!output.IsEmpty)
        {
            FuzzAssert.True(output.Length >= 5, "The output ends with a truncated message header");
            var type = output[0];
            var length = BinaryPrimitives.ReadInt32BigEndian(output[1..]);
            FuzzAssert.True(length >= 4 && length - 4 <= output.Length - 5, $"Invalid length {length} of the backend message '{(char)type}'");
            var message = new BackendMessage(type, output.Slice(5, length - 4).ToArray());
            CheckLayout(message);
            messages.Add(message);
            output = output[(length + 1)..];
        }

        return messages;
    }

    private static void CheckLayout(BackendMessage message)
    {
        var payload = message.Payload.AsSpan();
        var reader = new PayloadReader(payload, message.Type);
        switch (message.Type)
        {
            case Authentication:
                var code = reader.ReadInt32();
                switch (code)
                {
                    case 0 or 3:
                        break;

                    case 5:
                        _ = reader.ReadBytes(4);
                        break;

                    case 10:
                        // The list of the SASL mechanisms ends with an empty name
                        while (reader.ReadCString().Length > 0)
                        {
                        }

                        break;

                    case 11 or 12:
                        FuzzAssert.True(payload.Length > 4, "A SASL message must carry data");
                        _ = reader.ReadBytes(payload.Length - 4);
                        break;

                    default:
                        FuzzAssert.Fail($"Unexpected authentication request {code}");
                        break;
                }

                break;

            case ParameterStatus:
                FuzzAssert.True(reader.ReadCString().Length > 0, "A ParameterStatus must have a name");
                _ = reader.ReadCString();
                break;

            case BackendKeyData:
                _ = reader.ReadBytes(8);
                break;

            case ReadyForQuery:
                var status = reader.ReadBytes(1)[0];
                FuzzAssert.True(status is (byte)'I' or (byte)'T' or (byte)'E', $"Invalid transaction status 0x{status:X2}");
                break;

            case ParseComplete or BindComplete or CloseComplete or NoData or EmptyQueryResponse or PortalSuspended:
                break;

            case ParameterDescription:
                var parameterCount = reader.ReadInt16();
                FuzzAssert.True(parameterCount >= 0, "Negative parameter count");
                _ = reader.ReadBytes(parameterCount * 4);
                break;

            case RowDescription:
                var fieldCount = reader.ReadInt16();
                FuzzAssert.True(fieldCount >= 0, "Negative field count");
                for (var i = 0; i < fieldCount; i++)
                {
                    FuzzAssert.True(reader.ReadCString().Length > 0, "A column must have a name");
                    _ = reader.ReadBytes(4 + 2 + 4 + 2 + 4);
                    var format = reader.ReadInt16();
                    FuzzAssert.True(format is 0 or 1, $"Invalid column format {format}");
                }

                break;

            case DataRow:
                var columnCount = reader.ReadInt16();
                FuzzAssert.True(columnCount >= 0, "Negative column count");
                for (var i = 0; i < columnCount; i++)
                {
                    var valueLength = reader.ReadInt32();
                    FuzzAssert.True(valueLength >= -1, $"Invalid value length {valueLength}");
                    if (valueLength > 0)
                    {
                        _ = reader.ReadBytes(valueLength);
                    }
                }

                break;

            case CommandComplete:
                FuzzAssert.True(reader.ReadCString().Length > 0, "A CommandComplete must have a tag");
                break;

            case ErrorResponse or NoticeResponse:
                var fields = message.GetFields();
                FuzzAssert.True(fields.ContainsKey('S') && fields.ContainsKey('C') && fields.ContainsKey('M'), "An error or a notice must have a severity, a code and a message");
                FuzzAssert.True(fields['C'].Length == 5, $"Invalid SQLSTATE {FuzzAssert.Format(fields['C'])}");
                return;

            default:
                FuzzAssert.Fail($"Unexpected backend message type 0x{message.Type:X2}");
                break;
        }

        reader.CheckEnd();
    }

    internal sealed record BackendMessage(byte Type, byte[] Payload)
    {
        public bool IsError => Type is ErrorResponse;

        public int AuthenticationCode => BinaryPrimitives.ReadInt32BigEndian(Payload);

        /// <summary>Gets the fields of an ErrorResponse or a NoticeResponse: a code byte and a string each, then a zero byte.</summary>
        public Dictionary<char, string> GetFields()
        {
            var reader = new PayloadReader(Payload, Type);
            var fields = new Dictionary<char, string>();
            while (true)
            {
                var code = reader.ReadBytes(1)[0];
                if (code is 0)
                    break;

                // The server sends the severity (twice), the SQLSTATE and the message. Another code means that a string
                // held a zero byte, which ended it early.
                FuzzAssert.True(code is (byte)'S' or (byte)'V' or (byte)'C' or (byte)'M', $"Unexpected field 0x{code:X2} in {this}");
                FuzzAssert.True(fields.TryAdd((char)code, reader.ReadCString()), $"Duplicate field '{(char)code}'");
            }

            reader.CheckEnd();
            return fields;
        }

        public string ErrorCode => GetFields()['C'];

        public string Severity => GetFields()['S'];

        public override string ToString() => $"'{(char)Type}' ({Payload.Length} bytes)";
    }

    private ref struct PayloadReader(ReadOnlySpan<byte> payload, byte type)
    {
        private ReadOnlySpan<byte> _payload = payload;

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            FuzzAssert.True(count >= 0 && count <= _payload.Length, $"The backend message '{(char)type}' is truncated");
            var result = _payload[..count];
            _payload = _payload[count..];
            return result;
        }

        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));

        public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));

        public string ReadCString()
        {
            var end = _payload.IndexOf((byte)0);
            FuzzAssert.True(end >= 0, $"The backend message '{(char)type}' has an unterminated string");
            var value = Encoding.UTF8.GetString(_payload[..end]);
            _payload = _payload[(end + 1)..];
            return value;
        }

        public readonly void CheckEnd() => FuzzAssert.True(_payload.IsEmpty, $"The backend message '{(char)type}' has {_payload.Length} unexpected trailing bytes");
    }
}
