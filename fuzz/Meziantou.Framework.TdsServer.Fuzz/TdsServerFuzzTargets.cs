using System.Buffers.Binary;
using System.Net;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Protocol;
using Meziantou.Framework.Tds.QueryEngine;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TdsServer.Fuzz;

/// <summary>
/// A TDS connection is fed in process, through <c>TdsConnectionProcessor</c> over memory streams: malformed framing only
/// throws <see cref="InvalidDataException"/> or <see cref="EndOfStreamException"/>, which close the connection, and every
/// other malformed request gets an error response. The responses are well-formed packets whose messages end with a DONE
/// token. A request the server can decode must not fail: the echo handler returns values the serializer always accepts,
/// so an error logged by the processor (a parser or a serializer exception it turned into error 50005) is a failure.
/// The request parser never throws, and the query engine returns an error result for anything it does not support, SQL
/// text or RPC call. Every tabular result is decoded as a client reads it.
/// </summary>
public static partial class TdsServerFuzzTargets
{
    private const int PayloadSizePerPacket = 4096 - 8;

    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 50000);

    private static readonly int[] PacketSizes = [512, 513, 1000, 4096, 8000, 32768, ushort.MaxValue];

    private static readonly string[] SqlTokens =
    [
        "SELECT ", " FROM ", " WHERE ", " AND ", " OR ", " NOT ", " IN ", " IS NULL", " IS NOT NULL", " LIKE ", " BETWEEN ", " EXISTS ", " AS ", " ON ", " JOIN ", " INNER JOIN ", " LEFT JOIN ", " RIGHT JOIN ", " FULL JOIN ", " CROSS APPLY ",
        " GROUP BY ", " HAVING ", " ORDER BY ", " ASC", " DESC", " OFFSET ", " ROWS", " FETCH NEXT ", " ROWS ONLY", " TOP ", " DISTINCT ", " UNION ", " UNION ALL ", " INTERSECT ", " EXCEPT ", "WITH ", " CASE ", " WHEN ", " THEN ", " ELSE ", " END",
        "COUNT(*)", "COUNT(", "SUM(", "AVG(", "MIN(", "MAX(", "CAST(", "CONVERT(", "TRY_CAST(", "COALESCE(", "ISNULL(", "NULLIF(", "IIF(", "LEN(", "UPPER(", "LOWER(", "LTRIM(", "RTRIM(", "TRIM(", "SUBSTRING(", "REPLACE(", "CHARINDEX(", "CONCAT(",
        "ABS(", "ROUND(", "FLOOR(", "CEILING(", "DATEADD(", "DATEDIFF(", "YEAR(", "MONTH(", "DAY(", "GETDATE()", "SYSDATETIME()", "JSON_VALUE(", "JSON_QUERY(", "ISJSON(", "OPENJSON(", ".value(", ".query(", ".exist(", ".nodes(", "'lax $.name'", "'strict $.items[0]'",
        " AS int)", " AS nvarchar(max))", " AS bigint)", " AS decimal(38, 10))", " AS float)", " AS date)", " AS datetime2)", " AS xml)", " AS bit)", " AS uniqueidentifier)",
        "customers", "orders", "nullable_customers", "json_docs", "xml_docs", "Id", "Name", "Region", "Amount", "Score", "Payload", "c.", "o.", "*", ", ", "(", ")", "'", "''", "N'", "[", "]", "\"", "@p0", "@id", "--", "/*", "*/", ";",
        "=", "<>", "!=", "<", "<=", ">", ">=", "+", "-", "/", "%", "&", "|", "^", "~", "0", "1", "-1", "2147483647", "-2147483648", "9223372036854775807", "1.5", "1e308", "0x1F", "NULL", "'2024-01-31'", "'12:34:56'", "'/root/item'", "'(/root/item/@id)[1]'",
    ];

    // libFuzzer dictionaries hold bytes below 0x80 only (the tokens are written as UTF-8): packet headers, the ALL_HEADERS
    // block, UTF-16 text, and the TYPE_INFO of the parameter types whose token is ASCII
    private static readonly string[] MessageTokens =
    [
        "\u0001\u0001", "\u0003\u0001", "\u0006\u0001", "\u0010\u0001", "\u0012\u0001", "\u0001\u0000", "\u0000\u0008", "\u0016\u0000\u0000\u0000\u0012\u0000\u0000\u0000\u0002\u0000",
        "S\u0000E\u0000L\u0000E\u0000C\u0000T\u0000 \u0000", "@\u0000p\u0000", "\u0002@\u0000p\u0000", "\u000A\u0000", "\u000D\u0000", "\u0000\u0000", "\u0000\u0000\u0000\u0000", "\u0001\u0000\u0000\u0000",
        "&\u0001\u0001", "&\u0002\u0002", "&\u0004\u0004", "&\u0008\u0008", "&\u0004\u0000", "h\u0001\u0001", "m\u0004\u0004", "m\u0008\u0008", "$\u0010\u0010", "j\u0011&\u0002\u0011\u0001", "l\u0011&\u0000\u0005\u0000",
        "n\u0004\u0004", "n\u0008\u0008", "o\u0004\u0004", "o\u0008\u0008", "(\u0003", ")\u0007\u0005", ")\u0000\u0003", "*\u0007\u0008", "*\u0003\u0006", "+\u0007\u000A", "+\u0000\u0005", "\u0009\u0004\u0000\u0000\u0034", "\u0000\u0000\u0000\u0004\u0000",
    ];

    private static readonly string[] LoginTokens =
    [
        "\u005E\u0000", "\u0000\u0000", "\u0001\u0000", "\u0004\u0000\u0000\u0074", "s\u0000a\u0000", "\u0000\u0000\u0000\u0000",
        "\u0000\u0000\u000B\u0000\u0006", "\u0001\u0000\u0011\u0000\u0001", "\u0001\u0000\u0006\u0000\u0001\u007F", "\u0002", "\u0003", "\u007F",
    ];

    private const string DeniedProcedureName = "denied_customers";

    private const string ExecuteSqlParameterDeclarations = "@id int, @name nvarchar(50), @offset int, @factor int";

    private static readonly string[] GuidFormats = ["D", "N", "B", "P"];

    private static readonly TdsColumnType[] ColumnTypes = Enum.GetValues<TdsColumnType>();

    private static readonly UInt128 MaxDecimalMagnitude = UInt128.Parse(new string('9', 38), CultureInfo.InvariantCulture);

    private static readonly Customer[] Customers = [new(1, "Alice"), new(2, "Bob"), new(4, "David")];

    // Queries over these rows mirror the query engine tests. Each authorization mode gets its own handler, as the
    // options are snapshotted when the handler is created.
    private static readonly TdsQueryDelegate[] QueryEngineRpcHandlers = [.. Enum.GetValues<QueryEngineAuthorization>().Select(authorization => TdsQueryEngine.CreateQueryHandler(CreateQueryEngineOptions(authorization)))];

    private static readonly TdsQueryDelegate QueryEngineHandler = QueryEngineRpcHandlers[(int)QueryEngineAuthorization.AllowAll];

    private static readonly HashSet<string> StoredProcedureNames = new(CreateQueryEngineOptions(QueryEngineAuthorization.AllowAll).StoredProcedures.Keys, StringComparer.OrdinalIgnoreCase);

    // The procedure and parameter names as UTF-16, and the SQL of the custom scalar functions
    private static readonly string[] QueryEngineRpcTokens =
    [
        .. StoredProcedureNames.Concat(["@id", "@name", "@limit", "@number", "@flag", "@real", "@amount", "@offset", "@text", "@data", "@dayOfWeek", "@a", "@b"]).Select(name => string.Concat(name.Select(c => $"{c}\0"))),
        .. ((string[])["FUZZ_TEXT(", "FUZZ_COALESCE(", "SELECT ", " FROM ", "customers", "orders", " WHERE ", "Id", "Name", " = @id", "@name"]).Select(text => string.Concat(text.Select(c => $"{c}\0"))),
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("tds-session", Session) { Seeds = GetSessionSeeds, Dictionary = [.. MessageTokens, .. LoginTokens] },
        new("tds-messages", Messages) { Seeds = GetMessagesSeeds, Dictionary = MessageTokens },
        new("tds-request-parser", RequestParser) { Seeds = GetRequestParserSeeds, Dictionary = MessageTokens },
        new("tds-login", Login) { Seeds = GetLoginSeeds, Dictionary = LoginTokens },
        new("tds-prelogin", PreLogin) { Seeds = GetPreLoginSeeds, Dictionary = LoginTokens },
        new("tds-query-engine", QueryEngine) { Seeds = GetQueryEngineSeeds, Dictionary = SqlTokens },
        new("tds-query-engine-rpc", QueryEngineRpc) { Seeds = GetQueryEngineRpcSeeds, Dictionary = [.. MessageTokens, .. QueryEngineRpcTokens] },
        new("tds-response-serializer", ResponseSerializer) { Seeds = GetResponseSerializerSeeds },
    ];

    /// <summary>The input is everything the client sends on the connection.</summary>
    private static void Session(ReadOnlySpan<byte> data)
    {
        var options = new TdsServerOptions();
        var logger = new RecordingLogger();
        using var output = new MemoryStream();
        try
        {
            RunConnection(options, logger, data.ToArray(), output);
        }
        catch (InvalidDataException)
        {
            // Malformed framing: the server closes the connection
        }
        catch (EndOfStreamException)
        {
            // A packet shorter than its header says
        }

        CheckLogs(logger);
        _ = CheckResponses(output.ToArray(), options.PacketSize);
    }

    /// <summary>
    /// The harness logs in, then the input is a sequence of messages the harness frames correctly: a byte for the type
    /// and the packet split, two bytes for the length, then the payload. So mutations reach the request parsers instead
    /// of breaking the framing. The first byte selects the packet size of the server.
    /// </summary>
    private static void Messages(ReadOnlySpan<byte> data)
    {
        var options = new TdsServerOptions { PacketSize = FuzzInput.Pick(PacketSizes, FuzzInput.ReadByte(ref data)) };
        var input = new List<byte>();
        TdsWire.WriteMessage(input, TdsWire.PreLogin, TdsWire.PreLoginPayload(TdsWire.EncryptionNotSupported));
        TdsWire.WriteMessage(input, TdsWire.Login7, TdsWire.Login7Payload("sa", "password", "fuzz", "master"));

        var expectedResponses = new List<byte>();
        while (!data.IsEmpty)
        {
            var header = FuzzInput.ReadByte(ref data);
            var packetType = (header & 0x03) switch
            {
                0 => TdsWire.SqlBatch,
                1 => TdsWire.Rpc,
                2 => TdsWire.Attention,
                _ => FuzzInput.ReadByte(ref data),
            };

            var chunkSize = ((header >> 2) & 0x0F) is var split and not 0 ? split * 7 : int.MaxValue;
            var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
            var payload = data[..Math.Min(length, data.Length)];
            data = data[payload.Length..];
            TdsWire.WriteMessage(input, packetType, payload, chunkSize, extraStatus: (header & 0x40) != 0 ? (byte)0x08 : (byte)0x00);
            expectedResponses.Add(packetType);

            // An ATTENTION right after a request, as a client that cancels it sends
            if ((header & 0x80) != 0)
            {
                TdsWire.WriteMessage(input, TdsWire.Attention, []);
                expectedResponses.Add(TdsWire.Attention);
            }
        }

        var logger = new RecordingLogger();
        using var output = new MemoryStream();

        // The framing is valid and every message fits in MaxMessageSize, so nothing may escape
        RunConnection(options, logger, [.. input], output);
        CheckLogs(logger);
        var responses = CheckResponses(output.ToArray(), options.PacketSize);
        FuzzAssert.Equal(expectedResponses.Count + 2, responses.Count, "Each message must get exactly one response");
        FuzzAssert.Equal(TdsWire.PreLogin, responses[0].Type, "The first response must answer PRELOGIN");
        FuzzAssert.False(IsError(responses[1].Payload), "The login must succeed");
        for (var i = 0; i < expectedResponses.Count; i++)
        {
            var response = responses[i + 2].Payload;
            switch (expectedResponses[i])
            {
                case TdsWire.Attention:
                    FuzzAssert.True(response.AsSpan().SequenceEqual(TdsResponseSerializer.CreateAttentionResponse()), "An ATTENTION must be acknowledged with DONE_ATTN");
                    break;

                case TdsWire.SqlBatch or TdsWire.Rpc:
                    // The echo handler cannot fail, so an error token means that the request parser or the serializer threw
                    FuzzAssert.False(IsError(response), "A request got an error response");
                    break;

                default:
                    FuzzAssert.True(IsError(response), $"The unsupported packet type {expectedResponses[i]} must get an error response");
                    break;
            }
        }
    }

    /// <summary>The first byte selects SQLBatch or RPC, the rest is the payload of the message.</summary>
    private static void RequestParser(ReadOnlySpan<byte> data)
    {
        var isRpc = (FuzzInput.ReadByte(ref data) & 1) != 0;
        var packet = new TdsPacket { Type = isRpc ? TdsPacketType.Rpc : TdsPacketType.SqlBatch, Payload = data.ToArray() };

        // RPC parsing reports what it cannot decode through HasCompleteParameters, it never throws
        var context = TdsQueryRequestParser.Parse(packet, RemoteEndPoint, userContext: null);
        if (!isRpc)
        {
            FuzzAssert.True(context.RequestType is TdsQueryRequestType.SqlBatch && context.CommandText is not null, "A SQL batch must have a command text");
            FuzzAssert.Equal(0, context.Parameters.Count, "A SQL batch has no parameter");
        }
        else
        {
            FuzzAssert.True(context.RequestType is TdsQueryRequestType.Rpc, "An RPC request must be reported as such");
        }

        foreach (var parameter in context.Parameters)
        {
            CheckParameter(parameter);
        }

        // The echo holds the parameters in columns of their own type: the serializer accepts every value the parser decodes,
        // and a client reads back the values
        var echo = Echo(context);
        var decoded = TdsWire.ReadTabularResult(TdsResponseSerializer.CreateQueryResponse(echo, PayloadSizePerPacket), PayloadSizePerPacket);
        CheckDecodedResult(echo, decoded);
    }

    private static void Login(ReadOnlySpan<byte> data)
    {
        TdsLoginRequest login;
        try
        {
            login = TdsLoginParser.Parse(data);
        }
        catch (InvalidDataException)
        {
            FuzzAssert.True(data.Length < 94, "Only a payload shorter than the fixed part of LOGIN7 is invalid");
            return;
        }

        FuzzAssert.True(data.Length >= 94, "A payload shorter than the fixed part of LOGIN7 must be rejected");
        foreach (var value in (string?[])[login.UserName, login.Password, login.ApplicationName, login.Database])
        {
            FuzzAssert.True(value is null || (value.Length > 0 && value.Length <= ushort.MaxValue), $"Invalid login field length: {FuzzAssert.Format(value)}");
        }

        FuzzAssert.True(login.Sspi.Length <= data.Length, "The SSPI blob is longer than the payload");
        if (login.AuthenticationToken is { } token)
        {
            FuzzAssert.True(Convert.FromBase64String(token).AsSpan().SequenceEqual(login.Sspi), "The authentication token must be the SSPI blob");
        }
    }

    private static void PreLogin(ReadOnlySpan<byte> data)
    {
        TdsPreLoginEncryptionMode mode;
        try
        {
            mode = TdsPreLoginMessage.ParseEncryptionMode(data);
        }
        catch (InvalidDataException)
        {
            // The server only answers a payload it parsed, but CreateResponse must still only reject the others with
            // InvalidDataException. It does not validate the encryption mode it replaces, so it may accept them.
            try
            {
                _ = TdsPreLoginMessage.CreateResponse(data, TdsPreLoginEncryptionMode.NotSupported);
            }
            catch (InvalidDataException)
            {
            }

            return;
        }

        FuzzAssert.True(Enum.IsDefined(mode), $"Unexpected encryption mode {mode}");
        foreach (var responseMode in (TdsPreLoginEncryptionMode[])[TdsPreLoginEncryptionMode.Off, TdsPreLoginEncryptionMode.On, TdsPreLoginEncryptionMode.NotSupported, TdsPreLoginEncryptionMode.Required])
        {
            var response = TdsPreLoginMessage.CreateResponse(data, responseMode);
            FuzzAssert.Equal(data.Length, response.Length, "The response must keep the options of the request");
            FuzzAssert.Equal(responseMode, TdsPreLoginMessage.ParseEncryptionMode(response), "The response must carry the negotiated encryption mode");
        }
    }

    /// <summary>The input is the SQL text of a SQLBatch request, run against in-memory query roots.</summary>
    private static void QueryEngine(ReadOnlySpan<byte> data)
    {
        var context = new TdsQueryContext
        {
            RemoteEndPoint = RemoteEndPoint,
            RequestType = TdsQueryRequestType.SqlBatch,
            CommandText = FuzzText.Decode(data),
        };

        // Unsupported SQL is reported as an error result (50004): an exception escaping the engine is a failure
        var result = QueryEngineHandler(context, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        // The engine builds the result sets from the query, so the serializer must accept them
        _ = TdsWire.ReadTabularResult(TdsResponseSerializer.CreateQueryResponse(result, PayloadSizePerPacket), PayloadSizePerPacket);
    }

    /// <summary>
    /// The first byte selects what <see cref="TdsQueryEngineOptions.IsAuthorized"/> denies, the rest is an RPC payload:
    /// sp_executesql runs its statement with its parameters, any other procedure calls one of the stored procedures.
    /// </summary>
    private static void QueryEngineRpc(ReadOnlySpan<byte> data)
    {
        var authorization = (QueryEngineAuthorization)(FuzzInput.ReadByte(ref data) & 0x03);
        var packet = new TdsPacket { Type = TdsPacketType.Rpc, Payload = data.ToArray() };
        var context = TdsQueryRequestParser.Parse(packet, RemoteEndPoint, userContext: null);

        // Unsupported SQL, unknown procedures, and parameters that do not convert are error results
        var result = QueryEngineRpcHandlers[(int)authorization](context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var decoded = TdsWire.ReadTabularResult(TdsResponseSerializer.CreateQueryResponse(result, PayloadSizePerPacket), PayloadSizePerPacket);
        FuzzAssert.Equal(result.Error is null ? 0 : 1, decoded.Errors.Count, "An error result must be serialized as one ERROR token");
        if (result.Error is null)
        {
            CheckDecodedResult(result, decoded);
        }

        if (!context.HasCompleteParameters)
        {
            FuzzAssert.Equal(50004u, result.Error?.Number, "A request with incomplete parameters must be rejected");
            return;
        }

        if (context.ProcedureName is { } procedureName && StoredProcedureNames.Contains(procedureName))
        {
            var isDenied = authorization switch
            {
                QueryEngineAuthorization.DenyOrders => string.Equals(procedureName, DeniedProcedureName, StringComparison.OrdinalIgnoreCase),
                QueryEngineAuthorization.DenyAll or QueryEngineAuthorization.DenyStoredProcedures => true,
                _ => false,
            };

            if (isDenied)
            {
                FuzzAssert.Equal(229u, result.Error?.Number, $"The call to the denied procedure {procedureName} must be rejected");
            }
            else
            {
                FuzzAssert.True(result.Error?.Number is null or 50004, $"Unexpected error {result.Error?.Number} for the procedure {procedureName}");
            }
        }
    }

    /// <summary>
    /// The input describes a result: the packet size, the columns and their types, then the rows. A value of each type
    /// is either the CLR type the column maps to or another one it converts from without loss. The serializer must accept
    /// them, and a client must read back the same values.
    /// </summary>
    private static void ResponseSerializer(ReadOnlySpan<byte> data)
    {
        var payloadSizePerPacket = FuzzInput.Pick(PacketSizes, FuzzInput.ReadByte(ref data)) - 8;
        var result = new TdsQueryResult();
        var resultSetCount = (FuzzInput.ReadByte(ref data) % 3) + 1;
        for (var resultSetIndex = 0; resultSetIndex < resultSetCount && !data.IsEmpty; resultSetIndex++)
        {
            var resultSet = new TdsResultSet();
            var columnCount = (FuzzInput.ReadByte(ref data) % 8) + 1;
            for (var i = 0; i < columnCount; i++)
            {
                var header = FuzzInput.ReadByte(ref data);
                resultSet.Columns.Add(new TdsColumn(string.Create(CultureInfo.InvariantCulture, $"c{i}"), FuzzInput.Pick(ColumnTypes, (byte)(header & 0x3F)), isNullable: (header & 0x80) != 0));
            }

            var rowCount = FuzzInput.ReadByte(ref data) % 16;
            for (var row = 0; row < rowCount; row++)
            {
                var values = new object?[resultSet.Columns.Count];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = CreateValue(resultSet.Columns[i], ref data);
                }

                resultSet.Rows.Add(values);
            }

            result.ResultSets.Add(resultSet);
        }

        byte[] response;
        try
        {
            response = TdsResponseSerializer.CreateQueryResponse(result, payloadSizePerPacket);
        }
        catch (OverflowException) when (result.ResultSets.Any(HasDecimalOverflow))
        {
            // A decimal column has a single scale: the values whose magnitude does not fit in decimal(38, scale) once
            // rescaled to the largest scale of the column cannot be sent
            return;
        }

        FuzzAssert.False(result.ResultSets.Any(HasDecimalOverflow), "A decimal column whose values do not fit in decimal(38, scale) must be rejected");
        CheckDecodedResult(result, TdsWire.ReadTabularResult(response, payloadSizePerPacket));
    }

    private static object? CreateValue(TdsColumn column, ref ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data);
        if (column.IsNullable && selector % 8 == 0)
            return null;

        // Another CLR type the value converts from without loss
        var other = (selector & 0x08) != 0;
        var bytes = ReadBytes(ref data, 8);
        var int64 = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var int32 = (int)int64;
        return column.ColumnType switch
        {
            TdsColumnType.TinyInt => other ? (object)(int)bytes[0] : bytes[0],
            TdsColumnType.SmallInt => other ? (object)(sbyte)bytes[0] : (short)int32,
            TdsColumnType.Int32 => other ? (object)(ushort)int32 : int32,
            TdsColumnType.Int64 => other ? (object)(uint)int32 : int64,
            TdsColumnType.Boolean => other ? (object)(int)bytes[0] : (bytes[0] & 1) != 0,
            TdsColumnType.Real => other ? (object)(short)int32 : BitConverter.Int32BitsToSingle(int32),
            TdsColumnType.Double => other ? (object)BitConverter.Int32BitsToSingle(int32) : BitConverter.Int64BitsToDouble(int64),
            TdsColumnType.Decimal => other ? (object)int64 : CreateDecimal(bytes, ReadBytes(ref data, 5)),
            TdsColumnType.Money => other ? (object)int32 : int64 / 10000m,
            TdsColumnType.SmallMoney => other ? (object)(short)int32 : int32 / 10000m,
            TdsColumnType.NVarChar or TdsColumnType.Variant or TdsColumnType.UserDefined or TdsColumnType.Table or TdsColumnType.Xml => other ? (object)int32 : CreateText(bytes[0], ref data),
            TdsColumnType.Json => (selector >> 4) switch
            {
                0 => (object)int32,
                1 => JsonValue.Create(int64),
                2 => new JsonObject { ["text"] = CreateText(bytes[0], ref data) },
                _ => CreateText(bytes[0], ref data),
            },
            TdsColumnType.Binary => other ? (object)CreateText(bytes[0], ref data) : CreateBinary(bytes[0], ref data),
            TdsColumnType.Guid => other ? (object)new Guid([.. bytes, .. bytes]).ToString(FuzzInput.Pick(GuidFormats, bytes[1]), CultureInfo.InvariantCulture) : new Guid([.. bytes, .. ReadBytes(ref data, 8)]),
            TdsColumnType.Date => other ? (object)CreateDateTime(int64) : DateOnly.FromDateTime(CreateDateTime(int64)),
            TdsColumnType.Time => other ? (object)TimeSpan.FromTicks((long)((ulong)int64 % TimeSpan.TicksPerDay)) : new TimeOnly((long)((ulong)int64 % TimeSpan.TicksPerDay)),
            TdsColumnType.DateTime or TdsColumnType.DateTime2 => other ? (object)DateOnly.FromDateTime(CreateDateTime(int64)) : CreateDateTime(int64),
            _ => other ? (object)CreateDateTime(int64) : CreateDateTimeOffset(CreateDateTime(int64), (short)(BinaryPrimitives.ReadInt16LittleEndian(ReadBytes(ref data, 2)) % 841)),
        };
    }

    private static ReadOnlySpan<byte> ReadBytes(ref ReadOnlySpan<byte> data, int count)
    {
        var result = new byte[count];
        var available = Math.Min(count, data.Length);
        data[..available].CopyTo(result);
        data = data[available..];
        return result;
    }

    private static decimal CreateDecimal(ReadOnlySpan<byte> low, ReadOnlySpan<byte> high)
    {
        return new decimal(BinaryPrimitives.ReadInt32LittleEndian(low), BinaryPrimitives.ReadInt32LittleEndian(low[4..]), BinaryPrimitives.ReadInt32LittleEndian(high), (high[4] & 0x80) != 0, (byte)((high[4] & 0x7F) % 29));
    }

    /// <summary>A text of up to 127 bytes, repeated past the 4000 characters that fit in an nvarchar(4000) when the length has its high bit.</summary>
    private static string CreateText(byte length, ref ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(ReadBytes(ref data, length & 0x7F));
        return (length & 0x80) != 0 ? string.Concat(Enumerable.Repeat(text.Length == 0 ? "x" : text, (4001 / Math.Max(text.Length, 1)) + 1)) : text;
    }

    /// <summary>Up to 127 bytes, repeated past the 8000 bytes that fit in a varbinary(8000) when the length has its high bit.</summary>
    private static byte[] CreateBinary(byte length, ref ReadOnlySpan<byte> data)
    {
        var bytes = ReadBytes(ref data, length & 0x7F).ToArray();
        return (length & 0x80) != 0 ? [.. Enumerable.Repeat(bytes.Length == 0 ? [0] : bytes, (8001 / Math.Max(bytes.Length, 1)) + 1).SelectMany(chunk => chunk)] : bytes;
    }

    private static DateTime CreateDateTime(long ticks) => new((long)((ulong)ticks % ((ulong)DateTime.MaxValue.Ticks + 1)), DateTimeKind.Unspecified);

    private static DateTimeOffset CreateDateTimeOffset(DateTime utc, short offsetMinutes)
    {
        // The local time of the value must stay in the range of DateTime
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var localTicks = utc.Ticks + offset.Ticks;
        return new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(localTicks >= 0 && localTicks <= DateTime.MaxValue.Ticks ? offset : TimeSpan.Zero);
    }

    private static bool HasDecimalOverflow(TdsResultSet resultSet)
    {
        for (var i = 0; i < resultSet.Columns.Count; i++)
        {
            if (resultSet.Columns[i].ColumnType is not TdsColumnType.Decimal)
                continue;

            var scale = GetDecimalScale(resultSet, i);
            if (resultSet.Rows.Any(row => row[i] is { } value && GetDecimalMagnitude(Convert.ToDecimal(value, CultureInfo.InvariantCulture), scale) is null))
                return true;
        }

        return false;
    }

    private static byte GetDecimalScale(TdsResultSet resultSet, int column)
    {
        return resultSet.Rows.Select(row => row[column] is { } value ? Convert.ToDecimal(value, CultureInfo.InvariantCulture).Scale : (byte)0).DefaultIfEmpty((byte)0).Max();
    }

    /// <summary>The unscaled magnitude of the value at a larger scale, or null when it exceeds the 38 digits of decimal(38, scale).</summary>
    private static UInt128? GetDecimalMagnitude(decimal value, byte scale)
    {
        Span<int> bits = stackalloc int[4];
        _ = decimal.GetBits(value, bits);
        var magnitude = new UInt128((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        for (var i = value.Scale; i < scale; i++)
        {
            if (magnitude > MaxDecimalMagnitude / 10)
                return null;

            magnitude *= 10;
        }

        return magnitude <= MaxDecimalMagnitude ? magnitude : null;
    }

    /// <summary>Checks that the decoded tokens hold the info messages, the columns and the values of the result.</summary>
    private static void CheckDecodedResult(TdsQueryResult result, TdsWire.DecodedTabularResult decoded)
    {
        if (result.Error is not null)
            return;

        FuzzAssert.Equal(result.InfoMessages.Count, decoded.InfoMessages.Count, "Each info message must be an INFO token");
        FuzzAssert.Equal(result.ResultSets.Count, decoded.ResultSets.Count, "Each result set must be decoded");
        for (var i = 0; i < result.ResultSets.Count; i++)
        {
            var resultSet = result.ResultSets[i];
            var decodedResultSet = decoded.ResultSets[i];
            FuzzAssert.Equal(resultSet.Columns.Count, decodedResultSet.Columns.Count, "Each column must be in COLMETADATA");
            FuzzAssert.Equal(resultSet.Rows.Count, decodedResultSet.Rows.Count, "Each row must be a ROW token");
            for (var column = 0; column < resultSet.Columns.Count; column++)
            {
                var expectedColumn = resultSet.Columns[column];
                var decodedColumn = decodedResultSet.Columns[column];
                FuzzAssert.Equal(expectedColumn.Name, decodedColumn.Name, "The column name must be in COLMETADATA");
                FuzzAssert.Equal(expectedColumn.IsNullable, decodedColumn.IsNullable, $"The nullability of the column {expectedColumn.Name} must be in COLMETADATA");
                var scale = expectedColumn.ColumnType is TdsColumnType.Decimal ? GetDecimalScale(resultSet, column) : (byte)0;
                for (var row = 0; row < resultSet.Rows.Count; row++)
                {
                    var value = column < resultSet.Rows[row].Count ? resultSet.Rows[row][column] : null;
                    var expected = value is null ? null : GetWireValue(expectedColumn.ColumnType, value, scale);
                    var actual = decodedResultSet.Rows[row][column];
                    var isEqual = (expected, actual) switch
                    {
                        (byte[] expectedBytes, byte[] actualBytes) => expectedBytes.AsSpan().SequenceEqual(actualBytes),
                        (DateTimeOffset expectedOffset, DateTimeOffset actualOffset) => expectedOffset == actualOffset && expectedOffset.Offset == actualOffset.Offset,
                        _ => Equals(expected, actual),
                    };
                    FuzzAssert.True(isEqual, $"The value {FuzzAssert.Format(value)} of the {expectedColumn.ColumnType} column {expectedColumn.Name} must be decoded as {FuzzAssert.Format(expected)}, not {FuzzAssert.Format(actual)}");
                }
            }
        }
    }

    /// <summary>Gets the value a client decodes from a column of the type, as <see cref="TdsWire.ReadTabularResult"/> represents it.</summary>
    private static object GetWireValue(TdsColumnType columnType, object value, byte scale)
    {
        return columnType switch
        {
            TdsColumnType.TinyInt or TdsColumnType.SmallInt or TdsColumnType.Int32 or TdsColumnType.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            TdsColumnType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            TdsColumnType.Real => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            TdsColumnType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            TdsColumnType.Decimal => new TdsWire.DecodedDecimal(Convert.ToDecimal(value, CultureInfo.InvariantCulture) < 0, GetDecimalMagnitude(Convert.ToDecimal(value, CultureInfo.InvariantCulture), scale)!.Value, scale),

            // MONEY is a whole number of ten-thousandths
            TdsColumnType.Money or TdsColumnType.SmallMoney => decimal.Truncate(Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 10000m) / 10000m,
            TdsColumnType.Binary => value as byte[] ?? Encoding.UTF8.GetBytes(value.ToString() ?? ""),
            TdsColumnType.Guid => value as Guid? ?? Guid.Parse((string)value, CultureInfo.InvariantCulture),
            TdsColumnType.Date => DateOnly.FromDateTime(ToDateTime(value)),
            TdsColumnType.Time => value switch
            {
                TimeOnly time => time.ToTimeSpan(),
                _ => (TimeSpan)value,
            },
            TdsColumnType.DateTime => new TdsWire.DecodedDateTime((ToDateTime(value).Date - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)).Days, (uint)(ToDateTime(value).TimeOfDay.Ticks * 300 / TimeSpan.TicksPerSecond)),
            TdsColumnType.DateTime2 => ToDateTime(value),
            TdsColumnType.DateTimeOffset => value as DateTimeOffset? ?? new DateTimeOffset((DateTime)value, TimeSpan.Zero),

            // JSON is sent as UTF-8, which cannot carry an unpaired surrogate
            TdsColumnType.Json => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value switch
            {
                string text => text,
                JsonNode node => node.ToJsonString(),
                _ => JsonSerializer.Serialize(value),
            })),

            // The other types are sent as UTF-16 text. The encoder replaces an unpaired surrogate with U+FFFD, which is
            // what SqlClient decodes it to anyway.
            _ => Encoding.Unicode.GetString(Encoding.Unicode.GetBytes(columnType is TdsColumnType.NVarChar ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : value.ToString() ?? "")),
        };

        static DateTime ToDateTime(object value) => value switch
        {
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dateTimeOffset => dateTimeOffset.DateTime,
            _ => (DateTime)value,
        };
    }

    private static void RunConnection(TdsServerOptions options, RecordingLogger logger, byte[] input, MemoryStream output)
    {
        // The handlers complete synchronously and the streams are in memory, so the request never runs concurrently with
        // a read: the connection is over, and nothing is left running, once ProcessAsync returns
        RunConnectionAsync(options, logger, input, output).GetAwaiter().GetResult();
    }

    private static async Task RunConnectionAsync(TdsServerOptions options, RecordingLogger logger, byte[] input, MemoryStream output)
    {
        var processor = new TdsConnectionProcessor(options, Authenticate, EchoAsync, logger);
        using var inputStream = new MemoryStream(input, writable: false);
        await processor.ProcessAsync(inputStream, output, RemoteEndPoint, CancellationToken.None).ConfigureAwait(false);
    }

    private static ValueTask<TdsAuthenticationResult> Authenticate(TdsAuthenticationContext context, CancellationToken cancellationToken)
    {
        // The login fields flow into the responses: the database into an ENVCHANGE token, the user name into an error
        if (context.Password is { } password && password.StartsWith("deny", StringComparison.Ordinal))
            return ValueTask.FromResult(TdsAuthenticationResult.Fail($"Login failed for user '{context.UserName}'."));

        return ValueTask.FromResult(TdsAuthenticationResult.Success(context.Database ?? "master"));
    }

    private static ValueTask<TdsQueryResult> EchoAsync(TdsQueryContext context, CancellationToken cancellationToken)
    {
        foreach (var parameter in context.Parameters)
        {
            CheckParameter(parameter);
        }

        return ValueTask.FromResult(Echo(context));
    }

    /// <summary>
    /// A result that echoes the request in text and Variant columns, which serialize any value, then the parameters again,
    /// each in a column of the type the parser decoded.
    /// </summary>
    private static TdsQueryResult Echo(TdsQueryContext context)
    {
        var result = new TdsQueryResult();
        if (context.CommandText is not null)
        {
            result.InfoMessages.Add(context.CommandText);
        }

        var request = new TdsResultSet();
        request.Columns.Add(new TdsColumn("command_text", TdsColumnType.NVarChar));
        request.Columns.Add(new TdsColumn("procedure_name", TdsColumnType.NVarChar));
        request.Columns.Add(new TdsColumn("complete", TdsColumnType.Boolean, isNullable: false));
        request.Columns.Add(new TdsColumn("parameter_count", TdsColumnType.Int32, isNullable: false));
        request.Rows.Add([context.CommandText, context.ProcedureName, context.HasCompleteParameters, context.Parameters.Count]);
        result.ResultSets.Add(request);

        if (context.Parameters.Count > 0)
        {
            var parameters = new TdsResultSet();
            parameters.Columns.Add(new TdsColumn("name", TdsColumnType.NVarChar));
            parameters.Columns.Add(new TdsColumn("type", TdsColumnType.NVarChar, isNullable: false));
            parameters.Columns.Add(new TdsColumn("value", TdsColumnType.Variant));
            parameters.Columns.Add(new TdsColumn("text", TdsColumnType.NVarChar));
            parameters.Columns.Add(new TdsColumn("binary", TdsColumnType.Binary));
            foreach (var parameter in context.Parameters)
            {
                parameters.Rows.Add([parameter.Name, parameter.Type.ToString(), parameter.IsNull ? null : parameter.Value, parameter.AsString(), parameter.AsBinary()]);
            }

            result.ResultSets.Add(parameters);

            var typed = new TdsResultSet();
            for (var i = 0; i < context.Parameters.Count; i++)
            {
                typed.Columns.Add(new TdsColumn(string.Create(CultureInfo.InvariantCulture, $"p{i}"), context.Parameters[i].Type));
            }

            typed.Rows.Add([.. context.Parameters.Select(parameter => parameter.IsNull ? null : parameter.Value)]);
            result.ResultSets.Add(typed);
        }

        return result;
    }

    /// <summary>The accessors of a decoded parameter return <see langword="null"/> when the value does not convert, they do not throw.</summary>
    private static void CheckParameter(TdsQueryParameter parameter)
    {
        FuzzAssert.True(parameter.Name is not null && parameter.Value is not null, "A parameter must have a name and a value (DBNull for NULL)");
        FuzzAssert.Equal(parameter.Value is DBNull, parameter.IsNull, "IsNull must match a DBNull value");
        FuzzAssert.True(Enum.IsDefined(parameter.Type), $"Unexpected parameter type {parameter.Type}");
        _ = parameter.AsString();
        _ = parameter.AsInt32();
        _ = parameter.AsInt64();
        _ = parameter.AsBoolean();
        _ = parameter.AsDouble();
        _ = parameter.AsDecimal();
        _ = parameter.AsBinary();
        if (parameter.IsNull)
        {
            FuzzAssert.True(parameter.AsString() is null && parameter.AsInt32() is null && parameter.AsBinary() is null, "A NULL parameter must convert to null");
        }

        // Any value goes through the text or binary form the JSON and XML parsers read
        try
        {
            FuzzAssert.True(parameter.AsJson() is not null || parameter.IsNull, "A non-NULL parameter must parse as a JSON object or throw");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // A value that is not a JSON object
        }

        try
        {
            FuzzAssert.True(parameter.AsXml() is not null || parameter.IsNull, "A non-NULL parameter must parse as XML or throw");
        }
        catch (XmlException)
        {
            // Documented for a value that is not well-formed XML, or that declares a DTD
        }
    }

    /// <summary>The processor logs the exceptions it turns into responses; the only expected ones are the PRELOGIN and LOGIN7 payloads it cannot parse.</summary>
    private static void CheckLogs(RecordingLogger logger)
    {
        foreach (var entry in logger.Entries)
        {
            if (entry.Level >= LogLevel.Warning)
                throw new FuzzAssertionException($"The connection processor logged an error: {entry.Message}", entry.Exception ?? new InvalidOperationException(entry.Message));

            if (entry.Exception is not null and not InvalidDataException)
                throw new FuzzAssertionException($"Unexpected exception while parsing a login payload: {entry.Message}", entry.Exception);
        }
    }

    /// <summary>Splits the server output into messages, checking the framing of every packet and the DONE token that ends a tabular result.</summary>
    private static List<(byte Type, byte[] Payload)> CheckResponses(byte[] output, int packetSize)
    {
        var messages = new List<(byte Type, byte[] Payload)>();
        var payload = new List<byte>();
        byte? messageType = null;
        var position = 0;
        while (position < output.Length)
        {
            FuzzAssert.True(output.Length - position >= 8, "The output ends with a truncated packet header");
            var type = output[position];
            var status = output[position + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(position + 2, 2));
            FuzzAssert.True(type is TdsWire.TabularResult or TdsWire.PreLogin, $"Unexpected response packet type 0x{type:X2}");
            FuzzAssert.True(messageType is null || messageType == type, "The packets of a response message must share their type");
            FuzzAssert.True(length >= 8 && length <= packetSize, $"Invalid response packet length {length} (packet size {packetSize})");
            FuzzAssert.True(position + length <= output.Length, "The output ends with a truncated packet");
            messageType = type;
            payload.AddRange(output.AsSpan(position + 8, length - 8));
            position += length;
            if ((status & 0x01) != 0)
            {
                var message = payload.ToArray();
                if (type is TdsWire.PreLogin)
                {
                    // The server has no certificate, so it answers NOT_SUPPORTED, in a copy of the options of the request
                    FuzzAssert.Equal(TdsPreLoginEncryptionMode.NotSupported, TdsPreLoginMessage.ParseEncryptionMode(message), "The PRELOGIN response must advertise NOT_SUPPORTED");
                }
                else
                {
                    FuzzAssert.True(message.Length >= 13 && message[^13] == 0xFD, "A tabular result must end with a DONE token");
                    _ = TdsWire.ReadTabularResult(message, packetSize - 8);
                }

                messages.Add((type, message));
                payload.Clear();
                messageType = null;
            }
        }

        FuzzAssert.True(messageType is null, "The output ends in the middle of a message");
        return messages;
    }

    private static bool IsError(byte[] tabularResult) => tabularResult is [0xAA, ..];

    private static TdsQueryEngineOptions CreateQueryEngineOptions(QueryEngineAuthorization authorization)
    {
        var options = new TdsQueryEngineOptions();
        options.IsAuthorized = authorization switch
        {
            QueryEngineAuthorization.DenyOrders => (_, resourceKind, resourceName) => !string.Equals(resourceName, resourceKind is TdsQueryEngineResourceKind.QueryRoot ? "orders" : DeniedProcedureName, StringComparison.OrdinalIgnoreCase),
            QueryEngineAuthorization.DenyAll => (_, _, _) => false,
            QueryEngineAuthorization.DenyStoredProcedures => (_, resourceKind, _) => resourceKind is not TdsQueryEngineResourceKind.StoredProcedure,
            _ => options.IsAuthorized,
        };

        // The procedures cannot fail: the engine converts the RPC parameters to their parameter types, or reports an error
        options.StoredProcedures.Add("get_customer", (int id) => Customers.AsQueryable().Where(customer => customer.Id == id));
        options.StoredProcedures.Add("find_customers", FindCustomers);
        options.StoredProcedures.Add("echo_types", EchoTypes);
        options.StoredProcedures.Add("add", (long a, long b) => Task.FromResult(unchecked(a + b)));
        options.StoredProcedures.Add("text_length", (string? text) => ValueTask.FromResult(text?.Length));
        options.StoredProcedures.Add("nothing", () => { });
        options.StoredProcedures.Add("complete", (CancellationToken cancellationToken) => Task.Delay(0, cancellationToken));
        options.StoredProcedures.Add("result", (TdsQueryContext context) =>
        {
            var result = new TdsQueryResult();
            result.InfoMessages.Add(string.Create(CultureInfo.InvariantCulture, $"{context.Parameters.Count} parameters"));
            return result;
        });
        options.StoredProcedures.Add(DeniedProcedureName, () => Customers);

        // Custom scalar functions get the translated arguments, whatever their types
        options.AddScalarFunction("FUZZ_TEXT", arguments => arguments is [var argument]
            ? Expression.Call(typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(object), typeof(IFormatProvider)])!, Expression.Convert(argument, typeof(object)), Expression.Constant(CultureInfo.InvariantCulture, typeof(IFormatProvider)))
            : Expression.Constant(null, typeof(string)));
        options.AddScalarFunction("FUZZ_COALESCE", arguments => arguments is [var first, var second] && first.Type == second.Type && !first.Type.IsValueType
            ? Expression.Coalesce(first, second)
            : arguments.Count > 0 ? arguments[0] : Expression.Constant(null, typeof(object)));

        options.AddQueryRoot("customers", _ => Customers.AsQueryable());
        options.AddQueryRoot("orders", _ => new[] { new Order(1, "North", 10), new Order(2, "North", 20), new Order(3, "South", 5) }.AsQueryable());
        options.AddQueryRoot("nullable_customers", _ => new[] { new NullableCustomer(1, "Alice", 100), new NullableCustomer(2, "Bob", 5), new NullableCustomer(3, null, null) }.AsQueryable());
        options.AddQueryRoot("json_docs", _ => new[]
        {
            new JsonDocumentRow(1, """{"name":"Alice","address":{"city":"Paris"},"items":[1,2]}"""),
            new JsonDocumentRow(2, """{"name":"Bob"}"""),
            new JsonDocumentRow(3, "{ invalid }"),
            new JsonDocumentRow(4, null),
        }.AsQueryable());
        options.AddQueryRoot("xml_docs", _ => new[]
        {
            new XmlDocumentRow(1, "<root><item id=\"1\">Alpha</item><item id=\"2\">Beta</item></root>"),
            new XmlDocumentRow(2, "<root><item id=\"3\">Gamma</item></root>"),
        }.AsQueryable());
        return options;
    }

    private static IEnumerable<FuzzSeed> GetSessionSeeds()
    {
        var preLogin = TdsWire.Message(TdsWire.PreLogin, TdsWire.PreLoginPayload(TdsWire.EncryptionNotSupported));
        var login = TdsWire.Message(TdsWire.Login7, TdsWire.Login7Payload("sa", "Password123!", "fuzz", "master"));
        yield return Seed("empty", []);
        yield return Seed("prelogin", preLogin);
        yield return Seed("prelogin-encryption-off", TdsWire.Message(TdsWire.PreLogin, TdsWire.PreLoginPayload(TdsWire.EncryptionOff)));
        yield return Seed("prelogin-encryption-required", TdsWire.Message(TdsWire.PreLogin, TdsWire.PreLoginPayload(TdsWire.EncryptionRequired)));
        yield return Seed("prelogin-minimal", TdsWire.Message(TdsWire.PreLogin, [0x01, 0x00, 0x06, 0x00, 0x01, 0xFF, 0x02]));
        yield return Seed("login-empty-fields", [.. preLogin, .. TdsWire.Message(TdsWire.Login7, new byte[94])]);
        yield return Seed("login-sspi", [.. preLogin, .. TdsWire.Message(TdsWire.Login7, TdsWire.Login7Payload(null, null, "fuzz", null, [0x4E, 0x54, 0x4C, 0x4D, 0x53, 0x53, 0x50, 0x00, 0x01]))]);
        yield return Seed("login-denied", [.. preLogin, .. TdsWire.Message(TdsWire.Login7, TdsWire.Login7Payload("guest", "deny", "fuzz", "master"))]);
        yield return Seed("sql-batch", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.SqlBatch, TdsWire.SqlBatchPayload("SELECT 1"))]);
        yield return Seed("sql-batch-without-headers", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.SqlBatch, TdsWire.SqlBatchPayload("SELECT name FROM sys.databases", withAllHeaders: false))]);
        yield return Seed("sql-batch-split", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.SqlBatch, TdsWire.SqlBatchPayload("SELECT * FROM customers WHERE Id = 1"), chunkSize: 9)]);
        yield return Seed("rpc", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.Rpc, CreateExecuteSqlPayload())]);
        yield return Seed("rpc-then-attention", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.Rpc, CreateExecuteSqlPayload()), .. TdsWire.Message(TdsWire.Attention, [])]);
        yield return Seed("attention", [.. preLogin, .. login, .. TdsWire.Message(TdsWire.Attention, []), .. TdsWire.Message(TdsWire.SqlBatch, TdsWire.SqlBatchPayload("SELECT 2"))]);
        yield return Seed("unsupported-packet", [.. preLogin, .. login, .. TdsWire.Message(0x0E, [0x01, 0x02]), .. TdsWire.Message(TdsWire.SqlBatch, TdsWire.SqlBatchPayload("SELECT 3"))]);
        yield return Seed("login-first", login);
    }

    private static IEnumerable<FuzzSeed> GetMessagesSeeds()
    {
        yield return Seed("nothing", [3]);
        yield return Seed("sql-batch", CreateMessagesInput(3, (0x00, TdsWire.SqlBatchPayload("SELECT 1"))));
        yield return Seed("sql-batch-split", CreateMessagesInput(0, (0x0C, TdsWire.SqlBatchPayload("SELECT Id, Name FROM customers ORDER BY Name"))));
        yield return Seed("rpc", CreateMessagesInput(3, (0x01, CreateExecuteSqlPayload())));
        yield return Seed("rpc-split-attention", CreateMessagesInput(1, (0x85, CreateExecuteSqlPayload())));
        yield return Seed("rpc-by-name", CreateMessagesInput(2, (0x01, TdsWire.RpcPayload("dbo.GetCustomer", TdsWire.Parameter("@id", TdsWire.IntN(4, 1)), TdsWire.Parameter("@name", TdsWire.NVarChar("Alice"), status: 1)))));
        yield return Seed("all-types", CreateMessagesInput(6, (0x01, CreateAllTypesPayload())));
        yield return Seed("long-text", CreateMessagesInput(0, (0x00, TdsWire.SqlBatchPayload(new string('x', 300) + "\u00E9\U0001F600"))));
        yield return Seed("mixed", CreateMessagesInput(3, (0x02, []), (0x03, [0x01]), (0x40, TdsWire.SqlBatchPayload("SELECT 2")), (0x01, TdsWire.RpcPayload(13))));
    }

    private static FuzzSeed Seed(string name, byte[] data) => new(name, data);

    private static byte[] CreateMessagesInput(byte packetSize, params (byte Header, byte[] Payload)[] messages)
    {
        var result = new List<byte> { packetSize };
        foreach (var (header, payload) in messages)
        {
            result.Add(header);
            if ((header & 0x03) == 3)
            {
                // A raw packet type: an unsupported one
                result.Add(0x0E);
            }

            result.Add((byte)payload.Length);
            result.Add((byte)(payload.Length >> 8));
            result.AddRange(payload);
        }

        return [.. result];
    }

    private static IEnumerable<FuzzSeed> GetRequestParserSeeds()
    {
        yield return Seed("sql-batch", [0, .. TdsWire.SqlBatchPayload("SELECT 1")]);
        yield return Seed("sql-batch-without-headers", [0, .. TdsWire.SqlBatchPayload("SELECT @@VERSION", withAllHeaders: false)]);
        yield return Seed("rpc-sp-executesql", [1, .. CreateExecuteSqlPayload()]);
        yield return Seed("rpc-all-types", [1, .. CreateAllTypesPayload()]);
        yield return Seed("rpc-nulls", [1, .. CreateNullsPayload()]);
        yield return Seed("rpc-by-name", [1, .. TdsWire.RpcPayload("dbo.GetCustomer", TdsWire.Parameter("@id", TdsWire.IntN(8, long.MinValue)))]);
        yield return Seed("rpc-prepare", [1, .. TdsWire.RpcPayload(11, TdsWire.Parameter("@handle", TdsWire.IntN(4, null), status: 1), TdsWire.Parameter("@params", TdsWire.NVarChar("@p0 int")), TdsWire.Parameter("@stmt", TdsWire.NVarChar("SELECT @p0")))]);
        yield return Seed("rpc-varchar-collations", [1, .. CreateVarCharPayload()]);
        yield return Seed("rpc-date-out-of-range", [1, .. TdsWire.RpcPayload("p", TdsWire.Parameter("@d", [0x28, 0x03, 0xFF, 0xFF, 0xFF]))]);
    }

    private static IEnumerable<FuzzSeed> GetLoginSeeds()
    {
        yield return Seed("empty-fields", new byte[94]);
        yield return Seed("short", new byte[93]);
        yield return Seed("fields", TdsWire.Login7Payload("sa", "Password123!", "Microsoft SqlClient Data Provider", "master"));
        yield return Seed("unicode", TdsWire.Login7Payload("us\u00E9r", "p\u00E2ss\U0001F511", "app", "d\u00E9"));
        yield return Seed("sspi", TdsWire.Login7Payload(null, null, "app", null, [0x4E, 0x54, 0x4C, 0x4D, 0x53, 0x53, 0x50, 0x00, 0x01, 0x00, 0x00, 0x00]));
    }

    private static IEnumerable<FuzzSeed> GetPreLoginSeeds()
    {
        yield return Seed("empty", []);
        yield return Seed("not-supported", TdsWire.PreLoginPayload(TdsWire.EncryptionNotSupported));
        yield return Seed("off", TdsWire.PreLoginPayload(TdsWire.EncryptionOff));
        yield return Seed("required", TdsWire.PreLoginPayload(TdsWire.EncryptionRequired));
        yield return Seed("minimal", new byte[] { 0x01, 0x00, 0x06, 0x00, 0x01, 0xFF, 0x02 });

        // VERSION, ENCRYPTION, INSTOPT, THREADID, MARS, as SqlClient sends them
        yield return Seed("sqlclient", new byte[]
        {
            0x00, 0x00, 0x1A, 0x00, 0x06, 0x01, 0x00, 0x20, 0x00, 0x01, 0x02, 0x00, 0x21, 0x00, 0x01, 0x03, 0x00, 0x22, 0x00, 0x04, 0x04, 0x00, 0x26, 0x00, 0x01, 0xFF,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        });
    }

    /// <summary>The SQL texts of the query engine tests.</summary>
    private static IEnumerable<FuzzSeed> GetQueryEngineSeeds()
    {
        var index = 0;
        foreach (var commandText in GetQueryEngineTestCommandTexts())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index++}"), commandText);
        }

        yield return FuzzSeed.FromText("divide", "SELECT Id / 2 AS Half, Amount % 3 AS Remainder FROM orders WHERE Amount > 0");
        yield return FuzzSeed.FromText("cast", "SELECT CAST(Id AS nvarchar(10)) AS Text, CAST('12' AS int) AS Number FROM customers");
        yield return FuzzSeed.FromText("not-a-select", "UPDATE customers SET Name = 'x'");
        yield return FuzzSeed.FromText("invalid", "SELECT FROM WHERE");
        yield return FuzzSeed.FromText("custom-functions", "SELECT FUZZ_TEXT(Id) AS Text, FUZZ_COALESCE(Name, 'x') AS Name, FUZZ_TEXT() AS Empty FROM customers");
    }

    private static IEnumerable<string> GetQueryEngineTestCommandTexts()
    {
        var source = FuzzResources.ReadText(typeof(TdsServerFuzzTargets).Assembly, "TdsQueryEngineTests.cs");
        foreach (Match match in CommandTextRegex().Matches(source))
        {
            yield return GetRawStringValue(match);
        }
    }

    /// <summary>The parameterized SQL texts of the query engine tests through sp_executesql, and calls to each stored procedure in each authorization mode.</summary>
    private static IEnumerable<FuzzSeed> GetQueryEngineRpcSeeds()
    {
        var index = 0;
        foreach (var commandText in GetQueryEngineTestCommandTexts().Where(text => text.Contains('@', StringComparison.Ordinal)))
        {
            yield return Seed(string.Create(CultureInfo.InvariantCulture, $"test-{index++}"), [(byte)QueryEngineAuthorization.AllowAll, .. CreateExecuteSqlPayload(commandText)]);
        }

        foreach (var authorization in Enum.GetValues<QueryEngineAuthorization>())
        {
            var mode = (byte)authorization;
            var name = authorization.ToString().ToLowerInvariant();
            yield return Seed($"{name}-orders", [mode, .. CreateExecuteSqlPayload("SELECT o.Id, c.Name FROM orders o INNER JOIN customers c ON c.Id = o.Id WHERE o.Id <> @id")]);
            yield return Seed($"{name}-no-table", [mode, .. CreateExecuteSqlPayload("SELECT @id + @offset AS Total, FUZZ_TEXT(@name) AS Text")]);
            yield return Seed($"{name}-get-customer", [mode, .. TdsWire.RpcPayload("get_customer", TdsWire.Parameter("@id", TdsWire.IntN(4, 2)))]);
            yield return Seed($"{name}-denied", [mode, .. TdsWire.RpcPayload(DeniedProcedureName)]);
        }

        yield return Seed("custom-functions", [0, .. CreateExecuteSqlPayload("SELECT FUZZ_TEXT(Id) AS Text, FUZZ_COALESCE(Name, @name) AS Name FROM customers WHERE Id > @id")]);
        yield return Seed("find-customers", [0, .. TdsWire.RpcPayload("find_customers", TdsWire.Parameter("@name", TdsWire.NVarChar("li")))]);
        yield return Seed("find-customers-limit", [0, .. TdsWire.RpcPayload("dbo.find_customers", TdsWire.Parameter("@name", TdsWire.NVarChar(null)), TdsWire.Parameter("@limit", TdsWire.IntN(8, 1)))]);
        yield return Seed("echo-types", [0, .. TdsWire.RpcPayload(
            "echo_types",
            TdsWire.Parameter("@number", TdsWire.IntN(8, long.MinValue)),
            TdsWire.Parameter("@flag", TdsWire.BitN(value: true)),
            TdsWire.Parameter("@real", TdsWire.FloatN(double.NaN)),
            TdsWire.Parameter("@amount", TdsWire.DecimalN(-12345.6789m)),
            TdsWire.Parameter("@id", TdsWire.GuidN(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"))),
            TdsWire.Parameter("@moment", TdsWire.DateTime2N(new DateTime(2024, 2, 29, 12, 30, 0, DateTimeKind.Unspecified))),
            TdsWire.Parameter("@day", TdsWire.DateN(new DateOnly(1, 1, 1))),
            TdsWire.Parameter("@time", TdsWire.TimeN(new TimeOnly(23, 59, 59))),
            TdsWire.Parameter("@offset", TdsWire.DateTimeOffsetN(new DateTimeOffset(2024, 1, 1, 0, 30, 0, TimeSpan.FromHours(-14)))),
            TdsWire.Parameter("@data", TdsWire.VarBinaryMax([1, 2, 3])),
            TdsWire.Parameter("@text", TdsWire.NVarChar("héllo")),
            TdsWire.Parameter("@dayOfWeek", TdsWire.NVarChar("friday")))]);
        yield return Seed("echo-types-conversions", [0, .. TdsWire.RpcPayload(
            "echo_types",
            TdsWire.Parameter("@number", TdsWire.NVarChar("42")),
            TdsWire.Parameter("@flag", TdsWire.IntN(4, 1)),
            TdsWire.Parameter("@amount", TdsWire.MoneyN(1.5m)),
            TdsWire.Parameter("@id", TdsWire.NVarChar("6f9619ff-8b86-d011-b42d-00c04fc964ff")),
            TdsWire.Parameter("@day", TdsWire.NVarChar("2024-02-29")),
            TdsWire.Parameter("@dayOfWeek", TdsWire.IntN(4, 3)))]);
        yield return Seed("echo-types-invalid", [0, .. TdsWire.RpcPayload("echo_types", TdsWire.Parameter("@id", TdsWire.IntN(4, 1)), TdsWire.Parameter("@offset", TdsWire.NVarChar("x")))]);
        yield return Seed("add", [0, .. TdsWire.RpcPayload("add", TdsWire.Parameter("@a", TdsWire.IntN(8, long.MaxValue)), TdsWire.Parameter("@b", TdsWire.IntN(1, 1)))]);
        yield return Seed("add-missing", [0, .. TdsWire.RpcPayload("add", TdsWire.Parameter("@a", TdsWire.IntN(8, 1)))]);
        yield return Seed("text-length", [0, .. TdsWire.RpcPayload("text_length", TdsWire.Parameter("@text", TdsWire.NVarCharMax("abc")))]);
        yield return Seed("nothing", [0, .. TdsWire.RpcPayload("nothing")]);
        yield return Seed("complete", [0, .. TdsWire.RpcPayload("complete")]);
        yield return Seed("result", [0, .. TdsWire.RpcPayload("result", TdsWire.Parameter("@x", TdsWire.BitN(null)))]);
        yield return Seed("unknown", [2, .. TdsWire.RpcPayload("missing_proc")]);
        yield return Seed("incomplete", [0, .. TdsWire.RpcPayload("get_customer", TdsWire.Parameter("@id", [0x99, 0x01]))]);
        yield return Seed("execute-sql-without-statement", [0, .. TdsWire.RpcPayload(10)]);
    }

    /// <summary>Each column type, nullable or not, with the values of its CLR type and of the other types it converts from.</summary>
    private static IEnumerable<FuzzSeed> GetResponseSerializerSeeds()
    {
        var filler = Enumerable.Range(0, 512).Select(i => (byte)((i * 37) + 11)).ToArray();
        for (var type = 0; type < ColumnTypes.Length; type++)
        {
            var name = ColumnTypes[type].ToString().ToLowerInvariant();
            yield return Seed(name, [3, 0, 0, (byte)type, 4, .. filler]);
            yield return Seed($"{name}-nullable", [5, 0, 0, (byte)(type | 0x80), 8, .. filler.Reverse()]);
            yield return Seed($"{name}-other", [0, 0, 0, (byte)type, 2, 0x09, .. filler[..64], 0x0B, .. filler[64..]]);
        }

        yield return Seed("all-columns", [4, 2, 7, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 3, .. filler, 7, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 3, .. filler]);
        yield return Seed("long-text", [0, 0, 1, 10, 0x8A, 2, 1, 0x85, 0, 0, 0, 0, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', 1, 0x10, 0, 0, 0, 0, 0, 0, 0, (byte)'x']);
        yield return Seed("long-binary", [6, 0, 0, 11, 1, 1, 0x83, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3]);
        yield return Seed("decimal-overflow", [3, 0, 0, 7, 2, 1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 28]);
        yield return Seed("decimal-scales", [3, 0, 0, 0x87, 3, 1, 0x39, 0x30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x02, 1, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0]);
    }

    /// <summary>Gets the value of a raw string literal: the lines between the quotes, without the indentation of the closing quotes.</summary>
    private static string GetRawStringValue(Match match)
    {
        var indentation = match.Groups["indent"].Value;
        var lines = match.Groups["body"].Value.ReplaceLineEndings("\n").Split('\n');
        return string.Join('\n', lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
    }

    private static byte[] CreateExecuteSqlPayload(string statement)
    {
        return TdsWire.RpcPayload(
            10,
            TdsWire.Parameter("@stmt", TdsWire.NVarChar(statement)),
            TdsWire.Parameter("@params", TdsWire.NVarChar(ExecuteSqlParameterDeclarations)),
            TdsWire.Parameter("@id", TdsWire.IntN(4, 1)),
            TdsWire.Parameter("@name", TdsWire.NVarChar("Alice")),
            TdsWire.Parameter("@offset", TdsWire.IntN(4, 10)),
            TdsWire.Parameter("@factor", TdsWire.IntN(4, 2)));
    }

    private static IQueryable<Customer> FindCustomers(string? name, int limit = 10)
    {
        var customers = Customers.AsQueryable();
        return (name is null ? customers : customers.Where(customer => customer.Name.Contains(name, StringComparison.OrdinalIgnoreCase))).Take(limit);
    }

    private static EchoTypesRow EchoTypes(TdsQueryContext context, long number = 0, bool flag = false, double real = 0, decimal amount = 0, Guid id = default, DateTime moment = default, DateOnly day = default, TimeOnly time = default, DateTimeOffset offset = default, byte[]? data = null, string? text = null, DayOfWeek dayOfWeek = DayOfWeek.Sunday)
    {
        return new EchoTypesRow(context.Parameters.Count, number, flag, real, amount, id, moment, day, time, offset, data, text, dayOfWeek);
    }

    private static byte[] CreateExecuteSqlPayload()
    {
        return TdsWire.RpcPayload(
            10,
            TdsWire.Parameter("@stmt", TdsWire.NVarChar("SELECT Id, Name FROM customers WHERE Id = @p0 AND Name <> @p1")),
            TdsWire.Parameter("@params", TdsWire.NVarChar("@p0 int, @p1 nvarchar(4000)")),
            TdsWire.Parameter("@p0", TdsWire.IntN(4, 2)),
            TdsWire.Parameter("@p1", TdsWire.NVarCharMax("Alice")));
    }

    private static byte[] CreateAllTypesPayload()
    {
        return TdsWire.RpcPayload(
            10,
            TdsWire.Parameter("@stmt", TdsWire.NVarChar("SELECT 1")),
            TdsWire.Parameter("@tiny", TdsWire.IntN(1, 255)),
            TdsWire.Parameter("@small", TdsWire.IntN(2, -32768)),
            TdsWire.Parameter("@int", TdsWire.IntN(4, int.MaxValue)),
            TdsWire.Parameter("@big", TdsWire.IntN(8, long.MaxValue)),
            TdsWire.Parameter("@bit", TdsWire.BitN(value: true)),
            TdsWire.Parameter("@real", TdsWire.RealN(1.5f)),
            TdsWire.Parameter("@float", TdsWire.FloatN(-2.25e10)),
            TdsWire.Parameter("@guid", TdsWire.GuidN(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"))),
            TdsWire.Parameter("@decimal", TdsWire.DecimalN(-12345.6789m)),
            TdsWire.Parameter("@money", TdsWire.MoneyN(922337203685477.5807m)),
            TdsWire.Parameter("@smallmoney", TdsWire.SmallMoneyN(-214748.3648m)),
            TdsWire.Parameter("@datetime", TdsWire.DateTimeN(45000, 300 * 3600)),
            TdsWire.Parameter("@smalldatetime", TdsWire.SmallDateTimeN(45000, 720)),
            TdsWire.Parameter("@date", TdsWire.DateN(new DateOnly(2024, 2, 29))),
            TdsWire.Parameter("@time", TdsWire.TimeN(new TimeOnly(23, 59, 59, 999))),
            TdsWire.Parameter("@datetime2", TdsWire.DateTime2N(new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified))),
            TdsWire.Parameter("@dto", TdsWire.DateTimeOffsetN(new DateTimeOffset(2024, 1, 1, 0, 30, 0, TimeSpan.FromHours(14)))),
            TdsWire.Parameter("@nvarchar", TdsWire.NVarChar("h\u00E9llo \U0001F600")),
            TdsWire.Parameter("@xml", TdsWire.NVarChar("<root a=\"1\"><item>x</item></root>")),
            TdsWire.Parameter("@varchar", TdsWire.VarChar(TdsWire.DefaultCollation, "caf\u00E9"u8)),
            TdsWire.Parameter("@varbinary", TdsWire.VarBinary([0x01, 0x02, 0x03])),
            TdsWire.Parameter("@varbinarymax", TdsWire.VarBinaryMax([0x10, 0x20, 0x30, 0x40, 0x50])),
            TdsWire.Parameter("@json", TdsWire.Json("""{"a":[1,2,{"b":null}],"c":"d"}""")),
            TdsWire.Parameter("@output", TdsWire.IntN(4, null), status: 1));
    }

    private static byte[] CreateNullsPayload()
    {
        return TdsWire.RpcPayload(
            12,
            TdsWire.Parameter("@int", TdsWire.IntN(4, null)),
            TdsWire.Parameter("@bit", TdsWire.BitN(null)),
            TdsWire.Parameter("@nvarchar", TdsWire.NVarChar(null)),
            TdsWire.Parameter("@varbinarymax", [0xA5, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
            TdsWire.Parameter("@json", [0xF4, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]),
            TdsWire.Parameter(string.Empty, [0x24, 16, 0]));
    }

    private static byte[] CreateVarCharPayload()
    {
        return TdsWire.RpcPayload(
            "p",
            TdsWire.Parameter("@latin1", TdsWire.VarChar(TdsWire.DefaultCollation, [0x63, 0x61, 0x66, 0xE9])),
            TdsWire.Parameter("@japanese", TdsWire.VarChar([0x11, 0x04, 0xD0, 0x00, 0x00], [0x82, 0xA0])),
            TdsWire.Parameter("@utf8", TdsWire.VarChar([0x09, 0x04, 0xD0, 0x04, 0x00], "\u00E9"u8)),
            TdsWire.Parameter("@sortid", TdsWire.VarChar([0x00, 0x00, 0x00, 0x00, 0xC0], [0x82, 0xA0])),
            TdsWire.Parameter("@unknown", TdsWire.VarChar([0x00, 0x10, 0x00, 0x00, 0x00], [0x41])));
    }

    // command.CommandText = """ ... """; in the query engine tests
    [GeneratedRegex("""CommandText\s*=\s*(?<!\$)\"{3}[ \t]*\r?\n(?<body>[\s\S]*?)\r?\n(?<indent>[ \t]*)\"{3}(?!\")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex CommandTextRegex();

    private sealed record Customer(int Id, string Name);

    private sealed record Order(int Id, string Region, int Amount);

    private sealed record NullableCustomer(int Id, string? Name, int? Score);

    private sealed record JsonDocumentRow(int Id, string? Payload);

    private sealed record XmlDocumentRow(int Id, string Payload);

    private sealed record EchoTypesRow(int ParameterCount, long Number, bool Flag, double Real, decimal Amount, Guid Id, DateTime Moment, DateOnly Day, TimeOnly Time, DateTimeOffset Offset, byte[]? Data, string? Text, DayOfWeek DayOfWeek);

    private enum QueryEngineAuthorization
    {
        AllowAll,
        DenyOrders,
        DenyAll,
        DenyStoredProcedures,
    }
}
