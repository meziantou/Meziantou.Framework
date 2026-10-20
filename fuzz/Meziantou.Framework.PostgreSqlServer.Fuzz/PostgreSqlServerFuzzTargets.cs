using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Framework.PostgreSql.Protocol;
using Microsoft.Extensions.Logging;
using BackendMessage = Meziantou.Framework.PostgreSql.Fuzz.PgWire.BackendMessage;

namespace Meziantou.Framework.PostgreSql.Fuzz;

/// <summary>
/// A PostgreSQL connection is fed in process, through <c>PostgreSqlConnectionProcessor</c> over memory streams: malformed
/// framing and a malformed handshake only throw <see cref="InvalidDataException"/> or <see cref="EndOfStreamException"/>,
/// which close the connection, and every other malformed message gets an ErrorResponse (08P01). The output is a sequence
/// of well-formed backend messages. The echo query handler cannot fail, so an error logged by the processor or an
/// internal error (XX000) is a failure.
/// </summary>
public static class PostgreSqlServerFuzzTargets
{
    private const string ServerPassword = "secret";
    private const string HandlerErrorCode = "P0001";
    private const int ScramIterationCount = 4096;

    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 50000);

    private static readonly PostgreSqlAuthenticationMethod[] Methods =
    [
        PostgreSqlAuthenticationMethod.ClearTextPassword,
        PostgreSqlAuthenticationMethod.Md5Password,
        PostgreSqlAuthenticationMethod.ScramSha256,
    ];

    // The frontend messages the selector byte of pgsql-messages picks; RawType reads the type from the next byte
    private const byte RawType = 0;
    private static readonly byte[] MessageTypes = [PgWire.Query, PgWire.Parse, PgWire.Bind, PgWire.Describe, PgWire.Execute, PgWire.Close, PgWire.Sync, PgWire.Flush, RawType, RawType];

    // text, int2, int4, int8, float4, float8, numeric, bool, uuid, date, timestamp, timestamptz, bytea, varchar, json, jsonb, unknown, time
    private static readonly uint[] TypeOids = [25, 21, 23, 20, 700, 701, 1700, 16, 2950, 1082, 1114, 1184, 17, 1043, 114, 3802, 0, 1083];

    // libFuzzer dictionaries hold bytes below 0x80 only (the tokens are written as UTF-8): the request codes of the startup
    // packet, the OIDs of numeric, timestamptz and uuid, and 0xFF cannot be tokens
    private static readonly string[] MessageTokens =
    [
        "SELECT ", "SELECT 1", "BEGIN", "COMMIT", "ERROR", "NOTICE", "DO", "FAIL", " $1", " $2", "\u0000\u0000", "\u0000\u0001", "\u0000\u0002", "\u0000\u0000\u0000\u0000", "\u0000\u0000\u0000\u0001", "\u0000\u0000\u0000\u0004",
        "\u0000\u0000\u0000\u0008", "\u0000\u0000\u0000\u0017", "\u0000\u0000\u0000\u0019", "\u0000\u0000\u0000\u0014", "\u0000\u0000\u0000\u0015", "\u0000\u0000\u0000\u0010", "\u0000\u0000\u0004:",
        "\u0000\u0000\u0004Z", "S\u0000", "P\u0000", "s1\u0000", "p1\u0000", "Q", "P", "B", "D", "E", "C", "S", "H", "X", "p", "F", "d", "c", "f",
    ];

    private static readonly string[] StartupTokens =
    [
        "\u0000\u0000\u0000\u0008", "\u0000\u0003\u0000\u0000", "\u0000\u0002\u0000\u0000", "user\u0000", "database\u0000",
        "application_name\u0000", "client_encoding\u0000", "UTF8\u0000", "options\u0000", "replication\u0000", "postgres\u0000", "\u0000\u0000",
    ];

    private static readonly string[] AuthenticationTokens =
    [
        "SCRAM-SHA-256\u0000", "SCRAM-SHA-256-PLUS\u0000", "n,,", "y,,", "p=tls-server-end-point,,", "n=", ",r=", "r=", "c=", "c=biws", ",p=", "p=", "s=", "i=4096", ",", "=", "%r", "%c", "%p", "%m", "md5", "secret", "\u0000",
    ];

    private static readonly string[] ValueTokens =
    [
        "t", "f", "true", "false", "yes", "no", "on", "off", "0", "1", "-1", "NaN", "Infinity", "-Infinity", "1e308", "1.5", "-0", "32767", "-32768", "2147483647", "9223372036854775807", "79228162514264337593543950335",
        "2024-02-29", "2024-01-01 12:34:56.123456", "+02:00", "Z", "T", "infinity", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "{\"a\":1}", "[1]", "\u0000\u0000\u0000\u0000", "@\u0000",
    ];

    private static readonly string[] ScramTokens = ["n,,", "y,,", "p=tls-server-end-point,,", "n=", "r=", "c=", "c=biws", "c=eSws", "p=", "s=", "i=", "m=", "a=", ",", ",,", "=", "==", " "];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("pgsql-session", Session) { Seeds = GetSessionSeeds, Dictionary = [.. MessageTokens, .. StartupTokens] },
        new("pgsql-messages", Messages) { Seeds = GetMessagesSeeds, Dictionary = MessageTokens },
        new("pgsql-startup", Startup) { Seeds = GetStartupSeeds, Dictionary = StartupTokens },
        new("pgsql-authentication", Authentication) { Seeds = GetAuthenticationSeeds, Dictionary = AuthenticationTokens },
        new("pgsql-parameter-values", ParameterValues) { Seeds = GetParameterValuesSeeds, Dictionary = ValueTokens },
        new("pgsql-scram-parser", ScramParser) { Seeds = GetScramParserSeeds, Dictionary = ScramTokens },
    ];

    /// <summary>
    /// The first byte selects the authentication method and the limits of the server, the rest is everything the client
    /// sends on the connection.
    /// </summary>
    private static void Session(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var options = new PostgreSqlServerOptions { AuthenticationMethod = FuzzInput.Pick(Methods, flags) };
        if ((flags & 0x04) != 0)
        {
            options.MaxPreparedStatementsPerConnection = 2;
            options.MaxPortalsPerConnection = 2;
        }

        if ((flags & 0x08) != 0)
        {
            options.MaxMessageSize = 64;
        }

        var input = data.ToArray();
        var logger = new RecordingLogger();
        using var inputStream = new MemoryStream(input, writable: false);
        using var output = new MemoryStream();
        try
        {
            RunConnection(options, logger, inputStream, output);
        }
        catch (InvalidDataException)
        {
            // Malformed framing or handshake: the server closes the connection
        }
        catch (EndOfStreamException)
        {
            // A packet shorter than its header says
        }

        CheckLogs(logger);
        var responses = output.ToArray().AsSpan();
        if (IsSslRequest(input))
        {
            // The server has no certificate, so it refuses TLS with a single byte, outside of any message
            FuzzAssert.True(responses is [(byte)'N', ..], "An SSLRequest must be answered with 'N'");
            responses = responses[1..];
        }

        var messages = PgWire.ReadBackendMessages(responses);
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.IsError)
            {
                FuzzAssert.True(message.ErrorCode is not "XX000", "The server reported an internal error");
                if (message.Severity is "FATAL")
                {
                    FuzzAssert.Equal(messages.Count - 1, i, "A FATAL error must end the connection");
                }
            }

            if (message is { Type: PgWire.Authentication } && message.AuthenticationCode is 0)
            {
                // The session starts with the server parameters, the key of the CancelRequest, then ReadyForQuery
                FuzzAssert.True(messages.Count >= i + 9, "The session must start after AuthenticationOk");
                for (var j = 1; j <= 6; j++)
                {
                    FuzzAssert.Equal(PgWire.ParameterStatus, messages[i + j].Type, "Expected a ParameterStatus after AuthenticationOk");
                }

                FuzzAssert.Equal(PgWire.BackendKeyData, messages[i + 7].Type, "Expected BackendKeyData after the server parameters");
                FuzzAssert.Equal(PgWire.ReadyForQuery, messages[i + 8].Type, "Expected ReadyForQuery after BackendKeyData");
            }
        }
    }

    /// <summary>
    /// The harness logs in, then the input is a sequence of messages the harness frames correctly: a byte that selects the
    /// type (or reads it from the next byte), two bytes for the length, then the payload. So mutations reach the parsers of
    /// the messages instead of breaking the framing. The first byte sets small limits of prepared statements and portals.
    /// The responses must follow the protocol: one response per message, ErrorResponse (08P01) for a malformed message,
    /// then every message is ignored until Sync, which gets ReadyForQuery.
    /// </summary>
    private static void Messages(ReadOnlySpan<byte> data)
    {
        var options = new PostgreSqlServerOptions { AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword };
        if ((FuzzInput.ReadByte(ref data) & 0x01) != 0)
        {
            options.MaxPreparedStatementsPerConnection = 2;
            options.MaxPortalsPerConnection = 2;
        }

        var input = new List<byte>();
        input.AddRange(PgWire.Startup(("user", "fuzz"), ("database", "postgres")));
        input.AddRange(PgWire.Message(PgWire.PasswordMessage, PgWire.CString(ServerPassword)));
        var sent = new List<byte>();
        while (!data.IsEmpty)
        {
            var type = FuzzInput.Pick(MessageTypes, FuzzInput.ReadByte(ref data));
            if (type is RawType)
            {
                type = FuzzInput.ReadByte(ref data);
            }

            var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
            var payload = data[..Math.Min(length, data.Length)];
            data = data[payload.Length..];
            input.AddRange(PgWire.Message(type, payload));
            sent.Add(type);
            if (type is PgWire.Terminate)
                break;
        }

        var logger = new RecordingLogger();
        using var inputStream = new MemoryStream([.. input], writable: false);
        using var output = new MemoryStream();

        // The framing is valid and every message fits in MaxMessageSize, so nothing may escape
        RunConnection(options, logger, inputStream, output);
        CheckLogs(logger);

        var messages = PgWire.ReadBackendMessages(output.ToArray());
        var position = CheckLogin(messages, PostgreSqlAuthenticationMethod.ClearTextPassword);
        BackendMessage Next(byte request)
        {
            FuzzAssert.True(position < messages.Count, $"Missing response to the message '{(char)request}'");
            return messages[position++];
        }

        void ExpectProtocolError(BackendMessage response, byte request)
        {
            FuzzAssert.True(response.IsError, $"Unexpected response {response} to the message '{(char)request}'");
            FuzzAssert.Equal(PgWire.ProtocolViolation, response.ErrorCode, $"Unexpected error code for the message '{(char)request}'");
            FuzzAssert.Equal("ERROR", response.Severity, "A protocol error must not end the connection");
        }

        var skipping = false;
        foreach (var type in sent)
        {
            if (type is PgWire.Terminate)
                break;

            // After an error, the server ignores every message until Sync
            if (skipping && type is not PgWire.Sync)
                continue;

            BackendMessage response;
            switch (type)
            {
                case PgWire.Query:
                    // A simple query always ends with ReadyForQuery and never enters the error state
                    do
                    {
                        response = Next(type);
                        FuzzAssert.True(response.Type is PgWire.EmptyQueryResponse or PgWire.NoticeResponse or PgWire.RowDescription or PgWire.DataRow or PgWire.CommandComplete or PgWire.ReadyForQuery or PgWire.ErrorResponse, $"Unexpected response {response} to a Query");
                        if (response.IsError)
                        {
                            FuzzAssert.Equal(HandlerErrorCode, response.ErrorCode, "A Query can only fail in the handler");
                        }
                    }
                    while (response.Type is not PgWire.ReadyForQuery);
                    break;

                case PgWire.Parse or PgWire.Bind or PgWire.Close:
                    response = Next(type);
                    var expected = type switch
                    {
                        PgWire.Parse => PgWire.ParseComplete,
                        PgWire.Bind => PgWire.BindComplete,
                        _ => PgWire.CloseComplete,
                    };

                    if (response.Type != expected)
                    {
                        ExpectProtocolError(response, type);
                        skipping = true;
                    }

                    break;

                case PgWire.Describe:
                    response = Next(type);
                    if (response.IsError)
                    {
                        ExpectProtocolError(response, type);
                        skipping = true;
                        break;
                    }

                    if (response.Type is PgWire.ParameterDescription)
                    {
                        response = Next(type);
                    }

                    FuzzAssert.True(response.Type is PgWire.RowDescription or PgWire.NoData, $"Unexpected response {response} to a Describe");
                    break;

                case PgWire.Execute:
                    response = Next(type);
                    if (response.IsError)
                    {
                        FuzzAssert.True(response.ErrorCode is PgWire.ProtocolViolation or HandlerErrorCode, $"Unexpected error {FuzzAssert.Format(response.ErrorCode)} for an Execute");
                        skipping = true;
                        break;
                    }

                    if (response.Type is PgWire.EmptyQueryResponse)
                        break;

                    while (response.Type is PgWire.NoticeResponse or PgWire.DataRow)
                    {
                        response = Next(type);
                    }

                    FuzzAssert.True(response.Type is PgWire.CommandComplete or PgWire.PortalSuspended, $"Unexpected response {response} to an Execute");
                    break;

                case PgWire.Sync:
                    FuzzAssert.Equal(PgWire.ReadyForQuery, Next(type).Type, "A Sync must get ReadyForQuery");
                    skipping = false;
                    break;

                case PgWire.Flush:
                    break;

                default:
                    ExpectProtocolError(Next(type), type);
                    skipping = true;
                    break;
            }
        }

        FuzzAssert.Equal(messages.Count, position, "The server sent responses that no message asked for");
    }

    /// <summary>
    /// The first byte selects the authentication method, the rest is the start of the connection. The startup packet
    /// reader is checked on its own, then the processor answers the packet: 'N' to an SSLRequest, nothing to a
    /// CancelRequest, an authentication request to a valid StartupMessage, and a FATAL error to an unknown request code.
    /// </summary>
    private static void Startup(ReadOnlySpan<byte> data)
    {
        var method = FuzzInput.Pick(Methods, FuzzInput.ReadByte(ref data));
        var input = data.ToArray();
        var declaredLength = input.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(input) : 0;
        PostgreSqlStartupPacket? packet = null;
        Exception? readError = null;
        using (var stream = new MemoryStream(input, writable: false))
        {
            try
            {
                packet = PostgreSqlMessageReader.ReadStartupPacketAsync(stream, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            catch (InvalidDataException ex)
            {
                FuzzAssert.True(input.Length is > 0 and < 4 || declaredLength < 8 || declaredLength > PostgreSqlServerOptions.MaxStartupPacketSize, "A complete startup packet with a valid length was rejected");
                readError = ex;
            }
            catch (EndOfStreamException ex)
            {
                FuzzAssert.True(input.Length < declaredLength, "A complete startup packet was reported as truncated");
                readError = ex;
            }

            if (readError is null)
            {
                if (packet is null)
                {
                    FuzzAssert.Equal(0, input.Length, "Only an empty connection has no startup packet");
                    return;
                }

                FuzzAssert.True(declaredLength is >= 8 and <= PostgreSqlServerOptions.MaxStartupPacketSize, $"The startup packet length {declaredLength} must be rejected");
                FuzzAssert.Equal((long)declaredLength, stream.Position, "The reader must consume the whole startup packet and nothing else");
                FuzzAssert.Equal(BinaryPrimitives.ReadInt32BigEndian(input.AsSpan(4)), packet.RequestCode, "Invalid request code");
                FuzzAssert.True(packet.Payload.AsSpan().SequenceEqual(input.AsSpan(8, declaredLength - 8)), "The payload must be the body after the request code");
            }
        }

        Dictionary<string, string>? parameters = null;
        if (packet is { RequestCode: PgWire.ProtocolVersion3 })
        {
            try
            {
                parameters = PostgreSqlMessageReader.ParseStartupParameters(packet.Payload);
            }
            catch (InvalidDataException)
            {
                // A key without a value, or a string without its terminator
            }

            if (parameters is not null)
            {
                foreach (var (key, value) in parameters)
                {
                    FuzzAssert.True(key.Length > 0 && !key.Contains('\0', StringComparison.Ordinal) && !value.Contains('\0', StringComparison.Ordinal), $"Invalid startup parameter {FuzzAssert.Format(key)}");
                }

                // Writing the parameters back gives the same parameters
                var roundTrip = PostgreSqlMessageReader.ParseStartupParameters(PgWire.StartupParameters(parameters.Select(item => (item.Key, item.Value))));
                FuzzAssert.True(roundTrip.Count == parameters.Count && roundTrip.All(item => parameters.TryGetValue(item.Key, out var value) && value == item.Value), "The startup parameters do not round trip");
            }
        }

        // The processor reads the first packet only, then the connection ends
        var options = new PostgreSqlServerOptions { AuthenticationMethod = method };
        var logger = new RecordingLogger();
        using var firstPacket = new MemoryStream(input, 0, packet is null ? input.Length : declaredLength, writable: false);
        using var output = new MemoryStream();
        Exception? processError = null;
        try
        {
            RunConnection(options, logger, firstPacket, output);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            processError = ex;
        }

        CheckLogs(logger);
        var response = output.ToArray();
        if (packet is null)
        {
            FuzzAssert.Equal(readError?.GetType(), processError?.GetType(), "The processor must reject the packet the reader rejects");
            FuzzAssert.Equal(0, response.Length, "Nothing is answered to an invalid startup packet");
            return;
        }

        switch (packet.RequestCode)
        {
            case PgWire.SslRequestCode:
                FuzzAssert.True(processError is null && response is [(byte)'N'], "An SSLRequest must be refused with 'N'");
                break;

            case PgWire.CancelRequestCode:
                FuzzAssert.True(processError is null && response.Length == 0, "A CancelRequest gets no response");
                break;

            case PgWire.ProtocolVersion3 when parameters is null:
                FuzzAssert.True(processError is InvalidDataException && response.Length == 0, "Invalid startup parameters must close the connection");
                break;

            case PgWire.ProtocolVersion3:
                // The connection ends instead of sending the password
                FuzzAssert.True(processError is InvalidDataException, "The missing password message must close the connection");
                var messages = PgWire.ReadBackendMessages(response);
                FuzzAssert.True(messages is [{ Type: PgWire.Authentication }] && messages[0].AuthenticationCode == GetAuthenticationRequestCode(method), "A StartupMessage must get an authentication request");
                break;

            default:
                FuzzAssert.True(processError is null, "An unknown request code must get an error response");
                var error = PgWire.ReadBackendMessages(response);
                FuzzAssert.True(error is [{ IsError: true }] && error[0].ErrorCode == PgWire.ProtocolViolation && error[0].Severity == "FATAL", "An unknown request code must get a FATAL protocol violation");
                break;
        }
    }

    /// <summary>
    /// A client logs in with the method the first byte selects. The second byte selects the mode; the rest are fields
    /// separated by 0xFF: the password of the server, the password of the client (unless the mode reuses the password of
    /// the server), the user name, then two fields (in raw mode, the first one is prefixed by its 16-bit length). When the client computes the responses (a cleartext password, the MD5
    /// response from the salt, a SCRAM-SHA-256 exchange from the server nonce and salt), the login succeeds exactly when
    /// the passwords match, and the SCRAM server signature is the one the client expects. In raw mode the fields are the
    /// payloads of the password messages, in which the client substitutes the values it computes (<c>%m</c>, <c>%r</c>,
    /// <c>%c</c>, <c>%p</c>); a login can then only succeed with the password of the server.
    /// </summary>
    private static void Authentication(ReadOnlySpan<byte> data)
    {
        var method = FuzzInput.Pick(Methods, FuzzInput.ReadByte(ref data));
        var mode = FuzzInput.ReadByte(ref data);
        var raw = (mode & 0x01) != 0;
        var sendUserName = (mode & 0x04) == 0;
        var wrongMessageType = raw && (mode & 0x08) != 0;
        var alternative = (mode & 0x10) != 0;
        var serverPassword = ReadField(ref data);
        var clientPassword = (mode & 0x02) != 0 ? serverPassword : ReadField(ref data);
        var userName = ReadField(ref data).Replace("\0", "", StringComparison.Ordinal);
        if (userName.Length > 1000)
        {
            userName = userName[..1000];
        }

        // The raw payloads can hold any byte, so the first one is prefixed by its length instead of ending with 0xFF
        var field1 = raw ? ReadLengthPrefixed(ref data) : FuzzInput.ReadUntil(ref data, 0xFF).ToArray();
        var field2 = data.ToArray();

        var options = new PostgreSqlServerOptions { AuthenticationMethod = method };
        var logger = new RecordingLogger();
        using var output = new MemoryStream();
        var md5Salt = (byte[]?)null;
        var gs2Header = alternative ? "y,," : "n,,";
        var clientFirstBare = "";
        var serverFirst = (string?)null;
        var expectedServerSignature = (byte[]?)null;
        var passwordMessageType = wrongMessageType ? PgWire.Query : PgWire.PasswordMessage;

        // What the server decodes from the password message, for the cleartext and MD5 methods
        var sentPassword = (string?)null;

        byte[]? Script(int step, byte[] serverOutput)
        {
            switch (step)
            {
                case 0:
                    var parameters = new List<(string, string)>();
                    if (sendUserName)
                    {
                        parameters.Add(("user", userName));
                    }

                    parameters.Add(("database", "postgres"));
                    return PgWire.Startup([.. parameters]);

                case 1 when method is PostgreSqlAuthenticationMethod.ClearTextPassword:
                    var clearText = raw ? field1 : PgWire.CString(clientPassword);
                    sentPassword = DecodePasswordMessage(clearText);
                    return PgWire.Message(passwordMessageType, clearText);

                case 1 when method is PostgreSqlAuthenticationMethod.Md5Password:
                    md5Salt = GetAuthenticationRequest(serverOutput, 5).AsSpan(4, 4).ToArray();
                    var md5Response = ComputeMd5Response(clientPassword, userName, md5Salt);
                    if (alternative)
                    {
                        md5Response = md5Response.ToUpperInvariant();
                    }

                    var md5Payload = raw ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(field1).Replace("%m", md5Response, StringComparison.Ordinal)) : PgWire.CString(md5Response);
                    sentPassword = DecodePasswordMessage(md5Payload);
                    return PgWire.Message(passwordMessageType, md5Payload);

                case 1 when method is PostgreSqlAuthenticationMethod.ScramSha256:
                    if (raw)
                    {
                        clientFirstBare = GetClientFirstBare(field1);
                        return PgWire.Message(passwordMessageType, field1);
                    }

                    var nonce = Encoding.UTF8.GetString(field1).Replace(",", "", StringComparison.Ordinal);
                    if (string.IsNullOrWhiteSpace(nonce))
                    {
                        nonce = "fuzz";
                    }

                    clientFirstBare = "n=" + userName + ",r=" + nonce;
                    if (field2.Length > 0)
                    {
                        // Extensions the server must ignore
                        clientFirstBare += "," + Encoding.UTF8.GetString(field2);
                    }

                    return PgWire.Message(PgWire.PasswordMessage, PgWire.SaslInitialResponsePayload("SCRAM-SHA-256", Encoding.UTF8.GetBytes(gs2Header + clientFirstBare)));

                case 2 when method is PostgreSqlAuthenticationMethod.ScramSha256:
                    serverFirst = Encoding.UTF8.GetString(GetAuthenticationRequest(serverOutput, 11).AsSpan(4));
                    var serverAttributes = serverFirst.Split(',').Select(item => item.Split('=', 2)).ToDictionary(item => item[0], item => item[1], StringComparer.Ordinal);
                    FuzzAssert.True(serverAttributes.Count == 3 && serverAttributes["i"] == "4096", $"Invalid SCRAM server-first message {FuzzAssert.Format(serverFirst)}");
                    var salt = Convert.FromBase64String(serverAttributes["s"]);
                    FuzzAssert.Equal(16, salt.Length, "The SCRAM salt must have 16 bytes");
                    var fullNonce = serverAttributes["r"];
                    string clientFinal;
                    if (raw)
                    {
                        clientFinal = Encoding.UTF8.GetString(field2)
                            .Replace("%r", fullNonce, StringComparison.Ordinal)
                            .Replace("%c", Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,")), StringComparison.Ordinal);
                        var proofStart = clientFinal.IndexOf(",p=", StringComparison.Ordinal);
                        var authMessage = clientFirstBare + "," + serverFirst + "," + (proofStart < 0 ? clientFinal : clientFinal[..proofStart]);
                        clientFinal = clientFinal.Replace("%p", Convert.ToBase64String(ComputeScram(clientPassword, salt, authMessage).Proof), StringComparison.Ordinal);
                    }
                    else
                    {
                        var withoutProof = "c=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(gs2Header)) + ",r=" + fullNonce;
                        var (proof, serverSignature) = ComputeScram(clientPassword, salt, clientFirstBare + "," + serverFirst + "," + withoutProof);
                        expectedServerSignature = serverSignature;
                        clientFinal = withoutProof + ",p=" + Convert.ToBase64String(proof);
                    }

                    return PgWire.Message(passwordMessageType, Encoding.UTF8.GetBytes(clientFinal));

                default:
                    return null;
            }
        }

        Exception? error = null;
        try
        {
            var processor = new PostgreSqlConnectionProcessor(options, (context, cancellationToken) => AuthenticateAsync(context, method, sendUserName ? userName : null, serverPassword), EchoAsync, logger);
            using var input = new ScriptedClientStream(output, Script);
            RunConnection(processor, input, output);
        }
        catch (InvalidDataException ex) when (raw)
        {
            // The client sent a malformed SCRAM message or an unexpected message type
            error = ex;
        }

        CheckLogs(logger);
        var messages = PgWire.ReadBackendMessages(output.ToArray());
        FuzzAssert.True(messages.Count > 0 && messages[0].Type is PgWire.Authentication && messages[0].AuthenticationCode == GetAuthenticationRequestCode(method), "The server must start with an authentication request");
        var authenticated = messages.Any(message => message is { Type: PgWire.Authentication } && message.AuthenticationCode is 0);
        if (error is not null)
        {
            FuzzAssert.False(authenticated || messages.Any(message => message.IsError), "A connection closed for a malformed message must not answer it");
            return;
        }

        if (authenticated)
        {
            var position = CheckLogin(messages, method);
            FuzzAssert.Equal(messages.Count, position, "Unexpected messages after the login");
        }
        else
        {
            var last = messages[^1];
            FuzzAssert.True(last.IsError && last.Severity == "FATAL" && last.ErrorCode == "28P01", $"A failed login must end with a FATAL 28P01 error, not {last}");
        }

        if (wrongMessageType)
        {
            FuzzAssert.Fail("A message other than a password message must close the connection");
        }

        // PBKDF2 uses the password as an HMAC key, so SCRAM cannot tell apart the passwords that are the same key
        var passwordsMatch = method is PostgreSqlAuthenticationMethod.ScramSha256 ? IsSameHmacKey(clientPassword, serverPassword) : string.Equals(clientPassword, serverPassword, StringComparison.Ordinal);
        switch (method)
        {
            case PostgreSqlAuthenticationMethod.ClearTextPassword:
                FuzzAssert.Equal(string.Equals(sentPassword, serverPassword, StringComparison.Ordinal), authenticated, $"The login with the password {FuzzAssert.Format(sentPassword)} must succeed exactly when it is the password of the server");
                break;

            case PostgreSqlAuthenticationMethod.Md5Password:
                var expectedResponse = ComputeMd5Response(serverPassword, userName, md5Salt!);
                var md5Valid = sendUserName && userName.Length > 0 && string.Equals(sentPassword, expectedResponse, StringComparison.OrdinalIgnoreCase);
                FuzzAssert.Equal(md5Valid, authenticated, $"The MD5 response {FuzzAssert.Format(sentPassword)} must be accepted exactly when it is valid");
                break;

            case PostgreSqlAuthenticationMethod.ScramSha256:
                if (raw)
                {
                    FuzzAssert.True(!authenticated || passwordsMatch, "A SCRAM login succeeded without the key of the password of the server");
                }
                else
                {
                    FuzzAssert.Equal(passwordsMatch, authenticated, "A SCRAM login must succeed exactly when the passwords are the same HMAC key");
                }

                if (authenticated)
                {
                    var serverFinal = messages.Single(message => message is { Type: PgWire.Authentication } && message.AuthenticationCode is 12);
                    var signature = Encoding.UTF8.GetString(serverFinal.Payload.AsSpan(4));
                    if (expectedServerSignature is not null)
                    {
                        FuzzAssert.Equal("v=" + Convert.ToBase64String(expectedServerSignature), signature, "The server signature must prove that the server knows the password");
                    }
                }

                break;
        }
    }

    /// <summary>
    /// The first byte selects the type OID, the second the format (bit 0: binary, bit 7: NULL), the rest is the value of
    /// a bound parameter. A value is decoded to its CLR type or kept as text or bytes when it does not convert; encoding a
    /// decoded value gives it back: the same bytes for the fixed-size binary types, the same value through the text format.
    /// The accessors of the parameter never throw.
    /// </summary>
    private static void ParameterValues(ReadOnlySpan<byte> data)
    {
        var typeOid = FuzzInput.Pick(TypeOids, FuzzInput.ReadByte(ref data));
        var format = FuzzInput.ReadByte(ref data);
        var formatCode = format & 0x01;
        var rawValue = (format & 0x80) != 0 ? null : data.ToArray();
        var value = PostgreSqlValueConverter.DecodeParameterValue(typeOid, formatCode, rawValue);
        CheckParameter(new PostgreSqlQueryParameter
        {
            Name = "$1",
            Type = PostgreSqlTypeMapper.GetColumnType(typeOid),
            Value = value ?? DBNull.Value,
            TypeOid = typeOid,
            FormatCode = formatCode,
            RawValue = rawValue is null ? (ReadOnlyMemory<byte>?)null : rawValue,
        });
    }

    /// <summary>The input is a SCRAM client-first or client-final message, decoded as the server decodes it.</summary>
    private static void ScramParser(ReadOnlySpan<byte> data)
    {
        var message = Encoding.UTF8.GetString(data);
        var attributes = PostgreSqlScramHelper.ParseAttributes(message);
        foreach (var (key, value) in attributes)
        {
            FuzzAssert.True(key.Length > 0 && value.Length > 0 && !key.Contains(',', StringComparison.Ordinal) && !value.Contains(',', StringComparison.Ordinal), $"Invalid attribute {FuzzAssert.Format(key)}={FuzzAssert.Format(value)}");
            FuzzAssert.True(message.Contains(key + "=" + value, StringComparison.Ordinal), $"The attribute {FuzzAssert.Format(key)} is not in the message");
        }

        if (PostgreSqlScramHelper.TryParseClientFirstMessage(message, out var clientFirstBare, out var clientNonce, out var gs2Header))
        {
            FuzzAssert.Equal(message, gs2Header + clientFirstBare, "The client-first message must be the gs2 header followed by the bare message");
            FuzzAssert.True(gs2Header[0] is 'n' or 'y' && gs2Header.EndsWith(",,", StringComparison.Ordinal) && gs2Header.IndexOf(",,", StringComparison.Ordinal) == gs2Header.Length - 2, $"Invalid gs2 header {FuzzAssert.Format(gs2Header)}");
            FuzzAssert.True(!string.IsNullOrWhiteSpace(clientNonce) && PostgreSqlScramHelper.ParseAttributes(clientFirstBare).TryGetValue("r", out var nonce) && nonce == clientNonce, "The client nonce must be the r attribute of the bare message");

            // The channel binding of the client-final message repeats the gs2 header
            FuzzAssert.True(PostgreSqlScramHelper.IsExpectedChannelBinding(Convert.ToBase64String(Encoding.UTF8.GetBytes(gs2Header)), gs2Header), "The channel binding of the gs2 header must be accepted");
            FuzzAssert.False(PostgreSqlScramHelper.IsExpectedChannelBinding(null, gs2Header), "A missing channel binding must be rejected");

            var serverFirst = PostgreSqlScramHelper.BuildServerFirstMessage(clientNonce + "server", [1, 2, 3], ScramIterationCount);
            var serverAttributes = PostgreSqlScramHelper.ParseAttributes(serverFirst);
            FuzzAssert.True(serverAttributes.TryGetValue("i", out var iterations) && iterations == "4096" && serverAttributes.TryGetValue("s", out var salt) && salt == "AQID", $"Invalid server-first message {FuzzAssert.Format(serverFirst)}");
        }

        if (PostgreSqlScramHelper.TryParseClientFinalMessage(message, out var withoutProof, out var proof, out var finalNonce, out var channelBinding))
        {
            FuzzAssert.True(message.StartsWith(withoutProof + ",p=", StringComparison.Ordinal) && !withoutProof.Contains(",p=", StringComparison.Ordinal), "The message without the proof must end before the first proof attribute");
            FuzzAssert.True(attributes.TryGetValue("p", out var proofText) && Convert.FromBase64String(proofText).AsSpan().SequenceEqual(proof), "The proof must be the decoded p attribute");
            FuzzAssert.True(attributes.TryGetValue("r", out var nonce) && nonce == finalNonce && !string.IsNullOrWhiteSpace(finalNonce), "The nonce must be the r attribute");
            FuzzAssert.Equal(attributes.GetValueOrDefault("c"), channelBinding, "The channel binding must be the c attribute");
        }
    }

    private static void RunConnection(PostgreSqlServerOptions options, RecordingLogger logger, Stream input, MemoryStream output)
    {
        RunConnection(new PostgreSqlConnectionProcessor(options, AuthenticateAsync, EchoAsync, logger), input, output);
    }

    private static void RunConnection(PostgreSqlConnectionProcessor processor, Stream input, MemoryStream output)
    {
        // The handlers complete synchronously and the streams are in memory, so the connection is over once ProcessAsync returns
        RunConnectionAsync(processor, input, output).GetAwaiter().GetResult();
    }

    private static async Task RunConnectionAsync(PostgreSqlConnectionProcessor processor, Stream input, MemoryStream output)
    {
        await processor.ProcessAsync(input, output, RemoteEndPoint, CancellationToken.None).ConfigureAwait(false);
    }

    private static ValueTask<PostgreSqlAuthenticationResult> AuthenticateAsync(PostgreSqlAuthenticationContext context, CancellationToken cancellationToken)
    {
        // SCRAM requires ValidatePassword to compute the server signature. The other methods accept any password but "deny..."
        var valid = context.ValidatePassword(ServerPassword);
        var accepted = context.Method switch
        {
            PostgreSqlAuthenticationMethod.ScramSha256 => valid,
            PostgreSqlAuthenticationMethod.ClearTextPassword => context.Password?.StartsWith("deny", StringComparison.Ordinal) is not true,
            _ => true,
        };

        return ValueTask.FromResult(accepted ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("password authentication failed"));
    }

    private static ValueTask<PostgreSqlAuthenticationResult> AuthenticateAsync(PostgreSqlAuthenticationContext context, PostgreSqlAuthenticationMethod method, string? userName, string password)
    {
        FuzzAssert.Equal(method, context.Method, "The context must carry the authentication method");
        FuzzAssert.Equal(userName, context.UserName, "The context must carry the user of the startup packet");
        FuzzAssert.Equal("postgres", context.Database, "The context must carry the database of the startup packet");
        return ValueTask.FromResult(context.ValidatePassword(password) ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("password authentication failed"));
    }

    /// <summary>
    /// Echoes the request in a result set whose shape only depends on the command text, so that Describe and Execute
    /// agree. The text selects the other results: an error, a notice, a transaction status, or no result set.
    /// </summary>
    private static ValueTask<PostgreSqlQueryResult> EchoAsync(PostgreSqlQueryContext context, CancellationToken cancellationToken)
    {
        for (var i = 0; i < context.Parameters.Count; i++)
        {
            FuzzAssert.Equal("$" + (i + 1).ToString(CultureInfo.InvariantCulture), context.Parameters[i].Name, "Invalid parameter name");
            CheckParameter(context.Parameters[i]);
        }

        var text = context.CommandText ?? "";
        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError { Code = HandlerErrorCode, Message = "echo error" }));

        var result = new PostgreSqlQueryResult
        {
            TransactionStatus = text.StartsWith("BEGIN", StringComparison.Ordinal) ? PostgreSqlTransactionStatus.InTransaction
                : text.StartsWith("FAIL", StringComparison.Ordinal) ? PostgreSqlTransactionStatus.Failed
                : PostgreSqlTransactionStatus.Idle,
        };

        if (text.StartsWith("NOTICE", StringComparison.Ordinal))
        {
            result.Notices.Add("echo notice");
        }

        if (text.StartsWith("DO", StringComparison.Ordinal))
        {
            result.CommandTag = "DO";
            return ValueTask.FromResult(result);
        }

        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("kind", PostgreSqlColumnType.Text));
        resultSet.Columns.Add(new PostgreSqlColumn("name", PostgreSqlColumnType.Text));
        resultSet.Columns.Add(new PostgreSqlColumn("type_oid", PostgreSqlColumnType.Int64));
        resultSet.Columns.Add(new PostgreSqlColumn("format", PostgreSqlColumnType.Int16));
        resultSet.Columns.Add(new PostgreSqlColumn("is_null", PostgreSqlColumnType.Boolean));
        resultSet.Columns.Add(new PostgreSqlColumn("value", PostgreSqlColumnType.Text));
        resultSet.Columns.Add(new PostgreSqlColumn("raw", PostgreSqlColumnType.Bytea));
        resultSet.Rows.Add(["command", context.RequestType.ToString(), null, null, false, text, null]);
        foreach (var parameter in context.Parameters)
        {
            resultSet.Rows.Add(["parameter", parameter.Name, (long)parameter.TypeOid, (short)parameter.FormatCode, parameter.IsNull, parameter.AsString(), parameter.RawValue?.ToArray()]);
        }

        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    }

    /// <summary>A decoded parameter is consistent with its raw value, and its accessors return <see langword="null"/> instead of throwing.</summary>
    private static void CheckParameter(PostgreSqlQueryParameter parameter)
    {
        FuzzAssert.True(parameter.Value is not null, "A parameter must have a value (DBNull for NULL)");
        FuzzAssert.Equal(parameter.Value is DBNull, parameter.IsNull, "IsNull must match a DBNull value");
        FuzzAssert.Equal(parameter.IsNull, parameter.RawValue is null, "Only a NULL parameter has no raw value");
        FuzzAssert.Equal(PostgreSqlTypeMapper.GetColumnType(parameter.TypeOid), parameter.Type, "The type must be the type of the OID");
        FuzzAssert.True(parameter.FormatCode is 0 or 1, $"Invalid format code {parameter.FormatCode}");
        _ = parameter.AsString();
        _ = parameter.AsInt32();
        _ = parameter.AsInt64();
        _ = parameter.AsBoolean();
        _ = parameter.AsDouble();
        _ = parameter.AsDecimal();
        _ = parameter.AsGuid();
        _ = parameter.AsDateTime();
        _ = parameter.AsDateTimeOffset();
        _ = parameter.AsBinary();
        try
        {
            _ = parameter.AsJson();
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // Documented for a value that is not a JSON object
        }

        if (parameter.IsNull)
        {
            FuzzAssert.True(parameter.AsString() is null && parameter.AsInt64() is null && parameter.AsBinary() is null, "A NULL parameter must convert to null");
            return;
        }

        CheckDecodedValue(parameter.TypeOid, parameter.FormatCode, parameter.RawValue!.Value.ToArray(), parameter.Value);
    }

    private static void CheckDecodedValue(uint typeOid, int formatCode, byte[] rawValue, object value)
    {
        var columnType = PostgreSqlTypeMapper.GetColumnType(typeOid);
        if (formatCode == 0)
        {
            if (value is string text)
            {
                FuzzAssert.Equal(Encoding.UTF8.GetString(rawValue), text, "A text value that does not convert must be kept as is");
                return;
            }

            FuzzAssert.Equal(GetExpectedType(typeOid), value.GetType(), $"Unexpected type of a text value of the OID {typeOid}");
            CheckTextRoundTrip(typeOid, columnType, value);
            return;
        }

        switch (value)
        {
            case byte[] bytes:
                FuzzAssert.True(bytes.AsSpan().SequenceEqual(rawValue), "A binary value that does not convert must be kept as is");
                return;

            case string text:
                FuzzAssert.True(typeOid is 25 or 1043 or 114 or 3802, $"Unexpected text value for the OID {typeOid}");
                FuzzAssert.Equal(Encoding.UTF8.GetString(rawValue), text, "A binary text value must be decoded as UTF-8");
                return;

            case bool boolean:
                FuzzAssert.True(typeOid is 16 && rawValue.Length > 0 && boolean == (rawValue[0] != 0), "Invalid binary boolean");
                FuzzAssert.True(PostgreSqlValueConverter.EncodeResultValue(columnType, value, 1).AsSpan().SequenceEqual([boolean ? (byte)1 : (byte)0]), "A boolean must be encoded as one byte");
                break;

            case decimal:
                FuzzAssert.Equal(1700u, typeOid, "Only numeric values decode to decimal");
                FuzzAssert.Equal(value, PostgreSqlValueConverter.DecodeParameterValue(typeOid, 1, PostgreSqlValueConverter.EncodeResultValue(columnType, value, 1)), "A numeric value must round trip through the binary format");
                break;

            default:
                FuzzAssert.Equal(GetExpectedType(typeOid), value.GetType(), $"Unexpected type of a binary value of the OID {typeOid}");

                // The fixed-size binary types are encoded back to the same bytes
                var encoded = PostgreSqlValueConverter.EncodeResultValue(columnType, value, 1);
                FuzzAssert.True(encoded.AsSpan().SequenceEqual(rawValue), $"The binary value of the OID {typeOid} does not round trip: {Convert.ToHexString(rawValue)} -> {FuzzAssert.Format(value)} -> {Convert.ToHexString(encoded)}");
                break;
        }

        CheckTextRoundTrip(typeOid, columnType, value);
    }

    /// <summary>A decoded value is encoded as text and decoded back to the same value (the text timestamps have a precision of 1 microsecond).</summary>
    private static void CheckTextRoundTrip(uint typeOid, PostgreSqlColumnType columnType, object value)
    {
        // .NET 10 formats some powers of two (0x0410000000000000) with a shortest round-trippable string that parses to
        // the previous double; .NET 11 fixed it. The library is not at fault when the runtime cannot round trip the value.
        if (value is double number && double.Parse(number.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) != number && !double.IsNaN(number))
            return;

        if (value is float single && float.Parse(single.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) != single && !float.IsNaN(single))
            return;

        var text = PostgreSqlValueConverter.EncodeResultValue(columnType, value, 0);
        var decoded = PostgreSqlValueConverter.DecodeParameterValue(typeOid, 0, text);
        var expected = value switch
        {
            DateTime dateTime => new DateTime(dateTime.Ticks - (dateTime.Ticks % 10), dateTime.Kind),
            DateTimeOffset dateTimeOffset => new DateTimeOffset(dateTimeOffset.Ticks - (dateTimeOffset.Ticks % 10), dateTimeOffset.Offset),
            _ => value,
        };

        FuzzAssert.Equal(expected, decoded, $"The value of the OID {typeOid} does not round trip through its text {FuzzAssert.Format(Encoding.UTF8.GetString(text))}");
    }

    private static Type GetExpectedType(uint typeOid) => typeOid switch
    {
        16 => typeof(bool),
        21 => typeof(short),
        23 => typeof(int),
        20 => typeof(long),
        700 => typeof(float),
        701 => typeof(double),
        1700 => typeof(decimal),
        2950 => typeof(Guid),
        1082 => typeof(DateOnly),
        1114 => typeof(DateTime),
        1184 => typeof(DateTimeOffset),
        _ => typeof(string),
    };

    /// <summary>The processor logs the exceptions it turns into responses; the only expected ones are the protocol errors (InvalidDataException), at the Debug level.</summary>
    private static void CheckLogs(RecordingLogger logger)
    {
        foreach (var entry in logger.Entries)
        {
            if (entry.Level >= LogLevel.Warning)
                throw new FuzzAssertionException($"The connection processor logged an error: {entry.Message}", entry.Exception ?? new InvalidOperationException(entry.Message));

            if (entry.Exception is not null and not InvalidDataException)
                throw new FuzzAssertionException($"Unexpected exception in the connection processor: {entry.Message}", entry.Exception);
        }
    }

    /// <summary>Checks the messages of a successful login and returns the position of the first message after it.</summary>
    private static int CheckLogin(List<BackendMessage> messages, PostgreSqlAuthenticationMethod method)
    {
        var position = 0;
        FuzzAssert.True(messages.Count > position && messages[position].Type is PgWire.Authentication && messages[position].AuthenticationCode == GetAuthenticationRequestCode(method), "The server must start with an authentication request");
        position++;
        if (method is PostgreSqlAuthenticationMethod.ScramSha256)
        {
            foreach (var code in (int[])[11, 12])
            {
                FuzzAssert.True(messages.Count > position && messages[position].Type is PgWire.Authentication && messages[position].AuthenticationCode == code, $"Expected the SASL message {code}");
                position++;
            }
        }

        FuzzAssert.True(messages.Count >= position + 9, "The login must succeed");
        FuzzAssert.True(messages[position].Type is PgWire.Authentication && messages[position].AuthenticationCode is 0, "Expected AuthenticationOk");
        for (var i = 1; i <= 6; i++)
        {
            FuzzAssert.Equal(PgWire.ParameterStatus, messages[position + i].Type, "Expected a ParameterStatus after AuthenticationOk");
        }

        FuzzAssert.Equal(PgWire.BackendKeyData, messages[position + 7].Type, "Expected BackendKeyData after the server parameters");
        FuzzAssert.True(messages[position + 8].Type is PgWire.ReadyForQuery && messages[position + 8].Payload is [(byte)'I'], "Expected ReadyForQuery (idle) after BackendKeyData");
        return position + 9;
    }

    private static int GetAuthenticationRequestCode(PostgreSqlAuthenticationMethod method) => method switch
    {
        PostgreSqlAuthenticationMethod.ClearTextPassword => 3,
        PostgreSqlAuthenticationMethod.Md5Password => 5,
        _ => 10,
    };

    /// <summary>Gets the payload of the last authentication request of the server, which must have the given code.</summary>
    private static byte[] GetAuthenticationRequest(byte[] serverOutput, int code)
    {
        var message = PgWire.ReadBackendMessages(serverOutput)[^1];
        FuzzAssert.True(message.Type is PgWire.Authentication && message.AuthenticationCode == code, $"Expected the authentication request {code}, not {message}");
        return message.Payload;
    }

    private static bool IsSslRequest(byte[] input)
    {
        if (input.Length < 8)
            return false;

        var length = BinaryPrimitives.ReadInt32BigEndian(input);
        return length is >= 8 and <= PostgreSqlServerOptions.MaxStartupPacketSize && input.Length >= length && BinaryPrimitives.ReadInt32BigEndian(input.AsSpan(4)) == PgWire.SslRequestCode;
    }

    private static string ReadField(ref ReadOnlySpan<byte> data) => Encoding.UTF8.GetString(FuzzInput.ReadUntil(ref data, 0xFF));

    private static byte[] ReadLengthPrefixed(ref ReadOnlySpan<byte> data)
    {
        var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var value = data[..Math.Min(length, data.Length)].ToArray();
        data = data[value.Length..];
        return value;
    }

    /// <summary>The server decodes a password message as UTF-8, without its final zero byte.</summary>
    private static string DecodePasswordMessage(ReadOnlySpan<byte> payload) => Encoding.UTF8.GetString(payload is [.. var text, 0] ? text : payload);

    /// <summary>Gets the client-first-message-bare of a SASLInitialResponse payload, or an empty string when it is malformed.</summary>
    private static string GetClientFirstBare(ReadOnlySpan<byte> payload)
    {
        var end = payload.IndexOf((byte)0);
        if (end < 0 || payload.Length < end + 5)
            return "";

        var length = BinaryPrimitives.ReadInt32BigEndian(payload[(end + 1)..]);
        var data = payload[(end + 5)..];
        if (length < 0 || length > data.Length)
            return "";

        var message = Encoding.UTF8.GetString(data[..length]);
        var separator = message.IndexOf(",,", StringComparison.Ordinal);
        return separator < 0 ? "" : message[(separator + 2)..];
    }

    /// <summary>
    /// HMAC hashes a key longer than its block (64 bytes), then pads it with zeros: the passwords that differ only by
    /// trailing zero bytes, or a long password and its SHA-256 hash, are the same key.
    /// </summary>
    private static bool IsSameHmacKey(string password1, string password2)
    {
        static byte[] GetKeyBlock(string password)
        {
            var key = Encoding.UTF8.GetBytes(password);
            if (key.Length > 64)
            {
                key = SHA256.HashData(key);
            }

            var block = new byte[64];
            key.CopyTo(block, 0);
            return block;
        }

        return GetKeyBlock(password1).AsSpan().SequenceEqual(GetKeyBlock(password2));
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "PostgreSQL MD5 authentication requires MD5 hashing.")]
    private static string ComputeMd5Response(string password, string userName, byte[] salt)
    {
        var inner = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password + userName)));
        return "md5" + Convert.ToHexStringLower(MD5.HashData([.. Encoding.UTF8.GetBytes(inner), .. salt]));
    }

    /// <summary>Computes the client proof and the server signature of a SCRAM-SHA-256 exchange (RFC 5802), as a client does.</summary>
    private static (byte[] Proof, byte[] ServerSignature) ComputeScram(string password, byte[] salt, string authMessage)
    {
        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, ScramIterationCount, HashAlgorithmName.SHA256, 32);
        var clientKey = HMACSHA256.HashData(saltedPassword, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var authMessageBytes = Encoding.UTF8.GetBytes(authMessage);
        var clientSignature = HMACSHA256.HashData(storedKey, authMessageBytes);
        var proof = new byte[clientKey.Length];
        for (var i = 0; i < proof.Length; i++)
        {
            proof[i] = (byte)(clientKey[i] ^ clientSignature[i]);
        }

        var serverKey = HMACSHA256.HashData(saltedPassword, "Server Key"u8);
        return (proof, HMACSHA256.HashData(serverKey, authMessageBytes));
    }

    private static FuzzSeed Seed(string name, byte[] data) => new(name, data);

    private static byte[] Bytes(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    /// <summary>Sessions of the simple and the extended query protocols, after the login.</summary>
    private static IEnumerable<(string Name, (byte Type, byte[] Payload)[] Messages)> GetSessions()
    {
        static (byte, byte[]) Query(string sql) => (PgWire.Query, PgWire.CString(sql));
        static (byte, byte[]) Parse(string name, string sql, params uint[] types) => (PgWire.Parse, PgWire.ParsePayload(name, sql, types));
        static (byte, byte[]) Bind(string portal, string statement, short[] formats, byte[]?[] values, short[] resultFormats) => (PgWire.Bind, PgWire.BindPayload(portal, statement, formats, values, resultFormats));
        static (byte, byte[]) Describe(char target, string name) => (PgWire.Describe, PgWire.TargetPayload(target, name));
        static (byte, byte[]) Execute(string portal, int maxRows = 0) => (PgWire.Execute, PgWire.ExecutePayload(portal, maxRows));
        static (byte, byte[]) Close(char target, string name) => (PgWire.Close, PgWire.TargetPayload(target, name));
        (byte, byte[]) sync = (PgWire.Sync, []);

        yield return ("simple-query", [Query("SELECT 1")]);
        yield return ("simple-queries", [Query("BEGIN"), Query("SELECT * FROM t WHERE id = 1"), Query("COMMIT"), Query("NOTICE"), Query("DO $$ $$"), Query("ERROR"), Query("FAIL"), Query("SELECT 2")]);
        yield return ("empty-query", [Query(""), Query("   "), (PgWire.Query, [])]);
        yield return ("unicode-query", [Query("SELECT 'h\u00E9llo \U0001F600'")]);
        yield return ("extended-query", [Parse("", "SELECT $1, $2", 23, 25), Bind("", "", [], [Encoding.UTF8.GetBytes("42"), Encoding.UTF8.GetBytes("text")], []), Describe('P', ""), Execute(""), sync]);
        yield return ("extended-query-named", [Parse("s1", "SELECT $1", 20), Describe('S', "s1"), Bind("p1", "s1", [1], [PgWire.Int64(long.MinValue)], [1]), Describe('P', "p1"), Execute("p1"), Close('P', "p1"), Close('S', "s1"), sync]);
        yield return ("extended-query-binary", [
            Parse("s", "SELECT", 16, 21, 23, 20, 700, 701, 1700, 2950, 1082, 1114, 1184, 17, 25, 0),
            Bind("p", "s", [1], [[1], PgWire.Int16(-2), PgWire.Int32(3), PgWire.Int64(4), PgWire.Int32(0x3FC00000), PgWire.Int64(0x4002000000000000), [0, 2, 0, 0, 0, 0, 0, 2, 0, 1, 0x13, 0x88], Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", CultureInfo.InvariantCulture).ToByteArray(bigEndian: true), PgWire.Int32(8825), PgWire.Int64(762_525_296_123_456), PgWire.Int64(0), [0xDE, 0xAD], "text"u8.ToArray(), null], [0, 1]),
            Describe('P', "p"), Execute("p"), sync]);
        yield return ("extended-query-text-values", [
            Parse("s", "SELECT", 16, 21, 23, 20, 700, 701, 1700, 2950, 1082, 1114, 1184, 114),
            Bind("p", "s", [0], [.. ((string[])["yes", "-32768", "2147483647", "9223372036854775807", "1.5e3", "NaN", "79228162514264337593543950335", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "2024-02-29", "2024-01-01 12:34:56.123456", "2024-01-01 12:34:56+02", "{\"a\":[1,2]}"]).Select(Encoding.UTF8.GetBytes)], []),
            Execute("p"), sync]);
        yield return ("extended-query-suspended", [Parse("", "SELECT $1, $2"), Bind("", "", [], [[0x61], [0x62]], []), Execute("", 1), Execute("", 2), sync]);
        yield return ("extended-query-error", [Parse("", "ERROR"), Bind("", "", [], [], []), Describe('P', ""), Execute(""), Query("SELECT 1"), sync, Query("SELECT 2")]);
        yield return ("extended-query-no-result", [Parse("", "DO"), Bind("", "", [], [], []), Describe('P', ""), Execute(""), Parse("", " "), Bind("", "", [], [], []), Execute(""), sync]);
        yield return ("extended-query-flush", [Parse("s1", "BEGIN"), (PgWire.Flush, []), Bind("", "s1", [], [], []), Execute(""), sync]);
        yield return ("unknown-statement", [Bind("", "missing", [], [], []), Execute(""), sync, Describe('S', "missing"), sync, Describe('X', ""), sync, Close('X', ""), sync]);
        yield return ("invalid-bind", [Parse("s", "SELECT $1"), Bind("", "s", [0, 1], [[1]], []), sync, Bind("", "s", [2], [], []), sync, (PgWire.Bind, [(byte)'p', 0, (byte)'s', 0, 0, 0, 0, 1, 0x7F, 0xFF, 0xFF, 0xFF]), sync]);
        yield return ("unsupported-messages", [((byte)'F', [0, 0, 0, 1]), ((byte)'d', [1, 2, 3]), ((byte)'c', []), sync, (PgWire.PasswordMessage, PgWire.CString("again")), sync, Query("SELECT 3")]);
        yield return ("many-statements", [Parse("a", "SELECT 1"), Parse("b", "SELECT 2"), Parse("c", "SELECT 3"), Bind("x", "a", [], [], []), Bind("y", "b", [], [], []), Bind("z", "c", [], [], []), sync]);
    }

    private static IEnumerable<FuzzSeed> GetSessionSeeds()
    {
        var startup = PgWire.Startup(("user", "fuzz"), ("database", "postgres"), ("application_name", "fuzz"), ("client_encoding", "UTF8"));
        var password = PgWire.Message(PgWire.PasswordMessage, PgWire.CString(ServerPassword));
        var login = Bytes(startup, password);
        var terminate = PgWire.Message(PgWire.Terminate, []);
        yield return Seed("empty", [0]);
        yield return Seed("startup", [0, .. startup]);
        yield return Seed("login", [0, .. login]);
        yield return Seed("login-denied", [0, .. startup, .. PgWire.Message(PgWire.PasswordMessage, PgWire.CString("deny"))]);
        yield return Seed("login-md5", [1, .. startup, .. PgWire.Message(PgWire.PasswordMessage, PgWire.CString("md5" + new string('0', 32)))]);
        yield return Seed("login-scram", [2, .. startup, .. PgWire.Message(PgWire.PasswordMessage, PgWire.SaslInitialResponsePayload("SCRAM-SHA-256", "n,,n=fuzz,r=rOprNGfwEbeRWgbNEkqO"u8)), .. PgWire.Message(PgWire.PasswordMessage, "c=biws,r=rOprNGfwEbeRWgbNEkqO,p=dHzbZapWIk4jUhN+Ute9ytag9zjfMHgsqmmiz7AndVQ="u8)]);
        yield return Seed("ssl-request", [0, .. PgWire.StartupPacket(PgWire.SslRequestCode, []), .. login, .. PgWire.QueryMessage("SELECT 1"), .. terminate]);
        yield return Seed("ssl-request-twice", [0, .. PgWire.StartupPacket(PgWire.SslRequestCode, []), .. PgWire.StartupPacket(PgWire.SslRequestCode, [])]);
        yield return Seed("gssenc-request", [0, .. PgWire.StartupPacket(PgWire.GssEncRequestCode, [])]);
        yield return Seed("cancel-request", [0, .. PgWire.StartupPacket(PgWire.CancelRequestCode, [0, 0, 0, 1, 0, 0, 0, 2])]);
        yield return Seed("protocol-2", [0, .. PgWire.StartupPacket(0x00020000, PgWire.StartupParameters([("user", "fuzz")]))]);
        yield return Seed("message-too-large", [8, .. login, .. PgWire.QueryMessage("SELECT " + new string('x', 100))]);
        yield return Seed("truncated-message", [0, .. login, .. PgWire.QueryMessage("SELECT 1").AsSpan(0, 8)]);
        yield return Seed("limits", [4, .. login, .. PgWire.Message(PgWire.Parse, PgWire.ParsePayload("a", "SELECT 1")), .. PgWire.Message(PgWire.Parse, PgWire.ParsePayload("b", "SELECT 1")), .. PgWire.Message(PgWire.Parse, PgWire.ParsePayload("c", "SELECT 1")), .. PgWire.Message(PgWire.Sync, [])]);
        foreach (var (name, messages) in GetSessions())
        {
            yield return Seed(name, [0, .. login, .. messages.SelectMany(message => PgWire.Message(message.Type, message.Payload)), .. terminate]);
        }
    }

    private static IEnumerable<FuzzSeed> GetMessagesSeeds()
    {
        yield return Seed("nothing", [0]);
        yield return Seed("terminate", CreateMessagesInput(0, (PgWire.Terminate, []), (PgWire.Query, PgWire.CString("SELECT 1"))));
        foreach (var (name, messages) in GetSessions())
        {
            yield return Seed(name, CreateMessagesInput(name.StartsWith("many", StringComparison.Ordinal) ? (byte)1 : (byte)0, messages));
        }
    }

    private static byte[] CreateMessagesInput(byte flags, params (byte Type, byte[] Payload)[] messages)
    {
        var result = new List<byte> { flags };
        foreach (var (type, payload) in messages)
        {
            var selector = Array.IndexOf(MessageTypes, type);
            if (selector < 0 || type is RawType)
            {
                result.Add((byte)Array.IndexOf(MessageTypes, RawType));
                result.Add(type);
            }
            else
            {
                result.Add((byte)selector);
            }

            result.Add((byte)payload.Length);
            result.Add((byte)(payload.Length >> 8));
            result.AddRange(payload);
        }

        return [.. result];
    }

    private static IEnumerable<FuzzSeed> GetStartupSeeds()
    {
        yield return Seed("empty", [0]);
        yield return Seed("short-header", [0, 0, 0]);
        yield return Seed("startup", [0, .. PgWire.Startup(("user", "fuzz"), ("database", "postgres"))]);
        yield return Seed("startup-md5", [1, .. PgWire.Startup(("user", "fuzz"))]);
        yield return Seed("startup-scram", [2, .. PgWire.Startup(("user", "fuzz"), ("database", "postgres"), ("application_name", "psql"), ("client_encoding", "UTF8"), ("options", "-c search_path=public"))]);
        yield return Seed("startup-no-parameters", [0, .. PgWire.StartupPacket(PgWire.ProtocolVersion3, [])]);
        yield return Seed("startup-duplicate-parameter", [0, .. PgWire.Startup(("user", "a"), ("user", "b"))]);
        yield return Seed("startup-missing-value", [0, .. PgWire.StartupPacket(PgWire.ProtocolVersion3, "user\0"u8)]);
        yield return Seed("startup-unterminated", [0, .. PgWire.StartupPacket(PgWire.ProtocolVersion3, "user\0fuzz"u8)]);
        yield return Seed("startup-trailing-data", [0, .. PgWire.StartupPacket(PgWire.ProtocolVersion3, "user\0fuzz\0\0garbage"u8)]);
        yield return Seed("ssl-request", [0, .. PgWire.StartupPacket(PgWire.SslRequestCode, [])]);
        yield return Seed("gssenc-request", [0, .. PgWire.StartupPacket(PgWire.GssEncRequestCode, [])]);
        yield return Seed("cancel-request", [0, .. PgWire.StartupPacket(PgWire.CancelRequestCode, [0, 0, 0, 1, 0, 0, 0, 2])]);
        yield return Seed("cancel-request-short", [0, .. PgWire.StartupPacket(PgWire.CancelRequestCode, [0, 0, 0, 1])]);
        yield return Seed("protocol-2", [0, .. PgWire.StartupPacket(0x00020000, [])]);
        yield return Seed("length-too-small", [0, 0, 0, 0, 7, 0, 3, 0, 0]);
        yield return Seed("length-too-large", [0, 0, 0, 0x27, 0x11, 0, 3, 0, 0]);
        yield return Seed("truncated", [0, 0, 0, 0, 20, 0, 3, 0, 0, (byte)'u']);
    }

    private static IEnumerable<FuzzSeed> GetAuthenticationSeeds()
    {
        static byte[] Fields(byte method, byte mode, params byte[][] fields)
        {
            var result = new List<byte> { method, mode };
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    result.Add(0xFF);
                }

                result.AddRange(fields[i]);
            }

            return [.. result];
        }

        static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

        // In raw mode, the first payload is prefixed by its length
        static byte[] Raw(byte[] first, byte[] second) => [(byte)first.Length, (byte)(first.Length >> 8), .. first, .. second];

        var secret = Utf8(ServerPassword);
        var user = Utf8("fuzz");
        yield return Seed("cleartext", Fields(0, 0x02, secret, user));
        yield return Seed("cleartext-wrong-password", Fields(0, 0x00, secret, Utf8("wrong"), user));
        yield return Seed("cleartext-unicode", Fields(0, 0x02, Utf8("p\u00E2ss\U0001F511"), Utf8("us\u00E9r")));
        yield return Seed("cleartext-empty-password", Fields(0, 0x02, [], user));
        yield return Seed("cleartext-raw", Fields(0, 0x01, secret, Utf8("unused"), user, Raw(Utf8("secret\0"), [])));
        yield return Seed("cleartext-raw-unterminated", Fields(0, 0x01, secret, Utf8("unused"), user, Raw(secret, [])));
        yield return Seed("cleartext-raw-wrong-type", Fields(0, 0x0B, secret, user, Raw(Utf8("secret\0"), [])));
        yield return Seed("md5", Fields(1, 0x02, secret, user));
        yield return Seed("md5-uppercase", Fields(1, 0x12, secret, user));
        yield return Seed("md5-wrong-password", Fields(1, 0x00, secret, Utf8("wrong"), user));
        yield return Seed("md5-without-user", Fields(1, 0x06, secret, user));
        yield return Seed("md5-empty-user", Fields(1, 0x02, secret, []));
        yield return Seed("md5-raw", Fields(1, 0x03, secret, user, Raw(Utf8("%m\0"), [])));
        yield return Seed("md5-raw-invalid", Fields(1, 0x01, secret, Utf8("unused"), user, Raw(Utf8("md5\0"), [])));
        yield return Seed("scram", Fields(2, 0x02, secret, user, Utf8("rOprNGfwEbeRWgbNEkqO")));
        yield return Seed("scram-channel-binding-supported", Fields(2, 0x12, secret, user, Utf8("rOprNGfwEbeRWgbNEkqO")));
        yield return Seed("scram-wrong-password", Fields(2, 0x00, secret, Utf8("wrong"), user, Utf8("nonce")));
        yield return Seed("scram-password-trailing-zero", Fields(2, 0x00, secret, Utf8("secret\0"), user, Utf8("nonce")));
        yield return Seed("scram-extensions", Fields(2, 0x02, secret, Utf8("us\u00E9r"), Utf8("nonce"), Utf8("x=1,m=ext")));
        yield return Seed("scram-raw", Fields(2, 0x03, secret, user, Raw(PgWire.SaslInitialResponsePayload("SCRAM-SHA-256", Utf8("n,,n=fuzz,r=abc")), Utf8("c=%c,r=%r,p=%p"))));
        yield return Seed("scram-raw-wrong-nonce", Fields(2, 0x03, secret, user, Raw(PgWire.SaslInitialResponsePayload("SCRAM-SHA-256", Utf8("n,,n=fuzz,r=abc")), Utf8("c=%c,r=abc,p=%p"))));
        yield return Seed("scram-raw-channel-binding", Fields(2, 0x03, secret, user, Raw(PgWire.SaslInitialResponsePayload("SCRAM-SHA-256", Utf8("y,,n=fuzz,r=abc")), Utf8("c=%c,r=%r,p=%p"))));
        yield return Seed("scram-raw-plus", Fields(2, 0x03, secret, user, Raw(PgWire.SaslInitialResponsePayload("SCRAM-SHA-256-PLUS", Utf8("p=tls-server-end-point,,n=fuzz,r=abc")), Utf8("c=%c,r=%r,p=%p"))));
        yield return Seed("scram-raw-invalid-length", Fields(2, 0x03, secret, user, Raw([.. Utf8("SCRAM-SHA-256\0"), 0x7F, 0xFF, 0xFF, 0xFF, (byte)'n'], [])));
    }

    private static IEnumerable<FuzzSeed> GetParameterValuesSeeds()
    {
        static byte[] Value(uint typeOid, byte format, params byte[] value) => [(byte)Array.IndexOf(TypeOids, typeOid), format, .. value];

        static byte[] Text(uint typeOid, string value) => Value(typeOid, 0, Encoding.UTF8.GetBytes(value));

        yield return Seed("null", Value(23, 0x80));
        yield return Seed("text", Text(25, "h\u00E9llo"));
        yield return Seed("int2-text", Text(21, "-32768"));
        yield return Seed("int4-text", Text(23, "2147483647"));
        yield return Seed("int8-text", Text(20, "-9223372036854775808"));
        yield return Seed("int4-text-invalid", Text(23, "12a"));
        yield return Seed("float4-text", Text(700, "1.5e3"));
        yield return Seed("float8-text", Text(701, "-Infinity"));
        yield return Seed("numeric-text", Text(1700, "-12345.6789"));
        yield return Seed("bool-text", Text(16, " Yes "));
        yield return Seed("uuid-text", Text(2950, "{6f9619ff-8b86-d011-b42d-00c04fc964ff}"));
        yield return Seed("date-text", Text(1082, "2024-02-29"));
        yield return Seed("timestamp-text", Text(1114, "2024-01-01 12:34:56.1234567"));
        yield return Seed("timestamptz-text", Text(1184, "2024-01-01 12:34:56+02:00"));
        yield return Seed("json-text", Text(114, "{\"a\":[1,2]}"));
        yield return Seed("bool-binary", Value(16, 1, 1));
        yield return Seed("int2-binary", Value(21, 1, PgWire.Int16(short.MinValue)));
        yield return Seed("int4-binary", Value(23, 1, PgWire.Int32(42)));
        yield return Seed("int8-binary", Value(20, 1, PgWire.Int64(long.MaxValue)));
        yield return Seed("float4-binary", Value(700, 1, PgWire.Int32(0x3FC00000)));
        yield return Seed("float8-binary-nan", Value(701, 1, PgWire.Int64(0x7FF8000000000000)));
        yield return Seed("float8-binary-power-of-two", Value(701, 1, PgWire.Int64(0x0410000000000000)));
        yield return Seed("numeric-binary", Value(1700, 1, 0, 2, 0, 1, 0x40, 0, 0, 4, 0, 1, 0x09, 0x29));
        yield return Seed("numeric-binary-nan", Value(1700, 1, 0, 0, 0, 0, 0xC0, 0, 0, 0));
        yield return Seed("uuid-binary", Value(2950, 1, Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", CultureInfo.InvariantCulture).ToByteArray(bigEndian: true)));
        yield return Seed("date-binary", Value(1082, 1, PgWire.Int32(8825)));
        yield return Seed("date-binary-infinity", Value(1082, 1, PgWire.Int32(int.MaxValue)));
        yield return Seed("timestamp-binary", Value(1114, 1, PgWire.Int64(762_525_296_123_456)));
        yield return Seed("timestamptz-binary", Value(1184, 1, PgWire.Int64(-1)));
        yield return Seed("timestamp-binary-infinity", Value(1114, 1, PgWire.Int64(long.MaxValue)));
        yield return Seed("timestamptz-binary-minus-infinity", Value(1184, 1, PgWire.Int64(long.MinValue)));
        yield return Seed("numeric-binary-tiny", Value(1700, 1, 0, 2, 0x80, 0, 0, 0, 0, 0, 0x27, 0x0F, 0, 1));
        yield return Seed("bytea-binary", Value(17, 1, 0xDE, 0xAD, 0xBE, 0xEF));
        yield return Seed("jsonb-binary", Value(3802, 1, Encoding.UTF8.GetBytes("{\"a\":1}")));
        yield return Seed("int4-binary-wrong-size", Value(23, 1, 1, 2, 3));
        yield return Seed("unknown-oid", Value(0, 1, 1, 2, 3));
    }

    private static IEnumerable<FuzzSeed> GetScramParserSeeds()
    {
        yield return FuzzSeed.FromText("empty", "");
        yield return Utf8Seed("client-first", "n,,n=user,r=fyko+d2lbbFgONRv9qkxdawL");
        yield return Utf8Seed("client-first-y", "y,,n=,r=rOprNGfwEbeRWgbNEkqO");
        yield return Utf8Seed("client-first-plus", "p=tls-server-end-point,,n=,r=abc");
        yield return Utf8Seed("client-first-authzid", "n,a=admin,n=user,r=abc,m=ext");
        yield return Utf8Seed("client-first-no-nonce", "n,,n=user");
        yield return Utf8Seed("client-final", "c=biws,r=fyko+d2lbbFgONRv9qkxdawL3rfcNHYJY1ZVvWVs7j,p=v0X8v3Bz2T0CJGbJQyF0X+HI4Ts=");
        yield return Utf8Seed("client-final-invalid-proof", "c=biws,r=abc,p=***");
        yield return Utf8Seed("client-final-extension", "c=eSws,r=abc,x=1,p=AAAA");
        yield return Utf8Seed("attributes", " a = b ,,c=,=d,e==f,g");

        static FuzzSeed Utf8Seed(string name, string value) => new(name, Encoding.UTF8.GetBytes(value));
    }
}
