using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The resolved values of getComputedStyle() that are used values from the layout (https://drafts.csswg.org/cssom/#resolved-values).</summary>
public sealed class ResolvedValuesTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string Get(Document document, string id, string property, string? pseudoElement = null)
        => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!, pseudoElement).GetPropertyValue(property);

    [Theory]
    [InlineData("width: 100px; min-width: 200px", "width", "200px")]
    [InlineData("width: 300px; max-width: 200px", "width", "200px")]
    [InlineData("width: 50%; padding: 10px; border: 5px solid", "width", "400px")]
    [InlineData("width: 50%; padding: 10px; border: 5px solid; box-sizing: border-box", "width", "400px")]
    [InlineData("width: fit-content", "width", "20px")]
    [InlineData("width: min-content; zoom: 2", "width", "20px")] // In the coordinates of the element
    [InlineData("height: auto", "height", "10px")]
    [InlineData("height: 50%", "height", "10px")] // A percentage of an indefinite height
    [InlineData("width: 100px; height: 100px; overflow: scroll", "width", "100px")] // The scrollbar gutters are in the content box: the value round-trips
    [InlineData("width: 100px; height: 100px; overflow: scroll", "height", "100px")]
    [InlineData("display: inline; width: 50px", "width", "50px")] // width does not apply to the non-replaced inline boxes
    [InlineData("display: inline-block", "width", "20px")]
    [InlineData("display: none; width: 50%", "width", "50%")]
    [InlineData("display: contents; width: 50%", "width", "50%")]
    [InlineData("writing-mode: vertical-rl; block-size: 30px", "width", "30px")]
    [InlineData("writing-mode: vertical-rl; block-size: 30px", "inline-size", "20px")]
    public void Size(string style, string property, string expected)
    {
        var document = Parse($"<div id=t style='{style}'>XX</div>");
        Assert.Equal(expected, Get(document, "t", property));
    }

    [Theory]
    [InlineData("margin: auto", "margin-left", "300px")]
    [InlineData("margin: auto", "margin-right", "300px")]
    [InlineData("margin: auto", "margin-top", "0px")]
    [InlineData("margin-left: auto", "margin-left", "600px")]
    [InlineData("margin-left: auto", "margin-right", "0px")]
    [InlineData("margin-right: auto", "margin-right", "600px")]
    [InlineData("margin-right: 10%", "margin-right", "80px")] // Over-constrained: the specified value, not the space left
    [InlineData("margin: 10%", "margin-top", "80px")]
    [InlineData("padding: 10%", "padding-bottom", "80px")]
    [InlineData("margin: auto; position: relative; left: 10px", "margin-left", "300px")]
    [InlineData("margin: auto; position: absolute; left: 0; right: 0", "margin-left", "300px")]
    [InlineData("margin: auto; float: left", "margin-left", "0px")]
    [InlineData("margin: auto; display: inline-block", "margin-right", "0px")]
    [InlineData("display: none; margin: auto", "margin-left", "auto")]
    [InlineData("display: none; margin: 10%", "margin-left", "10%")]
    public void BoxEdges(string style, string property, string expected)
    {
        var document = Parse($"<div id=t style='width: 200px; height: 10px; {style}'></div>");
        Assert.Equal(expected, Get(document, "t", property));
    }

    [Fact]
    public void AutoMargins_FloatsShortenTheSpace()
    {
        // https://github.com/w3c/csswg-drafts/issues/9174: the auto margins are the distances to the content box of the container
        var document = Parse("<div style='width: 100px'><div style='float: right; width: 20px; height: 40px'></div><div id=t style='display: flow-root; width: 40px; height: 10px; margin: auto'></div></div>");
        Assert.Equal("20px", Get(document, "t", "margin-left"));
        Assert.Equal("40px", Get(document, "t", "margin-right"));
    }

    [Fact]
    public void InlineBox_MarginsAndPaddings()
    {
        var document = Parse("<div style='width: 200px'>X<span id=t style='margin: 10% auto; padding: 5% 1px'>X</span></div>");
        Assert.Equal("20px", Get(document, "t", "margin-top"));
        Assert.Equal("0px", Get(document, "t", "margin-left"));
        Assert.Equal("10px", Get(document, "t", "padding-top"));
        Assert.Equal("1px", Get(document, "t", "padding-left"));
    }

    [Fact]
    public void Zoom_ValuesAreInTheCoordinatesOfTheElement()
    {
        var document = Parse("<div style='width: 400px'><div id=t style='zoom: 2; width: 50%; margin-left: 10%; height: 10px'></div></div>");
        Assert.Equal("100px", Get(document, "t", "width"));
        Assert.Equal("20px", Get(document, "t", "margin-left"));
    }

    [Fact]
    public void PseudoElement()
    {
        var document = Parse("<style>#t::before { content: 'XXX'; display: block; padding: 10% }</style><div id=t style='width: 100px'></div>");
        Assert.Equal("80px", Get(document, "t", "width", "::before"));
        Assert.Equal("10px", Get(document, "t", "padding-left", "::before"));
    }

    [Fact]
    public void TransformOrigin_ResolvedAgainstTheReferenceBox()
    {
        var document = Parse("<div id=t style='height: 20px; padding: 10px; transform-origin: 50% 100%'></div><div id=c style='height: 20px; padding: 10px; transform-box: content-box; transform-origin: 50% 100%'></div><span id=i style='transform-origin: 50% 100%'>X</span>");
        Assert.Equal("400px 40px", Get(document, "t", "transform-origin"));
        Assert.Equal("390px 20px", Get(document, "c", "transform-origin"));
        Assert.Equal("50% 100%", Get(document, "i", "transform-origin")); // Not transformable
    }

    [Fact]
    public void Table_SizesOfTheTableBox()
    {
        var document = Parse("<table id=t style='margin: 0 auto; width: 100px; padding: 0'><tr><td>X</td></tr></table>");
        Assert.Equal("100px", Get(document, "t", "width"));
        Assert.Equal("350px", Get(document, "t", "margin-left"));
    }

    [Fact]
    public void ForcedLayout_OnlyForLayoutDependentProperties()
    {
        var document = Parse("<div id=t style='width: 50%; margin: 10px; padding: 1px'></div>");
        Assert.Null(document.LayoutEngineOrNull);
        Assert.Equal("10px", Get(document, "t", "margin-left"));
        Assert.Equal("1px", Get(document, "t", "padding-left"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "t", "color"));
        Assert.Null(document.LayoutEngineOrNull);
        Assert.Equal("400px", Get(document, "t", "width"));
        Assert.NotNull(document.LayoutEngineOrNull);
    }

    [Fact]
    public void NoBrowsingContext_SizesKnownWithoutLayout()
    {
        var document = HtmlDocumentParser.ParseDocument("<div id=t style='width: 50%; height: auto'></div>");
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("t")!);
        Assert.Equal("392px", style.GetResolvedValue("width")); // The block fills the body of the default 800px viewport
        Assert.Equal("auto", style.GetResolvedValue("height"));
        Assert.Null(document.LayoutEngineOrNull);
    }

    [Fact]
    public void GridTrackList_FromTheGridLayout()
    {
        // The used track sizes recorded by the grid layout, in the coordinates of the element (divided by its zoom)
        var document = Parse("<div id=t style='display: grid; width: 300px; grid-template-columns: [a] 1fr [b] 2fr; zoom: 2'><div style='height: 10px'></div></div>");
        Assert.Equal("[a] 100px [b] 200px", Get(document, "t", "grid-template-columns"));
        Assert.Equal("10px", Get(document, "t", "grid-template-rows"));
    }

    [Fact]
    public void Transition_StartsAfterForcedLayout()
    {
        var document = Parse("<div><div id=a style='width: 10px; height: 10px'></div><div id=b style='width: 10px; height: 10px'></div></div>");
        Assert.Equal("10px", Get(document, "a", "width"));
        var a = (HtmlElement)document.GetElementById("a")!;
        a.Style.SetProperty("transition-duration", "100s");
        a.Style.SetProperty("transition-delay", "-50s");
        a.Style.SetProperty("transition-timing-function", "linear");
        a.Style.SetProperty("transition-property", "width");
        a.Style.SetProperty("width", "40px");
        Assert.Equal("10px", Get(document, "b", "width"));
        Assert.Equal("25px", Get(document, "a", "width"));
    }

    [Fact]
    public void Transition_StartsAfterForcedLayout_StyleSheets()
    {
        var document = Parse("<div id=test></div>");
        var test = document.GetElementById("test")!;
        Assert.Equal("0px", Get(document, "test", "height"));
        var style1 = document.CreateElement("style");
        style1.TextContent = "#test { block-size: 0px } #test.transition { block-size: 100px }";
        document.Head!.AppendChild(style1);
        Assert.Equal("0px", Get(document, "test", "height"));
        var style2 = document.CreateElement("style");
        style2.TextContent = "#test { transition-property: block-size; transition-timing-function: linear; transition-duration: 10s; transition-delay: -5s }";
        document.Head!.AppendChild(style2);
        test.ClassName = "transition";
        Assert.Equal("50px", Get(document, "test", "height"));
    }
}
