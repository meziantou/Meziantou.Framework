using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class PopoverTests : IDisposable
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

    private static List<string> RecordToggleEvents(HtmlElement element)
    {
        var events = new List<string>();
        element.AddEventListener("beforetoggle", e => events.Add($"beforetoggle {((ToggleEvent)e).OldState}->{((ToggleEvent)e).NewState}"));
        element.AddEventListener("toggle", e => events.Add($"toggle {((ToggleEvent)e).OldState}->{((ToggleEvent)e).NewState}"));
        return events;
    }

    [Fact]
    public void ShowAndHidePopover_UpdatesStateAndFiresEvents()
    {
        var document = Load("<div id=p popover>content</div>");
        var popover = Get<HtmlElement>(document, "p");
        var events = RecordToggleEvents(popover);

        Assert.Equal("auto", popover.Popover);
        Assert.False(popover.Matches(":popover-open"));

        popover.ShowPopover();
        Assert.True(popover.Matches(":popover-open"));
        Assert.Equal(["beforetoggle closed->open"], events);

        popover.HidePopover();
        Assert.False(popover.Matches(":popover-open"));

        // The toggle events are coalesced: the popover is closed as it was before
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["beforetoggle closed->open", "beforetoggle open->closed", "toggle closed->closed"], events);
    }

    [Fact]
    public void ShowPopover_PopoverAttributeRemovedDuringTheFocusingSteps()
    {
        var document = Load("<div id=p popover><input id=input autofocus></div>");
        var popover = Get<HtmlElement>(document, "p");
        var events = RecordToggleEvents(popover);
        Get<HtmlElement>(document, "input").AddEventListener("focus", _ => popover.RemoveAttribute("popover"), new AddEventListenerOptions { Once = true });

        // Removing the attribute forgets the pending toggle event task: the toggle events are not coalesced
        popover.ShowPopover();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["beforetoggle closed->open", "beforetoggle open->closed", "toggle open->closed", "toggle closed->open"], events);
    }

    [Fact]
    public void ShowPopover_CanceledByBeforeToggle()
    {
        var document = Load("<div id=p popover=manual></div>");
        var popover = Get<HtmlElement>(document, "p");
        popover.AddEventListener("beforetoggle", e => e.PreventDefault());

        popover.ShowPopover();
        Assert.False(popover.Matches(":popover-open"));
    }

    [Fact]
    public void TogglePopover_WithForce()
    {
        var document = Load("<div id=p popover=manual></div>");
        var popover = Get<HtmlElement>(document, "p");

        Assert.True(popover.TogglePopover());
        Assert.False(popover.TogglePopover());
        Assert.True(popover.TogglePopover(force: true));
        Assert.True(popover.TogglePopover(new TogglePopoverOptions { Force = true }));
        Assert.False(popover.TogglePopover(force: false));
        Assert.False(popover.TogglePopover(force: false));
    }

    [Fact]
    public void PopoverMethods_ThrowOnInvalidElements()
    {
        var document = Load("<div id=d></div><div id=p popover></div>");
        var div = Get<HtmlElement>(document, "d");
        var ex = Assert.Throws<DomException>(() => div.ShowPopover());
        Assert.Equal("NotSupportedError", ex.Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(div.HidePopover).Name);

        var popover = Get<HtmlElement>(document, "p");
        popover.Remove();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => popover.ShowPopover()).Name);
        popover.HidePopover(); // Hiding a hidden popover does nothing

        // Popovers of documents without a browsing context are not fully active
        var inactive = HtmlDocumentParser.ParseDocument("<div id=p popover></div>");
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => Get<HtmlElement>(inactive, "p").ShowPopover()).Name);
    }

    [Fact]
    public void AutoPopovers_HideUnrelatedAutoPopovers()
    {
        var document = Load("""
            <div id=a popover><div id=nested popover></div></div>
            <div id=b popover></div>
            <div id=m popover=manual></div>
            """);
        var a = Get<HtmlElement>(document, "a");
        var nested = Get<HtmlElement>(document, "nested");
        var b = Get<HtmlElement>(document, "b");
        var manual = Get<HtmlElement>(document, "m");

        manual.ShowPopover();
        a.ShowPopover();
        nested.ShowPopover();
        Assert.True(a.Matches(":popover-open"));
        Assert.True(nested.Matches(":popover-open"));

        // b is not a descendant of a: the auto popover stack is hidden, manual popovers stay open
        b.ShowPopover();
        Assert.False(a.Matches(":popover-open"));
        Assert.False(nested.Matches(":popover-open"));
        Assert.True(b.Matches(":popover-open"));
        Assert.True(manual.Matches(":popover-open"));

        // The source makes the popover a child of the popover that contains the source
        var c = (HtmlElement)document.CreateElement("div");
        c.Popover = "auto";
        document.Body!.AppendChild(c);
        var sourceInB = (HtmlElement)b.AppendChild(document.CreateElement("button"));
        c.ShowPopover(new ShowPopoverOptions { Source = sourceInB });
        Assert.True(b.Matches(":popover-open"));
        Assert.True(c.Matches(":popover-open"));

        // Hiding a popover hides the popovers above it in the stack
        b.HidePopover();
        Assert.False(c.Matches(":popover-open"));

        // A showing popover passed as source is the ancestor of the new popover
        b.ShowPopover();
        c.ShowPopover(new ShowPopoverOptions { Source = b });
        Assert.True(b.Matches(":popover-open"));
        Assert.True(c.Matches(":popover-open"));
    }

    [Fact]
    public void RemovingOrChangingTheAttribute_HidesThePopover()
    {
        var document = Load("<div id=p popover></div>");
        var popover = Get<HtmlElement>(document, "p");
        var events = RecordToggleEvents(popover);

        popover.ShowPopover();
        popover.Popover = "manual";
        Assert.False(popover.Matches(":popover-open"));
        Assert.Contains("beforetoggle open->closed", events);

        popover.ShowPopover();
        popover.RemoveAttribute("popover");
        Assert.False(popover.Matches(":popover-open"));

        popover.Popover = "auto";
        popover.ShowPopover();
        popover.Remove();
        Assert.False(popover.Matches(":popover-open"));

        // The popover can be shown again once reinserted
        document.Body!.AppendChild(popover);
        popover.ShowPopover();
        Assert.True(popover.Matches(":popover-open"));
    }

    [Fact]
    public void PopoverTarget_TogglesThePopover()
    {
        var document = Load("""
            <button id=toggle popovertarget=p>Toggle</button>
            <button id=show popovertarget=p popovertargetaction=show>Show</button>
            <input id=hide type=button popovertarget=p popovertargetaction=hide>
            <div id=p popover></div>
            """);
        var popover = Get<HtmlElement>(document, "p");
        var toggle = Get<HtmlButtonElement>(document, "toggle");
        var events = new List<Element?>();
        popover.AddEventListener("beforetoggle", e => events.Add(((ToggleEvent)e).Source));

        Assert.Same(popover, toggle.PopoverTargetElement);
        toggle.Click();
        Assert.True(popover.Matches(":popover-open"));
        Get<HtmlButtonElement>(document, "show").Click();
        Assert.True(popover.Matches(":popover-open"));
        Get<HtmlInputElement>(document, "hide").Click();
        Assert.False(popover.Matches(":popover-open"));
        toggle.Click();
        toggle.Click();
        Assert.False(popover.Matches(":popover-open"));
        Assert.Equal([toggle, Get<HtmlInputElement>(document, "hide"), toggle, toggle], events);
    }

    [Fact]
    public void PopoverTarget_LabelWithoutControlInTheButton()
    {
        var document = Load("<button popovertarget=p><label id=label>label</label></button><div id=p popover></div>");
        var popover = Get<HtmlElement>(document, "p");

        // The label has no labeled control: the click activates the button
        Get<HtmlElement>(document, "label").Click();
        Assert.True(popover.Matches(":popover-open"));
    }

    [Fact]
    public void PopoverTarget_ClickInThePopoverNestedInTheButtonOfAShadowTree()
    {
        var document = Load("<div id=host></div>");
        var host = Get<HtmlElement>(document, "host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = ("<button popovertarget=p>Button<div popover id=p><span id=descendant>Popover</span></div></button>");
        var button = (HtmlButtonElement)shadowRoot.FirstChild!;
        var popover = (HtmlElement)shadowRoot.GetElementById("p")!;

        button.Click();
        Assert.True(popover.Matches(":popover-open"));

        // The activation behavior of the button sees the target of the event retargeted against the button, not the host
        ((HtmlElement)shadowRoot.GetElementById("descendant")!).Click();
        Assert.True(popover.Matches(":popover-open"));
    }

    [Fact]
    public void PopoverTarget_IgnoredForSubmitButtonsInForms()
    {
        var document = Load("<form><button id=s popovertarget=p>x</button><button id=b popovertarget=p type=button>x</button></form><div id=p popover></div>");
        var popover = Get<HtmlElement>(document, "p");

        // The submit button submits the form instead
        Get<HtmlButtonElement>(document, "s").Click();
        Assert.False(popover.Matches(":popover-open"));

        Get<HtmlButtonElement>(document, "b").Click();
        Assert.True(popover.Matches(":popover-open"));
    }

    [Fact]
    public void Command_FiresCommandEventAndRunsBuiltInCommands()
    {
        var document = Load("""
            <button id=b commandfor=p command=show-popover>Show</button>
            <button id=h commandfor=p command=HIDE-POPOVER>Hide</button>
            <button id=c commandfor=p command=--custom>Custom</button>
            <div id=p popover></div>
            """);
        var popover = Get<HtmlElement>(document, "p");
        var commands = new List<(string Command, Element? Source)>();
        popover.AddEventListener("command", e => commands.Add((((CommandEvent)e).Command, ((CommandEvent)e).Source)));

        var show = Get<HtmlButtonElement>(document, "b");
        Assert.Same(popover, show.CommandForElement);
        Assert.Equal("show-popover", show.Command);
        Assert.Equal("button", show.Type);

        show.Click();
        Assert.True(popover.Matches(":popover-open"));
        Get<HtmlButtonElement>(document, "h").Click();
        Assert.False(popover.Matches(":popover-open"));
        Get<HtmlButtonElement>(document, "c").Click();
        Assert.False(popover.Matches(":popover-open"));

        Assert.Equal([("show-popover", show), ("hide-popover", Get<HtmlButtonElement>(document, "h")), ("--custom", Get<HtmlButtonElement>(document, "c"))], commands);
    }

    [Fact]
    public void Command_CanceledEventDoesNotRunTheCommand()
    {
        var document = Load("<button id=b commandfor=p command=toggle-popover>x</button><div id=p popover></div>");
        var popover = Get<HtmlElement>(document, "p");
        popover.AddEventListener("command", e => e.PreventDefault());
        Get<HtmlButtonElement>(document, "b").Click();
        Assert.False(popover.Matches(":popover-open"));
    }

    [Fact]
    public void Command_InvalidCommandDoesNotFireEvent()
    {
        var document = Load("<button id=b commandfor=d command=show-popover>x</button><button id=u commandfor=d command=unknown>x</button><div id=d></div>");
        var target = Get<HtmlElement>(document, "d");
        var fired = 0;
        target.AddEventListener("command", _ => fired++);
        Get<HtmlButtonElement>(document, "u").Click();
        Assert.Equal(0, fired);

        // Popover commands are valid for any HTML element, even when it is not a popover
        Get<HtmlButtonElement>(document, "b").Click();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Command_DialogCommands()
    {
        var document = Load("""
            <button id=open commandfor=d command=show-modal>Open</button>
            <button id=close commandfor=d command=close value=done>Close</button>
            <button id=request commandfor=d command=request-close value=requested>Request close</button>
            <dialog id=d></dialog>
            """);
        var dialog = Get<HtmlDialogElement>(document, "d");
        var events = new List<string>();
        dialog.AddEventListener("beforetoggle", e => events.Add($"beforetoggle {((ToggleEvent)e).NewState} {((ToggleEvent)e).Source?.Id}"));
        dialog.AddEventListener("toggle", e => events.Add($"toggle {((ToggleEvent)e).NewState}"));
        dialog.AddEventListener("cancel", _ => events.Add("cancel"));
        dialog.AddEventListener("close", _ => events.Add("close"));

        Get<HtmlButtonElement>(document, "open").Click();
        Assert.True(dialog.Open);
        Assert.True(dialog.Matches(":modal"));
        _contexts.EventLoop.RunUntilIdle();

        Get<HtmlButtonElement>(document, "close").Click();
        Assert.False(dialog.Open);
        Assert.False(dialog.Matches(":modal"));
        Assert.Equal("done", dialog.ReturnValue);
        _contexts.EventLoop.RunUntilIdle();

        dialog.Show();
        Get<HtmlButtonElement>(document, "request").Click();
        Assert.False(dialog.Open);
        Assert.Equal("requested", dialog.ReturnValue);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(
            [
                "beforetoggle open open", "toggle open",
                "beforetoggle closed close", "toggle closed", "close",
                "beforetoggle open ", "cancel", "beforetoggle closed request", "toggle closed", "close",
            ],
            events);
    }

    [Fact]
    public void Dialog_ShowModalHidesPopovers()
    {
        var document = Load("<div id=p popover></div><dialog id=d></dialog>");
        var popover = Get<HtmlElement>(document, "p");
        var dialog = Get<HtmlDialogElement>(document, "d");

        popover.ShowPopover();
        dialog.ShowModal();
        Assert.False(popover.Matches(":popover-open"));
        Assert.True(dialog.Matches(":modal"));

        // A modal dialog cannot be shown as a popover
        dialog.Popover = "manual";
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => dialog.ShowPopover()).Name);
    }

    [Fact]
    public void CommandEvent_SourceIsRetargeted()
    {
        var document = Load("<div id=host></div><div id=p></div>");
        var host = Get<HtmlElement>(document, "host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var button = (HtmlButtonElement)shadowRoot.AppendChild(document.CreateElement("button"));
        var target = Get<HtmlElement>(document, "p");
        button.CommandForElement = target;
        button.Command = "--custom";

        CommandEvent? commandEvent = null;
        Element? sourceDuringDispatch = null;
        target.AddEventListener("command", e =>
        {
            commandEvent = (CommandEvent)e;
            sourceDuringDispatch = commandEvent.Source;
        });

        button.Click();
        Assert.Same(host, sourceDuringDispatch);
        Assert.Same(host, commandEvent!.Source);
    }

    [Fact]
    public void LightDismiss_ClickOutsideHidesTheAutoPopovers()
    {
        var document = Load("<span id=outside>outside</span><button id=invoker popovertarget=p>open</button><div id=p popover><span id=inside>inside</span></div><div id=m popover=manual>manual</div>");
        var popover = Get<HtmlElement>(document, "p");
        var manual = Get<HtmlElement>(document, "m");
        popover.ShowPopover();
        manual.ShowPopover();

        // A click inside the popover, or on its invoker, does not hide it
        Click(Get<Element>(document, "inside"));
        Click(Get<Element>(document, "invoker"), dispatchClick: false);
        Assert.True(popover.Matches(":popover-open"));

        // The pointerdown and pointerup events must have the same topmost clicked popover
        Press(Get<Element>(document, "inside"), "pointerdown");
        Press(Get<Element>(document, "outside"), "pointerup");
        Assert.True(popover.Matches(":popover-open"));

        // A click outside hides the auto popovers, even when the pointer events are canceled
        document.AddEventListener("pointerup", e => e.PreventDefault());
        Click(Get<Element>(document, "outside"));
        Assert.False(popover.Matches(":popover-open"));
        Assert.True(manual.Matches(":popover-open"));
    }

    [Fact]
    public void LightDismiss_ClosesTheDialogWithClosedByAny()
    {
        var document = Load("<span id=outside>outside</span><dialog id=d closedby=any><span id=inside>inside</span></dialog>");
        var dialog = Get<HtmlDialogElement>(document, "d");
        dialog.ShowModal();
        Click(Get<Element>(document, "inside"));
        Assert.True(dialog.Open);

        Click(Get<Element>(document, "outside"));
        Assert.False(dialog.Open);
    }

    [Fact]
    public void LightDismiss_ClickOnTheBackdropOfTheModalDialog()
    {
        var document = Load("<dialog id=d closedby=any>dialog</dialog>");
        var dialog = Get<HtmlDialogElement>(document, "d");
        dialog.ShowModal();

        // A click on the dialog outside of its border box targets its backdrop
        var box = dialog.GetBoundingClientRect();
        Click(dialog, (int)Math.Ceiling(box.X) + 1, (int)Math.Ceiling(box.Y) + 1);
        Assert.True(dialog.Open);
        Click(dialog, (int)Math.Floor(box.X) - 1, (int)box.Y + 1);
        Assert.False(dialog.Open);
    }

    private static void Click(Element target, int clientX, int clientY)
    {
        foreach (var type in new[] { "pointerdown", "pointerup" })
        {
            var @event = new PointerEvent(type, new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, ClientX = clientX, ClientY = clientY }) { IsTrusted = true };
            EventDispatcher.Dispatch(@event, target);
        }
    }

    /// <summary>The trusted pointer events of a click of the user (only trusted events light dismiss the popovers).</summary>
    private static void Click(Element target, bool dispatchClick = true)
    {
        Press(target, "pointerdown");
        Press(target, "pointerup");
        if (dispatchClick)
        {
            Press(target, "click");
        }
    }

    private static void Press(Element target, string type)
    {
        var @event = new PointerEvent(type, new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(@event, target);
    }
}
