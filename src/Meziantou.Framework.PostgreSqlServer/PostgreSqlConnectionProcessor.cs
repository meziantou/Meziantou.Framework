using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Diagnostics.CodeAnalysis;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
        PostgreSqlBackendSession? backendSession = null;

        try
        {
            var startupPacket = await PostgreSqlMessageReader.ReadStartupPacketAsync(input, cancellationToken).ConfigureAwait(false);
            if (startupPacket is null)
            {
                return;
            }

            while (startupPacket.RequestCode == PostgreSqlConstants.SslRequestCode)
            {
                var serverCertificate = _options.GetTlsCertificate();
                var canUpgradeToTls = serverCertificate is not null;
                await writer.WriteSslResponseAsync(canUpgradeToTls, cancellationToken).ConfigureAwait(false);
                if (!canUpgradeToTls)
                {
                    startupPacket = await PostgreSqlMessageReader.ReadStartupPacketAsync(input, cancellationToken).ConfigureAwait(false);
                    if (startupPacket is null)
                    {
                        return;
                    }

                    continue;
                }

                sslStream = await UpgradeToTlsAsync(input, output, serverCertificate!, cancellationToken).ConfigureAwait(false);
                input = sslStream;
                output = sslStream;
                writer = new PostgreSqlMessageWriter(output);
                startupPacket = await PostgreSqlMessageReader.ReadStartupPacketAsync(input, cancellationToken).ConfigureAwait(false);
                if (startupPacket is null)
                {
                    return;
                }
            }

            if (startupPacket.RequestCode == PostgreSqlConstants.CancelRequestCode)
            {
                HandleCancelRequest(startupPacket.Payload);
                return;
            }

            if (startupPacket.RequestCode != PostgreSqlConstants.ProtocolVersion3)
            {
                await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", "08P01", "Unsupported protocol version"), cancellationToken).ConfigureAwait(false);
                return;
            }

            startupParameters = PostgreSqlMessageReader.ParseStartupParameters(startupPacket.Payload);
            if (_options.RequireEncryption && sslStream is null)
            {
                await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", "28000", "TLS is required"), cancellationToken).ConfigureAwait(false);
                return;
            }

            var userName = startupParameters.TryGetValue("user", out var startupUserName) ? startupUserName : null;
            var database = startupParameters.TryGetValue("database", out var startupDatabase) ? startupDatabase : null;
            if (!await AuthenticateAsync(input, writer, remoteEndPoint, startupParameters, userName, database, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            (processId, secretKey, backendSession) = _options.RegisterBackendSession();
            await WriteSessionInitializedMessagesAsync(writer, processId, secretKey, cancellationToken).ConfigureAwait(false);

            var preparedStatements = new Dictionary<string, PostgreSqlStatement>(StringComparer.Ordinal);
            var portals = new Dictionary<string, PostgreSqlPortal>(StringComparer.Ordinal);
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await PostgreSqlMessageReader.ReadMessageAsync(input, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                switch (message.Type)
                {
                    case (byte)'Q':
                        await HandleSimpleQueryAsync(writer, remoteEndPoint, startupParameters, message, backendSession, cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'P':
                        HandleParseMessage(preparedStatements, message);
                        await writer.WriteMessageAsync((byte)'1', PostgreSqlResponseSerializer.CreateParseComplete(), cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'B':
                        HandleBindMessage(preparedStatements, portals, message);
                        await writer.WriteMessageAsync((byte)'2', PostgreSqlResponseSerializer.CreateBindComplete(), cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'D':
                        await HandleDescribeMessageAsync(writer, preparedStatements, portals, message, cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'E':
                        await HandleExecuteMessageAsync(writer, portals, remoteEndPoint, startupParameters, message, backendSession, cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'C':
                        HandleCloseMessage(preparedStatements, portals, message);
                        await writer.WriteMessageAsync((byte)'3', PostgreSqlResponseSerializer.CreateCloseComplete(), cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'S':
                        await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
                        break;
                    case (byte)'H':
                        break;
                    case (byte)'X':
                        return;
                    default:
                        await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("ERROR", "08P01", $"Unsupported frontend message '{(char)message.Type}'"), cancellationToken).ConfigureAwait(false);
                        await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogDebug(ex, "TLS authentication failed");
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

    private async ValueTask<bool> AuthenticateAsync(
        Stream input,
        PostgreSqlMessageWriter writer,
        EndPoint remoteEndPoint,
        IReadOnlyDictionary<string, string> startupParameters,
        string? userName,
        string? database,
        CancellationToken cancellationToken)
    {
        var context = new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = remoteEndPoint,
            Method = _options.AuthenticationMethod,
            UserName = userName,
            Database = database,
            StartupParameters = startupParameters,
        };

        context = _options.AuthenticationMethod switch
        {
            PostgreSqlAuthenticationMethod.ClearTextPassword => await HandleClearTextAuthenticationAsync(input, writer, context, cancellationToken).ConfigureAwait(false),
            PostgreSqlAuthenticationMethod.Md5Password => await HandleMd5AuthenticationAsync(input, writer, context, cancellationToken).ConfigureAwait(false),
            PostgreSqlAuthenticationMethod.ScramSha256 => await HandleScramAuthenticationAsync(input, writer, context, cancellationToken).ConfigureAwait(false),
            _ => context,
        };

        var result = await _authenticationHandler(context, cancellationToken).ConfigureAwait(false);
        if (!result.IsAuthenticated)
        {
            await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", result.ErrorCode, result.ErrorMessage ?? "Authentication failed"), cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (_options.AuthenticationMethod == PostgreSqlAuthenticationMethod.ScramSha256)
        {
            if (!context.TryGetScramServerFinalMessage(out var serverFinalMessage))
            {
                await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("FATAL", "28P01", "SCRAM validation did not provide server signature"), cancellationToken).ConfigureAwait(false);
                return false;
            }

            await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationSaslFinal(serverFinalMessage), cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationOk(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async ValueTask<PostgreSqlAuthenticationContext> HandleClearTextAuthenticationAsync(
        Stream input,
        PostgreSqlMessageWriter writer,
        PostgreSqlAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationClearTextPassword(), cancellationToken).ConfigureAwait(false);
        var passwordMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        return new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = context.RemoteEndPoint,
            Method = context.Method,
            UserName = context.UserName,
            Database = context.Database,
            StartupParameters = context.StartupParameters,
            Password = DecodeNullTerminatedString(passwordMessage.Payload),
        };
    }

    private static async ValueTask<PostgreSqlAuthenticationContext> HandleMd5AuthenticationAsync(
        Stream input,
        PostgreSqlMessageWriter writer,
        PostgreSqlAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var salt = new byte[4];
        RandomNumberGenerator.Fill(salt);
        await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationMd5Password(salt), cancellationToken).ConfigureAwait(false);

        var passwordMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        return new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = context.RemoteEndPoint,
            Method = context.Method,
            UserName = context.UserName,
            Database = context.Database,
            StartupParameters = context.StartupParameters,
            Md5Salt = salt,
            Md5PasswordResponse = DecodeNullTerminatedString(passwordMessage.Payload),
        };
    }

    private static async ValueTask<PostgreSqlAuthenticationContext> HandleScramAuthenticationAsync(
        Stream input,
        PostgreSqlMessageWriter writer,
        PostgreSqlAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationSasl(["SCRAM-SHA-256"]), cancellationToken).ConfigureAwait(false);
        var initialMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        var (mechanism, initialResponse) = ParseSaslInitialResponse(initialMessage.Payload);
        if (!string.Equals(mechanism, "SCRAM-SHA-256", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported SASL mechanism '{mechanism}'.");
        }

        var clientFirstMessage = Encoding.UTF8.GetString(initialResponse);
        if (!PostgreSqlScramHelper.TryParseClientFirstMessage(clientFirstMessage, out var clientFirstMessageBare, out var clientNonce))
        {
            throw new InvalidDataException("Invalid SCRAM client-first message.");
        }

        var serverNonce = PostgreSqlScramHelper.GenerateNonce();
        var fullNonce = clientNonce + serverNonce;
        var salt = PostgreSqlScramHelper.CreateSalt();
        const int IterationCount = 4096;
        var serverFirstMessage = PostgreSqlScramHelper.BuildServerFirstMessage(fullNonce, salt, IterationCount);
        await writer.WriteMessageAsync((byte)'R', PostgreSqlResponseSerializer.CreateAuthenticationSaslContinue(serverFirstMessage), cancellationToken).ConfigureAwait(false);

        var finalMessage = await ReadRequiredPasswordMessageAsync(input, cancellationToken).ConfigureAwait(false);
        var clientFinalMessage = Encoding.UTF8.GetString(finalMessage.Payload);
        if (!PostgreSqlScramHelper.TryParseClientFinalMessage(clientFinalMessage, out var clientFinalWithoutProof, out var clientProof, out var clientFinalNonce))
        {
            throw new InvalidDataException("Invalid SCRAM client-final message.");
        }

        if (!string.Equals(fullNonce, clientFinalNonce, StringComparison.Ordinal))
        {
            throw new InvalidDataException("SCRAM nonce mismatch.");
        }

        var authMessage = $"{clientFirstMessageBare},{serverFirstMessage},{clientFinalWithoutProof}";
        return new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = context.RemoteEndPoint,
            Method = context.Method,
            UserName = context.UserName,
            Database = context.Database,
            StartupParameters = context.StartupParameters,
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

    private static async ValueTask<PostgreSqlFrontendMessage> ReadRequiredPasswordMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var message = await PostgreSqlMessageReader.ReadMessageAsync(input, cancellationToken).ConfigureAwait(false);
        if (message is null || message.Type != (byte)'p')
        {
            throw new InvalidDataException("Expected password message.");
        }

        return message;
    }

    private async ValueTask WriteSessionInitializedMessagesAsync(PostgreSqlMessageWriter writer, int processId, int secretKey, CancellationToken cancellationToken)
    {
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("server_version", _options.ServerVersion), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("server_encoding", "UTF8"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("client_encoding", "UTF8"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("DateStyle", "ISO, MDY"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("integer_datetimes", "on"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'S', PostgreSqlResponseSerializer.CreateParameterStatus("standard_conforming_strings", "on"), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'K', PostgreSqlResponseSerializer.CreateBackendKeyData(processId, secretKey), cancellationToken).ConfigureAwait(false);
        await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
    }

    private void HandleCancelRequest(byte[] payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var processId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
        var secretKey = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4, 4));
        _ = _options.TryCancelBackendSession(processId, secretKey);
    }

    private static void HandleParseMessage(Dictionary<string, PostgreSqlStatement> preparedStatements, PostgreSqlFrontendMessage message)
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
        var parameterTypeOids = new List<uint>(Math.Max((int)parameterTypeCount, 0));
        for (var i = 0; i < parameterTypeCount; i++)
        {
            if (index + 4 > payload.Length)
            {
                throw new InvalidDataException("Invalid Parse message parameter type definition.");
            }

            parameterTypeOids.Add(BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(index, 4)));
            index += 4;
        }

        preparedStatements[statementName] = new PostgreSqlStatement
        {
            Name = statementName,
            Query = query,
            ParameterTypeOids = parameterTypeOids,
        };
    }

    private static void HandleBindMessage(Dictionary<string, PostgreSqlStatement> preparedStatements, Dictionary<string, PostgreSqlPortal> portals, PostgreSqlFrontendMessage message)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        var portalName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        var statementName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (!preparedStatements.TryGetValue(statementName, out var statement))
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
        var parameters = new List<PostgreSqlBoundParameter>(Math.Max((int)parameterCount, 0));
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
        portals[portalName] = new PostgreSqlPortal
        {
            Name = portalName,
            Statement = statement,
            Parameters = parameters,
            ResultFormatCodes = resultFormatCodes,
        };
    }

    private static async ValueTask HandleDescribeMessageAsync(
        PostgreSqlMessageWriter writer,
        Dictionary<string, PostgreSqlStatement> preparedStatements,
        Dictionary<string, PostgreSqlPortal> portals,
        PostgreSqlFrontendMessage message,
        CancellationToken cancellationToken)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        if (index >= payload.Length)
        {
            throw new InvalidDataException("Invalid Describe message.");
        }

        var describeType = payload[index++];
        var name = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (describeType == (byte)'S')
        {
            if (preparedStatements.TryGetValue(name, out var statement))
            {
                await writer.WriteMessageAsync((byte)'t', PostgreSqlResponseSerializer.CreateParameterDescription(statement.ParameterTypeOids), cancellationToken).ConfigureAwait(false);
                await writer.WriteMessageAsync((byte)'n', PostgreSqlResponseSerializer.CreateNoData(), cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        else if (describeType == (byte)'P' && portals.TryGetValue(name, out var portal))
        {
            if (TryInferDescribeResultSet(portal.Statement.Query, out var describedResultSet))
            {
                await writer.WriteMessageAsync((byte)'T', PostgreSqlResponseSerializer.CreateRowDescription(describedResultSet), cancellationToken).ConfigureAwait(false);
                portal.IsDescribed = true;
            }
            else
            {
                await writer.WriteMessageAsync((byte)'n', PostgreSqlResponseSerializer.CreateNoData(), cancellationToken).ConfigureAwait(false);
                portal.IsDescribed = false;
            }

            return;
        }

        await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse("ERROR", "26000", $"Unknown describe target '{name}'"), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleExecuteMessageAsync(
        PostgreSqlMessageWriter writer,
        Dictionary<string, PostgreSqlPortal> portals,
        EndPoint remoteEndPoint,
        IReadOnlyDictionary<string, string> startupParameters,
        PostgreSqlFrontendMessage message,
        PostgreSqlBackendSession? backendSession,
        CancellationToken cancellationToken)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        var portalName = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (index + 4 > payload.Length)
        {
            throw new InvalidDataException("Invalid Execute message.");
        }

        _ = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(index, 4));
        if (!portals.TryGetValue(portalName, out var portal))
        {
            throw new InvalidDataException($"Unknown portal '{portalName}'.");
        }

        var parameters = portal.Parameters
            .Select((parameter, parameterIndex) =>
            {
                var typeOid = parameter.TypeOid == 0 ? 25u : parameter.TypeOid;
                var columnType = PostgreSqlTypeMapper.GetColumnType(typeOid);
                var decodedValue = PostgreSqlValueConverter.DecodeParameterValue(typeOid, parameter.FormatCode, parameter.RawValue);
                return new PostgreSqlQueryParameter
                {
                    Name = $"${parameterIndex + 1}",
                    Type = columnType,
                    Value = decodedValue ?? DBNull.Value,
                };
            })
            .ToArray();

        var context = new PostgreSqlQueryContext
        {
            RemoteEndPoint = remoteEndPoint,
            StartupParameters = startupParameters,
            RequestType = PostgreSqlQueryRequestType.ExtendedQuery,
            CommandText = portal.Statement.Query,
            StatementName = portal.Statement.Name,
            PortalName = portal.Name,
            Parameters = parameters,
        };

        var result = await ExecuteQueryHandlerAsync(context, backendSession, cancellationToken).ConfigureAwait(false);
        await WriteQueryResultAsync(writer, result, includeReadyForQuery: false, includeRowDescription: !portal.IsDescribed, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleSimpleQueryAsync(
        PostgreSqlMessageWriter writer,
        EndPoint remoteEndPoint,
        IReadOnlyDictionary<string, string> startupParameters,
        PostgreSqlFrontendMessage message,
        PostgreSqlBackendSession? backendSession,
        CancellationToken cancellationToken)
    {
        var sqlText = DecodeNullTerminatedString(message.Payload);
        var context = new PostgreSqlQueryContext
        {
            RemoteEndPoint = remoteEndPoint,
            StartupParameters = startupParameters,
            RequestType = PostgreSqlQueryRequestType.SimpleQuery,
            CommandText = sqlText,
        };

        var result = await ExecuteQueryHandlerAsync(context, backendSession, cancellationToken).ConfigureAwait(false);
        await WriteQueryResultAsync(writer, result, includeReadyForQuery: true, includeRowDescription: true, cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (commandCancellationTokenSource is not null && commandCancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
            {
                Code = "57014",
                Message = "canceling statement due to user request",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in PostgreSQL query handler");
            return PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
            {
                Code = "XX000",
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

    private static async ValueTask WriteQueryResultAsync(PostgreSqlMessageWriter writer, PostgreSqlQueryResult result, bool includeReadyForQuery, bool includeRowDescription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Error is not null)
        {
            await writer.WriteMessageAsync((byte)'E', PostgreSqlResponseSerializer.CreateErrorResponse(result.Error), cancellationToken).ConfigureAwait(false);
            if (includeReadyForQuery)
            {
                await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var notice in result.Notices)
        {
            await writer.WriteMessageAsync((byte)'N', PostgreSqlResponseSerializer.CreateNoticeResponse(notice), cancellationToken).ConfigureAwait(false);
        }

        if (result.ResultSets.Count == 0)
        {
            var commandTag = string.IsNullOrWhiteSpace(result.CommandTag) ? "OK" : result.CommandTag;
            await writer.WriteMessageAsync((byte)'C', PostgreSqlResponseSerializer.CreateCommandComplete(commandTag, 0), cancellationToken).ConfigureAwait(false);
            if (includeReadyForQuery)
            {
                await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var resultSet in result.ResultSets)
        {
            if (includeRowDescription)
            {
                await writer.WriteMessageAsync((byte)'T', PostgreSqlResponseSerializer.CreateRowDescription(resultSet), cancellationToken).ConfigureAwait(false);
            }

            foreach (var row in resultSet.Rows)
            {
                await writer.WriteMessageAsync((byte)'D', PostgreSqlResponseSerializer.CreateDataRow(resultSet, row), cancellationToken).ConfigureAwait(false);
            }

            var commandTag = string.IsNullOrWhiteSpace(result.CommandTag) ? "SELECT" : result.CommandTag;
            await writer.WriteMessageAsync((byte)'C', PostgreSqlResponseSerializer.CreateCommandComplete(commandTag, resultSet.Rows.Count), cancellationToken).ConfigureAwait(false);
        }

        if (includeReadyForQuery)
        {
            await writer.WriteMessageAsync((byte)'Z', PostgreSqlResponseSerializer.CreateReadyForQuery(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void HandleCloseMessage(Dictionary<string, PostgreSqlStatement> preparedStatements, Dictionary<string, PostgreSqlPortal> portals, PostgreSqlFrontendMessage message)
    {
        var payload = message.Payload.AsSpan();
        var index = 0;
        if (index >= payload.Length)
        {
            throw new InvalidDataException("Invalid Close message.");
        }

        var closeType = payload[index++];
        var name = PostgreSqlMessageReader.ReadNullTerminatedString(payload, ref index);
        if (closeType == (byte)'S')
        {
            _ = preparedStatements.Remove(name);
        }
        else if (closeType == (byte)'P')
        {
            _ = portals.Remove(name);
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
        var result = new List<int>(Math.Max((int)count, 0));
        for (var i = 0; i < count; i++)
        {
            if (index + 2 > payload.Length)
            {
                throw new InvalidDataException("Invalid format code value.");
            }

            result.Add(BinaryPrimitives.ReadInt16BigEndian(payload.Slice(index, 2)));
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

        return parameterIndex < formatCodes.Count ? formatCodes[parameterIndex] : formatCodes[^1];
    }

    private static bool TryInferDescribeResultSet(string query, [NotNullWhen(true)] out PostgreSqlResultSet? resultSet)
    {
        resultSet = null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmedQuery = query.TrimStart();
        if (!trimmedQuery.StartsWith("select", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var selectClause = trimmedQuery["select".Length..].Trim();
        var fromIndex = selectClause.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
        if (fromIndex >= 0)
        {
            selectClause = selectClause[..fromIndex];
        }

        var expressions = selectClause.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (expressions.Length == 0)
        {
            return false;
        }

        resultSet = new PostgreSqlResultSet();
        for (var i = 0; i < expressions.Length; i++)
        {
            var expression = expressions[i];
            var columnName = $"column{i + 1}";
            var asIndex = expression.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asIndex >= 0 && asIndex + 4 < expression.Length)
            {
                columnName = expression[(asIndex + 4)..].Trim();
            }

            resultSet.Columns.Add(new PostgreSqlColumn(columnName, PostgreSqlColumnType.Text, isNullable: true));
        }

        return true;
    }

    private static async Task<SslStream> UpgradeToTlsAsync(Stream input, Stream output, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(certificate);

        var sslStream = new SslStream(new DuplexStream(input, output), leaveInnerStreamOpen: true);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        }, cancellationToken).ConfigureAwait(false);
        return sslStream;
    }

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Input and output streams are owned and disposed by the caller.")]
    private sealed class DuplexStream : Stream
    {
        private readonly Stream _readStream;
        private readonly Stream _writeStream;

        public DuplexStream(Stream readStream, Stream writeStream)
        {
            ArgumentNullException.ThrowIfNull(readStream);
            ArgumentNullException.ThrowIfNull(writeStream);

            _readStream = readStream;
            _writeStream = writeStream;
        }

        public override bool CanRead => _readStream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => _writeStream.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _writeStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _writeStream.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _readStream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _readStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _readStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _writeStream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _writeStream.WriteAsync(buffer, cancellationToken);
        }
    }
}
