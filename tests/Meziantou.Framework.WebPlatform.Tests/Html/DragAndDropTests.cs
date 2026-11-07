using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class DragAndDropTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

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
