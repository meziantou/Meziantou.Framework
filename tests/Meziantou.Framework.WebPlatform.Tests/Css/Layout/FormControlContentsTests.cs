using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The form controls whose parts are boxes (see <see cref="FormControlContents"/>): the rows of a list box with native
/// appearance, the <c>::slider-track</c>, <c>::slider-fill</c> and <c>::slider-thumb</c> of the slider-like controls with
/// primitive appearance, and the <c>::file-selector-button</c> of a file upload control.
/// </summary>
public sealed class FormControlContentsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport; the controls have the 10px/1 Ahem font, no border, no padding and no margin.</summary>
    private Document Parse(string html, double scrollbarThickness = 0)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = scrollbarThickness } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } select, input, progress, meter { font: 10px/1 Ahem; border: 0; padding: 0; margin: 0; vertical-align: top }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, without author style for the controls.</summary>
    private Document ParseUnstyled(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem; color: rgb(0, 128, 0) } .base { appearance: base; vertical-align: top }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static (double X, double Y, double Width, double Height) Rect(Document document, string selector)
    {
        var rect = document.QuerySelector(selector)!.GetBoundingClientRect();
        return (rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static BoxFragment GetFragment(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    /// <summary>Returns the border box of the box of a pseudo-element of a control, relative to the border box of the control.</summary>
    private static PhysicalRect GetPart(Document document, string id, string pseudoElement)
    {
        var control = GetFragment(document, id);
        var part = Find(control, pseudoElement) ?? throw new InvalidOperationException("The control has no " + pseudoElement + " box");
        var origin = LayoutEngine.MapToRoot(control, new PhysicalRect(0, 0, 0, 0)).Bounds;
        var bounds = LayoutEngine.MapToRoot(part, part.BorderBoxRect).Bounds;
        return new PhysicalRect(bounds.X - origin.X, bounds.Y - origin.Y, bounds.Width, bounds.Height);

        static BoxFragment? Find(Fragment fragment, string pseudoElement)
        {
            foreach (var child in fragment.Children)
            {
                if (child is BoxFragment { Box.PseudoElement: var name } box && name == pseudoElement)
                    return box;

                if (Find(child, pseudoElement) is { } found)
                    return found;
            }

            return null;
        }
    }

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
        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true, Composed = true }) { IsTrusted = true }, document.ActiveElement!);
        _contexts.EventLoop.RunUntilIdle();
    }

    [Fact]
    public void ListBox_RowsAreTheBoxesOfTheOptions()
    {
        // The natural size is the widest row and a line per row of the display size; the rows are stacked in the content box
        var document = Parse("<select id=s size=3><option id=a>aa<option id=b>bbbb<optgroup id=g label=ggg><option id=c>c</optgroup></select>");
        Assert.Equal((0, 0, 40, 30), Rect(document, "#s"));
        Assert.Equal((0, 0, 40, 10), Rect(document, "#a"));
        Assert.Equal((0, 10, 40, 10), Rect(document, "#b"));

        // The label of a group is a row, before its options
        Assert.Equal((0, 20, 40, 20), Rect(document, "#g"));
        Assert.Equal((0, 30, 40, 10), Rect(document, "#c"));
    }

    [Fact]
    public void ListBox_IsTheScrollContainerOfItsRows()
    {
        var document = Parse("<select id=s size=2><option>a<option>b<option>c<option id=d>d</select>");
        var select = document.GetElementById("s")!;
        Assert.Equal(20, select.ClientHeight);
        Assert.Equal(40, select.ScrollHeight);

        // The inline axis does not scroll
        Assert.Equal(select.ClientWidth, select.ScrollWidth);
        select.ScrollTop = 15;
        Assert.Equal(15, select.ScrollTop);
        Assert.Equal((0, 15, 10, 10), Rect(document, "#d"));
        select.ScrollLeft = 15;
        Assert.Equal(0, select.ScrollLeft);
    }

    [Fact]
    public void ListBox_ReservesTheSpaceOfItsScrollbar()
    {
        // The scrollbar of the block axis takes its space from the natural size, whether it is displayed or not
        var document = Parse("<select id=s size=2><option>aaa<option>b<option>c</select><select id=t size=2><option>aaa</select>", scrollbarThickness: 15);
        Assert.Equal((0, 0, 45, 20), Rect(document, "#s"));
        Assert.Equal(30, document.GetElementById("s")!.ClientWidth);
        Assert.Equal((45, 0, 45, 20), Rect(document, "#t"));
        Assert.Equal(45, document.GetElementById("t")!.ClientWidth);
    }

    [Fact]
    public void ListBox_VerticalWritingMode()
    {
        // The rows are stacked along the block axis: from the right in vertical-rl
        var document = Parse("<select id=s size=2 style='writing-mode: vertical-rl'><option id=a>aa<option id=b>b<option id=c>c</select>");
        Assert.Equal((0, 0, 20, 20), Rect(document, "#s"));
        Assert.Equal((10, 0, 10, 20), Rect(document, "#a"));
        Assert.Equal((0, 0, 10, 20), Rect(document, "#b"));
        var select = document.GetElementById("s")!;
        Assert.Equal(30, select.ScrollWidth);
        select.ScrollLeft = -10;
        Assert.Equal(-10, select.ScrollLeft);
        Assert.Equal((0, 0, 10, 20), Rect(document, "#c"));
    }

    [Fact]
    public void ListBox_RowShowsTheLabelOfTheOption()
    {
        var document = Parse("<select id=s multiple><option id=a label=xyz>a<option id=b> b <!-- c -->b </select>");
        var texts = new List<string>();
        Collect(GetFragment(document, "s"));
        Assert.Equal(["xyz", "b b"], texts);

        void Collect(Fragment fragment)
        {
            foreach (var child in fragment.Children)
            {
                if (child is TextFragment text)
                {
                    texts.Add(text.Run.Data[text.Start..text.End]);
                }

                Collect(child);
            }
        }
    }

    [Fact]
    public void ListBox_AuthorStylesApplyToTheRows()
    {
        var document = Parse("<style>option { background-color: rgb(1, 2, 3); block-size: 15px } option:checked { background-color: rgb(4, 5, 6) }</style><select id=s size=2><option id=a>a<option id=b selected>b</select>");
        Assert.Equal((0, 15, 10, 15), Rect(document, "#b"));
        var window = document.DefaultView!;
        Assert.Equal("rgb(1, 2, 3)", window.GetComputedStyle(document.GetElementById("a")!).GetPropertyValue("background-color"));
        Assert.Equal("rgb(4, 5, 6)", window.GetComputedStyle(document.GetElementById("b")!).GetPropertyValue("background-color"));
    }

    [Fact]
    public void ListBox_PointerPicksTheOptionOfTheRow()
    {
        var document = Parse("<select id=s multiple><option id=a>a<option id=b>b<option id=c>c</select>");
        var (a, b) = ((HtmlOptionElement)document.GetElementById("a")!, (HtmlOptionElement)document.GetElementById("b")!);
        Click(b);
        Assert.True(b.Selected);
        Click(a);
        Assert.True(a.Selected);
        Assert.False(b.Selected);
        Assert.Same(document.GetElementById("s"), document.ActiveElement);
    }

    [Fact]
    public void ListBox_KeysRevealTheSelectedRow()
    {
        var document = Parse("<select id=s size=2><option id=a selected>a<option>b<option>c<option id=d>d</select>");
        var select = (HtmlSelectElement)document.GetElementById("s")!;
        select.Focus();
        Press(document, "ArrowDown");
        Assert.Equal(0, select.ScrollTop);
        Press(document, "ArrowDown");
        Assert.Equal(2, select.SelectedIndex);
        Assert.Equal(10, select.ScrollTop);
        Press(document, "End");
        Assert.Equal(20, select.ScrollTop);
        Press(document, "Home");
        Assert.Equal(0, select.ScrollTop);
    }

    [Fact]
    public void Slider_PrimitiveAppearance_PartsAreBoxes()
    {
        // The track fills the content box, the fill is the progress of the control, the thumb is moved along the track
        var document = Parse("<input id=r type=range value=25 style='appearance: none; width: 116px; height: 20px'>");
        Assert.Equal(new PhysicalRect(0, 0, 116, 20), GetPart(document, "r", "slider-track"));
        Assert.Equal(new PhysicalRect(0, 0, 29, 20), GetPart(document, "r", "slider-fill"));
        Assert.Equal(new PhysicalRect(25, 2, 16, 16), GetPart(document, "r", "slider-thumb"));
    }

    [Fact]
    public void Slider_PartsHaveTheClampedAutomaticBlockSize()
    {
        // The content box of a control whose block size is auto is its natural block size clamped by its minimum and maximum
        // block sizes: the parts are laid out in the content box the control gets
        var document = Parse("<input id=a type=range style='appearance: none; width: 116px; max-height: 8px'><input id=b type=range style='appearance: none; width: 116px; min-height: 30px'>");
        Assert.Equal(new PhysicalRect(0, 0, 116, 8), GetPart(document, "a", "slider-track"));
        Assert.Equal(8, Rect(document, "#a").Height);
        Assert.Equal(new PhysicalRect(0, 0, 116, 30), GetPart(document, "b", "slider-track"));
        Assert.Equal(30, Rect(document, "#b").Height);
    }

    [Fact]
    public void Slider_RightToLeft()
    {
        var document = Parse("<input id=r type=range value=25 dir=rtl style='appearance: none; width: 116px; height: 20px'>");
        Assert.Equal(new PhysicalRect(87, 0, 29, 20), GetPart(document, "r", "slider-fill"));
        Assert.Equal(new PhysicalRect(75, 2, 16, 16), GetPart(document, "r", "slider-thumb"));
    }

    [Fact]
    public void Slider_AuthorStylesApplyToTheParts()
    {
        var document = Parse("""
            <style>
              input { appearance: none; width: 100px; height: 20px }
              input::slider-track { block-size: 4px; margin-block: 8px }
              input::slider-thumb { inline-size: 10px; block-size: 20px }
            </style>
            <input id=r type=range value=100>
            """);
        Assert.Equal(new PhysicalRect(0, 8, 100, 4), GetPart(document, "r", "slider-track"));
        Assert.Equal(new PhysicalRect(0, 8, 100, 4), GetPart(document, "r", "slider-fill"));
        Assert.Equal(new PhysicalRect(90, 0, 10, 20), GetPart(document, "r", "slider-thumb"));

        // The fill inherits from the track
        var window = document.DefaultView!;
        Assert.Equal("block", window.GetComputedStyle(document.GetElementById("r")!, "::slider-fill").GetPropertyValue("display"));
    }

    [Fact]
    public void Progress_And_Meter_PrimitiveAppearance()
    {
        var document = Parse("<progress id=p value=0.25 style='appearance: none'></progress><progress id=i style='appearance: none'></progress><meter id=m value=0.5 style='appearance: none'></meter>");
        Assert.Equal(new PhysicalRect(0, 0, 100, 10), GetPart(document, "p", "slider-track"));
        Assert.Equal(new PhysicalRect(0, 0, 25, 10), GetPart(document, "p", "slider-fill"));

        // The fill of an indeterminate progress bar has an inline size of zero
        Assert.Equal(new PhysicalRect(0, 0, 0, 10), GetPart(document, "i", "slider-fill"));
        Assert.Equal(new PhysicalRect(0, 0, 25, 10), GetPart(document, "m", "slider-fill"));
    }

    [Fact]
    public void Slider_NativeAppearance_IsAWidget()
    {
        // With native appearance, the control is painted: its pseudo-elements have no box. A progress bar whose author sets
        // a border or a background is a devolved widget: its parts are boxes
        var document = Parse("<input id=r type=range><progress id=p value=0.5></progress>");
        Assert.Empty(GetFragment(document, "r").Children);
        Assert.Equal(new PhysicalRect(0, 0, 50, 10), GetPart(document, "p", "slider-fill"));
    }

    [Fact]
    public void FileUploadControl_ButtonIsABox()
    {
        // An inline-block box sized by its line: the box of the button (2px borders, 1px and 6px paddings, a 4px margin), then the text
        var document = Parse("<input id=f type=file>");
        Assert.Equal(new PhysicalRect(0, 0, 126, 16), GetPart(document, "f", "file-selector-button"));
        Assert.Equal((0, 0, 270, 16), Rect(document, "#f"));

        var window = document.DefaultView!;
        var button = window.GetComputedStyle(document.GetElementById("f")!, "::file-selector-button");
        Assert.Equal("inline-block", button.GetPropertyValue("display"));
    }

    [Fact]
    public void FileUploadControl_AuthorStylesApplyToTheButton()
    {
        var document = Parse("<style>input::file-selector-button { border: 5px solid; padding: 10px; margin: 0 }</style><input id=f type=file multiple>");
        Assert.Equal(new PhysicalRect(0, 0, 150, 40), GetPart(document, "f", "file-selector-button"));
        Assert.Equal((0, 0, 290, 40), Rect(document, "#f"));
    }

    [Fact]
    public void BaseAppearance_UserAgentStyleSheetAppliesToTheControlAndItsPseudoElements()
    {
        var document = ParseUnstyled("<input id=t class=base><input id=n><progress id=p class=base value=0.5></progress><progress id=q style='appearance: none' value=0.5></progress>");
        var window = document.DefaultView!;
        var text = window.GetComputedStyle(document.GetElementById("t")!);
        Assert.Equal("rgb(0, 128, 0)", text.GetPropertyValue("color"));
        Assert.Equal("10px", text.GetPropertyValue("font-size"));
        Assert.Equal("border-box", text.GetPropertyValue("box-sizing"));
        Assert.Equal("rgba(0, 0, 0, 0)", text.GetPropertyValue("background-color"));
        Assert.Equal("1px", text.GetPropertyValue("border-top-width"));
        Assert.Equal("4px", text.GetPropertyValue("border-top-left-radius"));

        // The rules do not apply to the controls with native or primitive appearance
        var native = window.GetComputedStyle(document.GetElementById("n")!);
        Assert.Equal("0px", native.GetPropertyValue("border-top-width"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", native.GetPropertyValue("background-color"));

        // The pseudo-elements have the appearance of their originating element
        var track = window.GetComputedStyle(document.GetElementById("p")!, "::slider-track");
        Assert.Equal("1px", track.GetPropertyValue("border-top-width"));
        Assert.Equal("10px", track.GetPropertyValue("block-size"));
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle(document.GetElementById("p")!, "::slider-fill").GetPropertyValue("background-color"));
        var primitiveTrack = window.GetComputedStyle(document.GetElementById("q")!, "::slider-track");
        Assert.Equal("0px", primitiveTrack.GetPropertyValue("border-top-width"));
        Assert.Equal("rgb(128, 128, 128)", primitiveTrack.GetPropertyValue("background-color"));
    }

    [Fact]
    public void BaseAppearance_CheckboxHoldsItsCheckmark()
    {
        // A box of 1em x 1em with a font size of at least 24px, with the box of ::checkmark
        var document = ParseUnstyled("<input id=c class=base type=checkbox checked><input id=u class=base type=checkbox>");
        Assert.Equal((0, 0, 24, 24), Rect(document, "#c"));
        Assert.True(GetPart(document, "c", "checkmark").Width > 0);
        var window = document.DefaultView!;
        Assert.Equal("visible", window.GetComputedStyle(document.GetElementById("c")!, "::checkmark").GetPropertyValue("visibility"));
        Assert.Equal("hidden", window.GetComputedStyle(document.GetElementById("u")!, "::checkmark").GetPropertyValue("visibility"));
    }

    [Fact]
    public void BaseAppearance_ButtonHoldsItsLabel()
    {
        // An inline-flex box: 1px borders, paddings of 0.25em and 0.5em, a minimum size of 24px
        var document = ParseUnstyled("<input id=b class=base type=button value=ab>");
        Assert.Equal("inline-flex", document.DefaultView!.GetComputedStyle(document.GetElementById("b")!).GetPropertyValue("display"));
        Assert.Equal((0, 0, 32, 24), Rect(document, "#b"));
    }

    [Fact]
    public void BaseAppearance_ProgressAndMeterHoldTheirParts()
    {
        // Their intrinsic inline size is the one of HTML (10em and 5em); the track is as wide as the control (a content box
        // with 1px borders) and 1em tall
        var document = ParseUnstyled("<progress id=p class=base value=0.5></progress><meter id=m class=base value=0.2></meter>");
        Assert.Equal((0, 0, 100, 12), Rect(document, "#p"));
        Assert.Equal(new PhysicalRect(0, 0, 102, 12), GetPart(document, "p", "slider-track"));
        Assert.Equal(new PhysicalRect(1, 1, 50, 10), GetPart(document, "p", "slider-fill"));
        Assert.Equal((100, 0, 50, 12), Rect(document, "#m"));
        Assert.Equal(new PhysicalRect(1, 1, 10, 10), GetPart(document, "m", "slider-fill"));
    }

    [Fact]
    public void BaseAppearance_ProgressStretchesAsAFlexItem()
    {
        var document = ParseUnstyled("<div style='display: flex; flex-direction: column; width: 200px'><progress id=p class=base value=0.25></progress></div>");
        Assert.Equal((0, 0, 200, 12), Rect(document, "#p"));
        Assert.Equal(new PhysicalRect(1, 1, 50, 10), GetPart(document, "p", "slider-fill"));
    }

    [Fact]
    public void BaseAppearance_SliderThumbIsMovedAlongTheControl()
    {
        // inline-size: 100% in the style sheet of the specification; the thumb is 1em x 1em
        var document = ParseUnstyled("<div style='width: 110px'><input id=r class=base type=range value=50></div>");
        Assert.Equal(110, Rect(document, "#r").Width);
        var thumb = GetPart(document, "r", "slider-thumb");
        Assert.Equal((50, 10, 10), (thumb.X, thumb.Width, thumb.Height));
    }
}
