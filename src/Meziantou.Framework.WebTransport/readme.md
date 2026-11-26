# Meziantou.Framework.WebTransport

A [WebTransport over HTTP/3](https://datatracker.ietf.org/doc/draft-ietf-webtrans-http3/) client and server, built on
`Meziantou.Framework.MsQuic`:

- Sessions opened with an extended CONNECT request (RFC 9220), with custom headers and the negotiation of an
  application protocol (`wt-available-protocols` and `wt-protocol`)
- Bidirectional and unidirectional streams in both directions, with priorities and 32-bit application error codes
- Datagrams (RFC 9297)
- Closing with an error code and a reason, and draining
- Certificate pinning by SHA-256 hash, as the `serverCertificateHashes` option of the browsers

## Usage

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

## Requirements

The package needs MsQuic, see `Meziantou.Framework.MsQuic`.

## Compatibility

The HTTP/3 layer only implements what WebTransport needs: the QPACK dynamic table is disabled (capacity 0), the
listener answers the requests that are not WebTransport sessions with a 501 status, and a client session has its own
QUIC connection. The SETTINGS advertise the identifiers of the successive drafts (`SETTINGS_ENABLE_WEBTRANSPORT`,
`SETTINGS_WEBTRANSPORT_MAX_SESSIONS` and `SETTINGS_WT_MAX_SESSIONS`), so the browsers and the servers of the different
drafts interoperate. The flow control of WebTransport (draft 09 and later) is not implemented.
