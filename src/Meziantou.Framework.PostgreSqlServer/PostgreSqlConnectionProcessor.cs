using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Framework.PostgreSql.Protocol;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.PostgreSql;

internal sealed class PostgreSqlConnectionProcessor
{
    private readonly PostgreSqlServerOptions _options;
    private readonly PostgreSqlAuthenticationDelegate _authenticationHandler;
    private readonly PostgreSqlQueryDelegate _queryHandler;
    private readonly ILogger _logger;

    public PostgreSqlConnectionProcessor(
        PostgreSqlServerOptions options,
        PostgreSqlAuthenticationDelegate authenticationHandler,
        PostgreSqlQueryDelegate queryHandler,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authenticationHandler);
        ArgumentNullException.ThrowIfNull(queryHandler);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _authenticationHandler = authenticationHandler;
        _queryHandler = queryHandler;
        _logger = logger;
    }

    public async Task ProcessAsync(Stream input, Stream output, EndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        var writer = new PostgreSqlMessageWriter(output);
        SslStream? sslStream = null;
        var startupParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        int processId = default;
        int secretKey = default;

        try
        {
            using (var handshakeCts = CreateTimeoutTokenSource(_options.HandshakeTimeout, cancellationToken))
            {
                var handshakeToken = handshakeCts?.Token ?? cancellationToken;
                var startupPacket = await PostgreSqlMessageReader.ReadStartupPacketAsync(input, handshakeToken).ConfigureAwait(false);
                if (startupPacket is null)
                {
                    return;
                }

                // An SSLRequest is answered at most once. Looping would let a client stack SslStreams on one connection.
                if (startupPacket.RequestCode == PostgreSqlConstants.SslRequestCode)
                {
                    var serverCertificate = _options.GetTlsCertificate();
                    var canUpgradeToTls = serverCertificate is not null;
                    await writer.WriteSslResponseAsync(canUpgradeToTls, handshakeToken).ConfigureAwait(false);
                    if (canUpgradeToTls)
                    {
                        sslStream = await UpgradeToTlsAsync(input, output, serverCertificate!, handshakeToken).ConfigureAwait(false);
                        input = sslStream;
                        output = sslStream;
                        writer = new PostgreSqlMessageWriter(output);
                    }

                    startupPacket = await PostgreSqlMessageReader.ReadStartupPacketAsync(input, handshakeToken).ConfigureAwait(false);
                    if (startupPacket is null)
                    {
                        return;
                    }

                    if (startupPacket.RequestCode == PostgreSqlConstants.SslRequestCode)
                    {
                        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", PostgreSqlConstants.SqlStates.ProtocolViolation, "Duplicate SSLRequest"), handshakeToken).ConfigureAwait(false);
                        return;
                    }
                }

                if (_options.RequireEncryption && sslStream is null)
                {
                    // Checked before the CancelRequest branch so no pre-authentication path escapes the requirement.
                    await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", PostgreSqlConstants.SqlStates.InvalidAuthorizationSpecification, "TLS is required"), handshakeToken).ConfigureAwait(false);
                    return;
                }

                if (startupPacket.RequestCode == PostgreSqlConstants.CancelRequestCode)
                {
                    HandleCancelRequest(startupPacket.Payload, remoteEndPoint);
                    return;
                }

                if (startupPacket.RequestCode != PostgreSqlConstants.ProtocolVersion3)
                {
                    await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", PostgreSqlConstants.SqlStates.ProtocolViolation, "Unsupported protocol version"), handshakeToken).ConfigureAwait(false);
                    return;
                }

                startupParameters = PostgreSqlMessageReader.ParseStartupParameters(startupPacket.Payload);
                var userName = startupParameters.TryGetValue("user", out var startupUserName) ? startupUserName : null;
                var database = startupParameters.TryGetValue("database", out var startupDatabase) ? startupDatabase : null;
                if (!await AuthenticateAsync(input, writer, remoteEndPoint, startupParameters, userName, database, handshakeToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            (processId, secretKey, var backendSession) = _options.RegisterBackendSession();
            await WriteSessionInitializedMessagesAsync(writer, processId, secretKey, cancellationToken).ConfigureAwait(false);

            var session = new PostgreSqlSessionState(remoteEndPoint, startupParameters, backendSession);
            while (!cancellationToken.IsCancellationRequested)
            {
                PostgreSqlFrontendMessage? message;
                using (var idleCts = CreateTimeoutTokenSource(_options.IdleTimeout, cancellationToken))
                {
                    message = await PostgreSqlMessageReader.ReadMessageAsync(input, _options.MaxMessageSize, idleCts?.Token ?? cancellationToken).ConfigureAwait(false);
                }

                if (message is null)
                {
                    return;
                }

                if (message.Type == PostgreSqlConstants.Frontend.Terminate)
                {
                    return;
                }

                // The extended query protocol requires the backend to skip messages until Sync once an error occurs.
                if (session.InErrorState && message.Type != PostgreSqlConstants.Frontend.Sync)
                {
                    continue;
                }

                try
                {
                    await DispatchMessageAsync(writer, session, message, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (InvalidDataException ex)
                {
                    _logger.LogDebug(ex, "Protocol error from {RemoteEndPoint}", remoteEndPoint);
                    session.InErrorState = true;
                    await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("ERROR", PostgreSqlConstants.SqlStates.ProtocolViolation, ex.Message), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogDebug(ex, "TLS authentication failed for {RemoteEndPoint}", remoteEndPoint);
        }
        finally
        {
            if (processId != default && secretKey != default)
            {
                _options.UnregisterBackendSession(processId, secretKey);
            }

            if (sslStream is not null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static CancellationTokenSource? CreateTimeoutTokenSource(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        return cancellationTokenSource;
    }

    private async ValueTask DispatchMessageAsync(PostgreSqlMessageWriter writer, PostgreSqlSessionState session, PostgreSqlFrontendMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case PostgreSqlConstants.Frontend.Query:
                await HandleSimpleQueryAsync(writer, session, message, cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Parse:
                HandleParseMessage(session, message);
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParseComplete, PostgreSqlResponseSerializer.CreateParseComplete(), cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Bind:
                HandleBindMessage(session, message);
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.BindComplete, PostgreSqlResponseSerializer.CreateBindComplete(), cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Describe:
                await HandleDescribeMessageAsync(writer, session, message, cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Execute:
                await HandleExecuteMessageAsync(writer, session, message, cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Close:
                HandleCloseMessage(session, message);
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.CloseComplete, PostgreSqlResponseSerializer.CreateCloseComplete(), cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Sync:
                session.InErrorState = false;
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(session.TransactionStatus), cancellationToken).ConfigureAwait(false);
                break;
            case PostgreSqlConstants.Frontend.Flush:
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException($"Unsupported frontend message '{(char)message.Type}'.");
        }
    }


    private async ValueTask<bool> AuthenticateAsync(
        Stream input,
        PostgreSqlMessageWriter writer,
        EndPoint remoteEndPoint,
        IReadOnlyDictionary<string, string> startupParameters,
        string? userName,
        string? database,
        CancellationToken cancellationToken)
    {
        // The per-method handlers return only the material specific to their mechanism; the context is built
        // once here so a new shared property cannot be silently dropped from one of the three paths.
        var material = _options.AuthenticationMethod switch
        {
            PostgreSqlAuthenticationMethod.ClearTextPassword => await HandleClearTextAuthenticationAsync(input, writer, cancellationToken).ConfigureAwait(false),
            PostgreSqlAuthenticationMethod.Md5Password => await HandleMd5AuthenticationAsync(input, writer, cancellationToken).ConfigureAwait(false),
            PostgreSqlAuthenticationMethod.ScramSha256 => await HandleScramAuthenticationAsync(input, writer, cancellationToken).ConfigureAwait(false),
            _ => default,
        };

        var context = new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = remoteEndPoint,
            Method = _options.AuthenticationMethod,
            UserName = userName,
            Database = database,
            StartupParameters = startupParameters,
            Password = material.Password,
            Md5Salt = material.Md5Salt,
            Md5PasswordResponse = material.Md5PasswordResponse,
            ScramSalt = material.ScramSalt,
            ScramIterationCount = material.ScramIterationCount,
            ScramClientProof = material.ScramClientProof,
            ScramAuthMessage = material.ScramAuthMessage,
        };

        PostgreSqlAuthenticationResult result;
        try
        {
            result = await _authenticationHandler(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Mirrors the query handler: a faulting callback becomes a protocol error, not a dropped connection.
            _logger.LogError(ex, "Unhandled exception in PostgreSQL authentication handler for {RemoteEndPoint}", remoteEndPoint);
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", PostgreSqlConstants.SqlStates.InternalError, "Unhandled authentication handler exception"), cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!result.IsAuthenticated)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", result.ErrorCode, result.ErrorMessage ?? "Authentication failed"), cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (_options.AuthenticationMethod == PostgreSqlAuthenticationMethod.ScramSha256)
        {
            if (!context.TryGetScramServerFinalMessage(out var serverFinalMessage))
            {
                _logger.LogError("The authentication handler for {RemoteEndPoint} reported success under SCRAM-SHA-256 without calling ValidatePassword, which is required to compute the server signature.", remoteEndPoint);
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", PostgreSqlConstants.SqlStates.InternalError, "The authentication handler must call ValidatePassword when SCRAM-SHA-256 is used."), cancellationToken).ConfigureAwait(false);
                return false;
            }

            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationSaslFinal(serverFinalMessage), cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationOk(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The mechanism-specific material collected during the authentication exchange.</summary>
    private readonly record struct AuthenticationMaterial
    {
        public string? Password { get; init; }

        public byte[]? Md5Salt { get; init; }

        public string? Md5PasswordResponse { get; init; }

        public byte[]? ScramSalt { get; init; }

        public int ScramIterationCount { get; init; }

        public byte[]? ScramClientProof { get; init; }

        public string? ScramAuthMessage { get; init; }
    }

    private async ValueTask<AuthenticationMaterial> HandleClearTextAuthenticationAsync(Stream input, PostgreSqlMessageWriter writer, CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationClearTextPassword(), cancellationToken).ConfigureAwait(false);
        var passwordMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        return new AuthenticationMaterial
        {
            Password = DecodeNullTerminatedString(passwordMessage.Payload),
        };
    }

    private async ValueTask<AuthenticationMaterial> HandleMd5AuthenticationAsync(Stream input, PostgreSqlMessageWriter writer, CancellationToken cancellationToken)
    {
        var salt = new byte[4];
        RandomNumberGenerator.Fill(salt);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationMd5Password(salt), cancellationToken).ConfigureAwait(false);

        var passwordMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        return new AuthenticationMaterial
        {
            Md5Salt = salt,
            Md5PasswordResponse = DecodeNullTerminatedString(passwordMessage.Payload),
        };
    }

    private async ValueTask<AuthenticationMaterial> HandleScramAuthenticationAsync(Stream input, PostgreSqlMessageWriter writer, CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationSasl(["SCRAM-SHA-256"]), cancellationToken).ConfigureAwait(false);
        var initialMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        var (mechanism, initialResponse) = ParseSaslInitialResponse(initialMessage.Payload);
        if (!string.Equals(mechanism, "SCRAM-SHA-256", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported SASL mechanism '{mechanism}'.");
        }

        var clientFirstMessage = Encoding.UTF8.GetString(initialResponse);
        if (!PostgreSqlScramHelper.TryParseClientFirstMessage(clientFirstMessage, out var clientFirstMessageBare, out var clientNonce, out var gs2Header))
        {
            throw new InvalidDataException("Invalid SCRAM client-first message.");
        }

        var serverNonce = PostgreSqlScramHelper.GenerateNonce();
        var fullNonce = clientNonce + serverNonce;
        var salt = PostgreSqlScramHelper.CreateSalt();
        const int IterationCount = 4096;
        var serverFirstMessage = PostgreSqlScramHelper.BuildServerFirstMessage(fullNonce, salt, IterationCount);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.Authentication, PostgreSqlResponseSerializer.CreateAuthenticationSaslContinue(serverFirstMessage), cancellationToken).ConfigureAwait(false);

        var finalMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        var clientFinalMessage = Encoding.UTF8.GetString(finalMessage.Payload);
        if (!PostgreSqlScramHelper.TryParseClientFinalMessage(clientFinalMessage, out var clientFinalWithoutProof, out var clientProof, out var clientFinalNonce, out var channelBinding))
        {
            throw new InvalidDataException("Invalid SCRAM client-final message.");
        }

        if (!string.Equals(fullNonce, clientFinalNonce, StringComparison.Ordinal))
        {
            throw new InvalidDataException("SCRAM nonce mismatch.");
        }

        // RFC 5802 5.1: c= must repeat the gs2 header sent in client-first, so a stripped -PLUS mechanism is detected.
        if (!PostgreSqlScramHelper.IsExpectedChannelBinding(channelBinding, gs2Header))
        {
            throw new InvalidDataException("SCRAM channel binding mismatch.");
        }

        var authMessage = $"{clientFirstMessageBare},{serverFirstMessage},{clientFinalWithoutProof}";
        return new AuthenticationMaterial
        {
            ScramSalt = salt,
            ScramIterationCount = IterationCount,
            ScramClientProof = clientProof,
            ScramAuthMessage = authMessage,
        };
    }

    private static (string Mechanism, byte[] InitialResponse) ParseSaslInitialResponse(ReadOnlySpan<byte> payload)
    {
        var index = 0;
        var mechanism = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (index + 4 > payload.Length)
        {
            throw new InvalidDataException("Invalid SCRAM initial message.");
        }

        var initialResponseLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(index, 4));
        index += 4;
        if (initialResponseLength < 0 || index + initialResponseLength > payload.Length)
        {
            throw new InvalidDataException("Invalid SCRAM initial response length.");
        }

        var initialResponse = payload.Slice(index, initialResponseLength).ToArray();
        return (mechanism, initialResponse);
    }

    private static string DecodeNullTerminatedString(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var end = payload[^1] == 0 ? payload.Length - 1 : payload.Length;
        return Encoding.UTF8.GetString(payload[..end]);
    }

    private async ValueTask<PostgreSqlFrontendMessage> ReadRequiredPasswordMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var message = await PostgreSqlMessageReader.ReadMessageAsync(input, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
        if (message is null || message.Type != PostgreSqlConstants.Frontend.PasswordMessage)
        {
            throw new InvalidDataException("Expected password message.");
        }

        return message;
    }

    private async ValueTask WriteSessionInitializedMessagesAsync(PostgreSqlMessageWriter writer, int processId, int secretKey, CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("server_version", _options.ServerVersion), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("server_encoding", "UTF8"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("client_encoding", "UTF8"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("DateStyle", "ISO, MDY"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("integer_datetimes", "on"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterStatus, PostgreSqlResponseSerializer.CreateParameterStatus("standard_conforming_strings", "on"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.BackendKeyData, PostgreSqlResponseSerializer.CreateBackendKeyData(processId, secretKey), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
    }

    private void HandleCancelRequest(byte[] payload, EndPoint remoteEndPoint)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var processId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
        var secretKey = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4, 4));
        if (!_options.TryCancelBackendSession(processId, secretKey))
        {
            _logger.LogDebug("Ignored cancel request for unknown session from {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private void HandleParseMessage(PostgreSqlSessionState session, PostgreSqlFrontendMessage message)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        var statementName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        var query = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (index + 2 > payload.Length)
        {
            throw new InvalidDataException("Invalid Parse message.");
        }

        var parameterTypeCount = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(index, 2));
        index += 2;
        if (parameterTypeCount < 0)
        {
            throw new InvalidDataException("Invalid Parse message parameter type count.");
        }

        var parameterTypeOids = new List<uint>(parameterTypeCount);
        for (var i = 0; i < parameterTypeCount; i++)
        {
            if (index + 4 > payload.Length)
            {
                throw new InvalidDataException("Invalid Parse message parameter type definition.");
            }

            parameterTypeOids.Add(BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(index, 4)));
            index += 4;
        }

        // The name is client-supplied and entries live for the connection, so the count is bounded.
        if (!session.PreparedStatements.ContainsKey(statementName) && session.PreparedStatements.Count >= _options.MaxPreparedStatementsPerConnection)
        {
            throw new InvalidDataException($"The connection reached the limit of {_options.MaxPreparedStatementsPerConnection} prepared statements.");
        }

        session.PreparedStatements[statementName] = new PostgreSqlStatement
        {
            Name = statementName,
            Query = query,
            ParameterTypeOids = parameterTypeOids,
        };
    }

    private void HandleBindMessage(PostgreSqlSessionState session, PostgreSqlFrontendMessage message)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        var portalName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        var statementName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (!session.PreparedStatements.TryGetValue(statementName, out var statement))
        {
            throw new InvalidDataException($"Unknown prepared statement '{statementName}'.");
        }

        var parameterFormatCodes = ReadFormatCodes(payload, ref index);
        if (index + 2 > payload.Length)
        {
            throw new InvalidDataException("Invalid Bind message.");
        }

        var parameterCount = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(index, 2));
        index += 2;
        if (parameterCount < 0)
        {
            throw new InvalidDataException("Invalid Bind message parameter count.");
        }

        // The spec allows 0 (all text), 1 (applies to all), or exactly one code per parameter.
        if (parameterFormatCodes.Count > 1 && parameterFormatCodes.Count != parameterCount)
        {
            throw new InvalidDataException($"Bind supplied {parameterFormatCodes.Count} parameter format codes for {parameterCount} parameters.");
        }

        var parameters = new List<PostgreSqlBoundParameter>(parameterCount);
        for (var i = 0; i < parameterCount; i++)
        {
            if (index + 4 > payload.Length)
            {
                throw new InvalidDataException("Invalid Bind parameter length.");
            }

            var valueLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(index, 4));
            index += 4;
            byte[]? value = null;
            if (valueLength >= 0)
            {
                if (index + valueLength > payload.Length)
                {
                    throw new InvalidDataException("Invalid Bind parameter payload.");
                }

                value = payload.Slice(index, valueLength).ToArray();
                index += valueLength;
            }
            else if (valueLength != -1)
            {
                throw new InvalidDataException("Invalid Bind parameter length.");
            }

            var typeOid = i < statement.ParameterTypeOids.Count ? statement.ParameterTypeOids[i] : 0u;
            var formatCode = ResolveFormatCode(parameterFormatCodes, i);
            parameters.Add(new PostgreSqlBoundParameter
            {
                TypeOid = typeOid,
                FormatCode = formatCode,
                RawValue = value,
            });
        }

        var resultFormatCodes = ReadFormatCodes(payload, ref index);
        if (!session.Portals.ContainsKey(portalName) && session.Portals.Count >= _options.MaxPortalsPerConnection)
        {
            throw new InvalidDataException($"The connection reached the limit of {_options.MaxPortalsPerConnection} portals.");
        }

        session.Portals[portalName] = new PostgreSqlPortal
        {
            Name = portalName,
            Statement = statement,
            Parameters = parameters,
            ResultFormatCodes = resultFormatCodes,
        };
    }

    private async ValueTask HandleDescribeMessageAsync(PostgreSqlMessageWriter writer, PostgreSqlSessionState session, PostgreSqlFrontendMessage message, CancellationToken cancellationToken)
    {
        var payload = message.Payload.AsSpan();
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("Invalid Describe message.");
        }

        var index = 0;
        var describeType = payload[index++];
        var name = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);

        PostgreSqlStatement statement;
        PostgreSqlPortal? portal = null;
        if (describeType == PostgreSqlConstants.DescribeTarget.Statement)
        {
            if (!session.PreparedStatements.TryGetValue(name, out var describedStatement))
            {
                throw new InvalidDataException($"Unknown prepared statement '{name}'.");
            }

            statement = describedStatement;
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ParameterDescription, PostgreSqlResponseSerializer.CreateParameterDescription(statement.ParameterTypeOids), cancellationToken).ConfigureAwait(false);
        }
        else if (describeType == PostgreSqlConstants.DescribeTarget.Portal)
        {
            if (!session.Portals.TryGetValue(name, out portal))
            {
                throw new InvalidDataException($"Unknown portal '{name}'.");
            }

            statement = portal.Statement;
        }
        else
        {
            throw new InvalidDataException($"Invalid Describe target '{(char)describeType}'.");
        }

        // The result shape is the callback's to decide; guessing it from the SQL text produced wrong
        // column counts and types, and Execute must never emit a RowDescription of its own.
        var context = new PostgreSqlQueryContext
        {
            RemoteEndPoint = session.RemoteEndPoint,
            StartupParameters = session.StartupParameters,
            RequestType = PostgreSqlQueryRequestType.Describe,
            CommandText = statement.Query,
            StatementName = statement.Name,
            PortalName = portal?.Name,
            Parameters = portal is null ? [] : DecodeParameters(portal),
        };

        var result = await ExecuteQueryHandlerAsync(context, session.BackendSession, cancellationToken).ConfigureAwait(false);
        var describedResultSet = result.Error is null && result.ResultSets.Count > 0 ? result.ResultSets[0] : null;
        if (portal is not null)
        {
            portal.DescribedColumnCount = describedResultSet?.Columns.Count ?? 0;
        }

        if (describedResultSet is not null && describedResultSet.Columns.Count > 0)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.RowDescription, PostgreSqlResponseSerializer.CreateRowDescription(describedResultSet, portal?.ResultFormatCodes), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.NoData, PostgreSqlResponseSerializer.CreateNoData(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static PostgreSqlQueryParameter[] DecodeParameters(PostgreSqlPortal portal)
    {
        var parameters = new PostgreSqlQueryParameter[portal.Parameters.Count];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = portal.Parameters[i];
            var typeOid = parameter.TypeOid == 0 ? PostgreSqlTypeMapper.TextOid : parameter.TypeOid;
            var decodedValue = PostgreSqlValueConverter.DecodeParameterValue(typeOid, parameter.FormatCode, parameter.RawValue);
            parameters[i] = new PostgreSqlQueryParameter
            {
                Name = $"${i + 1}",
                Type = PostgreSqlTypeMapper.GetColumnType(typeOid),
                Value = decodedValue ?? DBNull.Value,
                TypeOid = typeOid,
                FormatCode = parameter.FormatCode,
                RawValue = parameter.RawValue,
            };
        }

        return parameters;
    }

    private async ValueTask HandleExecuteMessageAsync(PostgreSqlMessageWriter writer, PostgreSqlSessionState session, PostgreSqlFrontendMessage message, CancellationToken cancellationToken)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        var portalName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (index + 4 > payload.Length)
        {
            throw new InvalidDataException("Invalid Execute message.");
        }

        var maxRows = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(index, 4));
        if (maxRows < 0)
        {
            throw new InvalidDataException("Invalid Execute row limit.");
        }

        if (!session.Portals.TryGetValue(portalName, out var portal))
        {
            throw new InvalidDataException($"Unknown portal '{portalName}'.");
        }

        if (string.IsNullOrWhiteSpace(portal.Statement.Query))
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.EmptyQueryResponse, PostgreSqlResponseSerializer.CreateEmptyQueryResponse(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = new PostgreSqlQueryContext
        {
            RemoteEndPoint = session.RemoteEndPoint,
            StartupParameters = session.StartupParameters,
            RequestType = PostgreSqlQueryRequestType.ExtendedQuery,
            CommandText = portal.Statement.Query,
            StatementName = portal.Statement.Name,
            PortalName = portal.Name,
            Parameters = DecodeParameters(portal),
        };

        var result = await ExecuteQueryHandlerAsync(context, session.BackendSession, cancellationToken).ConfigureAwait(false);

        // Describe already told the client the shape. If execution disagrees, the DataRows would desynchronise
        // the connection, so this is reported as an error instead.
        if (result.Error is null && portal.DescribedColumnCount is { } describedColumnCount)
        {
            var executedColumnCount = result.ResultSets.Count > 0 ? result.ResultSets[0].Columns.Count : 0;
            if (executedColumnCount != describedColumnCount)
            {
                _logger.LogError(
                    "The query handler described {DescribedColumnCount} column(s) for '{CommandText}' but returned {ExecutedColumnCount} on execution. Return the same columns for PostgreSqlQueryRequestType.Describe as for the execution.",
                    describedColumnCount,
                    portal.Statement.Query,
                    executedColumnCount);
                result = PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
                {
                    Code = PostgreSqlConstants.SqlStates.InternalError,
                    Message = $"The query handler described {describedColumnCount} column(s) but returned {executedColumnCount} on execution.",
                });
            }
        }

        UpdateTransactionStatus(session, result);

        // Execute never emits RowDescription; the client learns the shape from Describe.
        await WriteQueryResultAsync(writer, result, session, includeReadyForQuery: false, includeRowDescription: false, maxRows, portal.ResultFormatCodes, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleSimpleQueryAsync(PostgreSqlMessageWriter writer, PostgreSqlSessionState session, PostgreSqlFrontendMessage message, CancellationToken cancellationToken)
    {
        var sqlText = DecodeNullTerminatedString(message.Payload);
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.EmptyQueryResponse, PostgreSqlResponseSerializer.CreateEmptyQueryResponse(), cancellationToken).ConfigureAwait(false);
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(session.TransactionStatus), cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = new PostgreSqlQueryContext
        {
            RemoteEndPoint = session.RemoteEndPoint,
            StartupParameters = session.StartupParameters,
            RequestType = PostgreSqlQueryRequestType.SimpleQuery,
            CommandText = sqlText,
        };

        var result = await ExecuteQueryHandlerAsync(context, session.BackendSession, cancellationToken).ConfigureAwait(false);
        UpdateTransactionStatus(session, result);
        await WriteQueryResultAsync(writer, result, session, includeReadyForQuery: true, includeRowDescription: true, maxRows: 0, resultFormatCodes: null, cancellationToken).ConfigureAwait(false);
    }

    private static void UpdateTransactionStatus(PostgreSqlSessionState session, PostgreSqlQueryResult result)
    {
        session.TransactionStatus = result.TransactionStatus switch
        {
            PostgreSqlTransactionStatus.InTransaction => PostgreSqlConstants.TransactionStatus.InTransaction,
            PostgreSqlTransactionStatus.Failed => PostgreSqlConstants.TransactionStatus.Failed,
            _ => PostgreSqlConstants.TransactionStatus.Idle,
        };

        if (result.Error is not null && session.TransactionStatus == PostgreSqlConstants.TransactionStatus.InTransaction)
        {
            session.TransactionStatus = PostgreSqlConstants.TransactionStatus.Failed;
        }
    }

    private async ValueTask<PostgreSqlQueryResult> ExecuteQueryHandlerAsync(PostgreSqlQueryContext context, PostgreSqlBackendSession? backendSession, CancellationToken cancellationToken)
    {
        var commandCancellationTokenSource = backendSession?.BeginCommand(cancellationToken);
        CancellationTokenSource? linkedTokenSource = null;
        try
        {
            if (commandCancellationTokenSource is null)
            {
                linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                commandCancellationTokenSource = linkedTokenSource;
            }

            return await _queryHandler(context, commandCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The connection itself is going away; this is not a query error.
            throw;
        }
        catch (OperationCanceledException) when (commandCancellationTokenSource is not null && commandCancellationTokenSource.IsCancellationRequested)
        {
            return PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
            {
                Code = PostgreSqlConstants.SqlStates.QueryCanceled,
                Message = "canceling statement due to user request",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in PostgreSQL query handler for {RemoteEndPoint}", context.RemoteEndPoint);
            return PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
            {
                Code = PostgreSqlConstants.SqlStates.InternalError,
                Message = "Unhandled query handler exception",
            });
        }
        finally
        {
            if (backendSession is not null && commandCancellationTokenSource is not null)
            {
                backendSession.EndCommand(commandCancellationTokenSource);
            }

            linkedTokenSource?.Dispose();
        }
    }

    private static async ValueTask WriteQueryResultAsync(
        PostgreSqlMessageWriter writer,
        PostgreSqlQueryResult result,
        PostgreSqlSessionState session,
        bool includeReadyForQuery,
        bool includeRowDescription,
        int maxRows,
        IReadOnlyList<int>? resultFormatCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Error is not null)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ErrorResponse, PostgreSqlResponseSerializer.CreateErrorResponse(result.Error), cancellationToken).ConfigureAwait(false);
            if (includeReadyForQuery)
            {
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(session.TransactionStatus), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                session.InErrorState = true;
            }

            return;
        }

        foreach (var notice in result.Notices)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.NoticeResponse, PostgreSqlResponseSerializer.CreateNoticeResponse(notice), cancellationToken).ConfigureAwait(false);
        }

        if (result.ResultSets.Count == 0)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.CommandComplete, PostgreSqlResponseSerializer.CreateCommandComplete(result.CommandTag, "OK", result.AffectedRowCount), cancellationToken).ConfigureAwait(false);
            if (includeReadyForQuery)
            {
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(session.TransactionStatus), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var resultSet in result.ResultSets)
        {
            if (includeRowDescription)
            {
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.RowDescription, PostgreSqlResponseSerializer.CreateRowDescription(resultSet, resultFormatCodes), cancellationToken).ConfigureAwait(false);
            }

            var rowCount = resultSet.Rows.Count;
            var suspended = maxRows > 0 && rowCount > maxRows;
            var rowsToWrite = suspended ? maxRows : rowCount;
            for (var i = 0; i < rowsToWrite; i++)
            {
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.DataRow, PostgreSqlResponseSerializer.CreateDataRow(resultSet, resultSet.Rows[i], resultFormatCodes), cancellationToken).ConfigureAwait(false);
            }

            if (suspended)
            {
                await writer.WriteMessageAsync(PostgreSqlConstants.Backend.PortalSuspended, PostgreSqlResponseSerializer.CreatePortalSuspended(), cancellationToken).ConfigureAwait(false);
                return;
            }

            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.CommandComplete, PostgreSqlResponseSerializer.CreateCommandComplete(result.CommandTag, "SELECT", result.AffectedRowCount ?? rowsToWrite), cancellationToken).ConfigureAwait(false);
        }

        if (includeReadyForQuery)
        {
            await writer.WriteMessageAsync(PostgreSqlConstants.Backend.ReadyForQuery, PostgreSqlResponseSerializer.CreateReadyForQuery(session.TransactionStatus), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void HandleCloseMessage(PostgreSqlSessionState session, PostgreSqlFrontendMessage message)
    {
        var payload = message.Payload.AsSpan();
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("Invalid Close message.");
        }

        var index = 0;
        var closeType = payload[index++];
        var name = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (closeType == PostgreSqlConstants.DescribeTarget.Statement)
        {
            _ = session.PreparedStatements.Remove(name);
        }
        else if (closeType == PostgreSqlConstants.DescribeTarget.Portal)
        {
            _ = session.Portals.Remove(name);
        }
        else
        {
            throw new InvalidDataException($"Invalid Close target '{(char)closeType}'.");
        }
    }

    private static List<int> ReadFormatCodes(ReadOnlySpan<byte> payload, ref int index)
    {
        if (index + 2 > payload.Length)
        {
            throw new InvalidDataException("Invalid format code segment.");
        }

        var count = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(index, 2));
        index += 2;
        if (count < 0)
        {
            throw new InvalidDataException("Invalid format code count.");
        }

        var result = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (index + 2 > payload.Length)
            {
                throw new InvalidDataException("Invalid format code value.");
            }

            var formatCode = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(index, 2));
            if (formatCode is not 0 and not 1)
            {
                throw new InvalidDataException($"Invalid format code '{formatCode}'.");
            }

            result.Add(formatCode);
            index += 2;
        }

        return result;
    }

    private static int ResolveFormatCode(List<int> formatCodes, int parameterIndex)
    {
        if (formatCodes.Count == 0)
        {
            return 0;
        }

        if (formatCodes.Count == 1)
        {
            return formatCodes[0];
        }

        return formatCodes[parameterIndex];
    }


    private static async Task<SslStream> UpgradeToTlsAsync(Stream input, Stream output, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(certificate);

        var sslStream = new SslStream(new DuplexStream(input, output), leaveInnerStreamOpen: true);
        try
        {
            await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed handshake is remotely triggerable, so the native TLS context must not be left to the finalizer.
            await sslStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return sslStream;
    }

}
