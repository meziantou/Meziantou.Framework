using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>MessageChannel and MessagePort (https://html.spec.whatwg.org/multipage/web-messaging.html#message-channels).</summary>
public sealed class MessageChannelTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    [Fact]
    public void Messages_AreDeliveredToTheEntangledPort()
    {
        var document = Load("""
            <script>
              const log = [];
              const channel = new MessageChannel();
              channel.port2.onmessage = e => log.push('port2:' + e.data.text + ':' + (e.data !== original));
              channel.port1.onmessage = e => log.push('port1:' + e.data);
              const original = { text: 'a' };
              channel.port1.postMessage(original);
              channel.port2.postMessage('b');
              log.push('sync');
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("sync,port2:a:true,port1:b", document.Title);
    }

    [Fact]
    public void Messages_AreQueuedUntilThePortIsStarted()
    {
        var document = Load("""
            <script>
              const log = [];
              const channel = new MessageChannel();
              channel.port2.addEventListener('message', e => log.push(e.data));
              channel.port1.postMessage(1);
              channel.port1.postMessage(2);
              setTimeout(() => {
                log.push('started');
                channel.port2.start();
                setTimeout(() => document.title = log.join(), 0);
              }, 0);
            </script>
            """);
        Assert.Equal("started,1,2", document.Title);
    }

    [Fact]
    public void Close_DisentanglesThePortsWithoutFiringACloseEvent()
    {
        var document = Load("""
            <script>
              const log = [];
              const channel = new MessageChannel();
              channel.port1.addEventListener('close', () => log.push('port1 closed'));
              channel.port2.addEventListener('close', () => log.push('port2 closed'));
              channel.port2.onmessage = () => log.push('received');
              channel.port1.close();
              channel.port1.postMessage('m');
              setTimeout(() => document.title = log.join() || 'none', 0);
            </script>
            """);
        Assert.Equal("none", document.Title);
    }

    [Fact]
    public void PostMessage_TransferringTheSourcePortThrowsADataCloneError()
    {
        var document = Load("""
            <script>
              const channel = new MessageChannel();
              try { channel.port1.postMessage('m', [channel.port1]); } catch (e) { document.title = e.name; }
            </script>
            """);
        Assert.Equal("DataCloneError", document.Title);
    }

    [Fact]
    public void Messages_GeometryObjectsAreCloned()
    {
        var document = Load("""
            <script>
              const channel = new MessageChannel();
              const matrix = new DOMMatrix('translate(10px, 20px) scale(2)');
              const quad = new DOMQuad({ x: 1, y: 2 });
              channel.port2.onmessage = e => {
                const { matrix: m, quad: q } = e.data;
                document.title = [m !== matrix, m instanceof DOMMatrix, m.toString(), q instanceof DOMQuad, q.p1 instanceof DOMPoint, q.p1.x, q.p1.y].join();
              };
              channel.port1.postMessage({ matrix, quad });
            </script>
            """);
        Assert.Equal("true,true,matrix(2, 0, 0, 2, 10, 20),true,true,1,2", document.Title);
    }
}
