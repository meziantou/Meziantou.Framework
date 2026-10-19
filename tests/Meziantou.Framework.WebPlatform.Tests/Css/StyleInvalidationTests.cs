using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>
/// The invalidation of the computed styles element by element (see StyleEngine.Invalidation.cs): the styles of the elements a
/// change may affect are computed again, the other styles are kept (the same <see cref="ComputedStyle"/> instances).
/// </summary>
public sealed class StyleInvalidationTests : IDisposable
{
    private const string Green = "rgb(0, 128, 0)";
    private const string Red = "rgb(255, 0, 0)";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock });
        var document = context.Navigate(_ => "<!DOCTYPE html>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private void Advance(double milliseconds)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, 1000d / 60);
            _clock.Advance(TimeSpan.FromMilliseconds(step));
            _contexts.EventLoop.RunUntilIdle();
            remaining -= step;
        }
    }

    private static Element Find(Document document, string selector) => document.QuerySelector(selector)!;

    private static string Get(Document document, string selector, string property) =>
        document.DefaultView!.GetComputedStyle(Find(document, selector)).GetPropertyValue(property);

    private static ComputedStyle Style(Document document, string selector) => document.GetStyleEngine().GetStyle(Find(document, selector));

    /// <summary>Computes the styles of all the elements of the document.</summary>
    private static Dictionary<Element, ComputedStyle> ComputeAll(Document document)
    {
        var engine = document.GetStyleEngine();
        var result = new Dictionary<Element, ComputedStyle>();
        foreach (var node in document.QuerySelectorAll("*"))
        {
            var element = (Element)node;
            result[element] = engine.GetStyle(element);
        }

        return result;
    }

    /// <summary>Returns the ids of the elements whose style was computed again since <paramref name="before"/>.</summary>
    private static string[] Restyled(Document document, Dictionary<Element, ComputedStyle> before)
    {
        var after = ComputeAll(document);
        return [.. before.Where(entry => after.TryGetValue(entry.Key, out var style) && !ReferenceEquals(style, entry.Value)).Select(entry => entry.Key.Id is { Length: > 0 } id ? id : entry.Key.LocalName)];
    }

    [Fact]
    public void ClassChange_RestylesTheElementAndTheDescendantsWhoseRulesMatch()
    {
        var document = Parse("<style>.on { color: green } .on .b { color: green }</style><div id=a><span id=b class=b></span><span id=c></span></div><div id=d><span id=e class=b></span></div>");
        var engine = document.GetStyleEngine();
        var before = ComputeAll(document);
        var count = engine.ElementInvalidationCount;

        Find(document, "#a").ClassList.Add("on");
        Assert.Equal(Green, Get(document, "#a", "color"));
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal(Green, Get(document, "#c", "color")); // (inherited)
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#e", "color"));
        Assert.Equal(["a", "b", "c"], Restyled(document, before));
        Assert.Equal(count + 1, engine.ElementInvalidationCount);
    }

    [Fact]
    public void ClassChange_WithoutRule_KeepsAllTheStyles()
    {
        var document = Parse("<style>.on { color: green }</style><div id=a><span id=b></span></div>");
        var before = ComputeAll(document);
        Find(document, "#a").ClassList.Add("unused");
        Assert.Empty(Restyled(document, before));
    }

    private Document ParseWithStyleSheets()
    {
        var document = Parse("<style>#a { color: red } @media all { #b { color: red } }</style><div id=a></div><div id=b></div>");
        document.AdoptedStyleSheets.Add(new CssStyleSheet(document));
        return document;
    }

    private static void ChangeStyleSheet(Document document, string change)
    {
        var sheet = (CssStyleSheet)document.StyleSheets[0];
        switch (change)
        {
            case "insertRule":
                sheet.InsertRule("#a { color: green }", sheet.CssRules.Length);
                break;
            case "deleteRule":
                sheet.DeleteRule(0);
                break;
            case "declaration":
                ((CssStyleRule)sheet.CssRules[0]).Style.SetProperty("color", "green");
                break;
            case "selectorText":
                ((CssStyleRule)sheet.CssRules[0]).SelectorText = "#b";
                break;
            case "media":
                sheet.Media.MediaText = "print";
                break;
            case "disabled":
                sheet.Disabled = true;
                break;
            case "groupingRule":
                ((CssMediaRule)sheet.CssRules[1]).InsertRule("#b { color: green }", 1);
                break;
            case "ruleMedia":
                ((CssMediaRule)sheet.CssRules[1]).Media.AppendMedium("print");
                break;
            case "adoptedStyleSheets":
                var adopted = new CssStyleSheet(document);
                adopted.ReplaceSync("#a { color: green }");
                document.AdoptedStyleSheets.Add(adopted);
                break;
            case "replaceSync":
                document.AdoptedStyleSheets[0].ReplaceSync("#a { color: green }");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, message: null);
        }
    }

    [Theory]
    [InlineData("insertRule")]
    [InlineData("deleteRule")]
    [InlineData("declaration")]
    [InlineData("selectorText")]
    [InlineData("media")]
    [InlineData("disabled")]
    [InlineData("groupingRule")]
    [InlineData("ruleMedia")]
    [InlineData("adoptedStyleSheets")]
    [InlineData("replaceSync")]
    public void StyleSheetChange_InvalidatesTheStylesOfTheDocument(string change)
    {
        var document = ParseWithStyleSheets();
        var engine = document.GetStyleEngine();
        var before = ComputeAll(document);
        var version = engine.Version;

        ChangeStyleSheet(document, change);
        Assert.NotEqual(version, engine.Version);
        Assert.NotEmpty(Restyled(document, before));
    }

    [Theory]
    [InlineData("insertRule")]
    [InlineData("deleteRule")]
    [InlineData("declaration")]
    [InlineData("selectorText")]
    [InlineData("media")]
    [InlineData("disabled")]
    [InlineData("groupingRule")]
    [InlineData("ruleMedia")]
    [InlineData("adoptedStyleSheets")]
    [InlineData("replaceSync")]
    public void StyleSheetChangeOfAnotherDocument_KeepsTheStylesAndTheLayout(string change)
    {
        var document = ParseWithStyleSheets();
        var other = ParseWithStyleSheets();
        var engine = document.GetStyleEngine();
        var layoutEngine = document.GetLayoutEngine();
        layoutEngine.EnsureLayout();
        var before = ComputeAll(document);
        var version = engine.Version;
        var layoutCount = layoutEngine.LayoutCount;

        ChangeStyleSheet(other, change);
        Assert.Equal(version, engine.Version);
        Assert.Empty(Restyled(document, before));
        Assert.False(layoutEngine.NeedsLayout);
        layoutEngine.EnsureLayout();
        Assert.Equal(layoutCount, layoutEngine.LayoutCount);
    }

    [Fact]
    public void ClassChange_SameCascadedValues_KeepsTheDescendants()
    {
        // The rule of the subtree scope matches no descendant: their styles are checked and kept
        var document = Parse("<style>.on .x { color: green }</style><div id=a><span id=b></span><span id=c></span></div>");
        var before = ComputeAll(document);
        Find(document, "#a").ClassList.Add("on");
        Assert.Empty(Restyled(document, before));
    }

    [Fact]
    public void IdChange()
    {
        var document = Parse("<style>#on { color: green } #on + span { color: green }</style><div id=a></div><span id=b></span><span id=c></span>");
        var before = ComputeAll(document);
        Find(document, "#a").Id = "on";
        Assert.Equal(Green, Get(document, "#on", "color"));
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal(["on", "b"], Restyled(document, before));
    }

    [Fact]
    public void AttributeChange_AttributeSelector()
    {
        var document = Parse("<style>[data-state=on] { color: green } [data-state=on] > i { color: green }</style><div id=a data-state=off><i id=b></i></div><div id=c></div>");
        var before = ComputeAll(document);
        Find(document, "#a").SetAttribute("data-state", "on");
        Assert.Equal(Green, Get(document, "#a", "color"));
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal(["a", "b"], Restyled(document, before));
    }

    [Fact]
    public void AttributeChange_AttrFunction()
    {
        // No selector references the attribute: the style references it with attr()
        var document = Parse("<style>#a { width: attr(data-width type(<length>)) } #a::before { content: attr(data-label) }</style><div id=a data-width=10px data-label=x></div>");
        Assert.Equal("10px", Get(document, "#a", "width"));
        Assert.Equal("\"x\"", document.DefaultView!.GetComputedStyle(Find(document, "#a"), "::before").GetPropertyValue("content"));
        Find(document, "#a").SetAttribute("data-width", "20px");
        Find(document, "#a").SetAttribute("data-label", "y");
        Assert.Equal("20px", Get(document, "#a", "width"));
        Assert.Equal("\"y\"", document.DefaultView!.GetComputedStyle(Find(document, "#a"), "::before").GetPropertyValue("content"));
    }

    [Fact]
    public void StyleAttributeChange_RestylesTheSubtree()
    {
        var document = Parse("<div id=a><span id=b></span></div><div id=c></div>");
        var before = ComputeAll(document);
        ((HtmlElement)Find(document, "#a")).Style.SetProperty("color", "green");
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal(["a", "b"], Restyled(document, before));
    }

    [Fact]
    public void InheritedValue_ReachesTheDescendants()
    {
        var document = Parse("<style>.big { font-size: 20px } .big span { width: 2em }</style><div id=a><p id=b><span id=c></span></p></div>");
        _ = ComputeAll(document);
        Find(document, "#a").ClassList.Add("big");
        Assert.Equal("20px", Get(document, "#c", "font-size"));
        Assert.Equal("40px", Get(document, "#c", "width"));
    }

    [Fact]
    public void SiblingCombinators()
    {
        var document = Parse("<style>.x + span { color: green } .x ~ b { color: green }</style><div><i id=a></i><span id=b></span><span id=c></span><b id=d></b></div>");
        var before = ComputeAll(document);
        Find(document, "#a").ClassList.Add("x");
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#c", "color"));
        Assert.Equal(Green, Get(document, "#d", "color"));
        Assert.Equal(["b", "d"], Restyled(document, before));

        // The insertion of an element changes the next sibling of #a
        Find(document, "#a").After(document.CreateElement("u"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#b", "color"));
        Assert.Equal(Green, Get(document, "#d", "color"));
    }

    [Fact]
    public void StructuralPseudoClasses_InsertionAndRemoval()
    {
        var document = Parse("<style>li:first-child { color: green } li:nth-child(even) { color: red } ul:empty { color: green }</style><ul id=list><li id=a></li><li id=b></li></ul><ul id=empty></ul>");
        Assert.Equal(Green, Get(document, "#a", "color"));
        Assert.Equal(Red, Get(document, "#b", "color"));
        Assert.Equal(Green, Get(document, "#empty", "color"));

        Find(document, "#list").Prepend(document.CreateElement("li"));
        Assert.Equal(Red, Get(document, "#a", "color"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#b", "color"));

        Find(document, "#list").FirstElementChild!.Remove();
        Assert.Equal(Green, Get(document, "#a", "color"));
        Assert.Equal(Red, Get(document, "#b", "color"));

        Find(document, "#empty").TextContent = "text";
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#empty", "color"));
    }

    [Fact]
    public void TreeCountingFunctions()
    {
        var document = Parse("<style>li { width: calc(sibling-index() * 10px) }</style><ul id=list><li id=a></li><li id=b></li></ul>");
        Assert.Equal("20px", Get(document, "#b", "width"));
        Find(document, "#list").Prepend(document.CreateElement("li"));
        Assert.Equal("30px", Get(document, "#b", "width"));
    }

    [Fact]
    public void Has_ChangeOfADescendant()
    {
        var document = Parse("<style>.card:has(.selected) { color: green } .list:has(> li + .selected) { color: green }</style><div class=card id=card><p><span id=s></span></p></div><ul class=list id=list><li></li><li id=item></li></ul><div id=other></div>");
        var before = ComputeAll(document);
        Find(document, "#s").ClassList.Add("selected");
        Find(document, "#item").ClassList.Add("selected");
        Assert.Equal(Green, Get(document, "#card", "color"));
        Assert.Equal(Green, Get(document, "#list", "color"));
        Assert.DoesNotContain("other", Restyled(document, before));

        Find(document, "#item").Remove();
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#list", "color"));
    }

    [Fact]
    public void Hover()
    {
        var document = Parse("<style>.card:hover { color: green } .card:hover .label { color: green }</style><div class=card id=card><span id=label class=label></span><b id=b></b></div><div class=card id=other></div>");
        var before = ComputeAll(document);
        document.DesignatedElement = Find(document, "#b");
        Assert.Equal(Green, Get(document, "#card", "color"));
        Assert.Equal(Green, Get(document, "#label", "color"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#other", "color"));
        Assert.DoesNotContain("other", Restyled(document, before));

        document.DesignatedElement = null;
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#card", "color"));
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#label", "color"));
    }

    [Fact]
    public void FocusWithin_FocusedElementRemoved()
    {
        var document = Parse("<style>div:focus-within { color: green }</style><div id=parent><input id=child></div>");
        ((HtmlElement)Find(document, "#child")).Focus();
        Assert.Equal(Green, Get(document, "#parent", "color"));
        Find(document, "#child").Remove();
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#parent", "color"));
    }

    [Fact]
    public void Transition_StartsFromTheStyleBeforeTheChange()
    {
        var document = Parse("<style>#a { width: 100px; transition: width 1s linear } #a.wide { width: 200px }</style><div id=a></div><div id=b></div>");
        var styleOfB = Style(document, "#b");
        Assert.Equal("100px", Get(document, "#a", "width"));

        Find(document, "#a").ClassList.Add("wide");
        Assert.Equal("100px", Get(document, "#a", "width"));
        Assert.Single(Find(document, "#a").GetAnimations());
        Advance(500);
        Assert.InRange(double.Parse(Get(document, "#a", "width").TrimEnd('p', 'x'), CultureInfo.InvariantCulture), 140, 160);
        Assert.Same(styleOfB, Style(document, "#b"));
    }

    [Fact]
    public void Transition_OfAPseudoElementWhoseElementKeepsItsStyle()
    {
        var document = Parse("<style>#a::before { content: 'x'; color: red; transition: color 1s linear } #a.on::before { color: green }</style><div id=a></div><div id=b></div>");
        var engine = document.GetStyleEngine();
        var a = Find(document, "#a");
        var styleOfA = engine.GetStyle(a);
        var styleOfB = Style(document, "#b");
        Assert.Equal(Red, document.DefaultView!.GetComputedStyle(a, "::before").GetPropertyValue("color"));

        a.ClassList.Add("on");

        // (the style of another element is read: the transition starts without a read of the style of the pseudo-element)
        Assert.Same(styleOfB, Style(document, "#b"));
        Assert.Same(styleOfA, engine.GetStyle(a));
        Assert.NotNull(document.AnimationsOrNull?.GetElementAnimations(a)?.Get("before"));
    }

    [Fact]
    public void Transition_OfAnInsertedElement_StartsFromItsStartingStyle()
    {
        var document = Parse("<style>.item { opacity: 1; transition: opacity 1s linear } @starting-style { .item { opacity: 0 } }</style><div id=list><div class=item></div></div><div id=b></div>");
        var engine = document.GetStyleEngine();
        _ = ComputeAll(document);
        var styleOfB = Style(document, "#b");
        var count = engine.ElementInvalidationCount;
        var item = document.CreateElement("div");
        item.ClassList.Add("item");
        var container = document.CreateElement("div");
        container.AppendChild(item);

        Find(document, "#list").AppendChild(container);

        // (the style of another element is read: the transition starts without a read of the style of the inserted element)
        Assert.Same(styleOfB, Style(document, "#b"));
        Assert.Equal(count + 1, engine.ElementInvalidationCount);
        Assert.NotNull(document.AnimationsOrNull?.GetElementAnimations(item)?.Get(pseudoElement: null));
    }

    [Fact]
    public void Animation_RestylesTheTargetOnly()
    {
        var document = Parse("<style>@keyframes fade { from { opacity: 0 } to { opacity: 1 } } #a { animation: fade 1s linear }</style><div id=a><span id=c></span></div><div id=b></div>");
        Assert.Equal("0", Get(document, "#a", "opacity"));
        var styleOfB = Style(document, "#b");
        Advance(500);
        Assert.InRange(double.Parse(Get(document, "#a", "opacity"), CultureInfo.InvariantCulture), 0.4, 0.6);
        Assert.Same(styleOfB, Style(document, "#b"));
    }

    [Fact]
    public void ContainerQuery_SizeChangedByAClass()
    {
        var document = Parse("<style>#c { container-type: inline-size; width: 100px } #c.wide { width: 300px } @container (width > 200px) { #t { color: green } }</style><div id=c><div id=t></div></div><div id=other></div>");
        _ = ((HtmlElement)Find(document, "#c")).OffsetWidth;
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#t", "color"));
        Find(document, "#c").ClassList.Add("wide");
        _ = ((HtmlElement)Find(document, "#c")).OffsetWidth;
        Assert.Equal(Green, Get(document, "#t", "color"));
    }

    [Fact]
    public void StyleSheetChange_InvalidatesAllTheStyles()
    {
        var document = Parse("<style id=s>div { color: red }</style><div id=a></div>");
        var engine = document.GetStyleEngine();
        Assert.Equal(Red, Get(document, "#a", "color"));
        var count = engine.FullInvalidationCount;
        Find(document, "#s").TextContent = "div { color: green }";
        Assert.Equal(Green, Get(document, "#a", "color"));
        Assert.Equal(count + 1, engine.FullInvalidationCount);
    }

    [Fact]
    public void AutoDirectionality_TextChange()
    {
        var document = Parse("<style>:dir(rtl) { color: green }</style><div dir=auto id=a><span id=b>abc</span></div>");
        Assert.Equal("ltr", Get(document, "#b", "direction"));
        Find(document, "#b").FirstChild!.NodeValue = "\u05D0";
        Assert.Equal(Green, Get(document, "#b", "color"));
        Assert.Equal("rtl", Get(document, "#a", "direction"));
    }

    [Fact]
    public void Transition_OfAnInheritedRegisteredCustomProperty()
    {
        var document = Parse("<div id=container><div id=target></div></div>");
        Meziantou.Framework.WebPlatform.Css.Om.Css.RegisterProperty(document, new Meziantou.Framework.WebPlatform.Css.Om.PropertyDefinition { Name = "--my-length", Syntax = "<length>", InitialValue = "100px", Inherits = true });
        var target = (HtmlElement)Find(document, "#target");
        var container = (HtmlElement)Find(document, "#container");
        target.Style.SetProperty("margin-left", "var(--my-length)");
        Assert.Equal("100px", Get(document, "#target", "margin-left"));
        container.Style.SetProperty("transition", "--my-length 1s -500ms linear");
        container.Style.SetProperty("--my-length", "200px");
        Assert.Equal("150px", Get(document, "#target", "margin-left"));
    }

    [Fact]
    public void Move_ThroughARemovedSubtree()
    {
        // The removal of #c from #b is not recorded (#b is removed first): the insertion of #c discards its style
        var document = Parse("<style>#a > #c { color: green }</style><div id=a><div id=b><div id=c></div></div></div>");
        var b = Find(document, "#b");
        var c = Find(document, "#c");
        Assert.Equal("rgb(0, 0, 0)", Get(document, "#c", "color"));
        b.Remove();
        Find(document, "#a").AppendChild(c);
        Assert.Equal(Green, Get(document, "#c", "color"));
    }

    [Fact]
    public void Animation_CurrentColorKeyframe_FollowsTheInheritedColor()
    {
        var document = Parse("<style>@property --c { syntax: '<color>'; inherits: false; initial-value: black } @keyframes test { from { --c: currentcolor } to { --c: rgb(200, 200, 200) } } #outer { color: rgb(100, 100, 100) } #div { animation: test 100s -50s linear paused }</style><div id=outer><div id=div></div></div>");
        Assert.Equal("rgb(150, 150, 150)", Get(document, "#div", "--c"));
        ((HtmlElement)Find(document, "#outer")).Style.SetProperty("color", "rgb(50, 50, 50)");
        Assert.Equal("rgb(125, 125, 125)", Get(document, "#div", "--c"));
    }
}
