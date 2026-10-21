using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Clipboard API and events (https://w3c.github.io/clipboard-apis/): navigator.clipboard, ClipboardItem and the copy, cut and paste actions.</summary>
public sealed class ClipboardTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, SystemClipboard? clipboard = null, Func<Document, ClipboardAccess, bool>? permission = null, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            Clipboard = clipboard,
            ClipboardPermission = permission,
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    private string Run(Document document)
    {
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void WriteText_ThenReadText()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <script>
              navigator.clipboard.writeText('hello')
                .then(() => navigator.clipboard.readText())
                .then(text => document.title = text + ':' + (navigator.clipboard === navigator.clipboard) + ':' + (navigator.clipboard instanceof EventTarget));
            </script>
            """, clipboard);

        Assert.Equal("hello:true:true", Run(document));
        Assert.Equal("hello", clipboard.ReadText());
        Assert.Equal("text/plain", Assert.Single(Assert.Single(clipboard.Items).Representations).Name);
    }

    [Fact]
    public void ReadText_WithoutText_IsRejectedWithNotFoundError()
    {
        var document = Load("""
            <script>
              navigator.clipboard.readText().then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("NotFoundError", Run(document));
    }

    [Fact]
    public void Read_ReturnsTheItemsOfTheSystemClipboard()
    {
        var clipboard = new SystemClipboard();
        clipboard.Write([new SystemClipboardItem(
        [
            new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes("text")),
            new SystemClipboardRepresentation("text/html", Encoding.UTF8.GetBytes("<p onclick='alert(1)'>html</p><script>alert(2)</script>")),
            new SystemClipboardRepresentation("application/x-unknown", Encoding.UTF8.GetBytes("ignored")),
        ])]);

        var document = Load("""
            <script>
              (async () => {
                const items = await navigator.clipboard.read();
                const item = items[0];
                const text = await (await item.getType('text/plain')).text();
                const html = await item.getType('text/html');
                const htmlText = await html.text();
                document.title = [items.length, item.types.join('|'), item.presentationStyle, text, html.type, htmlText.includes('script'), htmlText.includes('onclick'), htmlText.includes('<p>html</p>')].join();
              })();
            </script>
            """, clipboard);

        Assert.Equal("1,text/plain|text/html,unspecified,text,text/html,false,false,true", Run(document));
    }

    [Fact]
    public void Read_UnsanitizedHtml()
    {
        var clipboard = new SystemClipboard();
        clipboard.Write([new SystemClipboardItem([new SystemClipboardRepresentation("text/html", Encoding.UTF8.GetBytes("<p onclick='f()'>html</p>"))])]);
        var document = Load("""
            <script>
              (async () => {
                const [item] = await navigator.clipboard.read({ unsanitized: ['text/html'] });
                document.title = await (await item.getType('text/html')).text();
                await navigator.clipboard.read({ unsanitized: ['text/plain'] }).catch(e => document.title += ':' + e.name);
              })();
            </script>
            """, clipboard);

        Assert.Equal("<p onclick='f()'>html</p>:NotAllowedError", Run(document));
    }

    [Fact]
    public void GetType_AfterTheClipboardChanged_IsRejectedWithInvalidStateError()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("first");
        var document = Load("""
            <script>
              (async () => {
                const [item] = await navigator.clipboard.read();
                await navigator.clipboard.writeText('second');
                await item.getType('text/plain').catch(e => document.title = e.name);
              })();
            </script>
            """, clipboard);

        Assert.Equal("InvalidStateError", Run(document));
    }

    [Fact]
    public void Write_StandardAndWebCustomFormats()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <script>
              (async () => {
                await navigator.clipboard.write([new ClipboardItem({
                  'text/plain': 'plain',
                  'text/html': Promise.resolve(new Blob(['<b>bold</b>'], { type: 'text/html' })),
                  'web application/x-custom': new Blob(['custom'], { type: 'web application/x-custom' }),
                }, { presentationStyle: 'inline' })]);
                const [item] = await navigator.clipboard.read();
                const custom = await item.getType('web application/x-custom');
                document.title = [item.types.join('|'), custom.type, await custom.text()].join();
              })();
            </script>
            """, clipboard);

        Assert.Equal("text/plain|text/html|web application/x-custom,web application/x-custom,custom", Run(document));
        var item = Assert.Single(clipboard.Items);
        Assert.Equal(PresentationStyle.Inline, item.PresentationStyle);
        Assert.Equal(["text/plain", "text/html", "application/web;type=\"custom/format0\"", SystemClipboard.WebCustomFormatMapName], item.Representations.Select(r => r.Name));
        Assert.Equal("{\"application/x-custom\":\"application/web;type=\\u0022custom/format0\\u0022\"}", Encoding.UTF8.GetString(item.GetRepresentation(SystemClipboard.WebCustomFormatMapName)!.Data.Span));
    }

    [Fact]
    public void Write_RejectedData_IsRejectedWithNotAllowedError()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("unchanged");
        var document = Load("""
            <script>
              (async () => {
                const results = [];
                await navigator.clipboard.write([new ClipboardItem({ 'text/plain': Promise.reject(new Error()) })]).catch(e => results.push(e.name));
                await navigator.clipboard.write([new ClipboardItem({ 'application/x-unknown': 'data' })]).catch(e => results.push(e.name));
                await navigator.clipboard.write([new ClipboardItem({ 'text/plain': new Blob(['x'], { type: 'text/html' }) })]).catch(e => results.push(e.name));
                document.title = results.join();
              })();
            </script>
            """, clipboard);

        Assert.Equal("NotAllowedError,NotAllowedError,NotAllowedError", Run(document));
        Assert.Equal("unchanged", clipboard.ReadText());
    }

    [Fact]
    public void ClipboardItem_Constructor()
    {
        var document = Load("""
            <script>
              const results = [];
              for (const items of [{}, { 'not a type': 'x' }, { 'text/plain': 'a', 'TEXT/PLAIN': 'b' }]) {
                try { new ClipboardItem(items); results.push('created'); } catch (e) { results.push(e.name); }
              }
              const item = new ClipboardItem({ 'text/plain;charset=utf-8': 'a', 'web text/plain': 'b' });
              results.push(item.types.join('|'), Object.isFrozen(item.types), item.types === item.types);
              results.push(ClipboardItem.supports('text/html'), ClipboardItem.supports('web foo/bar'), ClipboardItem.supports('foo/bar'));
              item.getType('text/plain;charset=utf-8').then(blob => blob.text()).then(text => document.title = results.concat(text).join());
            </script>
            """);

        Assert.Equal("TypeError,TypeError,TypeError,text/plain;charset=utf-8|web text/plain,true,true,true,true,false,a", Run(document));
    }

    [Fact]
    public async Task ClipboardItem_DotNet()
    {
        var blob = new Blob(["<b>x</b>"], new BlobPropertyBag { Type = "text/html" });
        var item = new ClipboardItem([new KeyValuePair<string, StringOrBlob>("text/plain", "text"), new KeyValuePair<string, StringOrBlob>("text/html", blob)]);
        Assert.Equal(["text/plain", "text/html"], item.Types);
        Assert.Same(blob, await item.GetType("text/html"));
        Assert.Equal("text", await (await item.GetType("text/plain")).Text());
        var exception = await Assert.ThrowsAsync<DomException>(() => item.GetType("image/png"));
        Assert.Equal("NotFoundError", exception.Name);
        Assert.Throws<TypeErrorException>(() => { _ = item.GetType("not a type"); });
    }

    [Fact]
    public void Permission_Denied()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("secret");
        var document = Load("""
            <script>
              (async () => {
                const results = [];
                await navigator.clipboard.readText().catch(e => results.push(e.name));
                await navigator.clipboard.read().catch(e => results.push(e.name));
                await navigator.clipboard.writeText('x').then(() => results.push('written'));
                results.push(document.execCommand('paste'), document.queryCommandEnabled('paste'), document.queryCommandEnabled('copy'));
                document.title = results.join();
              })();
            </script>
            """, clipboard, permission: (_, access) => access == ClipboardAccess.Write);

        Assert.Equal("NotAllowedError,NotAllowedError,written,false,false,true", Run(document));
        Assert.Equal("x", clipboard.ReadText());
    }

    [Fact]
    public void InsecureContext_DoesNotExposeTheClipboard()
    {
        var document = Load("""
            <script>
              document.title = [typeof navigator, typeof navigator.clipboard, 'clipboard' in navigator, typeof ClipboardItem, typeof Clipboard, typeof ClipboardEvent, window.clientInformation === navigator, navigator.platform].join();
            </script>
            """, url: "http://example.com/");

        Assert.Equal("object,undefined,false,undefined,undefined,function,true,", document.Title);
    }

    [Fact]
    public void UnfocusedDocument_CannotUseTheAsyncClipboard()
    {
        var document = Load("""
            <iframe srcdoc="<script>parent.frameClipboard = navigator.clipboard;</script>"></iframe>
            <script>
              window.onload = () => frameClipboard.writeText('x').catch(e => document.title = e.name);
            </script>
            """);

        Assert.Equal("NotAllowedError", Run(document));
    }

    [Fact]
    public void ExecCommandCopy_CopiesTheSelection()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <p id=p>Hello <b>world</b></p>
            <script>
              const log = [];
              document.addEventListener('copy', e => log.push(e.type, e.isTrusted, e.composed, e.bubbles, e.cancelable, e.target.localName, e.clipboardData.types.length));
              navigator.clipboard.addEventListener('clipboardchange', e => log.push(e.type, e.types.join('|'), typeof e.changeId));
              getSelection().selectAllChildren(document.getElementById('p'));
              log.push(document.execCommand('copy'));
              document.title = log.join();
            </script>
            """, clipboard);

        Assert.Equal("copy,true,true,true,true,body,0,clipboardchange,text/plain|text/html,bigint,true", document.Title);
        Assert.Equal("Hello world", clipboard.ReadText());
        Assert.Equal("Hello <b>world</b>", Encoding.UTF8.GetString(Assert.Single(clipboard.Items).GetRepresentation("text/html")!.Data.Span));
    }

    [Fact]
    public void ExecCommandCopy_CanceledEvent_WritesTheClipboardData()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("previous");
        var document = Load("""
            <script>
              let saved;
              document.addEventListener('copy', e => {
                e.clipboardData.setData('text/plain', 'custom');
                e.clipboardData.setData('application/x-data', 'data');
                saved = e.clipboardData;
                e.preventDefault();
              });
              document.execCommand('copy');
              // The data is not available outside of the event handler
              document.title = saved.types.length + ',' + saved.getData('text/plain');
            </script>
            """, clipboard);

        Assert.Equal("0,", document.Title);
        Assert.Equal("custom", clipboard.ReadText());
        Assert.Equal(["text/plain", "application/x-data"], Assert.Single(clipboard.Items).Representations.Select(r => r.Name));
    }

    [Fact]
    public void ExecCommandCopy_CanceledEvent_ClearData_ClearsTheClipboard()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("previous");
        Load("""
            <script>
              document.addEventListener('copy', e => { e.clipboardData.clearData(); e.preventDefault(); });
              document.execCommand('copy');
            </script>
            """, clipboard);

        Assert.Empty(clipboard.Items);
    }

    [Fact]
    public void ExecCommandCut_RemovesTheSelectedTextOfATextControl()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <input id=input value="Hello world">
            <script>
              const input = document.getElementById('input');
              const log = [];
              input.addEventListener('cut', e => log.push(e.type, e.target === input));
              input.addEventListener('input', e => log.push(e.inputType));
              input.focus();
              input.setSelectionRange(5, 11);
              log.push(document.execCommand('cut'), input.value, input.selectionStart);
              document.title = log.join();
            </script>
            """, clipboard);

        Assert.Equal("cut,true,deleteByCut,true,Hello,5", document.Title);
        Assert.Equal(" world", clipboard.ReadText());
    }

    [Fact]
    public void ExecCommandCut_OutsideOfAnEditableContext_ReturnsFalse()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <p id=p>text</p>
            <script>
              getSelection().selectAllChildren(document.getElementById('p'));
              document.title = document.execCommand('cut') + ',' + document.getElementById('p').textContent;
            </script>
            """, clipboard);

        Assert.Equal("false,text", document.Title);
        Assert.Empty(clipboard.Items);
    }

    [Fact]
    public void ExecCommandPaste_InsertsTheTextInATextArea()
    {
        var clipboard = new SystemClipboard();
        clipboard.Write([new SystemClipboardItem(
        [
            new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes("pasted")),
            new SystemClipboardRepresentation("text/html", Encoding.UTF8.GetBytes("<i>pasted</i>")),
            new SystemClipboardRepresentation("image/png", new byte[] { 1, 2, 3 }),
        ])]);

        var document = Load("""
            <textarea id=t>ab</textarea>
            <script>
              const t = document.getElementById('t');
              const log = [];
              t.addEventListener('paste', e => {
                const data = e.clipboardData;
                log.push(data.types.join('|'), data.getData('text/html'), data.files.length, data.files[0].type, data.files[0].size);
                data.setData('text/plain', 'ignored');
              });
              t.addEventListener('input', e => log.push(e.inputType, e.data));
              t.focus();
              t.setSelectionRange(1, 1);
              log.push(document.execCommand('paste'), t.value, t.selectionStart);
              document.title = log.join();
            </script>
            """, clipboard);

        Assert.Equal("text/plain|text/html|Files,<i>pasted</i>,1,image/png,3,insertFromPaste,pasted,true,apastedb,7", document.Title);
    }

    [Fact]
    public void ExecCommandPaste_InsertsTheTextInAnEditingHost()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("XY");
        var document = Load("""
            <div id=e contenteditable>ab</div>
            <script>
              const e = document.getElementById('e');
              const log = [];
              // As the other commands, the paste command fires no beforeinput event
              e.addEventListener('beforeinput', ev => log.push(ev.type, ev.inputType));
              e.addEventListener('input', ev => log.push(ev.type, ev.inputType));
              getSelection().collapse(e.firstChild, 1);
              log.push(document.execCommand('paste'), e.textContent);
              document.title = log.join();
            </script>
            """, clipboard);

        Assert.Equal("input,insertFromPaste,true,aXYb", document.Title);
    }

    [Fact]
    public void ExecCommandPaste_CanceledEvent_DoesNotInsertTheText()
    {
        var clipboard = new SystemClipboard();
        clipboard.WriteText("XY");
        var document = Load("""
            <div id=e contenteditable>ab</div>
            <script>
              const e = document.getElementById('e');
              e.addEventListener('paste', ev => ev.preventDefault());
              getSelection().collapse(e.firstChild, 1);
              document.title = document.execCommand('paste') + ',' + e.textContent;
            </script>
            """, clipboard);

        Assert.Equal("false,ab", document.Title);
    }

    [Fact]
    public void SystemClipboardChange_FiresClipboardChange()
    {
        var clipboard = new SystemClipboard();
        var document = Load("""
            <script>
              const log = [];
              navigator.clipboard.addEventListener('clipboardchange', e => {
                log.push(e.types.join('|') || 'none', e.isTrusted, e.bubbles, e.cancelable);
                document.title = log.join();
              });
            </script>
            """, clipboard);

        // A change by another application
        clipboard.Write([new SystemClipboardItem([new SystemClipboardRepresentation("image/png", new byte[] { 1 }), new SystemClipboardRepresentation("image/svg+xml", new byte[] { 2 })])]);
        clipboard.Clear();
        Assert.Equal("image/png,true,false,false,none,true,false,false", Run(document));
    }

    [Fact]
    public void ClipboardChangeEvent_Constructor()
    {
        var document = Load("""
            <script>
              const types = ['text/plain'];
              const e = new ClipboardChangeEvent('clipboardchange', { types, changeId: 12345678901234567890n });
              types.push('x');
              const d = new ClipboardChangeEvent('x');
              document.title = [e.types.join('|'), e.changeId, typeof e.changeId, d.types.length, d.changeId, new ClipboardEvent('copy').clipboardData].join();
            </script>
            """);

        Assert.Equal("text/plain,12345678901234567890,bigint,0,0,", document.Title);
    }

    [Fact]
    public void ExecCommandCopy_WithoutScriptEngine()
    {
        var clipboard = new SystemClipboard();
        var context = _contexts.Create(new BrowsingContextOptions { Clipboard = clipboard });
        var document = context.Navigate("https://example.com/", _ => "<p id=p>copied text</p>");
        _contexts.EventLoop.RunUntilIdle();

        var events = new List<string>();
        document.AddEventListener("copy", e => events.Add(e.Type + ":" + e.IsTrusted));
        document.GetSelection()!.SelectAllChildren(document.GetElementById("p")!);
        Assert.True(document.ExecCommand("copy"));
        Assert.Equal(["copy:True"], events);
        Assert.Equal("copied text", clipboard.ReadText());
    }
}
