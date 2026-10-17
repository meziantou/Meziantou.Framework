using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
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
/// The request parser never throws, and the query engine returns an error result for anything it does not support.
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

    // Queries over these rows mirror the query engine tests
    private static readonly TdsQueryDelegate QueryEngineHandler = TdsQueryEngine.CreateQueryHandler(CreateQueryEngineOptions());

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("tds-session", Session) { Seeds = GetSessionSeeds, Dictionary = [.. MessageTokens, .. LoginTokens] },
        new("tds-messages", Messages) { Seeds = GetMessagesSeeds, Dictionary = MessageTokens },
        new("tds-request-parser", RequestParser) { Seeds = GetRequestParserSeeds, Dictionary = MessageTokens },
        new("tds-login", Login) { Seeds = GetLoginSeeds, Dictionary = LoginTokens },
        new("tds-prelogin", PreLogin) { Seeds = GetPreLoginSeeds, Dictionary = LoginTokens },
        new("tds-query-engine", QueryEngine) { Seeds = GetQueryEngineSeeds, Dictionary = SqlTokens },
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

        // The echo of the request only holds text and Variant columns, which accept any value
        _ = TdsResponseSerializer.CreateQueryResponse(Echo(context), PayloadSizePerPacket);
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
        _ = TdsResponseSerializer.CreateQueryResponse(result, PayloadSizePerPacket);
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

    /// <summary>A result that echoes the request in text and Variant columns, which serialize any value.</summary>
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

        if (parameter.Type is TdsColumnType.Json)
        {
            try
            {
                _ = parameter.AsJson();
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // Documented for a value that is not a JSON object
            }
        }

        if (parameter.Type is TdsColumnType.NVarChar)
        {
            try
            {
                _ = parameter.AsXml();
            }
            catch (XmlException)
            {
                // Documented for a value that is not well-formed XML
            }
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

    private static TdsQueryEngineOptions CreateQueryEngineOptions()
    {
        var options = new TdsQueryEngineOptions();
        options.AddQueryRoot("customers", _ => new[] { new Customer(1, "Alice"), new Customer(2, "Bob"), new Customer(4, "David") }.AsQueryable());
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
        var source = FuzzResources.ReadText(typeof(TdsServerFuzzTargets).Assembly, "TdsQueryEngineTests.cs");
        var index = 0;
        foreach (Match match in CommandTextRegex().Matches(source))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index++}"), GetRawStringValue(match));
        }

        yield return FuzzSeed.FromText("divide", "SELECT Id / 2 AS Half, Amount % 3 AS Remainder FROM orders WHERE Amount > 0");
        yield return FuzzSeed.FromText("cast", "SELECT CAST(Id AS nvarchar(10)) AS Text, CAST('12' AS int) AS Number FROM customers");
        yield return FuzzSeed.FromText("not-a-select", "UPDATE customers SET Name = 'x'");
        yield return FuzzSeed.FromText("invalid", "SELECT FROM WHERE");
    }

    /// <summary>Gets the value of a raw string literal: the lines between the quotes, without the indentation of the closing quotes.</summary>
    private static string GetRawStringValue(Match match)
    {
        var indentation = match.Groups["indent"].Value;
        var lines = match.Groups["body"].Value.ReplaceLineEndings("\n").Split('\n');
        return string.Join('\n', lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
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
}
