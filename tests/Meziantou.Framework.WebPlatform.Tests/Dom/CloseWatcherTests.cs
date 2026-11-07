using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-requests-and-close-watchers</summary>
public sealed class CloseWatcherTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static T Get<T>(Document document, string id)
        where T : Element => (T)document.GetElementById(id)!;

    private static CloseWatcher CreateWatcher(Document document, List<string> events, string name, bool preventCancel = false)
    {
        var watcher = new CloseWatcher(document.DefaultView!);
        watcher.AddEventListener("cancel", e =>
        {
            events.Add($"{name} cancel {e.Cancelable}");
            if (preventCancel)
            {
                e.PreventDefault();
            }
        });
        watcher.AddEventListener("close", e => events.Add($"{name} close {e.Cancelable}"));
        return watcher;
    }

    /// <summary>Dispatches the trusted keyboard events of a key, as the host does for the user input.</summary>
    private static void PressKey(Document document, string key)
    {
        foreach (var type in new[] { "keydown", "keyup" })
        {
            var @event = new KeyboardEvent(type, new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true }) { IsTrusted = true };
            EventDispatcher.Dispatch(@event, document.ActiveElement ?? document.DocumentElement!);
        }
    }

    [Fact]
    public void RequestClose_FiresCancelThenClose()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        var watcher = CreateWatcher(document, events, "w");

        watcher.RequestClose();
        Assert.Equal(["w cancel True", "w close False"], events);

        // The watcher is no longer active
        watcher.RequestClose();
        watcher.Close();
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void RequestClose_CancelPreventsTheClose()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        var watcher = CreateWatcher(document, events, "w", preventCancel: true);

        watcher.RequestClose();
        watcher.RequestClose();
        Assert.Equal(["w cancel True", "w cancel True"], events);

        watcher.Close();
        Assert.Equal(["w cancel True", "w cancel True", "w close False"], events);
    }

    [Fact]
    public void Destroy_FiresNoEvent()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        var watcher = CreateWatcher(document, events, "w");

        watcher.Destroy();
        watcher.RequestClose();
        watcher.Close();
        PressKey(document, "Escape");
        Assert.Empty(events);
    }

    [Fact]
    public void SelectPicker_HasACloseWatcherOfTheWindow()
    {
        var document = Load("<select id=s><option>a<option>b</select>");
        var select = Get<HtmlSelectElement>(document, "s");
        var manager = document.DefaultView!.CloseWatcherManager;
        select.Focus();
        select.OpenPicker(focusVisible: true);
        Assert.True(select.Matches(":open"));
        Assert.Single(manager.Groups);

        // The close request closes the picker, and nothing watches the next one
        PressKey(document, "Escape");
        Assert.False(select.Matches(":open"));
        Assert.Empty(manager.Groups);
        Assert.Same(select, document.ActiveElement);

        // A close watcher created while the picker is open, without user activation, is in the group of the picker: one
        // close request closes both
        select.OpenPicker(focusVisible: true);
        var events = new List<string>();
        CreateWatcher(document, events, "w");
        Assert.Single(manager.Groups);
        PressKey(document, "Escape");
        Assert.Equal(["w cancel False", "w close False"], events);
        Assert.False(select.Matches(":open"));
        Assert.Empty(manager.Groups);

        // A picker that is closed otherwise has no close watcher anymore
        select.OpenPicker(focusVisible: true);
        select.ClosePicker(focusSelect: true);
        Assert.Empty(manager.Groups);
    }

    [Fact]
    public void Signal_DestroysTheWatcher()
    {
        var document = Load("<p>content</p>");
        var controller = new AbortController();
        var watcher = new CloseWatcher(document.DefaultView!, new CloseWatcherOptions { Signal = controller.Signal });
        var closed = false;
        watcher.OnClose = _ => closed = true;

        controller.Abort();
        watcher.RequestClose();
        Assert.False(closed);
    }

    [Fact]
    public void Escape_WithoutUserActivation_CancelIsNotCancelable()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        CreateWatcher(document, events, "w", preventCancel: true);

        // The Escape key is not an activation triggering input event
        PressKey(document, "Escape");
        Assert.Equal(["w cancel False", "w close False"], events);
    }

    [Fact]
    public void Escape_WithUserActivation_CancelCanPreventTheCloseOnce()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        CreateWatcher(document, events, "w", preventCancel: true);

        PressKey(document, "a");
        PressKey(document, "Escape");
        Assert.Equal(["w cancel True"], events);

        // Preventing the close consumed the history-action activation
        PressKey(document, "Escape");
        Assert.Equal(["w cancel True", "w cancel False", "w close False"], events);
    }

    [Fact]
    public void Escape_CanceledKeydown_IsNotACloseRequest()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        CreateWatcher(document, events, "w");
        document.AddEventListener("keydown", e => e.PreventDefault());

        PressKey(document, "Escape");
        Assert.Empty(events);
    }

    [Fact]
    public void Escape_WatchersCreatedWithoutUserActivationAreGrouped()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        CreateWatcher(document, events, "w1");
        CreateWatcher(document, events, "w2");
        CreateWatcher(document, events, "w3");

        PressKey(document, "Escape");
        Assert.Equal(["w3 cancel False", "w3 close False", "w2 cancel False", "w2 close False", "w1 cancel False", "w1 close False"], events);
    }

    [Fact]
    public void Escape_UserActivationAllowsANewGroup()
    {
        var document = Load("<p>content</p>");
        var events = new List<string>();
        CreateWatcher(document, events, "w1");
        PressKey(document, "a");
        CreateWatcher(document, events, "w2");

        // One activation allows one more group; the third watcher joins the group of the second one
        CreateWatcher(document, events, "w3");

        PressKey(document, "Escape");
        Assert.Equal(["w3 cancel False", "w3 close False", "w2 cancel False", "w2 close False"], events);

        events.Clear();
        PressKey(document, "Escape");
        Assert.Equal(["w1 cancel False", "w1 close False"], events);
    }

    [Fact]
    public void Escape_ClosesTheDialogAndThePopoverOfTheLastGroup()
    {
        var document = Load("<dialog id=dialog>dialog</dialog><div id=popover popover>popover</div>");
        var events = new List<string>();
        var dialog = Get<HtmlDialogElement>(document, "dialog");
        var popover = Get<HtmlElement>(document, "popover");
        dialog.AddEventListener("cancel", e => events.Add($"dialog cancel {e.Cancelable}"));
        dialog.AddEventListener("close", _ => events.Add("dialog close"));

        CreateWatcher(document, events, "w");
        PressKey(document, "a");
        dialog.ShowModal();
        popover.ShowPopover();

        // The dialog and the popover are in the same group: the watcher created before the activation stays
        PressKey(document, "Escape");
        _contexts.EventLoop.RunUntilIdle();
        Assert.False(popover.Matches(":popover-open"));
        Assert.False(dialog.Open);
        Assert.Equal(["dialog cancel False", "dialog close"], events);

        events.Clear();
        PressKey(document, "Escape");
        Assert.Equal(["w cancel False", "w close False"], events);
    }

    [Fact]
    public void Escape_DialogClosedByNone_IsNotClosed()
    {
        var document = Load("<dialog id=dialog closedby=none>dialog</dialog>");
        var dialog = Get<HtmlDialogElement>(document, "dialog");
        var events = new List<string>();
        CreateWatcher(document, events, "w");
        dialog.ShowModal();

        // The disabled close watcher of the dialog is skipped: the request reaches the next watcher of the group
        PressKey(document, "Escape");
        Assert.True(dialog.Open);
        Assert.Equal(["w cancel False", "w close False"], events);

        // requestClose() enables the close watcher of the dialog
        dialog.RequestClose();
        Assert.False(dialog.Open);
    }

    [Fact]
    public void Constructor_DocumentThatIsNotFullyActive_Throws()
    {
        var document = Load("<iframe id=frame srcdoc='<p>frame</p>'></iframe>");
        var frame = Get<HtmlIFrameElement>(document, "frame");
        var window = frame.ContentWindow!;
        var events = new List<string>();
        var watcher = new CloseWatcher(window);
        watcher.OnCancel = _ => events.Add("cancel");
        watcher.OnClose = _ => events.Add("close");
        frame.Remove();

        watcher.RequestClose();
        watcher.Close();
        Assert.Empty(events);
        var exception = Assert.Throws<DomException>(() => new CloseWatcher(window));
        Assert.Equal("InvalidStateError", exception.Name);
    }
}
