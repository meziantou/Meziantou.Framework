using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>BroadcastChannel (https://html.spec.whatwg.org/multipage/web-messaging.html#broadcasting-to-other-browsing-contexts) and the structured clone of the messages.</summary>
public sealed class BroadcastChannelTests : IDisposable
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

    private sealed class FuncLoader(Func<Url, string> loader) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource(loader(new Url(request.Url))));
    }

    [Fact]
    public void Messages_AreDeliveredToTheOtherWindowsOfTheSameOrigin()
    {
        var document = Load(
            """
            <iframe id=same src="https://example.com/frame"></iframe>
            <iframe id=other src="https://other.example/frame"></iframe>
            <script>
              var log = [];
              const a = new BroadcastChannel('c');
              const b = new BroadcastChannel('c');
              const unrelated = new BroadcastChannel('d');
              a.onmessage = e => log.push('a:' + e.data);
              b.onmessage = e => log.push('b:' + e.data + ':' + e.origin);
              unrelated.onmessage = e => log.push('unrelated');
              window.onload = () => {
                frames[0].postFromFrame('from same');
                a.postMessage('from a');
                setTimeout(() => document.title = log.join(), 0);
              };
            </script>
            """,
            url => """
                <script>
                  const channel = new BroadcastChannel('c');
                  channel.onmessage = e => parent.log.push(location.hostname + ':' + e.data);
                  function postFromFrame(message) { channel.postMessage(message); }

                  // The cross-origin frame cannot be called by its parent: it posts its message itself
                  if (location.hostname === 'other.example') channel.postMessage('from other');
                </script>
                """);

        Assert.Equal("a:from same,b:from same:https://example.com,b:from a:https://example.com,example.com:from a", document.Title);
    }

    [Fact]
    public void Messages_AreStructuredClones()
    {
        var document = Load("""
            <script>
              const a = new BroadcastChannel('c');
              const b = new BroadcastChannel('c');
              const original = { text: 'x', date: new Date(42), map: new Map([[1, 'one']]), set: new Set([2]), list: [1, , 3], bytes: new Uint8Array([1, 2]), regexp: /a/gi, error: new RangeError('m'), boxed: new String('s') };
              original.self = original;
              b.onmessage = e => {
                const data = e.data;
                document.title = [
                  data !== original, data.self === data, data.text, data.date instanceof Date, data.date.getTime(),
                  data.map.get(1), data.set.has(2), data.list.length, 1 in data.list, data.bytes instanceof Uint8Array, data.bytes.join(' '),
                  data.regexp.source + '/' + data.regexp.flags, data.error instanceof RangeError, data.error.message, typeof data.boxed, String(data.boxed),
                ].join();
              };
              a.postMessage(original);
              original.text = 'changed';
            </script>
            """);
        Assert.Equal("true,true,x,true,42,one,true,3,false,true,1 2,a/gi,true,m,object,s", document.Title);
    }

    [Fact]
    public void Messages_PlatformObjectsAreCloned()
    {
        var document = Load("""
            <script>
              const a = new BroadcastChannel('c');
              const b = new BroadcastChannel('c');
              const point = new DOMPoint(1, 2);
              const blob = new Blob(['abc'], { type: 'text/plain' });
              b.onmessage = e => {
                const { point: p, blob: copy, exception } = e.data;
                copy.text().then(text => {
                  document.title = [p !== point, p instanceof DOMPoint, p.x, p.y, copy !== blob, copy.type, text, exception.name, exception.message].join();
                });
              };
              a.postMessage({ point, blob, exception: new DOMException('m', 'NotFoundError') });
              point.x = 5;
            </script>
            """);
        Assert.Equal("true,true,1,2,true,text/plain,abc,NotFoundError,m", document.Title);
    }

    [Fact]
    public void PostMessage_ThrowsADataCloneError()
    {
        var document = Load("""
            <script>
              const channel = new BroadcastChannel('c');
              const results = [];
              for (const value of [() => 1, Symbol('s'), { f() {} }, document.documentElement, new WeakMap()]) {
                try { channel.postMessage(value); results.push('no error'); } catch (e) { results.push(e instanceof DOMException ? e.name : e); }
              }
              document.title = results.join();
            </script>
            """);
        Assert.Equal("DataCloneError,DataCloneError,DataCloneError,DataCloneError,DataCloneError", document.Title);
    }

    [Fact]
    public void Close()
    {
        var document = Load("""
            <script>
              const log = [];
              const a = new BroadcastChannel('c');
              const b = new BroadcastChannel('c');
              b.onmessage = () => log.push('received');
              a.postMessage('m');
              b.close();
              let error = 'no error';
              try { b.postMessage('x'); } catch (e) { error = e.name; }
              setTimeout(() => document.title = [b.name, log.length, error].join(), 0);
            </script>
            """);
        Assert.Equal("c,0,InvalidStateError", document.Title);
    }
}
