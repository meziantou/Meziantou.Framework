using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Buffers;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Protocol;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds;

internal sealed class TdsConnectionProcessor
{
    private readonly TdsServerOptions _options;
    private readonly TdsAuthenticationDelegate _authenticationHandler;
    private readonly TdsQueryDelegate _queryHandler;
    private readonly ILogger _logger;

    public TdsConnectionProcessor(
        TdsServerOptions options,
        TdsAuthenticationDelegate authenticationHandler,
        TdsQueryDelegate queryHandler,
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

        var transportInput = input;
        var transportOutput = output;
        var writer = new TdsPacketWriter(output, _options.PacketSize);
        SslStream? sslStream = null;
        Task<TdsPacket?>? pendingRead = null;
        var usingTls = false;
        TdsTransactionManager? transactions = null;
        TdsPreLoginNegotiationResult? negotiationResult;
        try
        {
            var preLoginPacket = await TdsPacketReader.ReadAsync(input, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
            if (preLoginPacket is null)
            {
                return;
            }

            if (preLoginPacket.Type != TdsPacketType.PreLogin)
            {
                _logger.LogDebug("Unexpected first TDS packet type {PacketType}", preLoginPacket.Type);
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(18456, "Invalid pre-login sequence"), cancellationToken).ConfigureAwait(false);
                return;
            }

            TdsPreLoginEncryptionMode clientEncryptionMode;
            try
            {
                clientEncryptionMode = TdsPreLoginMessage.ParseEncryptionMode(preLoginPacket.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse PRELOGIN payload");
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(18456, "Invalid pre-login packet"), cancellationToken).ConfigureAwait(false);
                return;
            }

            var serverCertificate = _options.GetTlsCertificate();
            negotiationResult = TdsPreLoginEncryptionNegotiator.Negotiate(
                clientEncryptionMode,
                serverSupportsEncryption: serverCertificate is not null,
                serverRequiresEncryption: _options.RequireEncryption);

            await writer.WriteAsync(TdsPacketType.PreLogin, TdsPreLoginMessage.CreateResponse(preLoginPacket.Payload, negotiationResult.Value.ResponseEncryptionMode), cancellationToken).ConfigureAwait(false);
            if (negotiationResult.Value.RejectConnection)
            {
                _logger.LogDebug("PRELOGIN encryption negotiation failed. ClientMode={ClientMode}, ServerResponse={ServerResponse}", clientEncryptionMode, negotiationResult.Value.ResponseEncryptionMode);
                return;
            }

            if (negotiationResult.Value.UpgradeToTls)
            {
                sslStream = await UpgradeToTlsAsync(transportInput, transportOutput, serverCertificate!, _options.PacketSize, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
                usingTls = true;
                input = sslStream;
                output = sslStream;
                writer = new TdsPacketWriter(output, _options.PacketSize);
            }

            var loginPacket = await TdsPacketReader.ReadAsync(input, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
            if (loginPacket is null || loginPacket.Type != TdsPacketType.Login7)
            {
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(18456, "Missing LOGIN7 packet"), cancellationToken).ConfigureAwait(false);
                return;
            }

            TdsLoginRequest login;
            try
            {
                login = TdsLoginParser.Parse(loginPacket.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse LOGIN7 payload");
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(18456, "Invalid login packet"), cancellationToken).ConfigureAwait(false);
                return;
            }

            var authenticationContext = new TdsAuthenticationContext
            {
                RemoteEndPoint = remoteEndPoint,
                UserName = login.UserName,
                Password = login.Password,
                AuthenticationToken = login.AuthenticationToken,
                Database = login.Database,
                ApplicationName = login.ApplicationName,
            };

            var authenticationResult = await _authenticationHandler(authenticationContext, cancellationToken).ConfigureAwait(false);
            if (!authenticationResult.IsAuthenticated)
            {
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateLoginError(authenticationResult), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (negotiationResult.Value.DowngradeAfterLogin && usingTls)
            {
                // The client asked for encryption of the login packet only, so the rest of the session goes back
                // to the raw transport. Keep the SslStream reference so the finally block still disposes it.
                usingTls = false;
                input = transportInput;
                output = transportOutput;
                writer = new TdsPacketWriter(output, _options.PacketSize);
            }

            await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateLoginSuccess(authenticationResult), cancellationToken).ConfigureAwait(false);

            transactions = new TdsTransactionManager(_options.TransactionHandler, remoteEndPoint, authenticationResult.UserContext);

            TdsPacket? bufferedPacket = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                TdsPacket? packet;
                if (bufferedPacket is not null)
                {
                    packet = bufferedPacket;
                    bufferedPacket = null;
                }
                else if (pendingRead is not null)
                {
                    var completedRead = pendingRead;
                    pendingRead = null;
                    packet = await completedRead.ConfigureAwait(false);
                }
                else
                {
                    packet = await TdsPacketReader.ReadAsync(input, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
                }

                if (packet is null)
                {
                    return;
                }

                if (packet.Type == TdsPacketType.Attention)
                {
                    await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateAttentionResponse(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (packet.Type == TdsPacketType.TransactionManager)
                {
                    await writer.WriteAsync(TdsPacketType.TabularResult, await ProcessTransactionRequestAsync(transactions, packet, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (packet.Type != TdsPacketType.SqlBatch && packet.Type != TdsPacketType.Rpc)
                {
                    await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(50001, $"Unsupported packet type: {packet.Type}"), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // The request runs while the connection keeps reading, so an ATTENTION packet sent by the client
                // in the middle of a long query is seen instead of waiting behind it. AbandonRequest takes over
                // the ownership of the cancellation token source, hence the null assignments.
                var requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    var executionTask = ExecuteRequestAsync(packet, remoteEndPoint, authenticationResult.UserContext, transactions.TransactionId, writer.PayloadSizePerPacket, requestCancellationTokenSource.Token);
                    var attentionReceived = false;
                    while (!executionTask.IsCompleted && !attentionReceived && bufferedPacket is null)
                    {
                        pendingRead ??= TdsPacketReader.ReadAsync(input, _options.MaxMessageSize, cancellationToken).AsTask();
                        if (await Task.WhenAny(executionTask, pendingRead).ConfigureAwait(false) != pendingRead)
                        {
                            break;
                        }

                        var concurrentRead = pendingRead;
                        pendingRead = null;

                        TdsPacket? incomingPacket;
                        try
                        {
                            incomingPacket = await concurrentRead.ConfigureAwait(false);
                        }
                        catch
                        {
                            AbandonRequest(requestCancellationTokenSource, executionTask);
                            requestCancellationTokenSource = null;
                            throw;
                        }

                        if (incomingPacket is null)
                        {
                            // The client is gone, so there is nobody left to send the response to.
                            AbandonRequest(requestCancellationTokenSource, executionTask);
                            requestCancellationTokenSource = null;
                            return;
                        }

                        if (incomingPacket.Type == TdsPacketType.Attention)
                        {
                            attentionReceived = true;
                        }
                        else
                        {
                            // A client is not supposed to send another request before the current one completes,
                            // but keep the packet so it is served after the response instead of being dropped.
                            bufferedPacket = incomingPacket;
                        }
                    }

                    if (attentionReceived)
                    {
                        // MS-TDS requires the attention to be acknowledged with a DONE token carrying DONE_ATTN.
                        // The handler is signalled and then abandoned: a handler that ignores its cancellation
                        // token must not keep the connection from acknowledging the attention and from serving
                        // the next request.
                        AbandonRequest(requestCancellationTokenSource, executionTask);
                        requestCancellationTokenSource = null;
                        await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateAttentionResponse(), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var responsePayload = await executionTask.ConfigureAwait(false);
                    await writer.WriteAsync(TdsPacketType.TabularResult, responsePayload, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    requestCancellationTokenSource?.Dispose();
                }
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogDebug(ex, "TLS authentication failed");
        }
        finally
        {
            ObserveExceptions(pendingRead);
            await AbortTransactionAsync(transactions).ConfigureAwait(false);
            if (sslStream is not null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<byte[]> ProcessTransactionRequestAsync(TdsTransactionManager transactions, TdsPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            return await transactions.ProcessAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Unhandled exception in transaction handler");
            return TdsResponseSerializer.CreateTransactionResponse(endedTransactionId: null, committed: false, begunTransactionId: null, new TdsQueryError
            {
                Number = 50002,
                Message = "Unhandled transaction handler exception",
            });
        }
    }

    private async ValueTask AbortTransactionAsync(TdsTransactionManager? transactions)
    {
        if (transactions is null)
            return;

        try
        {
            await transactions.AbortAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in transaction handler while rolling back the transaction of a closed connection");
        }
    }

    private async Task<byte[]> ExecuteRequestAsync(TdsPacket packet, EndPoint remoteEndPoint, ClaimsPrincipal? userContext, long? transactionId, int payloadSizePerPacket, CancellationToken cancellationToken)
    {
        var isRpc = packet.Type == TdsPacketType.Rpc;
        try
        {
            var queryContext = TdsQueryRequestParser.Parse(packet, remoteEndPoint, userContext, transactionId);
            TdsQueryResult queryResult;
            try
            {
                queryResult = TryGetTransactionLevelQueryResult(queryContext)
                    ?? await _queryHandler(queryContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in query handler");
                queryResult = TdsQueryResult.FromError(new TdsQueryError
                {
                    Number = 50002,
                    State = 1,
                    Class = 16,
                    Message = "Unhandled query handler exception",
                });
            }

            return TdsResponseSerializer.CreateQueryResponse(queryResult, payloadSizePerPacket, isRpc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Request parsing and response serialization run on caller-supplied data, so a bad request
            // or a value that does not match its declared column type must not drop the connection.
            _logger.LogError(ex, "Failed to build the TDS response");
            return TdsResponseSerializer.CreateQueryResponse(
                TdsQueryResult.FromError(new TdsQueryError
                {
                    Number = 50005,
                    State = 1,
                    Class = 16,
                    Message = "Failed to build the query response",
                }),
                payloadSizePerPacket);
        }
    }

    /// <summary>
    /// SqlClient reads <c>@@TRANCOUNT</c> with <c>set @out = @@trancount</c> before it creates a savepoint or rolls
    /// back, and treats the transaction as gone when the value is not positive. The server answers it, since it is
    /// the one tracking the transaction.
    /// </summary>
    private static TdsQueryResult? TryGetTransactionLevelQueryResult(TdsQueryContext context)
    {
        if (context.RequestType is not TdsQueryRequestType.Rpc
            || !string.Equals(context.ProcedureName, "sp_executesql", StringComparison.OrdinalIgnoreCase)
            || context.Parameters is not [var statement, _, { IsOutput: true } output]
            || !string.Equals(statement.AsString()?.Trim(), $"set {output.Name} = @@trancount", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var result = new TdsQueryResult();
        result.OutputParameters.Add(new TdsOutputParameter(output.Name, TdsColumnType.Int32, context.TransactionId is null ? 0 : 1));
        return result;
    }

    /// <summary>
    /// Signals the running request and stops waiting for it. The cancellation token source is disposed once the
    /// request actually completes so a handler still holding the token cannot observe a disposed source.
    /// </summary>
    private static void AbandonRequest(CancellationTokenSource requestCancellationTokenSource, Task task)
    {
        requestCancellationTokenSource.Cancel();
        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                _ = completedTask.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            requestCancellationTokenSource,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveExceptions(Task? task)
    {
        if (task is null)
            return;

        _ = task.ContinueWith(static completedTask => _ = completedTask.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    [SuppressMessage("Security", "CA5398:Do not hardcode SslProtocols", Justification = "SqlClient interoperability with TDS-over-TLS requires TLS 1.2 during PRELOGIN encryption upgrade.")]
    private static async Task<SslStream> UpgradeToTlsAsync(Stream input, Stream output, X509Certificate2 certificate, int packetSize, int maxMessageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(certificate);

        var baseStream = new TdsTlsPacketStream(input, output, packetSize, maxMessageSize);
        var sslStream = new SslStream(baseStream, leaveInnerStreamOpen: true);

        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        }, cancellationToken).ConfigureAwait(false);

        baseStream.SwitchToRawMode();
        return sslStream;
    }

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Transport streams are owned and disposed by the caller.")]
    private sealed class TdsTlsPacketStream : Stream
    {
        private readonly Stream _readStream;
        private readonly Stream _writeStream;
        private readonly TdsPacketWriter _packetWriter;
        private readonly int _maxMessageSize;
        private ReadOnlyMemory<byte> _pendingReadPayload;
        private bool _useTdsPacketMode = true;

        public TdsTlsPacketStream(Stream readStream, Stream writeStream, int packetSize, int maxMessageSize)
        {
            ArgumentNullException.ThrowIfNull(readStream);
            ArgumentNullException.ThrowIfNull(writeStream);

            _readStream = readStream;
            _writeStream = writeStream;
            _packetWriter = new TdsPacketWriter(writeStream, packetSize);
            _maxMessageSize = maxMessageSize;
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

        public override int ReadTimeout
        {
            get => _readStream.ReadTimeout;
            set => _readStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => _writeStream.WriteTimeout;
            set => _writeStream.WriteTimeout = value;
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
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var readLength = ReadCoreAsync(rentedBuffer.AsMemory(0, buffer.Length), cancellationToken: default).AsTask().GetAwaiter().GetResult();
                rentedBuffer.AsSpan(0, readLength).CopyTo(buffer);
                return readLength;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ReadCoreAsync(buffer, cancellationToken);
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
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteAsync(buffer.ToArray(), cancellationToken: default).AsTask().GetAwaiter().GetResult();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_useTdsPacketMode)
            {
                return _packetWriter.WriteAsync(TdsPacketType.PreLogin, buffer, cancellationToken);
            }

            return _writeStream.WriteAsync(buffer, cancellationToken);
        }

        private async ValueTask<int> ReadCoreAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (!_useTdsPacketMode)
            {
                if (!_pendingReadPayload.IsEmpty)
                {
                    var bufferedBytesToCopy = Math.Min(destination.Length, _pendingReadPayload.Length);
                    _pendingReadPayload[..bufferedBytesToCopy].CopyTo(destination);
                    _pendingReadPayload = _pendingReadPayload[bufferedBytesToCopy..];
                    return bufferedBytesToCopy;
                }

                return await _readStream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            if (_pendingReadPayload.IsEmpty)
            {
                while (true)
                {
                    var packet = await TdsPacketReader.ReadAsync(_readStream, _maxMessageSize, cancellationToken).ConfigureAwait(false);
                    if (packet is null)
                    {
                        return 0;
                    }

                    _pendingReadPayload = packet.Payload;
                    if (!_pendingReadPayload.IsEmpty)
                    {
                        break;
                    }
                }
            }

            var bytesToCopy = Math.Min(destination.Length, _pendingReadPayload.Length);
            _pendingReadPayload[..bytesToCopy].CopyTo(destination);
            _pendingReadPayload = _pendingReadPayload[bytesToCopy..];
            return bytesToCopy;
        }

        public void SwitchToRawMode()
        {
            _useTdsPacketMode = false;
        }
    }
}
