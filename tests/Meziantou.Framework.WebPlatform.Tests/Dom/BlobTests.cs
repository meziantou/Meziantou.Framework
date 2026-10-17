using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>Blob (https://w3c.github.io/FileAPI/#blob-section) and the blob URL store (https://w3c.github.io/FileAPI/#url).</summary>
public sealed class BlobTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
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
    public async Task Constructor_ConcatenatesTheParts()
    {
        var blob = new Blob(["a\U000000e9", new Blob(["b"]), new byte[] { 0x63 }, new ReadOnlyMemory<byte>([0x64])], new BlobPropertyBag { Type = "Text/Plain" });
        Assert.Equal(6UL, blob.Size);
        Assert.Equal("text/plain", blob.Type);
        Assert.Equal("a\U000000e9bcd", await blob.Text());
        Assert.Equal([0x61, 0xC3, 0xA9, 0x62, 0x63, 0x64], await blob.Bytes());
        Assert.Equal(await blob.Bytes(), await blob.ArrayBuffer());
    }

    [Fact]
    public void Constructor_InvalidTypeIsEmpty()
    {
        Assert.Equal("", new Blob([], new BlobPropertyBag { Type = "text/plain\U000000e9" }).Type);
    }

    [Fact]
    public async Task Constructor_NativeLineEndings()
    {
        var blob = new Blob(["a\r\nb\rc\nd"], new BlobPropertyBag { Endings = "native" });
        var newLine = OperatingSystem.IsWindows() ? "\r\n" : "\n";
        Assert.Equal("a" + newLine + "b" + newLine + "c" + newLine + "d", await blob.Text());
        Assert.Equal("a\r\nb\rc\nd", await new Blob(["a\r\nb\rc\nd"]).Text());
    }

    [Fact]
    public async Task Text_RemovesTheByteOrderMark()
    {
        var blob = new Blob([new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }]);
        Assert.Equal("a", await blob.Text());
        Assert.Equal(4UL, blob.Size);
    }

    [Fact]
    public async Task Slice()
    {
        var blob = new Blob(["abcdef"], new BlobPropertyBag { Type = "text/plain" });
        var slice = blob.Slice(1, -2, "TEXT/HTML");
        Assert.Equal("bcd", await slice.Text());
        Assert.Equal("text/html", slice.Type);
        Assert.Equal("", blob.Slice().Type);
        Assert.Equal("ef", await blob.Slice(-2).Text());
    }

    [Fact]
    public void Script_ArrayBufferAndBytes()
    {
        var document = Load("""
            <script>
              const blob = new Blob(['ab'], { type: 'text/plain' });
              Promise.all([blob.arrayBuffer(), blob.bytes()]).then(([buffer, bytes]) => {
                document.title = [buffer instanceof ArrayBuffer, buffer.byteLength, bytes instanceof Uint8Array, bytes.join(' ')].join();
              });
            </script>
            """);
        Assert.Equal("true,2,true,97 98", document.Title);
    }

    [Fact]
    public void CreateObjectUrl_ReturnsABlobUrlOfTheOrigin()
    {
        var document = Load("""
            <body><script>
              const url = URL.createObjectURL(new Blob(['a']));
              const other = URL.createObjectURL(new Blob(['a']));
              document.title = [/^blob:https:\/\/example\.com\/[0-9a-f-]{36}$/.test(url), url !== other, new URL(url).origin].join();
            </script>
            """);
        Assert.Equal("true,true,https://example.com", document.Title);
    }

    [Fact]
    public void Iframe_NavigatesToABlobUrl()
    {
        var document = Load("""
            <iframe id=frame></iframe>
            <script>
              const iframe = document.getElementById('frame');
              iframe.onload = () => {
                const doc = iframe.contentDocument;
                document.title = [doc.URL.startsWith('blob:'), doc.contentType, doc.body.innerHTML, doc.characterSet].join('|');
              };
              iframe.src = URL.createObjectURL(new Blob(['<meta charset=utf-8><p>héllo'], { type: 'text/html' }));
            </script>
            """);
        Assert.Equal("true|text/html|<p>héllo</p>|UTF-8", document.Title);
    }

    [Fact]
    public void Iframe_RevokedBlobUrlIsANetworkError()
    {
        var document = Load("""
            <iframe id=frame></iframe>
            <script>
              const iframe = document.getElementById('frame');
              const url = URL.createObjectURL(new Blob(['<p>a'], { type: 'text/html' }));
              URL.revokeObjectURL(url);
              iframe.onload = () => {
                let access;
                try { access = iframe.contentDocument === null ? 'null' : iframe.contentDocument.body.innerHTML; } catch (e) { access = e.name; }
                document.title = access;
              };
              iframe.src = url;
            </script>
            """);
        Assert.Equal("null", document.Title);
    }

    [Fact]
    public void Iframe_UrlRevokedAfterTheNavigationStarts()
    {
        var document = Load("""
            <iframe id=frame></iframe>
            <script>
              const iframe = document.getElementById('frame');
              const url = URL.createObjectURL(new Blob(['<p>a'], { type: 'text/html' }));
              iframe.onload = () => { document.title = iframe.contentDocument.body.innerHTML; };
              iframe.src = url;
              URL.revokeObjectURL(url);
            </script>
            """);
        Assert.Equal("<p>a</p>", document.Title);
    }

    [Fact]
    public void RevokeObjectUrl_WithFragment_DoesNotRevoke()
    {
        var document = Load("""
            <body><script>
              const url = URL.createObjectURL(new Blob(['a']));
              URL.revokeObjectURL(url + '#fragment');
              document.body.dataset.url = url;
            </script>
            """);
        var record = WebPlatform.Urls.UrlParser.Parse(document.Body!.GetAttribute("data-url")!, baseUrl: null)!;
        Assert.NotNull(document.BrowsingContext!.BlobUrls.Resolve(record));
    }

    [Fact]
    public void FileReader_FiresTheEventsFromTasks()
    {
        var document = Load("""
            <script>
              const events = [];
              const reader = new FileReader();
              for (const type of ['loadstart', 'progress', 'load', 'loadend']) {
                reader.addEventListener(type, e => events.push(type + ':' + e.loaded + '/' + e.total + ':' + reader.readyState));
              }
              reader.onloadend = () => {
                const textReader = new FileReader();
                textReader.onload = () => {
                  const urlReader = new FileReader();
                  urlReader.onload = () => {
                    const bufferReader = new FileReader();
                    bufferReader.onload = () => {
                      document.title = [events.join(' '), reader.result, textReader.result, urlReader.result, new Uint8Array(bufferReader.result).join(' '), bufferReader.result === bufferReader.result].join('|');
                    };
                    bufferReader.readAsArrayBuffer(new Blob(['ab']));
                  };
                  urlReader.readAsDataURL(new Blob(['ab'], { type: 'text/plain' }));
                };
                textReader.readAsText(new Blob([new Uint8Array([0xE9])], { type: 'text/plain;charset=windows-1252' }));
              };
              reader.readAsText(new Blob(['hé']));
              events.push('sync:' + reader.readyState);
            </script>
            """);
        Assert.Equal("sync:1 loadstart:0/3:1 progress:3/3:1 load:3/3:2 loadend:3/3:2|hé|é|data:text/plain;base64,YWI=|97 98|true", document.Title);
    }

    [Fact]
    public void FileReader_Abort()
    {
        var document = Load("""
            <script>
              const events = [];
              const reader = new FileReader();
              for (const type of ['loadstart', 'load', 'abort', 'loadend']) {
                reader.addEventListener(type, () => events.push(type));
              }
              reader.readAsText(new Blob(['a']));
              reader.abort();
              let error;
              try { reader.readAsText(new Blob(['b'])); reader.readAsText(new Blob(['c'])); } catch (e) { error = e.name; }
              reader.onload = () => { document.title = events.join(' ') + '|' + error + '|' + reader.result; };
            </script>
            """);
        Assert.Equal("abort loadend loadstart load|InvalidStateError|b", document.Title);
    }

    [Fact]
    public void FileReader_WithoutWindow_IsSynchronous()
    {
        var reader = new FileReader();
        var events = new List<string>();
        reader.AddEventListener("load", _ => events.Add("load"));
        reader.ReadAsBinaryString(new Blob([new byte[] { 0x61, 0xFF }]));
        Assert.Equal(["load"], events);
        Assert.Equal(FileReader.Done, reader.ReadyState);
        Assert.Equal("aÿ", reader.Result?.StringValue);
    }

    [Fact]
    public void BlobUrlStore_EntriesAreRemovedWithTheirDocument()
    {
        var document = Load("""
            <body><iframe id=frame srcdoc="<script>parent.document.body.dataset.url = URL.createObjectURL(new Blob(['x']));</script>"></iframe>
            """);
        var browsingContext = document.BrowsingContext!;
        var url = document.Body!.GetAttribute("data-url")!;
        var record = WebPlatform.Urls.UrlParser.Parse(url, baseUrl: null)!;
        Assert.NotNull(browsingContext.BlobUrls.Resolve(record));

        document.GetElementById("frame")!.Remove();
        Assert.Null(browsingContext.BlobUrls.Resolve(record));
    }

    [Fact]
    public void CreateObjectUrl_FromDotNet()
    {
        var document = Load("<p>");
        Document.CurrentGlobalDocument = document;
        try
        {
            var url = Url.CreateObjectUrl(new Blob([Encoding.UTF8.GetBytes("<p>b")], new BlobPropertyBag { Type = "text/html" }));
            var record = WebPlatform.Urls.UrlParser.Parse(url, baseUrl: null)!;
            Assert.NotNull(document.BrowsingContext!.BlobUrls.Resolve(record));
            Url.RevokeObjectUrl(url);
            Assert.Null(document.BrowsingContext!.BlobUrls.Resolve(record));
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = null;
        }
    }
}
