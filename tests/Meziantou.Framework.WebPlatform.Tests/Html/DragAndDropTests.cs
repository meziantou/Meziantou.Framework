using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class DragAndDropTests : IDisposable
{
    // Two boxes stacked in the viewport: #a (and #source) from y=0 to 100, #b (and #target) from y=100 to 200
    private const string Style = "<!DOCTYPE html><style>body { margin: 0 } body > * { display: block; width: 100px; height: 100px; margin: 0 }</style>";

    private readonly BrowsingContextScope _contexts = new();
    private ScriptEngine? _engine;

    public void Dispose()
    {
        _contexts.Dispose();
        if (_engine is not null)
        {
            TestScriptEngines.Dispose(_engine);
        }
    }

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private Document LoadWithScripts(string html)
    {
        _engine = TestScriptEngines.Create();
        var document = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine }).Navigate("https://example.com/", _ => Style + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private string? Evaluate(Document document, string code)
    {
        _contexts.EventLoop.RunUntilIdle();
        return TestScriptEngines.Evaluate(_engine!, document.DefaultView!, code);
    }

    private static List<string> Log(Document document, params string[] types)
    {
        var events = new List<string>();
        foreach (var type in types)
        {
            document.AddEventListener(type, e => events.Add($"{e.Type}@{(e.Target as Element)?.Id}{(e.IsTrusted ? "" : " untrusted")}"), new AddEventListenerOptions { Capture = true });
        }

        return events;
    }

    private static readonly string[] DragEventTypes = ["dragstart", "drag", "dragenter", "dragleave", "dragover", "drop", "dragend"];

    private static readonly DragPointerState Pressed = new(10, 10, Buttons: 1);

    [Fact]
    public void DataTransfer_Items()
    {
        var dataTransfer = new DataTransfer();
        Assert.Equal("none", dataTransfer.DropEffect);
        Assert.Equal("none", dataTransfer.EffectAllowed);

        dataTransfer.SetData("Text", "hello");
        dataTransfer.Items.Add(new Meziantou.Framework.WebPlatform.Dom.FileApi.File(["abc"], "a.txt"));
        Assert.Equal(["text/plain", "Files"], dataTransfer.Types);
        Assert.Equal("hello", dataTransfer.GetData("text/plain"));
        Assert.Single(dataTransfer.Files);
        Assert.Equal(2u, dataTransfer.Items.Length);
        Assert.Equal("string", dataTransfer.Items[0]!.Kind);

        var types = dataTransfer.Types;
        Assert.Same(types, dataTransfer.Types);
        Assert.Throws<DomException>(() => dataTransfer.Items.Add("other", "TEXT/PLAIN"));

        var item = dataTransfer.Items[1]!;
        dataTransfer.Items.Remove(1);
        Assert.Equal("", item.Kind);
        Assert.Null(item.GetAsFile());
        Assert.NotSame(types, dataTransfer.Types);

        dataTransfer.SetData("text/uri-list", "# comment\r\nhttps://example.com/\r\nhttps://example.org/");
        Assert.Equal("https://example.com/", dataTransfer.GetData("URL"));
    }

    [Fact]
    public void DragAndDrop_Events()
    {
        var document = Load("<div id=source draggable=true>drag</div><div id=target>drop</div>");
        var source = document.GetElementById("source")!;
        var target = document.GetElementById("target")!;
        var events = new List<string>();
        foreach (var type in new[] { "dragstart", "drag", "dragenter", "dragleave", "dragover", "drop", "dragend", "pointercancel" })
        {
            document.AddEventListener(type, e => events.Add($"{e.Type}@{((Element)e.Target!).Id}"));
        }

        source.AddEventListener("dragstart", e =>
        {
            var dataTransfer = ((DragEvent)e).DataTransfer!;
            dataTransfer.SetData("text/plain", "payload");
            dataTransfer.EffectAllowed = "move";
        });
        target.AddEventListener("dragover", e => e.PreventDefault());
        string? dropped = null;
        target.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            dropped = ((DragEvent)e).DataTransfer!.GetData("text/plain");
        });
        string? dragEndEffect = null;
        source.AddEventListener("dragend", e => dragEndEffect = ((DragEvent)e).DataTransfer!.DropEffect);

        var operation = DragAndDropOperation.TryStart(source, Pressed);
        Assert.NotNull(operation);
        operation.Update(source, Pressed);
        operation.Update(target, Pressed);
        operation.End(Pressed with { Buttons = 0 });

        Assert.True(operation.IsEnded);
        Assert.Equal("payload", dropped);
        Assert.Equal("move", dragEndEffect);
        Assert.Equal(
            [
                "dragstart@source", "pointercancel@source",
                "drag@source", "dragenter@source", "dragover@source",
                "drag@source", "dragenter@target", "dragleave@source", "dragover@target",
                "drag@source", "drop@target", "dragend@source",
            ],
            events);
    }

    [Fact]
    public void DragAndDrop_NotDropped()
    {
        var document = Load("<a id=link href=/page>link</a><p id=target>text</p>");
        var link = document.GetElementById("link")!;
        string? uriList = null;
        link.AddEventListener("dragstart", e => uriList = ((DragEvent)e).DataTransfer!.GetData("text/uri-list"));
        var events = new List<string>();
        document.AddEventListener("drop", e => events.Add(e.Type));
        document.AddEventListener("dragleave", e => events.Add(e.Type));
        document.AddEventListener("dragend", e => events.Add(e.Type + ":" + ((DragEvent)e).DataTransfer!.DropEffect));

        var operation = DragAndDropOperation.TryStart(link.FirstChild!, Pressed)!;
        operation.Update(document.GetElementById("target"), Pressed);
        operation.End(Pressed);

        Assert.Equal("https://example.com/page", uriList);
        Assert.Equal(["dragleave", "dragend:none"], events);
    }

    [Fact]
    public void DragAndDrop_NothingDraggedOrCanceled()
    {
        var document = Load("<div id=plain>text</div><div id=source draggable=true><input id=input></div>");
        Assert.Null(DragAndDropOperation.TryStart(document.GetElementById("plain")!, Pressed));
        Assert.Null(DragAndDropOperation.TryStart(document.GetElementById("input")!, Pressed));

        var source = document.GetElementById("source")!;
        source.AddEventListener("dragstart", e => e.PreventDefault());
        Assert.Null(DragAndDropOperation.TryStart(source, Pressed));
    }

    [Fact]
    public void Mouse_DragsAnElementAndDropsIt()
    {
        var document = Load(Style + "<div id=source draggable=true></div><div id=target></div>");
        var source = document.GetElementById("source")!;
        var target = document.GetElementById("target")!;
        source.AddEventListener("dragstart", e => ((DragEvent)e).DataTransfer!.SetData("text/plain", "payload"));
        target.AddEventListener("dragover", e => e.PreventDefault());
        string? dropped = null;
        target.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            dropped = ((DragEvent)e).DataTransfer!.GetData("text/plain");
        });
        var events = Log(document, [.. DragEventTypes, "pointerup", "mouseup", "click"]);

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        mouse.Down();
        mouse.Move(50, 60);
        mouse.Move(50, 150);
        mouse.Up();

        Assert.Equal("payload", dropped);
        Assert.Equal(
            [
                "dragstart@source", "drag@source", "dragenter@source", "dragover@source",
                "drag@source", "dragenter@target", "dragleave@source", "dragover@target",
                "drag@source", "drop@target", "dragend@source",
            ],
            events);
    }

    [Fact]
    public void Mouse_DragsTheSelectionOfATextControl()
    {
        var document = Load(Style + "<input id=source value='Drag me'><div id=target></div>");
        var source = (HtmlInputElement)document.GetElementById("source")!;
        var target = document.GetElementById("target")!;
        target.AddEventListener("dragover", e => e.PreventDefault());
        string? dropped = null;
        target.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            dropped = ((DragEvent)e).DataTransfer!.GetData("text/plain");
        });
        var events = Log(document, "dragstart", "drop", "dragend");
        source.Select();
        source.Focus();

        // A press in the selected text keeps the selection, and the moves drag it: the input element is the source node
        var mouse = document.BrowsingContext!.Input.Mouse;
        var rect = source.GetBoundingClientRect();
        mouse.Move(rect.X + (rect.Width / 4), rect.Y + (rect.Height / 2));
        mouse.Down();
        mouse.Move(50, 150);
        mouse.Up();

        Assert.Equal("Drag me", dropped);
        Assert.Equal(["dragstart@source", "drop@target", "dragend@source"], events);
    }

    [Fact]
    public void Mouse_ClickInTheSelectionOfATextControlCollapsesIt()
    {
        var document = Load(Style + "<input id=source value='Drag me'>");
        var source = (HtmlInputElement)document.GetElementById("source")!;
        source.Select();
        source.Focus();

        var mouse = document.BrowsingContext!.Input.Mouse;
        var rect = source.GetBoundingClientRect();
        mouse.Move(rect.X + (rect.Width / 4), rect.Y + (rect.Height / 2));
        mouse.Down();
        Assert.Equal(((uint?)0, (uint?)7), (source.SelectionStart, source.SelectionEnd));
        mouse.Up();

        Assert.True(source.SelectionStart > 0);
        Assert.Equal(source.SelectionStart, source.SelectionEnd);
    }

    [Fact]
    public void Mouse_EscapeCancelsTheDrag()
    {
        var document = Load(Style + "<div id=source draggable=true></div><div id=target></div>");
        document.GetElementById("target")!.AddEventListener("dragover", e => e.PreventDefault());
        var events = Log(document, "dragleave", "drop", "dragend", "keydown", "keyup");
        string? dropEffect = null;
        document.AddEventListener("dragend", e => dropEffect = ((DragEvent)e).DataTransfer!.DropEffect);

        var input = document.BrowsingContext!.Input;
        input.Mouse.Move(50, 50);
        input.Mouse.Down();
        input.Mouse.Move(50, 150);

        // The drag consumes the keys pressed during it, their repeats and their releases, even after Escape ends it
        input.Keyboard.Press("a");
        input.Keyboard.Down("b");
        input.Keyboard.Press("Escape");
        input.Keyboard.Down("b");
        input.Keyboard.Up("b");
        input.Mouse.Up();
        input.Keyboard.Press("c");

        Assert.Equal(["dragleave@target", "dragend@source", "keydown@", "keyup@"], events);
        Assert.Equal("none", dropEffect);
    }

    [Fact]
    public void Mouse_DragAndDropWithScripts()
    {
        var document = LoadWithScripts("""
            <div id=first draggable=true>first</div><div id=second>second</div>
            <script>
            window.log = [];
            const first = document.getElementById('first');
            first.addEventListener('dragstart', e => {
                e.dataTransfer.setData('text/plain', e.target.id);
                e.dataTransfer.effectAllowed = 'move';
                log.push('dragstart:' + e.isTrusted);
            });
            const second = document.getElementById('second');
            second.addEventListener('dragover', e => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; });
            second.addEventListener('drop', e => {
                e.preventDefault();
                const id = e.dataTransfer.getData('text/plain');
                second.after(document.getElementById(id));
                log.push('drop:' + e.isTrusted + ':' + id + ':' + e.clientY);
            });
            first.addEventListener('dragend', e => log.push('dragend:' + e.dataTransfer.dropEffect));
            </script>
            """);

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        mouse.Down();
        mouse.Move(50, 60);
        mouse.Move(50, 150);
        mouse.Up();

        Assert.Equal("dragstart:true|drop:true:first:150|dragend:move", Evaluate(document, "log.join('|')"));
        Assert.Equal("second,first", Evaluate(document, "[...document.querySelectorAll('div')].map(div => div.id).join()"));
    }

    [Fact]
    public void Drop_FilesFromOutside()
    {
        var document = Load(Style + "<div id=a></div><div id=b></div>");
        var b = document.GetElementById("b")!;
        b.AddEventListener("dragover", e => e.PreventDefault());
        var files = new List<string>();
        IReadOnlyList<string>? types = null;
        var dragOverFileCount = -1;
        b.AddEventListener("dragover", e => dragOverFileCount = ((DragEvent)e).DataTransfer!.Files.Count);
        b.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            var dataTransfer = ((DragEvent)e).DataTransfer!;
            types = dataTransfer.Types;
            foreach (var file in dataTransfer.Files)
            {
                files.Add($"{file.Name} {file.Type} {file.Size}");
            }
        });
        var events = Log(document, [.. DragEventTypes, "pointermove", "mousemove", "pointerup", "mouseup", "click"]);

        var data = new DragData();
        data.AddFile("notes.txt", "hello"u8.ToArray());
        data.AddFile("data.bin", new byte[] { 1, 2, 3 }, type: "application/x-custom");
        data.SetString("text/plain", "notes.txt");

        var mouse = document.BrowsingContext!.Input.Mouse;
        mouse.Move(50, 50);
        events.Clear();
        mouse.BeginDrag(data);
        mouse.Move(50, 150);
        mouse.Up();

        // There is no dragstart, drag or dragend (the source is outside the page), and no pointer event
        Assert.Equal(["dragenter@a", "dragover@a", "dragenter@b", "dragleave@a", "dragover@b", "drop@b"], events);
        Assert.Equal(0, dragOverFileCount);
        Assert.Equal(["Files", "text/plain"], types!.Order(StringComparer.Ordinal));
        Assert.Equal(["notes.txt text/plain 5", "data.bin application/x-custom 3"], files);
        Assert.Equal(0, mouse.Buttons);
    }

    [Fact]
    public void Drop_NotAcceptedFiresDragLeave()
    {
        var document = Load(Style + "<div id=a></div>");
        var events = Log(document, DragEventTypes);
        var data = new DragData();
        data.AddFile("a.txt", "a"u8.ToArray());

        document.BrowsingContext!.Input.Drop(50, 50, data);

        Assert.Equal(["dragenter@a", "dragover@a", "dragleave@a"], events);
    }

    [Fact]
    public void Drop_EscapeCancelsADragFromOutside()
    {
        var document = Load(Style + "<div id=a></div>");
        document.GetElementById("a")!.AddEventListener("dragover", e => e.PreventDefault());
        var events = Log(document, [.. DragEventTypes, "keydown"]);
        var data = new DragData();
        data.AddFile("a.txt", "a"u8.ToArray());

        var input = document.BrowsingContext!.Input;
        input.Mouse.Move(50, 50);
        input.Mouse.BeginDrag(data);
        input.Keyboard.Press("Escape");
        input.Mouse.Up();

        Assert.Equal(["dragenter@a", "dragover@a", "dragleave@a"], events);
        Assert.Throws<InvalidOperationException>(() => input.CreatePointer("touch").BeginDrag(data));
    }

    [Fact]
    public void Drop_DirectoryReadWithTheEntriesApi()
    {
        var document = LoadWithScripts("""
            <div id=zone></div>
            <script>
            window.log = [];
            const zone = document.getElementById('zone');
            zone.addEventListener('dragover', e => e.preventDefault());
            zone.addEventListener('drop', e => {
                e.preventDefault();
                log.push('files:' + [...e.dataTransfer.files].map(f => f.name + '/' + f.size + '/' + f.type).join());
                for (const item of e.dataTransfer.items) {
                    const entry = item.webkitGetAsEntry();
                    log.push(entry.constructor.name + ':' + entry.name + ':' + entry.fullPath + ':' + entry.isDirectory);
                    if (entry.isDirectory) {
                        walk(entry);
                        entry.getFile('sub/../sub/b.txt', {}, file => file.file(f => f.text().then(t => log.push('getFile:' + file.fullPath + '=' + t))));
                        entry.getFile('missing.txt', {}, undefined, err => log.push('missing:' + err.name));
                        entry.getFile('sub', {}, undefined, err => log.push('directory:' + err.name));
                        entry.getFile('a.txt', { create: true }, undefined, err => log.push('create:' + err.name));
                        entry.getDirectory('sub\\b', {}, undefined, err => log.push('backslash:' + err.name));
                        entry.getDirectory('/', {}, root => root.createReader().readEntries(entries => log.push('root:' + entries.map(e => e.name).join())));
                    }
                }
            });
            function walk(directory) {
                const reader = directory.createReader();
                reader.readEntries(entries => {
                    for (const entry of entries) {
                        if (entry.isDirectory) {
                            walk(entry);
                        } else {
                            entry.file(file => log.push('file:' + entry.fullPath + ':' + file.name + ':' + file.size));
                            entry.getParent(parent => log.push('parent:' + entry.name + ':' + parent.fullPath));
                        }
                    }

                    reader.readEntries(rest => log.push('end:' + directory.fullPath + ':' + rest.length));
                });
            }
            </script>
            """);

        var data = new DragData();
        var folder = data.AddDirectory("folder");
        folder.AddFile("a.txt", "abc"u8.ToArray());
        folder.AddDirectory("sub").AddFile("b.txt", "hello"u8.ToArray());
        data.AddFile("c.png", new byte[4]);
        document.BrowsingContext!.Input.Drop(50, 50, data);

        var log = Evaluate(document, "log.sort().join('|')")!.Split('|');
        Assert.Equal(
            [
                "FileSystemDirectoryEntry:folder:/folder:true",
                "FileSystemFileEntry:c.png:/c.png:false",
                "backslash:TypeMismatchError",
                "create:SecurityError",
                "directory:TypeMismatchError",
                "end:/folder/sub:0",
                "end:/folder:0",
                "file:/folder/a.txt:a.txt:3",
                "file:/folder/sub/b.txt:b.txt:5",
                "files:folder/0/,c.png/4/image/png",
                "getFile:/folder/sub/b.txt=hello",
                "missing:NotFoundError",
                "parent:a.txt:/folder",
                "parent:b.txt:/folder/sub",
                "root:folder,c.png",
            ],
            log);
    }

    [Fact]
    public void Entries_OfTheFilesOfAScriptCreatedDataTransfer()
    {
        var document = LoadWithScripts("""
            <script>
            window.log = [];
            const file = new File(['hello'], 'a.txt', { type: 'text/plain' });
            const dataTransfer = new DataTransfer();
            dataTransfer.items.add(file);
            dataTransfer.items.add(new File(['x'], 'b.txt'));
            dataTransfer.items.add('text', 'text/plain');
            dataTransfer.items.add(new File([], 'future.txt', { lastModified: 1e15 }));
            dataTransfer.items[3].webkitGetAsEntry().file(f => log.push('lastModified:' + f.lastModified));

            // Two files with the same name: each entry gives its own file, and a removed file is not found
            const other = new DataTransfer();
            other.items.add(new File(['first'], 'same.txt'));
            other.items.add(new File(['second!'], 'same.txt'));
            const second = other.items[1].webkitGetAsEntry();
            second.file(f => log.push('duplicate:' + f.size));
            const removed = other.items[0].webkitGetAsEntry();
            other.items.remove(0);
            removed.file(() => log.push('removed:found'), e => log.push('removed:' + e.name));
            const entry = dataTransfer.items[0].webkitGetAsEntry();
            log.push(entry.fullPath + ':' + (dataTransfer.items[2].webkitGetAsEntry() === null));
            entry.file(f => log.push('same:' + (f === file)));
            entry.filesystem.root.createReader().readEntries(entries => log.push('root:' + entries.map(e => e.fullPath).join()));
            </script>
            """);

        Assert.Equal("/a.txt:true|lastModified:1000000000000000|duplicate:7|removed:NotFoundError|same:true|root:/a.txt,/b.txt,/future.txt", Evaluate(document, "log.join('|')"));
    }

    [Fact]
    public void Drop_DirectoryReadWithFileSystemHandles()
    {
        var document = LoadWithScripts("""
            <div id=zone></div>
            <script>
            window.log = [];
            const zone = document.getElementById('zone');
            zone.addEventListener('dragover', e => e.preventDefault());
            zone.addEventListener('drop', async e => {
                e.preventDefault();
                const handles = await Promise.all([...e.dataTransfer.items].map(item => item.getAsFileSystemHandle()));
                for (const handle of handles) {
                    if (!handle) {
                        log.push('null');
                        continue;
                    }

                    log.push(handle.kind + ':' + handle.name);
                    if (handle.kind === 'directory') {
                        for await (const [name, child] of handle.entries()) {
                            log.push('child:' + name + ':' + child.kind);
                        }

                        const file = await (await handle.getFileHandle('a.txt')).getFile();
                        log.push('content:' + await file.text());
                    }
                }
            });
            </script>
            """);

        var data = new DragData();
        var folder = data.AddDirectory("folder");
        folder.AddFile("a.txt", "abc"u8.ToArray());
        folder.AddDirectory("sub");
        data.SetString("text/plain", "text");
        document.BrowsingContext!.Input.Drop(50, 50, data);

        Assert.Equal("directory:folder|child:a.txt:file|child:sub:directory|content:abc|null", Evaluate(document, "log.join('|')"));
    }

    [Fact]
    public void Drop_SelectsTheFilesOfAFileInput()
    {
        var document = Load(Style + "<input id=single type=file><input id=multiple type=file multiple><input id=directory type=file webkitdirectory>");
        var events = Log(document, "input", "change");
        var data = new DragData();
        data.AddFile("a.txt", "a"u8.ToArray());
        data.AddFile("b.txt", "b"u8.ToArray());
        data.AddDirectory("folder").AddDirectory("sub").AddFile("c.txt", "c"u8.ToArray());

        var input = document.BrowsingContext!.Input;
        var single = (HtmlInputElement)document.GetElementById("single")!;
        var multiple = (HtmlInputElement)document.GetElementById("multiple")!;
        var directory = (HtmlInputElement)document.GetElementById("directory")!;
        Drop(single);
        Drop(multiple);
        Drop(directory);

        Assert.Equal(["input@single", "change@single", "input@multiple", "change@multiple", "input@directory", "change@directory"], events);
        Assert.Equal(["a.txt"], single.Files!.Select(f => f.Name));
        Assert.Equal(["a.txt", "b.txt"], multiple.Files!.Select(f => f.Name));
        Assert.Equal(["/a.txt", "/b.txt"], multiple.WebkitEntries.Select(e => e.FullPath));
        Assert.Equal([":a.txt", ":b.txt", "folder/sub/c.txt:c.txt"], directory.Files!.Select(f => f.WebkitRelativePath + ":" + f.Name));
        Assert.True(directory.Webkitdirectory);

        void Drop(HtmlInputElement element)
        {
            var rect = element.GetBoundingClientRect();
            input.Drop(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), data);
        }
    }

    [Fact]
    public void Drop_Example()
    {
        #region WebPlatform_DropFiles
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>body { margin: 0 } #zone { width: 200px; height: 200px }</style>
            <div id="zone"></div>
            """);

        var dropped = new List<string>();
        var zone = document.GetElementById("zone")!;
        zone.AddEventListener("dragover", e => e.PreventDefault()); // The zone accepts the drop
        zone.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            foreach (var file in ((DragEvent)e).DataTransfer!.Files)
            {
                dropped.Add(file.Name);
            }
        });

        // The files and the folders that the user drags from the file manager (or DragData.AddFromDisk(path))
        var data = new DragData();
        data.AddFile("notes.txt", "hello"u8.ToArray());
        data.AddDirectory("photos").AddFile("cat.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        // The mouse enters the window with the button pressed, moves over the zone, and releases the button
        var mouse = context.Input.Mouse;
        mouse.Move(300, 300);
        mouse.BeginDrag(data);
        mouse.Move(100, 100);
        mouse.Up();
        Assert.Equal(["notes.txt", "photos"], dropped);

        // The same drop in one call
        context.Input.Drop(100, 100, data);
        Assert.Equal(["notes.txt", "photos", "notes.txt", "photos"], dropped);
        #endregion
    }

    [Fact]
    public void DragData_AddFromDisk()
    {
        var root = Directory.CreateTempSubdirectory("meziantou-webplatform-drop-");
        try
        {
            var folder = root.CreateSubdirectory("photos");
            System.IO.File.WriteAllText(Path.Combine(folder.FullName, "a.png"), "png");
            folder.CreateSubdirectory("2024");
            System.IO.File.WriteAllText(Path.Combine(folder.FullName, "2024", "b.txt"), "text");

            var data = new DragData();
            var directory = Assert.IsType<DragDataDirectory>(data.AddFromDisk(folder.FullName));
            Assert.Equal("photos", directory.Name);
            Assert.Equal(["2024", "a.png"], directory.Entries.Select(e => e.Name));
            var png = Assert.IsType<DragDataFile>(directory.Entries[1]);
            Assert.Equal("image/png", png.Type);
            Assert.Equal("png", Encoding.UTF8.GetString(png.Content.Span));
            Assert.NotNull(png.LastModified);
            Assert.Equal("b.txt", Assert.Single(Assert.IsType<DragDataDirectory>(directory.Entries[0]).Entries).Name);

            Assert.Throws<ArgumentException>(() => data.AddFromDisk(folder.FullName));
            Assert.Throws<FileNotFoundException>(() => data.AddFromDisk(Path.Combine(root.FullName, "missing")));
            Assert.Throws<ArgumentException>(() => data.AddFile("a/b", default));
            Assert.Throws<ArgumentException>(() => data.EffectAllowed = "invalid");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DragAndDrop_TextIntoTextControl()
    {
        var document = Load("<div id=source draggable=true>drag</div><textarea id=target>a</textarea>");
        document.GetElementById("source")!.AddEventListener("dragstart", e => ((DragEvent)e).DataTransfer!.SetData("text", "b"));
        var target = (HtmlTextAreaElement)document.GetElementById("target")!;

        var operation = DragAndDropOperation.TryStart(document.GetElementById("source")!, Pressed)!;
        operation.Update(target, Pressed);
        operation.End(Pressed);

        Assert.Equal("ab", target.Value);
    }
}
