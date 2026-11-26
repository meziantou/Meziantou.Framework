using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Meziantou.Framework.MsQuic.Tests;

public sealed class MsQuicConnectionTests
{
    [Fact]
    public async Task Example_DatagramsAndStreams()
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        #region MsQuic_DatagramsAndStreams
        // A self-signed certificate for the server
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var ephemeralCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(10));
        using var certificate = X509CertificateLoader.LoadPkcs12(ephemeralCertificate.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
        var protocol = new SslApplicationProtocol("example");

        // The server accepts datagrams and echoes them
        var listenerOptions = new MsQuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ConnectionOptionsCallback = (clientHello, cancellationToken) => ValueTask.FromResult(new MsQuicServerConnectionOptions
            {
                EnableDatagrams = true,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ApplicationProtocols = [protocol],
                },
            }),
        };
        listenerOptions.ApplicationProtocols.Add(protocol);
        await using var listener = await MsQuicListener.ListenAsync(listenerOptions);

        // The client trusts the certificate of the server by its hash
        var serverCertificateHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        var connectTask = MsQuicConnection.ConnectAsync(new MsQuicClientConnectionOptions
        {
            RemoteEndPoint = listener.LocalEndPoint,
            EnableDatagrams = true,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [protocol],
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (sender, serverCertificate, chain, errors) =>
                    serverCertificate is not null && serverCertificate.GetCertHash(HashAlgorithmName.SHA256).AsSpan().SequenceEqual(serverCertificateHash),
            },
        });

        await using var serverConnection = await listener.AcceptConnectionAsync();
        await using var client = await connectTask;

        // Datagrams: unreliable, unordered, at most MaxDatagramSendSize bytes
        await client.SendDatagramAsync("ping"u8.ToArray());
        var datagram = await serverConnection.ReceiveDatagramAsync();
        await serverConnection.SendDatagramAsync(datagram);
        var echo = await client.ReceiveDatagramAsync();
        Assert.Equal("ping", Encoding.UTF8.GetString(echo));

        // Streams: reliable and ordered
        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional);
        await stream.WriteAsync("hello"u8.ToArray(), completeWrites: true);
        await using var serverStream = await serverConnection.AcceptInboundStreamAsync();
        var received = new byte[5];
        await serverStream.ReadExactlyAsync(received);
        Assert.Equal("hello", Encoding.UTF8.GetString(received));

        await client.CloseAsync(errorCode: 0);
        #endregion
    }

    [Fact]
    public async Task Datagrams_MaxSizeIsReportedAndLargerDatagramsAreRejected()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await WaitUntilAsync(() => client.IsDatagramSendEnabled);
        Assert.True(client.MaxDatagramSendSize > 1000);

        var exception = await Assert.ThrowsAsync<MsQuicException>(() => client.SendDatagramAsync(new byte[client.MaxDatagramSendSize + 1]));
        Assert.Equal(MsQuicError.DatagramTooLarge, exception.Error);

        var payload = RandomNumberGenerator.GetBytes(client.MaxDatagramSendSize);
        await client.SendDatagramAsync(payload);
        Assert.Equal(payload, await server.ReceiveDatagramAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Datagrams_NotEnabledByThePeer()
    {
        await using var quic = await LoopbackQuic.StartAsync(options => options.EnableDatagrams = false);
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        Assert.False(client.IsDatagramSendEnabled);
        Assert.Equal(0, client.MaxDatagramSendSize);
        var exception = await Assert.ThrowsAsync<MsQuicException>(() => client.SendDatagramAsync(new byte[10]));
        Assert.Equal(MsQuicError.DatagramsNotEnabled, exception.Error);
    }

    [Fact]
    public async Task BidirectionalStream_LargePayloadIsEchoed()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        var payload = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional);
        var echoTask = Task.Run(async () =>
        {
            await using var serverStream = await server.AcceptInboundStreamAsync();
            await serverStream.CopyToAsync(serverStream);
            serverStream.CompleteWrites();
            await serverStream.WritesClosed;
        });

        var writeTask = Task.Run(async () =>
        {
            for (var offset = 0; offset < payload.Length; offset += 100_000)
            {
                await stream.WriteAsync(payload.AsMemory(offset, Math.Min(100_000, payload.Length - offset)));
            }

            stream.CompleteWrites();
        });

        using var received = new MemoryStream();
        await stream.CopyToAsync(received).WaitAsync(TimeSpan.FromSeconds(60));
        await writeTask;
        await echoTask;
        Assert.Equal(payload, received.ToArray());
        Assert.Equal(payload.Length, stream.BytesRead);
        Assert.Equal(payload.Length, stream.BytesWritten);
        await stream.ReadsClosed;
    }

    [Fact]
    public async Task UnidirectionalStreams_InBothDirections()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var outbound = await client.OpenOutboundStreamAsync(MsQuicStreamType.Unidirectional);
        Assert.False(outbound.CanRead);
        Assert.True(outbound.CanWrite);
        await outbound.WriteAsync("from client"u8.ToArray(), completeWrites: true);

        await using var inbound = await server.AcceptInboundStreamAsync();
        Assert.Equal(MsQuicStreamType.Unidirectional, inbound.Type);
        Assert.Equal(outbound.Id, inbound.Id);
        Assert.False(inbound.CanWrite);
        Assert.Equal("from client", await ReadToEndAsync(inbound));

        await using var serverOutbound = await server.OpenOutboundStreamAsync(MsQuicStreamType.Unidirectional);
        await serverOutbound.WriteAsync("from server"u8.ToArray(), completeWrites: true);
        await using var clientInbound = await client.AcceptInboundStreamAsync();
        Assert.Equal(3, clientInbound.Id); // The first server-initiated unidirectional stream (RFC 9000, section 2.1)
        Assert.Equal("from server", await ReadToEndAsync(clientInbound));
    }

    [Fact]
    public async Task Stream_Priority()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional);
        Assert.Equal(0x7FFF, stream.Priority);
        stream.Priority = 0xFFFF;
        Assert.Equal(0xFFFF, stream.Priority);
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Priority = 0x10000);
    }

    [Fact]
    public async Task Stream_PeerAbortsItsWrites()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional);
        await stream.WriteAsync("x"u8.ToArray());
        await using var serverStream = await server.AcceptInboundStreamAsync();
        serverStream.Abort(MsQuicAbortDirection.Write, 42);

        var exception = await Assert.ThrowsAsync<MsQuicException>(async () => await stream.ReadAsync(new byte[10]));
        Assert.Equal(MsQuicError.StreamAborted, exception.Error);
        Assert.Equal(42, exception.ApplicationErrorCode);
    }

    [Fact]
    public async Task Stream_PeerStopsReading()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional);
        await stream.WriteAsync("x"u8.ToArray());
        await using var serverStream = await server.AcceptInboundStreamAsync();
        serverStream.Abort(MsQuicAbortDirection.Read, 7);

        var exception = await Assert.ThrowsAsync<MsQuicException>(() => stream.WritesClosed.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(MsQuicError.StreamAborted, exception.Error);
        Assert.Equal(7, exception.ApplicationErrorCode);
        await Assert.ThrowsAsync<MsQuicException>(async () => await stream.WriteAsync("y"u8.ToArray()));
    }

    [Fact]
    public async Task Stream_LimitReachedWithoutWaiting()
    {
        await using var quic = await LoopbackQuic.StartAsync(options => options.MaxInboundBidirectionalStreams = 1);
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var first = await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional, waitUntilAvailable: false);
        var exception = await Assert.ThrowsAsync<MsQuicException>(async () => await client.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional, waitUntilAvailable: false));
        Assert.Equal(MsQuicError.StreamLimitReached, exception.Error);
    }

    [Fact]
    public async Task Close_ThePeerReceivesTheErrorCode()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        await client.CloseAsync(0x1234);
        var reason = await server.Closed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MsQuicError.ConnectionAborted, reason.Error);
        Assert.Equal(0x1234, reason.ApplicationErrorCode);

        var acceptException = await Assert.ThrowsAsync<MsQuicException>(async () => await server.AcceptInboundStreamAsync());
        Assert.Equal(MsQuicError.ConnectionAborted, acceptException.Error);
    }

    [Fact]
    public async Task Connect_RejectedCertificate()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var options = quic.CreateClientOptions();
        options.ClientAuthenticationOptions.RemoteCertificateValidationCallback = (_, _, _, _) => false;

        var exception = await Assert.ThrowsAsync<MsQuicException>(async () => await MsQuicConnection.ConnectAsync(options));
        Assert.Equal(MsQuicError.HandshakeFailure, exception.Error);
    }

    [Fact]
    public async Task Connect_UntrustedCertificateWithoutCallback()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var options = quic.CreateClientOptions();
        options.ClientAuthenticationOptions.RemoteCertificateValidationCallback = null;

        var exception = await Assert.ThrowsAsync<MsQuicException>(async () => await MsQuicConnection.ConnectAsync(options));
        Assert.Equal(MsQuicError.HandshakeFailure, exception.Error);
    }

    [Fact]
    public async Task Connect_ExposesTheConnectionProperties()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var (client, server) = await quic.ConnectAsync();
        await using var clientConnection = client;
        await using var serverConnection = server;

        Assert.Equal(LoopbackQuic.Protocol, client.NegotiatedApplicationProtocol);
        Assert.Equal(LoopbackQuic.Protocol, server.NegotiatedApplicationProtocol);
        Assert.Equal(quic.Listener.LocalEndPoint, client.RemoteEndPoint);
        Assert.Equal(client.LocalEndPoint, server.RemoteEndPoint);
        Assert.Equal("localhost", client.TargetHostName);
        Assert.Equal("localhost", server.TargetHostName);
        Assert.Equal(quic.Certificate.Thumbprint, client.RemoteCertificate?.Thumbprint);
    }

    [Fact]
    public async Task Statistics()
    {
        await using var quic = await LoopbackQuic.StartAsync();
        var options = quic.CreateClientOptions();
        options.CongestionControlAlgorithm = MsQuicCongestionControlAlgorithm.Bbr;
        var (client, server) = await quic.ConnectAsync(options);
        await using var clientConnection = client;
        await using var serverConnection = server;

        await using var stream = await client.OpenOutboundStreamAsync(MsQuicStreamType.Unidirectional);
        await stream.WriteAsync(new byte[10_000], completeWrites: true);
        await stream.WritesClosed.WaitAsync(TimeSpan.FromSeconds(10));

        var statistics = client.GetStatistics();
        Assert.True(statistics.SmoothedRoundTripTime > TimeSpan.Zero);
        Assert.True(statistics.StreamBytesSent >= 10_000);
        Assert.True(statistics.BytesSent > statistics.StreamBytesSent);
        Assert.True(statistics.PacketsSent > 0);
        Assert.True(statistics.PacketsReceived > 0);
        Assert.True(statistics.PathMtu >= 1200);
        Assert.True(statistics.CongestionWindow > 0);
    }

    [Fact]
    public async Task Connect_InvalidOptions()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await MsQuicConnection.ConnectAsync(new MsQuicClientConnectionOptions { RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1) }));
        await Assert.ThrowsAsync<ArgumentException>(async () => await MsQuicConnection.ConnectAsync(new MsQuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1),
            MaxInboundBidirectionalStreams = 100_000,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions { ApplicationProtocols = [LoopbackQuic.Protocol] },
        }));
    }

    private static async Task<string> ReadToEndAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > timeout)
                throw new TimeoutException();

            await Task.Delay(10);
        }
    }
}
