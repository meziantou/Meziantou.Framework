using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.MsQuic;

namespace Meziantou.Framework.WebTransport.Tests;

public sealed class WebTransportSessionTests
{
    [Fact]
    public async Task Example_ClientAndServer()
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        #region WebTransport_ClientAndServer
        // The certificate of the server: ECDSA P-256 and valid at most 14 days, so a client can trust it by its hash
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var ephemeralCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(10));
        using var certificate = X509CertificateLoader.LoadPkcs12(ephemeralCertificate.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);

        await using var listener = await WebTransportListener.ListenAsync(new WebTransportListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate,
        });

        // The server accepts the session and echoes the first bidirectional stream
        var serverTask = Task.Run(async () =>
        {
            var sessionRequest = await listener.AcceptAsync();
            await using var serverSession = await sessionRequest.AcceptAsync(protocol: sessionRequest.Protocols.Count > 0 ? sessionRequest.Protocols[0] : null);
            await using var serverStream = await serverSession.AcceptStreamAsync();
            await serverStream.CopyToAsync(serverStream);
            serverStream.CompleteWrites();
            await serverSession.Closed;
        });

        var options = new WebTransportClientOptions();
        options.ServerCertificateHashes.Add(SHA256.HashData(certificate.RawData));
        options.Protocols.Add("echo-v1");
        await using var session = await WebTransportSession.ConnectAsync(new Uri($"https://localhost:{listener.LocalEndPoint.Port}/echo"), options);
        Assert.Equal("echo-v1", session.Protocol);

        await using var stream = await session.OpenBidirectionalStreamAsync();
        await stream.WriteAsync("hello"u8.ToArray(), completeWrites: true);
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());

        await session.CloseAsync(errorCode: 0, reason: "done");
        await serverTask;
        #endregion
    }

    [Fact]
    public async Task Connect_RequestAndResponseHeaders()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var options = server.CreateClientOptions();
        options.Headers.Add(KeyValuePair.Create("origin", "https://example.com"));
        options.Headers.Add(KeyValuePair.Create("X-Custom", "value"));
        options.Protocols.Add("a");
        options.Protocols.Add("b");

        var connectTask = WebTransportSession.ConnectAsync(server.GetUri("/path?query=1"), options).AsTask();
        var request = await server.Listener.AcceptAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("/path?query=1", request.Path);
        Assert.Equal($"localhost:{server.Listener.LocalEndPoint.Port}", request.Authority);
        Assert.Equal("https://example.com", request.Origin);
        Assert.Equal("value", request.Headers.Single(header => header.Key == "x-custom").Value);
        Assert.Equal(["a", "b"], request.Protocols);

        await using var serverSession = await request.AcceptAsync("b", [KeyValuePair.Create("x-response", "1")]);
        await using var session = await connectTask;
        Assert.Equal("b", session.Protocol);
        Assert.Equal("1", session.ResponseHeaders.Single(header => header.Key == "x-response").Value);
        Assert.Equal(serverSession.SessionId, session.SessionId);
    }

    [Fact]
    public async Task Connect_Rejected()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var connectTask = WebTransportSession.ConnectAsync(server.GetUri("/missing"), server.CreateClientOptions()).AsTask();
        var request = await server.Listener.AcceptAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await request.RejectAsync(404);

        var exception = await Assert.ThrowsAsync<WebTransportException>(() => connectTask);
        Assert.Equal(WebTransportError.SessionRejected, exception.Error);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Accept_InvalidProtocolCanStillBeRejected()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var connectTask = WebTransportSession.ConnectAsync(server.GetUri("/"), server.CreateClientOptions()).AsTask();
        var request = await server.Listener.AcceptAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        // A protocol must be printable ASCII: the request is not answered by the failed AcceptAsync
        await Assert.ThrowsAsync<ArgumentException>(async () => await request.AcceptAsync("écho"));
        await request.RejectAsync(400);

        var exception = await Assert.ThrowsAsync<WebTransportException>(() => connectTask);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task Connect_CertificateHashMismatch()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var options = new WebTransportClientOptions();
        options.ServerCertificateHashes.Add(new byte[32]);

        var exception = await Assert.ThrowsAsync<WebTransportException>(async () => await WebTransportSession.ConnectAsync(server.GetUri("/"), options));
        Assert.Equal(WebTransportError.ConnectionFailed, exception.Error);
    }

    [Fact]
    public async Task Connect_HostNameNotResolved()
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        // .invalid never resolves (RFC 6761): the DNS failure is a connection failure, not a SocketException
        var exception = await Assert.ThrowsAsync<WebTransportException>(async () => await WebTransportSession.ConnectAsync(new Uri("https://does-not-exist.invalid/")));
        Assert.Equal(WebTransportError.ConnectionFailed, exception.Error);
        var quicException = Assert.IsType<MsQuicException>(exception.InnerException);
        Assert.Equal(MsQuicError.HostUnreachable, quicException.Error);
    }

    [Fact]
    public async Task Streams_InBothDirections()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;
        await using var acceptedSession = serverSession;

        // Client to server, unidirectional
        await using (var uni = await session.OpenUnidirectionalStreamAsync())
        {
            await uni.WriteAsync("uni from client"u8.ToArray(), completeWrites: true);
            await using var received = await serverSession.AcceptStreamAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(MsQuicStreamType.Unidirectional, received.Type);
            Assert.False(received.IsOutbound);
            Assert.Equal("uni from client", await ReadToEndAsync(received));
            Assert.Equal(15, received.BytesRead);
        }

        // Server to client, bidirectional
        await using var serverStream = await serverSession.OpenBidirectionalStreamAsync();
        await serverStream.WriteAsync("ping"u8.ToArray(), completeWrites: true);
        await using var clientStream = await session.AcceptStreamAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MsQuicStreamType.Bidirectional, clientStream.Type);
        Assert.Equal("ping", await ReadToEndAsync(clientStream));
        await clientStream.WriteAsync("pong"u8.ToArray(), completeWrites: true);
        Assert.Equal("pong", await ReadToEndAsync(serverStream));
        Assert.Equal(4, serverStream.BytesWritten);
    }

    [Fact]
    public async Task Stream_AbortedWithAnApplicationErrorCode()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;
        await using var acceptedSession = serverSession;

        await using var stream = await session.OpenBidirectionalStreamAsync();
        await stream.WriteAsync("x"u8.ToArray());
        await using var serverStream = await serverSession.AcceptStreamAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        serverStream.Abort(MsQuicAbortDirection.Write, 123456);

        var exception = await Assert.ThrowsAsync<WebTransportException>(async () => await stream.ReadAsync(new byte[10]));
        Assert.Equal(WebTransportError.StreamAborted, exception.Error);
        Assert.Equal(123456u, exception.StreamErrorCode);
    }

    [Fact]
    public async Task Datagrams()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;
        await using var acceptedSession = serverSession;

        await WaitUntilAsync(() => session.MaxDatagramSize > 0);
        await session.SendDatagramAsync("datagram"u8.ToArray());
        var received = await serverSession.ReceiveDatagramAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("datagram", Encoding.UTF8.GetString(received.Span));

        await serverSession.SendDatagramAsync(received);
        var echo = await session.ReceiveDatagramAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("datagram", Encoding.UTF8.GetString(echo.Span));

        var exception = await Assert.ThrowsAsync<WebTransportException>(() => session.SendDatagramAsync(new byte[session.MaxDatagramSize + 1]));
        Assert.Equal(WebTransportError.DatagramNotSent, exception.Error);
    }

    [Fact]
    public async Task Close_ThePeerReceivesTheCodeAndTheReason()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;
        await using var acceptedSession = serverSession;

        await using var stream = await session.OpenBidirectionalStreamAsync();
        await stream.WriteAsync("x"u8.ToArray());
        await using var serverStream = await serverSession.AcceptStreamAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await session.CloseAsync(7, "bye");
        Assert.Equal(new WebTransportCloseInfo(7, "bye"), await session.Closed);
        Assert.Equal(new WebTransportCloseInfo(7, "bye"), await serverSession.Closed.WaitAsync(TimeSpan.FromSeconds(10)));

        // The streams of a closed session are aborted
        var exception = await Assert.ThrowsAsync<WebTransportException>(async () => await serverStream.ReadAsync(new byte[10]).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(WebTransportError.SessionClosed, exception.Error);
        await Assert.ThrowsAsync<WebTransportException>(async () => await session.OpenBidirectionalStreamAsync());
    }

    [Fact]
    public async Task Drain()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;
        await using var acceptedSession = serverSession;

        Assert.False(session.Draining.IsCompleted);
        await serverSession.DrainAsync();
        await session.Draining.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(session.Closed.IsCompleted);
    }

    [Fact]
    public async Task Close_ServerDisposesTheSession()
    {
        await using var server = await LoopbackWebTransport.StartAsync();
        var (session, serverSession) = await server.ConnectAsync();
        await using var clientSession = session;

        await serverSession.DisposeAsync();
        Assert.Equal(new WebTransportCloseInfo(0, ""), await session.Closed.WaitAsync(TimeSpan.FromSeconds(10)));
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
