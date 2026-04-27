using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
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
        TdsPreLoginNegotiationResult? negotiationResult = null;
        try
        {
            var preLoginPacket = await TdsPacketReader.ReadAsync(input, cancellationToken).ConfigureAwait(false);
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
                sslStream = await UpgradeToTlsAsync(transportInput, transportOutput, serverCertificate!, _options.PacketSize, cancellationToken).ConfigureAwait(false);
                input = sslStream;
                output = sslStream;
                writer = new TdsPacketWriter(output, _options.PacketSize);
            }

            var loginPacket = await TdsPacketReader.ReadAsync(input, cancellationToken).ConfigureAwait(false);
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

            if (negotiationResult.Value.DowngradeAfterLogin && sslStream is not null)
            {
                sslStream = null;
                input = transportInput;
                output = transportOutput;
                writer = new TdsPacketWriter(output, _options.PacketSize);
            }

            await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateLoginSuccess(authenticationResult), cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await TdsPacketReader.ReadAsync(input, cancellationToken).ConfigureAwait(false);
                if (packet is null)
                {
                    return;
                }

                if (packet.Type == TdsPacketType.Attention)
                {
                    await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateAttentionResponse(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (packet.Type != TdsPacketType.SqlBatch && packet.Type != TdsPacketType.Rpc)
                {
                    await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateProtocolError(50001, $"Unsupported packet type: {packet.Type}"), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var queryContext = TdsQueryRequestParser.Parse(packet, remoteEndPoint);
                TdsQueryResult queryResult;
                try
                {
                    queryResult = await _queryHandler(queryContext, cancellationToken).ConfigureAwait(false);
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

                await writer.WriteAsync(TdsPacketType.TabularResult, TdsResponseSerializer.CreateQueryResponse(queryResult), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogDebug(ex, "TLS authentication failed");
        }
        finally
        {
            if (sslStream is not null)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage("Security", "CA5398:Do not hardcode SslProtocols", Justification = "SqlClient interoperability with TDS-over-TLS requires TLS 1.2 during PRELOGIN encryption upgrade.")]
    private static async Task<SslStream> UpgradeToTlsAsync(Stream input, Stream output, X509Certificate2 certificate, int packetSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(certificate);

        var baseStream = new TdsTlsPacketStream(input, output, packetSize);
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
        private ReadOnlyMemory<byte> _pendingReadPayload;
        private bool _useTdsPacketMode = true;

        public TdsTlsPacketStream(Stream readStream, Stream writeStream, int packetSize)
        {
            ArgumentNullException.ThrowIfNull(readStream);
            ArgumentNullException.ThrowIfNull(writeStream);

            _readStream = readStream;
            _writeStream = writeStream;
            _packetWriter = new TdsPacketWriter(writeStream, packetSize);
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
                    var packet = await TdsPacketReader.ReadAsync(_readStream, cancellationToken).ConfigureAwait(false);
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
