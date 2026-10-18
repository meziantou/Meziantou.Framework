using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#focus</summary>
public sealed class FocusTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock });
        return context.Navigate("https://example.com/", _ => html);
    }

    /// <summary>Runs the event loop until the next rendering opportunity (the focus fixup rule and the autofocus run there).</summary>
    private void RunRenderingOpportunity()
    {
        _contexts.EventLoop.RunUntilIdle();
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
    }

    private static T Get<T>(Document document, string selector)
        where T : Element => (T)document.QuerySelector(selector)!;

    [Fact]
    public void Focus_FocusableElement()
    {
        var document = Load("<input id=a><div id=b></div><div id=c tabindex=-1></div><a id=d href=x></a><a id=e></a>");
        Assert.Same(document.Body, document.ActiveElement);
        Assert.True(document.HasFocus());

        foreach (var (id, focusable) in new[] { ("a", true), ("b", false), ("c", true), ("d", true), ("e", false) })
        {
            document.Body!.Focus();
            var element = Get<HtmlElement>(document, "#" + id);
            element.Focus();
            Assert.Equal(focusable, ReferenceEquals(element, document.ActiveElement));
            Assert.Equal(focusable, element.Matches(":focus"));
        }
    }

    [Fact]
    public void Focus_NotFocusableElements()
    {
        var document = Load("""
            <input id=disabled disabled>
            <input id=hidden type=hidden>
            <div hidden><input id=in-hidden></div>
            <input id=display-none style="display: none">
            <input id=invisible style="visibility: hidden">
            <div inert><input id=inert></div>
            <div style="display: contents" tabindex=0 id=contents></div>
            <select><option id=option tabindex=-1></option></select>
            """);

        // The options of a select that is not a customizable select are rendered by the select
        foreach (var id in new[] { "disabled", "hidden", "in-hidden", "display-none", "invisible", "inert", "option" })
        {
            Get<HtmlElement>(document, "#" + id).Focus();
            Assert.Same(document.Body, document.ActiveElement);
        }

        // An element with display: contents delegates its rendering to its children
        Get<HtmlElement>(document, "#contents").Focus();
        Assert.Equal("contents", document.ActiveElement!.Id);
    }

    [Fact]
    public void Focus_DocumentWithoutBrowsingContext_DoesNothing()
    {
        var document = new DomParser().ParseFromString("<input>", DomParserSupportedType.TextHtml);
        var input = Get<HtmlElement>(document, "input");
        input.Focus();
        Assert.Same(document.Body, document.ActiveElement);
        Assert.False(document.HasFocus());
    }

    [Fact]
    public void FocusEvents_AreFiredInOrder()
    {
        var document = Load("<input id=a><input id=b>");
        var a = Get<HtmlElement>(document, "#a");
        var b = Get<HtmlElement>(document, "#b");
        var events = new List<string>();
        foreach (var element in new[] { a, b })
        {
            foreach (var type in new[] { "focus", "blur", "focusin", "focusout" })
            {
                element.AddEventListener(type, e =>
                {
                    var focusEvent = (FocusEvent)e;
                    events.Add($"{type}:{((Element)e.Target!).Id}:{(focusEvent.RelatedTarget as Element)?.Id}:{e.Bubbles}:{e.IsTrusted}:{document.ActiveElement?.Id}");
                });
            }
        }

        a.Focus();
        b.Focus();
        b.Blur();
        Assert.Equal(
            [
                "focus:a::False:True:a",
                "focusin:a::True:True:a",
                "blur:a:b:False:True:",
                "focusout:a:b:True:True:",
                "focus:b:a:False:True:b",
                "focusin:b:a:True:True:b",
                "blur:b::False:True:",
                "focusout:b::True:True:",
            ],
            events);
        Assert.Same(document.Body, document.ActiveElement);
    }

    [Fact]
    public void ShadowRoot_ActiveElementIsRetargeted()
    {
        var document = Load("<div id=host></div><div id=other></div>");
        var host = Get<HtmlElement>(document, "#host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var input = document.CreateElement("input");
        shadowRoot.AppendChild(input);
        var otherRoot = Get<HtmlElement>(document, "#other").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });

        ((HtmlElement)input).Focus();
        Assert.Same(host, document.ActiveElement);
        Assert.Same(input, shadowRoot.ActiveElement);
        Assert.Null(otherRoot.ActiveElement);
        Assert.True(host.Matches(":focus"));
        Assert.True(host.Matches(":focus-within"));
        Assert.True(document.Body!.Matches(":focus-within"));
        Assert.False(document.Body.Matches(":focus"));
    }

    [Fact]
    public void FocusWithin_StopsAtTheTopLayer()
    {
        // :focus-within matches the flat tree ancestors "up to and including the first top layer element"
        var document = Load("<div id=outer><dialog id=modal><input id=a></dialog><dialog id=plain open><input id=b></dialog><div id=popover popover><input id=c></div></div>");
        var outer = Get<HtmlElement>(document, "#outer");
        Get<HtmlDialogElement>(document, "#modal").ShowModal();
        Get<HtmlElement>(document, "#a").Focus();
        Assert.True(Get<HtmlElement>(document, "#modal").Matches(":focus-within"));
        Assert.False(outer.Matches(":focus-within"));
        Assert.False(document.Body!.Matches(":focus-within"));
        Get<HtmlDialogElement>(document, "#modal").Close();

        // A dialog that is not modal is not in the top layer
        Get<HtmlElement>(document, "#b").Focus();
        Assert.True(outer.Matches(":focus-within"));

        Get<HtmlElement>(document, "#popover").ShowPopover();
        Get<HtmlElement>(document, "#c").Focus();
        Assert.True(Get<HtmlElement>(document, "#popover").Matches(":focus-within"));
        Assert.False(outer.Matches(":focus-within"));
    }

    [Fact]
    public void DelegatesFocus_FocusesTheFirstFocusableElement()
    {
        var document = Load("<div id=host></div>");
        var host = Get<HtmlElement>(document, "#host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, DelegatesFocus = true });
        shadowRoot.InnerHtml = "<span>text</span><input id=first><input id=second autofocus>";

        host.Focus();
        Assert.Equal("second", shadowRoot.ActiveElement!.Id);
        Assert.Same(host, document.ActiveElement);
    }

    [Fact]
    public void RemovingTheFocusedElement_FocusesTheViewportWithoutEvents()
    {
        var document = Load("<div><input></div>");
        var input = Get<HtmlElement>(document, "input");
        var blurred = false;
        input.AddEventListener("blur", _ => blurred = true);
        input.Focus();
        document.QuerySelector("div")!.Remove();
        Assert.Same(document.Body, document.ActiveElement);
        Assert.False(blurred);
    }

    [Fact]
    public void FocusFixupRule_BlursAnElementThatIsNoLongerFocusable()
    {
        var document = Load("<input>");
        var input = Get<HtmlInputElement>(document, "input");
        var blurred = false;
        input.AddEventListener("blur", _ => blurred = true);
        input.Focus();
        input.Disabled = true;

        // The fixup runs at the next rendering opportunity
        Assert.Same(input, document.ActiveElement);
        RunRenderingOpportunity();
        Assert.Same(document.Body, document.ActiveElement);
        Assert.True(blurred);
    }

    [Fact]
    public void Autofocus_FocusesTheFirstCandidate()
    {
        var document = Load("<input id=a><input id=b autofocus><input id=c autofocus>");
        Assert.Same(document.Body, document.ActiveElement);
        RunRenderingOpportunity();
        Assert.Equal("b", document.ActiveElement!.Id);
        Assert.True(document.ActiveElement.Matches(":focus-visible"));
    }

    [Fact]
    public void ModalDialog_FocusesItsContentAndMakesTheDocumentInert()
    {
        var document = Load("<button id=opener>open</button><input id=outside><dialog><p>text</p><button id=inside>ok</button></dialog>");
        var opener = Get<HtmlElement>(document, "#opener");
        var dialog = Get<HtmlDialogElement>(document, "dialog");
        opener.Focus();
        dialog.ShowModal();
        Assert.Equal("inside", document.ActiveElement!.Id);

        // The other elements are inert
        Get<HtmlElement>(document, "#outside").Focus();
        Assert.Equal("inside", document.ActiveElement!.Id);

        // The previously focused element is focused again when the dialog is closed
        dialog.Close();
        Assert.Same(opener, document.ActiveElement);
    }

    [Fact]
    public void Dialog_RestoresTheFocusWhenTheFocusedElementIsSlottedIntoTheDialog()
    {
        var document = Load("<button id=opener>open</button><div id=host><button id=slotted>slotted</button></div>");
        var host = Get<HtmlElement>(document, "#host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<dialog><slot></slot></dialog>";
        var dialog = (HtmlDialogElement)shadowRoot.FirstChild!;
        var opener = Get<HtmlElement>(document, "#opener");
        opener.Focus();
        dialog.Show();
        Get<HtmlElement>(document, "#slotted").Focus();

        // The focused element is a flat tree descendant of the dialog
        dialog.Close();
        Assert.Same(opener, document.ActiveElement);
    }

    [Fact]
    public void Popover_FocusesItsAutofocusElementAndRestoresTheFocus()
    {
        var document = Load("<button id=opener>open</button><div popover><input id=inside autofocus></div>");
        var opener = Get<HtmlElement>(document, "#opener");
        var popover = Get<HtmlElement>(document, "[popover]");
        opener.Focus();
        popover.ShowPopover();
        Assert.Equal("inside", document.ActiveElement!.Id);

        popover.HidePopover();
        Assert.Same(opener, document.ActiveElement);
    }

    [Fact]
    public void Iframe_ModalDialogDoesNotBlockTheNestedDocuments()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock });
        var document = context.Navigate("https://example.com/", _ => "<dialog><button>ok</button></dialog><iframe id=blocked srcdoc='<input>'></iframe><div inert><iframe id=inert srcdoc='<input>'></iframe></div>");
        _contexts.EventLoop.RunUntilIdle();
        Get<HtmlDialogElement>(document, "dialog").ShowModal();

        // The iframe is blocked by the modal dialog, but not the elements of its document (as in the browsers)
        var blockedInput = Get<HtmlElement>(Get<HtmlIFrameElement>(document, "#blocked").ContentDocument!, "input");
        blockedInput.Focus();
        Assert.Same(blockedInput, blockedInput.NodeDocument.ActiveElement);

        // The inert iframe cannot be focused, but the elements of its document can (as in the browsers)
        var inertInput = Get<HtmlElement>(Get<HtmlIFrameElement>(document, "#inert").ContentDocument!, "input");
        inertInput.Focus();
        Assert.Same(inertInput, inertInput.NodeDocument.ActiveElement);
    }

    [Fact]
    public void Iframe_TheContainerIsTheActiveElementOfTheParentDocument()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock });
        var document = context.Navigate("https://example.com/", _ => "<iframe srcdoc='<input>'></iframe><input id=outer>");
        _contexts.EventLoop.RunUntilIdle();
        var iframe = Get<HtmlIFrameElement>(document, "iframe");
        var innerDocument = iframe.ContentDocument!;
        var innerInput = Get<HtmlElement>(innerDocument, "input");

        innerInput.Focus();
        Assert.Same(iframe, document.ActiveElement);
        Assert.Same(innerInput, innerDocument.ActiveElement);
        Assert.True(document.HasFocus());
        Assert.True(innerDocument.HasFocus());
        Assert.False(iframe.Matches(":focus"));

        // The document that loses the focus to an element of another document no longer has a focused element (as in the browsers)
        Get<HtmlElement>(document, "#outer").Focus();
        Assert.False(innerDocument.HasFocus());
        Assert.Same(innerDocument.Body, innerDocument.ActiveElement);
    }

    [Fact]
    public void Tab_MovesTheFocusInTabIndexOrder()
    {
        var document = Load("""
            <input id=a>
            <input id=b tabindex=2>
            <input id=c tabindex=-1>
            <div id=host></div>
            <input id=d tabindex=1>
            <button id=e disabled>e</button>
            <a id=f href=x>f</a>
            """);
        var shadowRoot = Get<HtmlElement>(document, "#host").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<input id=shadow>";

        var order = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            PressKey(document, "Tab");
            order.Add(document.ActiveElement == document.Body ? "body" : shadowRoot.ActiveElement?.Id ?? document.ActiveElement!.Id);
        }

        Assert.Equal(["d", "b", "a", "shadow", "f", "body", "d"], order);
        Assert.True(Get<HtmlElement>(document, "#d").Matches(":focus-visible"));
    }

    /// <summary>The focused element of the document, in its shadow trees.</summary>
    private static string GetFocusedId(Document document)
    {
        var focused = document.ActiveElement;
        while (focused?.AttachedShadowRoot?.ActiveElement is { } inner)
        {
            focused = inner;
        }

        return focused is null || ReferenceEquals(focused, document.Body) ? "body" : focused.Id;
    }

    [Fact]
    public void SequentialNavigation_RadioGroupIsASingleStop()
    {
        var document = Load("<input id=start><input type=radio name=g id=a><input type=radio name=g id=b disabled><input type=radio name=g id=c><input type=radio id=alone><input id=end>");
        Get<HtmlElement>(document, "#start").Focus();

        // Without checked radio button: the first one, in both directions
        Assert.Equal(["a", "alone", "end"], PressTab(document, 3));
        Assert.Equal(["alone", "a", "start"], PressTab(document, 3, shift: true));

        // The checked radio button, unless it is disabled
        Get<HtmlInputElement>(document, "#c").Checked = true;
        Assert.Equal(["c", "alone"], PressTab(document, 2));
        Get<HtmlInputElement>(document, "#b").Checked = true;
        Get<HtmlElement>(document, "#start").Focus();
        Assert.Equal(["a"], PressTab(document, 1));
    }

    [Fact]
    public void ArrowKeys_FocusAndCheckTheNextRadioButtonOfTheGroup()
    {
        var document = Load("<input type=radio name=g id=a><input type=radio name=g id=b disabled><input type=radio name=g id=c><input type=radio name=other id=d><div dir=rtl><input type=radio name=r id=r1><input type=radio name=r id=r2></div>");
        var events = new List<string>();
        foreach (var radio in document.QuerySelectorAll("input").OfType<HtmlInputElement>())
        {
            radio.AddEventListener("change", e => events.Add("change " + ((Element)e.Target!).Id));
        }

        Get<HtmlElement>(document, "#a").Focus();

        // The disabled radio button is skipped, and the navigation wraps around
        PressKey(document, "ArrowDown");
        Assert.Equal("c", GetFocusedId(document));
        Assert.True(Get<HtmlInputElement>(document, "#c").Checked);
        PressKey(document, "ArrowRight");
        Assert.Equal("a", GetFocusedId(document));
        PressKey(document, "ArrowUp");
        Assert.Equal("c", GetFocusedId(document));
        Assert.Equal(["change c", "change a", "change c"], events);
        Assert.False(Get<HtmlInputElement>(document, "#d").Checked);

        // The left and right arrow keys are reversed in a right-to-left radio button
        Get<HtmlElement>(document, "#r1").Focus();
        PressKey(document, "ArrowLeft");
        Assert.Equal("r2", GetFocusedId(document));
    }

    private static List<string> PressTab(Document document, int count, bool shift = false)
    {
        var order = new List<string>();
        for (var i = 0; i < count; i++)
        {
            PressKey(document, "Tab", shift);
            order.Add(GetFocusedId(document));
        }

        return order;
    }

    [Fact]
    public void Tab_SlotsAreFocusNavigationScopes()
    {
        // The slots are not focused (they generate no box), but their tabindex orders their scope in the shadow tree; the
        // fallback content of a slot is in the scope of the slot
        var document = Load("""
            <input id=before>
            <div id=host>
              <input id=assigned2 slot=s1 tabindex=2>
              <input id=assigned1 slot=s1 tabindex=1>
            </div>
            <input id=after>
            """);
        var shadowRoot = Get<HtmlElement>(document, "#host").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = """
            <input id=shadow1 tabindex=1>
            <slot name=s2 tabindex=3><input id=fallback0 tabindex=0><input id=fallback1 tabindex=1></slot>
            <slot id=slot1 name=s1 tabindex=2></slot>
            <input id=shadow4 tabindex=4>
            """;

        string[] expected = ["before", "shadow1", "assigned1", "assigned2", "fallback1", "fallback0", "shadow4", "after", "body"];
        Assert.Equal(expected, PressTab(document, expected.Length));
        Assert.Equal(expected.Reverse().Skip(1), PressTab(document, expected.Length - 1, shift: true));

        // A slot is focusable by script, and sequentially focusable when it generates a box
        var slot = (HtmlElement)shadowRoot.QuerySelector("#slot1")!;
        slot.Focus();
        Assert.Same(slot, shadowRoot.ActiveElement);
        slot.SetAttribute("style", "display: block");
        ((HtmlElement)shadowRoot.QuerySelector("#shadow1")!).Focus();
        Assert.Equal(["slot1", "assigned1"], PressTab(document, 2));
    }

    [Fact]
    public void Tab_ContinuesInTheEnclosingScopesFromAnElementThatIsNotInTheNavigationOrder()
    {
        var document = Load("""
            <input id=a tabindex=2>
            <div id=host tabindex=-1></div>
            <input id=b tabindex=1>
            <input id=c>
            """);
        var shadowRoot = Get<HtmlElement>(document, "#host").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<input id=inner1 tabindex=1><input id=inner2 tabindex=2><input id=skipped tabindex=-1>";

        // The shadow tree of a host with a negative tabindex is skipped
        Assert.Equal(["b", "a", "c", "body"], PressTab(document, 4));

        // From an element of the shadow tree, the navigation follows the scope of the host, then continues after (or before)
        // the host in tree order
        ((HtmlElement)shadowRoot.QuerySelector("#inner1")!).Focus();
        Assert.Equal(["inner2", "b", "a", "c"], PressTab(document, 4));
        ((HtmlElement)shadowRoot.QuerySelector("#skipped")!).Focus();
        Assert.Equal(["b"], PressTab(document, 1));
        ((HtmlElement)shadowRoot.QuerySelector("#inner1")!).Focus();
        Assert.Equal(["a"], PressTab(document, 1, shift: true));
        ((HtmlElement)shadowRoot.QuerySelector("#skipped")!).Focus();
        Assert.Equal(["inner2", "inner1", "a"], PressTab(document, 3, shift: true));

        // From the host itself, the navigation enters its shadow tree
        Get<HtmlElement>(document, "#host").Focus();
        Assert.Equal(["inner1"], PressTab(document, 1));
    }

    [Fact]
    public void Tab_FollowsTheReadingFlow()
    {
        var document = Load("""
            <style>
              .grid { display: grid; grid-template-columns: 50px 50px; grid-template-areas: "d b" "c a"; }
              .flex { display: flex; }
            </style>
            <div id=order class=grid style="reading-flow: grid-order">
              <button id=o2 style="order: 2" tabindex=1></button>
              <div style="display: contents"><button id=o3 style="order: 3"></button><button id=o1 style="order: 1"></button></div>
              <button id=first style="order: 9; reading-order: -1"></button>
              <button id=abs style="position: absolute"></button>
              <div id=item tabindex=0 style="order: 4"><button id=i2></button><button id=i1 tabindex=1></button></div>
            </div>
            <div class=grid style="reading-flow: grid-rows">
              <button id=ra style="grid-area: a"></button><button id=rb style="grid-area: b"></button><button id=rc style="grid-area: c"></button><button id=rd style="grid-area: d"></button>
            </div>
            <div class=grid style="reading-flow: grid-columns">
              <button id=ca style="grid-area: a"></button><button id=cb style="grid-area: b"></button><button id=cc style="grid-area: c"></button><button id=cd style="grid-area: d"></button>
            </div>
            <div class=flex style="reading-flow: flex-visual; flex-direction: row-reverse">
              <button id=v1></button><button id=v2 style="order: 1"></button><button id=v3></button>
            </div>
            <div class=flex style="reading-flow: flex-flow; flex-direction: row-reverse">
              <button id=f1></button><button id=f2 style="order: 1"></button><button id=f3></button>
            </div>
            <div style="reading-flow: source-order">
              <button id=s1 style="reading-order: 1"></button><button id=s2></button>
            </div>
            <span style="reading-flow: source-order">
              <button id=n1 style="reading-order: 1"></button><button id=n2></button>
            </span>
            """);

        string[] expected =
        [
            // grid-order: reading-order, then the order-modified document order (the positive tabindex of an item is ignored, an
            // element with display: contents is at the position of its first item, the items are scopes), then the boxes that
            // are not laid out by the container
            "first", "o1", "o3", "o2", "item", "i1", "i2", "abs",
            "rd", "rb", "rc", "ra",
            "cd", "cc", "cb", "ca",
            // flex-visual follows the lines, flex-flow the order-modified document order
            "v2", "v3", "v1",
            "f1", "f3", "f2",
            "s2", "s1",
            // An inline box is not a reading flow container
            "n1", "n2",
            "body",
        ];
        Assert.Equal(expected, PressTab(document, expected.Length));
        Assert.Equal(expected.Reverse().Skip(1), PressTab(document, expected.Length - 1, shift: true));
    }

    [Fact]
    public void Blur_UnfocusesTheFocusedElementThatIsNoLongerFocusable()
    {
        var document = Load("<div id=host></div>");
        var host = Get<HtmlElement>(document, "#host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, DelegatesFocus = true });
        shadowRoot.InnerHtml = "<div id=a tabindex=0></div><div id=b tabindex=0></div>";
        host.Focus();
        Assert.Equal("a", shadowRoot.ActiveElement!.Id);

        // The focus fixup rule has not run yet: as in the browsers, blur() unfocuses the element
        shadowRoot.ActiveElement.RemoveAttribute("tabindex");
        host.Blur();
        Assert.Null(shadowRoot.ActiveElement);
        Assert.Same(document.Body, document.ActiveElement);

        host.Focus();
        Assert.Equal("b", shadowRoot.ActiveElement!.Id);
    }

    [Fact]
    public void Tab_ShiftMovesBackward()
    {
        var document = Load("<input id=a><input id=b><input id=c>");
        Get<HtmlElement>(document, "#b").Focus();
        PressKey(document, "Tab", shift: true);
        Assert.Equal("a", document.ActiveElement!.Id);
    }

    [Fact]
    public void Tab_ContinuesFromThePositionOfTheRemovedFocusedElement()
    {
        var document = Load("<button id=a>a</button><div id=c>c</div><button id=b>b</button><button id=d>d</button><div id=moved></div>");
        Get<HtmlElement>(document, "#b").Focus();

        // The starting point is the position of the removed element, kept when its previous sibling is removed or gets children
        Get<HtmlElement>(document, "#b").Remove();
        Get<HtmlElement>(document, "#c").AppendChild(document.CreateElement("button")).TextContent = "child";
        PressKey(document, "Tab");
        Assert.Equal("d", document.ActiveElement!.Id);

        Get<HtmlElement>(document, "#d").Remove();
        Get<HtmlElement>(document, "#moved").MoveBefore(Get<HtmlElement>(document, "#c"), null);
        PressKey(document, "Tab", shift: true);
        Assert.Equal("a", document.ActiveElement!.Id);
    }

    [Fact]
    public void Tab_ContinuesFromTheSlotOfTheRemovedSlottedElement()
    {
        var document = Load("<div id=host><button slot=a id=a>a</button><button slot=c id=c>c</button><button slot=b id=b>b</button></div>");
        var shadowRoot = Get<HtmlElement>(document, "#host").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<slot name=a></slot><slot name=b></slot><slot name=c></slot>";
        Get<HtmlElement>(document, "#b").Focus();
        Get<HtmlElement>(document, "#b").Remove();
        PressKey(document, "Tab");
        Assert.Equal("c", document.ActiveElement!.Id);
    }

    [Fact]
    public void Tab_ContinuesFromTheBlurredElement()
    {
        var document = Load("<button id=a>a</button><button id=b>b</button><button id=c>c</button>");
        var b = Get<HtmlElement>(document, "#b");
        b.Focus();
        b.Blur();
        Assert.Same(document.Body, document.ActiveElement);
        PressKey(document, "Tab");
        Assert.Equal("c", document.ActiveElement!.Id);
    }

    [Fact]
    public void Tab_ContinuesFromTheClickedNodeInTheFocusedElement()
    {
        var document = Load("<button id=a>a</button><div id=focusable tabindex=0><button id=b>b</button><span>text</span><button id=c>c</button></div>");
        Dispatch(document.QuerySelector("span")!, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        Assert.Equal("focusable", document.ActiveElement!.Id);
        PressKey(document, "Tab");
        Assert.Equal("c", document.ActiveElement!.Id);
    }

    [Fact]
    public void FragmentNavigation_FocusesTheTarget()
    {
        var document = Load("<button id=a>a</button><input id=target><div id=div>div</div><button id=b>b</button>");
        Get<HtmlElement>(document, "#a").Focus();
        document.Location!.Hash = "target";
        Assert.Equal("target", document.ActiveElement!.Id);

        // A target that is not focusable becomes the sequential focus navigation starting point, and the viewport is focused
        document.Location!.Hash = "div";
        Assert.Same(document.Body, document.ActiveElement);
        PressKey(document, "Tab");
        Assert.Equal("b", document.ActiveElement!.Id);
    }

    [Fact]
    public void MouseDown_FocusesTheClickedFocusableArea()
    {
        var document = Load("<button><span>text</span></button><p>paragraph</p>");
        var button = Get<HtmlElement>(document, "button");
        Dispatch(document.QuerySelector("span")!, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        Assert.Same(button, document.ActiveElement);
        Assert.False(button.Matches(":focus-visible"));

        // A click on an element that is not focusable focuses the viewport
        Dispatch(document.QuerySelector("p")!, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        Assert.Same(document.Body, document.ActiveElement);
    }

    [Fact]
    public void MouseEvents_StopAtTheDisabledFormControls()
    {
        var document = Load("<div id=parent><button disabled><span>text</span></button><fieldset disabled><p>text</p></fieldset></div>");
        var events = new List<string>();
        foreach (var element in document.QuerySelectorAll("div, button, span, fieldset, p"))
        {
            element.AddEventListener("click", e => events.Add(((Element)e.CurrentTarget!).LocalName));
            element.AddEventListener("mousemove", e => events.Add("mousemove " + ((Element)e.CurrentTarget!).LocalName));
        }

        // The trusted click is not dispatched to the disabled button, and does not propagate from its descendants to it
        Dispatch(Get<HtmlElement>(document, "button"), new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true }));
        Assert.Empty(events);
        Dispatch(Get<HtmlElement>(document, "span"), new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true }));
        Assert.Equal(["span"], events);

        // The other mouse events, the untrusted clicks and the disabled fieldsets do not stop the path
        events.Clear();
        Dispatch(Get<HtmlElement>(document, "span"), new MouseEvent("mousemove", new MouseEventInit { Bubbles = true }));
        Get<HtmlElement>(document, "span").Click();
        Dispatch(Get<HtmlElement>(document, "p"), new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true }));
        Assert.Equal(["mousemove span", "mousemove button", "mousemove div", "span", "button", "div", "p", "fieldset", "div"], events);

        // click() does nothing on a disabled form control, but a disabled fieldset is not a form control
        events.Clear();
        Get<HtmlElement>(document, "button").Click();
        Get<HtmlElement>(document, "fieldset").Click();
        Assert.Equal(["fieldset", "div"], events);
    }

    [Fact]
    public void FocusVisible_IsDecidedBeforeTheFocusEvents()
    {
        var document = Load("<div id=a tabindex=0></div>");
        var a = Get<HtmlElement>(document, "#a");
        bool? focusVisible = null;
        a.AddEventListener("focus", _ => focusVisible = a.Matches(":focus-visible"));
        PressKey(document, "Tab");
        Assert.True(focusVisible);
    }

    [Fact]
    public void FocusVisible_ScriptFocusAfterAClick()
    {
        var document = Load("<div id=a tabindex=0></div><div id=b tabindex=0></div><p>text</p>");
        var a = Get<HtmlElement>(document, "#a");
        var b = Get<HtmlElement>(document, "#b");

        // After a click that focuses an element, the elements focused by script do not indicate their focus
        Dispatch(a, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        b.Focus();
        Assert.False(b.Matches(":focus-visible"));

        // A keyboard interaction (other than a shortcut) indicates the focus
        Dispatch(b, new KeyboardEvent("keydown", new KeyboardEventInit { Key = "c", CtrlKey = true, Bubbles = true, Cancelable = true }));
        Assert.False(b.Matches(":focus-visible"));
        Dispatch(b, new KeyboardEvent("keydown", new KeyboardEventInit { Key = "c", Bubbles = true, Cancelable = true }));
        Assert.True(b.Matches(":focus-visible"));

        // After a click that does not focus an element, the elements focused by script indicate their focus
        Dispatch(a, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        Dispatch(document.QuerySelector("p")!, new MouseEvent("mousedown", new MouseEventInit { Bubbles = true, Cancelable = true }));
        b.Focus();
        Assert.True(b.Matches(":focus-visible"));
    }

    [Fact]
    public void FocusVisible_DoesNotMatchTheShadowHost()
    {
        var document = Load("<div id=host></div>");
        var host = Get<HtmlElement>(document, "#host");
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open, DelegatesFocus = true });
        shadowRoot.InnerHtml = "<input>";
        host.Focus();
        Assert.True(host.Matches(":focus"));
        Assert.False(host.Matches(":focus-visible"));
        Assert.True(shadowRoot.QuerySelector("input")!.Matches(":focus-visible"));
    }

    [Fact]
    public void RemovingTheFocusedElement_TabContinuesFromItsPosition()
    {
        var document = Load("<input id=a><div><input id=b></div><input id=c>");
        Get<HtmlElement>(document, "#b").Focus();
        Get<HtmlElement>(document, "#b").Remove();
        PressKey(document, "Tab");
        Assert.Equal("c", document.ActiveElement!.Id);
    }

    [Fact]
    public void HoverAndActive_FollowThePointerEvents()
    {
        var document = Load("<label for=input><span id=text>label</span></label><input id=input><div id=other></div>");
        var text = Get<HtmlElement>(document, "#text");
        var label = Get<HtmlElement>(document, "label");
        var input = Get<HtmlElement>(document, "#input");
        Assert.Empty(document.QuerySelectorAll(":hover"));

        // The designated element, its ancestors in the flat tree and the labeled control of a designated label match :hover
        Dispatch(text, new PointerEvent("pointerover", new PointerEventInit { Bubbles = true }));
        Assert.True(text.Matches(":hover"));
        Assert.True(label.Matches(":hover"));
        Assert.True(document.Body!.Matches(":hover"));
        Assert.True(input.Matches(":hover"));
        Assert.False(input.Matches(":active"));

        // A press makes the element active until all the buttons are released, even when the event is canceled
        var activeDuringPress = false;
        text.AddEventListener("pointerdown", e =>
        {
            activeDuringPress = label.Matches(":active");
            e.PreventDefault();
        });
        Dispatch(text, new PointerEvent("pointerdown", new PointerEventInit { Bubbles = true, Cancelable = true, Buttons = 1 }));
        Assert.True(activeDuringPress);
        Assert.True(input.Matches(":active"));
        Dispatch(text, new PointerEvent("pointerup", new PointerEventInit { Bubbles = true, Cancelable = true }));
        Assert.Empty(document.QuerySelectorAll(":active"));

        // The boundary events that leave an element designate the related target
        var other = Get<HtmlElement>(document, "#other");
        Dispatch(text, new PointerEvent("pointerout", new PointerEventInit { Bubbles = true, RelatedTarget = other }));
        Assert.False(text.Matches(":hover"));
        Assert.True(other.Matches(":hover"));
    }

    [Fact]
    public void Active_SpaceKeyActivation()
    {
        var document = Load("<button>ok</button>");
        var button = Get<HtmlElement>(document, "button");
        button.Focus();
        Dispatch(button, new KeyboardEvent("keydown", new KeyboardEventInit { Key = " ", Bubbles = true, Cancelable = true }));
        Assert.True(button.Matches(":active"));

        // As in browsers, the ancestors of the button do not match
        Assert.Same(button, document.QuerySelector(":active"));
        Dispatch(button, new KeyboardEvent("keyup", new KeyboardEventInit { Key = " ", Bubbles = true, Cancelable = true }));
        Assert.False(button.Matches(":active"));
    }

    [Fact]
    public void Active_TabEndsTheSpaceKeyActivation()
    {
        var document = Load("<button>a</button><button>b</button>");
        var button = Get<HtmlElement>(document, "button");
        button.Focus();
        Dispatch(button, new KeyboardEvent("keydown", new KeyboardEventInit { Key = " ", Bubbles = true, Cancelable = true }));
        Assert.True(button.Matches(":active"));
        Dispatch(button, new KeyboardEvent("keydown", new KeyboardEventInit { Key = "Tab", Bubbles = true, Cancelable = true }));
        Assert.Empty(document.QuerySelectorAll(":active"));
    }

    [Fact]
    public void UserValidity_IsSetWhenTheUserCommitsAChange()
    {
        var document = Load("<form><input id=email type=email required><input id=check type=checkbox required><input type=submit><input type=reset></form>");
        var email = Get<HtmlInputElement>(document, "#email");
        var check = Get<HtmlInputElement>(document, "#check");
        var changes = 0;
        email.AddEventListener("change", _ => changes++);

        // A change made by script does not match :user-invalid
        email.Value = "x";
        Assert.False(email.Matches(":user-invalid"));
        email.Value = "";

        // The edits of the user are committed when the control loses the focus
        email.Focus();
        WebPlatform.Html.Editing.UserEditTextControl(email, "insertText", "x");
        Assert.False(email.Matches(":user-invalid"));
        email.Blur();
        Assert.True(email.Matches(":user-invalid"));
        Assert.False(email.Matches(":user-valid"));
        Assert.Equal(1, changes);

        // A click on a checkbox by the user
        Dispatch(check, new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true }));
        Assert.True(check.Matches(":user-valid"));

        // The reset algorithm resets the user validity, and the submission of the form sets it
        var form = Get<HtmlFormElement>(document, "form");
        form.Reset();
        Assert.Empty(document.QuerySelectorAll(":user-valid, :user-invalid"));
        form.RequestSubmit();
        Assert.True(email.Matches(":user-invalid"));
        Assert.True(check.Matches(":user-invalid"));
    }

    [Fact]
    public void Escape_ClosesTheModalDialog()
    {
        var document = Load("<dialog><button>ok</button></dialog>");
        var dialog = Get<HtmlDialogElement>(document, "dialog");
        dialog.ShowModal();
        PressKey(document, "Escape");
        Assert.False(dialog.Open);
    }

    [Fact]
    public void KeyboardActivation_ClicksTheButton()
    {
        var document = Load("<button>ok</button>");
        var button = Get<HtmlElement>(document, "button");
        var clicks = 0;
        button.AddEventListener("click", _ => clicks++);
        button.Focus();
        PressKey(document, "Enter");
        PressKey(document, " ");
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Focus_FocusVisibleFalse_TextControlDoesNotIndicateTheFocus()
    {
        var document = Load("<input id=a><div id=b contenteditable></div>");
        foreach (var id in new[] { "a", "b" })
        {
            var element = Get<HtmlElement>(document, "#" + id);
            element.Focus();
            Assert.True(element.Matches(":focus-visible"));
            element.Focus(new FocusOptions { FocusVisible = false });
            Assert.False(element.Matches(":focus-visible"));
            document.Body!.Focus();
            element.Focus(new FocusOptions { FocusVisible = false });
            Assert.False(element.Matches(":focus-visible"));
        }
    }

    [Fact]
    public void Focus_UpdatesTheSelection()
    {
        var document = Load("<p id=p>text</p><input id=input><button id=button></button><div id=editable contenteditable><b>bold</b></div>");
        var selection = document.GetSelection()!;
        selection.Collapse(Get<HtmlElement>(document, "#p"), 0);

        // A text control: the selection is collapsed before the control
        Get<HtmlElement>(document, "#input").Focus();
        Assert.Same(document.Body, selection.AnchorNode);
        Assert.Equal(1u, selection.AnchorOffset);

        // Another element: the selection does not change
        Get<HtmlElement>(document, "#button").Focus();
        Assert.Same(document.Body, selection.AnchorNode);

        // An editing host: the caret is at the start of its content, unless the selection is already in it
        var editable = Get<HtmlElement>(document, "#editable");
        editable.Focus();
        Assert.Same(editable.QuerySelector("b")!.FirstChild, selection.AnchorNode);
        Assert.Equal(0u, selection.AnchorOffset);

        selection.Collapse(editable.QuerySelector("b")!.FirstChild, 2);
        editable.Blur();
        editable.Focus();
        Assert.Same(editable, document.ActiveElement);
        Assert.Equal(2u, selection.AnchorOffset);
    }

    [Fact]
    public void Focus_AreaOfARenderedImageMap()
    {
        var document = Load("""
            <map name=map><area id=area href=#a shape=rect coords=0,0,10,10><area id=no-href shape=rect coords=0,0,10,10></map>
            <map name=unused><area id=unused href=#b></map>
            <img usemap=#map>
            """);

        foreach (var (id, focusable) in new[] { ("area", true), ("no-href", false), ("unused", false) })
        {
            (document.ActiveElement as HtmlElement)?.Blur();
            Get<HtmlElement>(document, "#" + id).Focus();
            Assert.Equal(focusable, document.ActiveElement!.Id == id);
        }

        // The area is not focusable when the image is not rendered
        (document.ActiveElement as HtmlElement)?.Blur();
        Get<HtmlElement>(document, "img").SetAttribute("hidden", "");
        Get<HtmlElement>(document, "#area").Focus();
        Assert.Same(document.Body, document.ActiveElement);
    }

    [Fact]
    public void AccessKeyLabel_FirstSingleCodePointToken()
    {
        var document = Load("""
            <button id=a accesskey=b></button>
            <button id=b accesskey="ab  1 c"></button>
            <button id=c accesskey="ab cd"></button>
            <button id=d></button>
            """);

        Assert.Equal("Alt+Shift+B", Get<HtmlElement>(document, "#a").AccessKeyLabel);
        Assert.Equal("Alt+Shift+1", Get<HtmlElement>(document, "#b").AccessKeyLabel);
        Assert.Equal("", Get<HtmlElement>(document, "#c").AccessKeyLabel);
        Assert.Equal("", Get<HtmlElement>(document, "#d").AccessKeyLabel);
    }

    [Fact]
    public void AccessKey_TriggersTheCommandOfTheElement()
    {
        var document = Load("""
            <button id=hidden-button accesskey=x hidden></button>
            <div hidden><a id=in-hidden accesskey=x href=#hidden></a></div>
            <button id=button accesskey=x></button>
            <div id=div tabindex=0 accesskey=d></div>
            <button id=disabled accesskey=e disabled></button>
            <select><option>1</option><option id=option accesskey=o>2</option></select>
            <fieldset><legend accesskey=l>legend</legend><label><input id=checkbox type=checkbox></label></fieldset>
            """);
        var clicks = new List<string>();
        document.AddEventListener("click", e => clicks.Add(((Element)e.Target!).Id));

        // The first element with the access key whose command is visible and enabled is focused and clicked
        PressAccessKey(document, "x");
        Assert.Equal(["button"], clicks);
        Assert.Equal("button", document.ActiveElement!.Id);
        Assert.True(document.ActiveElement.Matches(":focus-visible"));

        // The key of a letter matches the access key whatever its case
        PressAccessKey(document, "D");
        Assert.Equal(["button", "div"], clicks);
        Assert.Equal("div", document.ActiveElement!.Id);

        // A disabled command does nothing
        PressAccessKey(document, "e");
        Assert.Equal(["button", "div"], clicks);

        // The command of an option picks the option
        var option = Get<HtmlOptionElement>(document, "#option");
        PressAccessKey(document, "o");
        Assert.True(option.Selected);

        // The command of a legend is the command of its accesskey delegatee
        var checkbox = Get<HtmlInputElement>(document, "#checkbox");
        PressAccessKey(document, "l");
        Assert.True(checkbox.Checked);
        Assert.Same(checkbox, document.ActiveElement);
    }

    private static void PressAccessKey(Document document, string key)
    {
        var target = document.ActiveElement ?? document.DocumentElement!;
        Dispatch(target, new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, AltKey = true, ShiftKey = true, Bubbles = true, Cancelable = true }));
    }

    private static void PressKey(Document document, string key, bool shift = false)
    {
        var target = document.ActiveElement ?? document.DocumentElement!;
        var focused = document.ActiveElement is { AttachedShadowRoot: { } shadowRoot } ? shadowRoot.ActiveElement ?? target : target;
        Dispatch(focused, new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, ShiftKey = shift, Bubbles = true, Cancelable = true }));
        Dispatch(document.ActiveElement ?? document.DocumentElement!, new KeyboardEvent("keyup", new KeyboardEventInit { Key = key, ShiftKey = shift, Bubbles = true, Cancelable = true }));
    }

    /// <summary>Dispatches a trusted event, as the host does for the user input.</summary>
    private static void Dispatch(Node target, Event @event)
    {
        @event.IsTrusted = true;
        EventDispatcher.Dispatch(@event, target);
    }
}
