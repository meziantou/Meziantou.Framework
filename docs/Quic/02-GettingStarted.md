---
title: Getting started
description: Install MsQuic, then open a QUIC connection with datagrams, or a WebTransport session.
---

## Install MsQuic

On Windows, MsQuic ships with .NET: there is nothing to install. On Linux, install the `libmsquic` package from the
[Microsoft package repository](https://learn.microsoft.com/linux/packages):

```bash
sudo apt-get install libmsquic
```

On macOS, install it with Homebrew:

```bash
brew install libmsquic
```

[MsQuicLibrary.IsSupported](cref:Meziantou.Framework.MsQuic.MsQuicLibrary.IsSupported) tells whether the library was
loaded, and [MsQuicLibrary.NotSupportedReason](cref:Meziantou.Framework.MsQuic.MsQuicLibrary.NotSupportedReason) why not.

## Exchange datagrams and streams

```bash
dotnet add package Meziantou.Framework.MsQuic
```

The example starts a listener with a self-signed certificate, connects to it, and exchanges a datagram and a stream:

<!-- ref:../../tests/Meziantou.Framework.MsQuic.Tests/MsQuicConnectionTests.cs;region=MsQuic_DatagramsAndStreams;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Open a WebTransport session

```bash
dotnet add package Meziantou.Framework.WebTransport
```

The example starts a WebTransport server, connects to it with a client that trusts the certificate by its hash, and
echoes a stream:

<!-- ref:../../tests/Meziantou.Framework.WebTransport.Tests/WebTransportSessionTests.cs;region=WebTransport_ClientAndServer;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->
