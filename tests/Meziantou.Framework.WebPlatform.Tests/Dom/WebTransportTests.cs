using Meziantou.Framework.WebPlatform.Tests.Scripting;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebTransport;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The WebTransport interface (https://w3c.github.io/webtransport/), with a loopback HTTP/3 server and with a stub loader.</summary>
public sealed class WebTransportTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private BrowsingContext CreateContext(DocumentLoader loader) => _contexts.Create(new BrowsingContextOptions
    {
        DocumentLoader = loader,
        ScriptEngine = _engine,
        UnhandledScriptError = (_, error) => _errors.Add(error),
    });

    private Document Load(BrowsingContext context, string html, string url = "https://localhost/")
    {
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    // The tasks of the network are not awaited by RunUntilIdle: the events of a session arrive at any time
    private async Task WaitForTitleAsync(Document document)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (document.Title.Length == 0)
        {
            _contexts.EventLoop.RunUntilIdle();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The script did not complete");

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WebTransport_ExchangesStreamsAndDatagramsWithAServer()
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var ephemeralCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(10));
        using var certificate = X509CertificateLoader.LoadPkcs12(ephemeralCertificate.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
        await using var listener = await WebTransportListener.ListenAsync(new WebTransportListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate,
        });

        var serverTask = Task.Run(async () =>
        {
            var request = await listener.AcceptAsync(TestContext.Current.CancellationToken);
            await using var session = await request.AcceptAsync("chat", [KeyValuePair.Create("x-response", "yes"), KeyValuePair.Create("wt-protocol", "\"hidden\"")]);

            // Echo the bidirectional stream
            await using (var stream = await session.AcceptStreamAsync(TestContext.Current.CancellationToken))
            {
                await stream.CopyToAsync(stream, TestContext.Current.CancellationToken);
                stream.CompleteWrites();
                await stream.WritesClosed;
            }

            // Echo the datagram, then open a unidirectional stream
            var datagram = await session.ReceiveDatagramAsync(TestContext.Current.CancellationToken);
            await session.SendDatagramAsync(datagram, TestContext.Current.CancellationToken);
            await using (var uni = await session.OpenUnidirectionalStreamAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                await uni.WriteAsync("from server"u8.ToArray(), completeWrites: true, TestContext.Current.CancellationToken);
                await uni.WritesClosed;
            }

            var closeInfo = await session.Closed.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return (request, closeInfo);
        }, TestContext.Current.CancellationToken);

        var hash = string.Join(',', SHA256.HashData(certificate.RawData).Select(b => b.ToString(CultureInfo.InvariantCulture)));
        var context = CreateContext(DocumentLoader.Create());
        var document = Load(context, $$"""
            <script>
              const log = [];
              const wt = new WebTransport('https://localhost:{{listener.LocalEndPoint.Port}}/echo?x=1', {
                serverCertificateHashes: [{ algorithm: 'sha-256', value: new Uint8Array([{{hash}}]) }],
                protocols: ['chat', 'other'],
                headers: { 'X-Test': ' value ', 'Cookie': 'forbidden' },
              });
              log.push('initial:' + wt.reliability + ':' + (wt.responseHeaders === null));
              (async () => {
                try {
                  await wt.ready;
                  log.push('ready:' + wt.reliability + ':' + wt.protocol + ':' + wt.responseHeaders.get('x-response') + ':' + wt.responseHeaders.has('wt-protocol'));

                  const bidi = await wt.createBidirectionalStream();
                  const writer = bidi.writable.getWriter();
                  await writer.write(new TextEncoder().encode('hello'));
                  await writer.close();
                  log.push('echo:' + await new Response(bidi.readable).text());

                  const datagramWriter = wt.datagrams.createWritable().getWriter();
                  await datagramWriter.write(new Uint8Array([1, 2, 3]));
                  const { value: datagram } = await wt.datagrams.readable.getReader().read();
                  log.push('datagram:' + datagram.join(',') + ':' + (wt.datagrams.maxDatagramSize > 1000));

                  const { value: uni } = await wt.incomingUnidirectionalStreams.getReader().read();
                  log.push('uni:' + (uni instanceof WebTransportReceiveStream) + ':' + await new Response(uni).text());

                  const stats = await wt.getStats();
                  log.push('stats:' + (stats.bytesSent > 0) + ':' + (stats.smoothedRtt >= 0) + ':' + stats.datagrams.droppedIncoming);

                  wt.close({ closeCode: 7, reason: 'bye' });
                  const info = await wt.closed;
                  log.push('closed:' + info.closeCode + ':' + info.reason);

                  // The send order of a stream whose session ended can still be set
                  bidi.writable.sendOrder = 9.2e18;
                  log.push('sendOrder:' + bidi.writable.sendOrder);
                } catch (e) {
                  log.push('error:' + e.name + ':' + e.message);
                }

                document.title = log.join('|');
              })();
            </script>
            """);

        await WaitForTitleAsync(document);
        Assert.Equal("initial:pending:true|ready:supports-unreliable:chat:yes:false|echo:hello|datagram:1,2,3:true|uni:true:from server|stats:true:true:0|closed:7:bye|sendOrder:9200000000000000000", document.Title);

        var (request, closeInfo) = await serverTask;
        Assert.Equal("/echo?x=1", request.Path);
        Assert.Equal("https://localhost", request.Origin);
        Assert.Equal(["chat", "other"], request.Protocols);
        Assert.Equal("value", request.Headers.Single(header => header.Key == "x-test").Value);
        Assert.DoesNotContain(request.Headers, header => header.Key == "cookie");
        Assert.Equal(new WebTransportCloseInfo(7, "bye"), closeInfo);
    }

    [Fact]
    public async Task WebTransport_SendsTheRequestToTheLoader()
    {
        var loader = new StubLoader();
        var context = CreateContext(loader);
        var document = Load(context, """
            <script>
              const wt = new WebTransport('https://example.com:4433/session', {
                protocols: ['a', 'b'],
                headers: [['X-One', '1'], ['Host', 'evil.example']],
                serverCertificateHashes: [{ algorithm: 'sha-256', value: new Uint8Array(32) }],
                congestionControl: 'low-latency',
                anticipatedConcurrentIncomingUnidirectionalStreams: 5,
              });
              wt.ready.catch(e => {
                document.title = e.constructor.name + ':' + e.source + ':' + e.streamErrorCode;
              });
            </script>
            """, "https://example.com/page");

        await WaitForTitleAsync(document);

        // The loader returned null: the session failed
        Assert.Equal("WebTransportError:session:null", document.Title);
        var request = Assert.Single(loader.Requests);
        Assert.Equal("https://example.com:4433/session", request.Url);
        Assert.Equal([KeyValuePair.Create("origin", "https://example.com"), KeyValuePair.Create("x-one", "1")], request.Headers);
        Assert.Equal(["a", "b"], request.Protocols);
        Assert.Equal(32, Assert.Single(request.ServerCertificateHashes).Length);
        Assert.Equal("low-latency", request.CongestionControl);
        Assert.Equal(5, request.AnticipatedConcurrentIncomingUnidirectionalStreams);
        Assert.Null(request.AnticipatedConcurrentIncomingBidirectionalStreams);
    }

    [Fact]
    public void WebTransport_ValidatesTheArguments()
    {
        var context = CreateContext(new StubLoader());
        var document = Load(context, """
            <script>
              function error(f) {
                try {
                  f();
                  return 'none';
                } catch (e) {
                  return e.name;
                }
              }

              document.title = [
                error(() => new WebTransport('http://example.com/')),
                error(() => new WebTransport('https://example.com/#fragment')),
                error(() => new WebTransport('relative')),
                error(() => new WebTransport('https://example.com/', { protocols: ['a', 'a'] })),
                error(() => new WebTransport('https://example.com/', { protocols: [''] })),
                error(() => new WebTransport('https://example.com/', { allowPooling: true, serverCertificateHashes: [{ algorithm: 'sha-256', value: new Uint8Array(32) }] })),
                error(() => new WebTransport('https://example.com/', { serverCertificateHashes: [{ algorithm: 'sha-256' }] })),
                error(() => new WebTransport('https://example.com/', { congestionControl: 'fast' })),
                error(() => new WebTransport('https://example.com/', { anticipatedConcurrentIncomingBidirectionalStreams: 65536 })),
                error(() => new WebTransportError('m', { source: 'session', streamErrorCode: 3 }).streamErrorCode),
                String(new WebTransportError('m', { source: 'session' }).source),
                String(WebTransport.supportsReliableOnly),
              ].join(',');
            </script>
            """, "https://example.com/");

        Assert.Equal("SyntaxError,SyntaxError,SyntaxError,SyntaxError,SyntaxError,NotSupportedError,TypeError,TypeError,TypeError,none,session,false", document.Title);
    }

    [Fact]
    public void WebTransport_IsOnlyExposedInSecureContexts()
    {
        var context = CreateContext(new StubLoader());
        var document = Load(context, "<script>document.title = typeof WebTransport + ',' + typeof WebTransportError;</script>", "http://example.com/");
        Assert.Equal("undefined,undefined", document.Title);
    }

    /// <summary>A loader that records the WebTransport requests and rejects them.</summary>
    private sealed class StubLoader : DocumentLoader
    {
        public List<WebTransportConnectionRequest> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<DocumentResource?>(null);

        public override ValueTask<WebTransportConnection?> ConnectWebTransportAsync(WebTransportConnectionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult<WebTransportConnection?>(null);
        }
    }
}
