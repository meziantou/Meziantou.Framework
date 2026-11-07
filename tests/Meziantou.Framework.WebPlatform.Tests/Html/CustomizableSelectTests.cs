using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The select element rendered with base appearance (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2) and its picker.</summary>
public sealed class CustomizableSelectTests : IDisposable
{
    private const string Style = "<style>body { margin: 0 } select, select::picker(select) { appearance: base-select }</style>";
    private const string Options = "<option id=one>one</option><option id=two>two</option><option id=three disabled>three</option><option id=four>four</option>";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, string style = Style)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html>" + style + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlSelectElement Select(Document document) => (HtmlSelectElement)document.QuerySelector("select")!;

    private void Click(Element element)
    {
        var rect = element.GetBoundingClientRect();
        var mouse = element.NodeDocument.BrowsingContext!.Input.Mouse;
        mouse.Move(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        mouse.Down();
        mouse.Up();
        _contexts.EventLoop.RunUntilIdle();
    }

    private void Press(Document document, string key)
    {
        var target = document.ActiveElement!;
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, target);
        _contexts.EventLoop.RunUntilIdle();
    }

    [Fact]
    public void UserAgentStyleSheet_DependsOnTheAppearance()
    {
        var document = Load("<select id=base><option>one</select><select id=native style='appearance: auto'><option>one</select><select id=list multiple><option>one</select>");
        var window = document.DefaultView!;
        var baseStyle = window.GetComputedStyle(document.GetElementById("base")!);
        Assert.Equal("inline-flex", baseStyle.GetPropertyValue("display"));
        Assert.Equal("rgba(0, 0, 0, 0)", baseStyle.GetPropertyValue("background-color"));
        Assert.Equal("content", baseStyle.GetPropertyValue("field-sizing"));

        Assert.Equal("inline-block", window.GetComputedStyle(document.GetElementById("native")!).GetPropertyValue("display"));

        // A list box with base appearance is a scroll container of its options
        var listStyle = window.GetComputedStyle(document.GetElementById("list")!);
        Assert.Equal("inline-block", listStyle.GetPropertyValue("display"));
        Assert.Equal("auto", listStyle.GetPropertyValue("overflow-y"));

        // The options are slotted in the select popover: they are flex containers with base appearance
        Assert.Equal("flex", window.GetComputedStyle(document.QuerySelector("#base option")!).GetPropertyValue("display"));
        Assert.Equal("block", window.GetComputedStyle(document.QuerySelector("#native option")!).GetPropertyValue("display"));
    }

    [Fact]
    public void Options_InheritFromThePicker()
    {
        var document = Load("<select><button>b</button><option id=one>one</select>", Style + "<style>select::picker(select) { color: rgb(1, 2, 3) }</style>");
        var window = document.DefaultView!;
        Assert.Equal("rgb(1, 2, 3)", window.GetComputedStyle(document.GetElementById("one")!).GetPropertyValue("color"));
        Assert.NotEqual("rgb(1, 2, 3)", window.GetComputedStyle(document.QuerySelector("button")!).GetPropertyValue("color"));
    }

    [Fact]
    public void HighlightPseudoElements_OfTheOptionsInheritFromTheOnesOfThePicker()
    {
        var document = Load("<select><button>b</button><option id=one>one</select>", Style + """
            <style>
              select::selection { color: rgb(0, 0, 255); background-color: rgb(255, 0, 0) }
              select::picker(select)::selection { background-color: rgb(0, 128, 0) }
              select::picker(select)::highlight(foo) { text-decoration-line: underline }
            </style>
            """);
        var window = document.DefaultView!;
        var option = document.GetElementById("one")!;

        // The highlight pseudo-elements of the picker inherit from the ones of the select element, and the ones of its contents
        // from the ones of the picker
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle(option, "::selection").GetPropertyValue("background-color"));
        Assert.Equal("rgb(0, 0, 255)", window.GetComputedStyle(option, "::selection").GetPropertyValue("color"));
        Assert.Equal("underline", window.GetComputedStyle(option, "::highlight(foo)").GetPropertyValue("text-decoration-line"));
        Assert.Equal("none", window.GetComputedStyle(option, "::highlight(bar)").GetPropertyValue("text-decoration-line"));

        // The button is not in the picker
        Assert.Equal("rgb(255, 0, 0)", window.GetComputedStyle(document.QuerySelector("button")!, "::selection").GetPropertyValue("background-color"));
    }

    [Fact]
    public void Click_OpensThePickerBelowTheSelectAndFocusesTheSelectedOption()
    {
        var document = Load("<select>" + Options + "</select>");
        var select = Select(document);
        var one = document.GetElementById("one")!;
        Assert.False(select.Matches(":open"));
        Assert.Equal(0, one.GetBoundingClientRect().Height);

        Click(select);
        Assert.True(select.Matches(":open"));
        Assert.Same(one, document.ActiveElement);

        // The picker is anchored to the select element: its options are boxes below it
        var selectRect = select.GetBoundingClientRect();
        var optionRect = one.GetBoundingClientRect();
        Assert.True(optionRect.Height >= 24);
        Assert.True(optionRect.Top >= selectRect.Bottom);
        Assert.Equal(selectRect.Left + 1, optionRect.Left);

        // A second press on the select element closes the picker and focuses the select element
        Click(select);
        Assert.False(select.Matches(":open"));
        Assert.Same(select, document.ActiveElement);
    }

    [Fact]
    public void ClickOnAnOption_PicksItAndClosesThePicker()
    {
        var document = Load("<select>" + Options + "</select>");
        var select = Select(document);
        var events = new List<string>();
        select.AddEventListener("input", _ => events.Add("input"));
        select.AddEventListener("change", _ => events.Add("change"));

        Click(select);
        Click(document.GetElementById("three")!);
        Assert.True(select.Matches(":open"));
        Assert.Equal("one", select.Value);

        Click(document.GetElementById("two")!);
        Assert.False(select.Matches(":open"));
        Assert.Equal("two", select.Value);
        Assert.Equal(["input", "change"], events);
        Assert.Same(select, document.ActiveElement);
    }

    [Fact]
    public void Keyboard_OpensThePickerFocusesTheOptionsAndPicks()
    {
        var document = Load("<select>" + Options + "</select>");
        var select = Select(document);
        select.Focus();
        Press(document, "ArrowDown");
        Assert.True(select.Matches(":open"));
        Assert.Equal("one", document.ActiveElement!.Id);

        // The disabled option is skipped, and the value does not change until an option is picked
        Press(document, "ArrowDown");
        Press(document, "ArrowDown");
        Assert.Equal("four", document.ActiveElement!.Id);
        Press(document, "Home");
        Assert.Equal("one", document.ActiveElement!.Id);
        Press(document, "t");
        Assert.Equal("two", document.ActiveElement!.Id);
        Assert.Equal("one", select.Value);

        Press(document, "Enter");
        Assert.False(select.Matches(":open"));
        Assert.Equal("two", select.Value);
        Assert.Same(select, document.ActiveElement);
    }

    [Fact]
    public void EscapeAndLightDismiss_CloseThePicker()
    {
        var document = Load("<select>" + Options + "</select><div id=outside style='position: absolute; left: 400px; top: 300px; width: 50px; height: 50px'></div>");
        var select = Select(document);
        Click(select);
        Press(document, "Escape");
        Assert.False(select.Matches(":open"));
        Assert.Same(select, document.ActiveElement);

        Click(select);
        Assert.True(select.Matches(":open"));
        Click(document.GetElementById("outside")!);
        Assert.False(select.Matches(":open"));
    }

    [Fact]
    public void Picker_HidesTheOtherPopoversButItsAncestors()
    {
        var document = Load("<div popover id=p1><select>" + Options + "</select></div><div popover id=p2>other</div>");
        var p1 = (HtmlElement)document.GetElementById("p1")!;
        var p2 = (HtmlElement)document.GetElementById("p2")!;
        p1.ShowPopover();
        _contexts.EventLoop.RunUntilIdle();
        var select = Select(document);
        Click(select);
        Assert.True(select.Matches(":open"));
        Assert.True(p1.Matches(":popover-open"));

        // Another auto popover hides the picker and the popover of the select element
        p2.ShowPopover();
        Assert.False(select.Matches(":open"));
        Assert.False(p1.Matches(":popover-open"));
    }

    [Fact]
    public void ButtonAndFallbackText()
    {
        var document = Load("<select id=a><option>first<option selected>second label</select><select id=b><button><selectedcontent></selectedcontent></button><option>x</select>");
        var a = document.GetElementById("a")!;

        // The select fallback button text is the label of the selected option: the box is sized by it
        var width = a.GetBoundingClientRect().Width;
        ((HtmlSelectElement)a).SelectedIndex = 0;
        Assert.True(a.GetBoundingClientRect().Width < width);

        // The button of a select element is inert: the select element is the target of the pointer
        var b = document.GetElementById("b")!;
        var rect = b.GetBoundingClientRect();
        Assert.Same(b, document.ElementFromPoint(rect.Left + 12, rect.Top + (rect.Height / 2)));
        Assert.Equal("x", document.QuerySelector("selectedcontent")!.TextContent);
    }

    [Fact]
    public void ListBoxWithBaseAppearance_TogglesTheOptions()
    {
        var document = Load("<button id=before>b</button><select multiple>" + Options + "</select><select size=3 id=single>" + Options.Replace("id=", "class=", StringComparison.Ordinal) + "</select>", "<style>body { margin: 0 } select { appearance: base-select }</style>");
        var select = Select(document);
        var one = (HtmlOptionElement)document.GetElementById("one")!;
        var two = (HtmlOptionElement)document.GetElementById("two")!;
        Click(one);
        Click(two);
        Assert.True(one.Selected && two.Selected);
        Assert.Same(two, document.ActiveElement);
        Click(two);
        Assert.False(two.Selected);
        Click(document.GetElementById("three")!);
        Assert.Equal(1u, select.SelectedOptions.Length);

        // The space key toggles the focused option, the arrow keys focus the other ones (the disabled option is skipped)
        two.Focus();
        Press(document, "ArrowDown");
        Assert.Equal("four", document.ActiveElement!.Id);
        Press(document, " ");
        Assert.Equal(2u, select.SelectedOptions.Length);

        // The selected option of a single selection can be unselected
        var single = (HtmlSelectElement)document.GetElementById("single")!;
        var first = single.QuerySelector(".one")!;
        Click(first);
        Assert.Equal(0, single.SelectedIndex);
        Click(first);
        Assert.Equal(-1, single.SelectedIndex);
    }

    [Fact]
    public void NativeAppearance_KeysPickTheOptionsAndTheStateOfThePickerIsTracked()
    {
        var document = Load("<select>" + Options + "</select>", "<style>body { margin: 0 }</style>");
        var select = Select(document);
        var events = new List<string>();
        select.AddEventListener("change", _ => events.Add(select.Value));
        select.Focus();
        Press(document, "ArrowDown");
        Press(document, "ArrowDown");
        Assert.Equal(["two", "four"], events);
        Assert.False(select.Matches(":open"));

        Click(select);
        Assert.True(select.Matches(":open"));
        Press(document, "Escape");
        Assert.False(select.Matches(":open"));
    }

    [Fact]
    public void ChangingTheAppearance_ClosesThePicker()
    {
        var document = Load("<select>" + Options + "</select>");
        var select = Select(document);
        Click(select);
        Assert.True(select.Matches(":open"));
        select.SetAttribute("style", "appearance: auto");
        document.CheckOpenSelectPickers();
        Assert.False(select.Matches(":open"));
    }

    [Fact]
    public void Picker_ScrollsTheFocusedOptionIntoView()
    {
        var options = string.Concat(Enumerable.Range(1, 200).Select(i => $"<option id=o{i}>Option {i}</option>"));
        var document = Load("<select>" + options + "</select>");
        var select = Select(document);
        select.Value = "Option 100";
        Click(select);
        var selected = document.GetElementById("o100")!;
        Assert.Same(selected, document.ActiveElement);

        // The select popover is a scroll container in the space under the select element: the selected option is in it
        var rect = selected.GetBoundingClientRect();
        Assert.True(rect.Top >= select.GetBoundingClientRect().Bottom, $"top {rect.Top}");
        Assert.True(rect.Bottom <= 600, $"bottom {rect.Bottom}");

        // PageDown focuses the last option of the scrollport, then scrolls by a page
        Press(document, "PageDown");
        var first = document.ActiveElement!;
        Assert.Equal(rect.Top, selected.GetBoundingClientRect().Top);
        Assert.NotSame(selected, first);
        Press(document, "PageDown");
        Assert.NotSame(first, document.ActiveElement);
        Assert.NotEqual(rect.Top, selected.GetBoundingClientRect().Top);
    }

    [Fact]
    public void NativeListBox_APressOnARowPicksItsOption()
    {
        var document = Load("<select multiple size=4 style='font: 16px/20px monospace; padding: 0; border: 1px solid'>" + Options + "</select>", "<style>body { margin: 0 }</style>");
        var select = Select(document);
        var mouse = document.BrowsingContext!.Input.Mouse;
        var changes = 0;
        select.AddEventListener("change", _ => changes++);

        // The rows have the line height of the select element
        mouse.Move(10, 1 + 30);
        mouse.Down();
        mouse.Up();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("two", select.Value);
        Assert.Equal(1, changes);

        // The disabled option is not picked
        mouse.Move(10, 1 + 50);
        mouse.Down();
        mouse.Up();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("two", select.Value);

        // The arrow keys select the next option that is not disabled
        Assert.Same(select, document.ActiveElement);
        Press(document, "ArrowDown");
        Assert.Equal("four", select.Value);
        Assert.Equal(1u, select.SelectedOptions.Length);
    }
}
