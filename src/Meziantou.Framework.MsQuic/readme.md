# Meziantou.Framework.MsQuic

A .NET binding of [MsQuic](https://github.com/microsoft/msquic), the QUIC implementation that `System.Net.Quic` uses,
with the features `System.Net.Quic` does not expose:

- **Datagrams** (RFC 9221): unreliable messages on the connection, with the maximum size MsQuic reports as path MTU
  discovery progresses
- **Stream priorities**: the data of the streams with a higher priority is sent first
- **Connection statistics**: round-trip time, bytes and packets sent and received, losses, congestion window
- **Congestion control**: CUBIC or BBR
- **Stream limits**: open a stream without waiting when the peer does not allow one more

The API follows the shape of `System.Net.Quic`: `MsQuicListener`, `MsQuicConnection` and `MsQuicStream` (a `Stream`).

## Usage

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

## Requirements

- On Windows, MsQuic ships with .NET.
- On Linux, install the `libmsquic` package from the [Microsoft package repository](https://learn.microsoft.com/linux/packages).
- On macOS, install it with `brew install libmsquic`.
- The process must be 64-bit. `MsQuicLibrary.IsSupported` and `MsQuicLibrary.NotSupportedReason` tell whether MsQuic is
  available.
- On Linux and macOS, the private key of the server certificate must be exportable: MsQuic loads it from a PKCS #12
  export.
