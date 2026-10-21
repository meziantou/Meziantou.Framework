using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>window.postMessage() (https://html.spec.whatwg.org/multipage/web-messaging.html#posting-messages).</summary>
public sealed class WindowPostMessageTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, Func<Url, string>? loader = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            DocumentLoader = loader is null ? null : new FuncLoader(loader),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Loads a cross-origin isolated document, whose frames have an embedder policy.</summary>
    private Document LoadCrossOriginIsolated(string html, Func<Url, string>? loader = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            DocumentLoader = loader is null ? null : new FuncLoader(loader, CrossOriginIsolationHeaders),
        });

        var document = context.Navigate("https://example.com/", _ => new DocumentResource(html) { Headers = CrossOriginIsolationHeaders });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    private static readonly KeyValuePair<string, string>[] CrossOriginIsolationHeaders =
    [
        new("Content-Type", "text/html"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Embedder-Policy", "require-corp"),
        new("Cross-Origin-Resource-Policy", "cross-origin"),
    ];

    private sealed class FuncLoader(Func<Url, string> loader, IReadOnlyList<KeyValuePair<string, string>>? headers = null) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(headers is null ? new DocumentResource(loader(new Url(request.Url))) : new DocumentResource(loader(new Url(request.Url))) { Headers = [.. headers] });
    }

    [Fact]
    public void PostMessage_FiresAMessageEventWithAClone()
    {
        var document = Load("""
            <script>
              const log = [];
              const original = { text: 'a' };
              window.onmessage = e => log.push([e.data.text, e.data !== original, e.origin, e.source === window, e.ports.length, e.isTrusted].join(':'));
              window.postMessage(original, '*');
              log.push('sync');
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("sync,a:true:https://example.com:true:0:true", document.Title);
    }

    [Fact]
    public void PostMessage_TargetOriginIsChecked()
    {
        var document = Load("""
            <script>
              const log = [];
              window.onmessage = e => log.push(e.data);
              window.postMessage('star', '*');
              window.postMessage('slash', '/');
              window.postMessage('same', 'https://example.com/path?query');
              window.postMessage('options', { targetOrigin: 'https://example.com' });
              window.postMessage('default');
              window.postMessage('other', 'https://other.example');
              try { window.postMessage('invalid', 'not a url'); } catch (e) { log.push(e.name); }
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("SyntaxError,star,slash,same,options,default", document.Title);
    }

    [Fact]
    public void PostMessage_TransfersArrayBuffersAndMessagePorts()
    {
        var document = Load("""
            <script>
              const log = [];
              const buffer = new Uint8Array([1, 2, 3]).buffer;
              const channel = new MessageChannel();
              channel.port1.onmessage = e => log.push('port:' + e.data);
              window.onmessage = e => {
                log.push(e.data.buffer.byteLength + ':' + new Uint8Array(e.data.buffer)[2] + ':' + (e.ports[0] instanceof MessagePort));
                e.ports[0].postMessage('reply');
              };
              window.postMessage({ buffer }, '*', [buffer, channel.port2]);
              log.push('detached:' + buffer.byteLength);
              try { window.postMessage(null, '*', [buffer]); } catch (e) { log.push(e.name); }
              setTimeout(() => setTimeout(() => document.title = log.join(), 0), 0);
            </script>
            """);
        Assert.Equal("detached:0,DataCloneError,3:3:true,port:reply", document.Title);
    }

    [Fact]
    public void PostMessage_DataCloneErrorIsThrownSynchronously()
    {
        var document = Load("""
            <script>
              const log = [];
              window.onmessage = () => log.push('message');
              for (const value of [() => {}, Symbol(), new Response()]) {
                try { window.postMessage(value, '*'); } catch (e) { log.push(e.name); }
              }
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("DataCloneError,DataCloneError,DataCloneError", document.Title);
    }

    [Fact]
    public void PostMessage_CrossOriginWindowsReceiveTheOriginAndTheSourceOfTheCaller()
    {
        var document = Load(
            """
            <iframe src="https://other.example/frame"></iframe>
            <script>
              window.onmessage = e => document.title = [e.data, e.origin, e.source === frames[0]].join();
              window.onload = () => frames[0].postMessage('ping', 'https://other.example');
            </script>
            """,
            url => """
                <script>
                  window.onmessage = e => e.source.postMessage(e.data + ':' + e.origin, e.origin);
                </script>
                """);

        Assert.Equal("ping:https://example.com,https://other.example,true", document.Title);
    }

    [Fact]
    public void StructuredClone_ErrorsKeepTheirStack()
    {
        var document = Load("""
            <script>
              const error = new TypeError('m');
              const clone = structuredClone(error);
              document.title = [clone instanceof TypeError, clone.message, typeof clone.stack, clone.stack === error.stack, Object.hasOwn(clone, 'stack')].join();
            </script>
            """);
        Assert.Equal("true,m,string,true,false", document.Title);
    }

    [Fact]
    public void SharedArrayBuffer_IsSharedWithTheRealmsOfTheAgentCluster()
    {
        var document = LoadCrossOriginIsolated("""
            <script>
              const buffer = new SharedArrayBuffer(4);
              new Uint8Array(buffer)[0] = 1;
              window.onmessage = e => {
                const view = new Uint8Array(e.data);
                view[1] = 2;
                document.title = [self.crossOriginIsolated, e.data !== buffer, e.data instanceof SharedArrayBuffer, view[0], new Uint8Array(buffer)[1]].join();
              };
              window.postMessage(buffer, '*');
            </script>
            """);
        Assert.Equal("true,true,true,1,2", document.Title);
    }

    [Fact]
    public void SharedArrayBuffer_CannotBeDeserializedInAnotherAgentCluster()
    {
        var document = LoadCrossOriginIsolated(
            """
            <iframe src="https://other.example/frame"></iframe>
            <script>
              window.onmessage = e => document.title = e.data;
              window.onload = () => frames[0].postMessage(new SharedArrayBuffer(1), '*');
            </script>
            """,
            url => """
                <script>
                  window.onmessage = () => parent.postMessage('message', '*');
                  window.onmessageerror = e => parent.postMessage('messageerror:' + e.origin + ':' + (e.data === null), '*');
                </script>
                """);

        Assert.Equal("messageerror:https://example.com:true", document.Title);
    }

    [Fact]
    public void SharedArrayBuffer_IsNotExposedWithoutCrossOriginIsolation()
    {
        var document = Load("""
            <script>
              document.title = [typeof SharedArrayBuffer, self.crossOriginIsolated].join();
            </script>
            """);
        Assert.Equal("undefined,false", document.Title);
    }
}
