using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The incremental update of the box tree and of the layout after a change. The tests do not run in parallel with the other
/// tests: a change of a style sheet through the CSSOM by another test (the process-wide CssStyleVersion) invalidates all the
/// styles.
/// </summary>
[Collection(nameof(StyleInvalidationCollection))]
public sealed class IncrementalLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var document = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } }).Navigate(_ => "<!DOCTYPE html>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static Element Find(Document document, string selector) => document.QuerySelector(selector) ?? throw new InvalidOperationException(selector);

    [Fact]
    public void DerivedStyles_AreKeptWithTheirStyle()
    {
        var document = Parse("<div id=d>a<p>b</p></div><ul><li id=li style='word-spacing: 2px'>x</li></ul>");
        var first = BoxTree.Build(document);
        var second = BoxTree.Build(document);

        // The anonymous block box around the text of the div, and the bullet of the list item (not spaced by word-spacing)
        var anonymous1 = first.GetPrincipalBox(Find(document, "#d"))!.FirstChild!;
        var anonymous2 = second.GetPrincipalBox(Find(document, "#d"))!.FirstChild!;
        Assert.True(anonymous1.IsAnonymous);
        Assert.Same(anonymous1.Style, anonymous2.Style);

        var bullet1 = ((ContainerBox)first.GetPseudoElementBox(Find(document, "#li"), "marker")!).FirstChild!;
        var bullet2 = ((ContainerBox)second.GetPseudoElementBox(Find(document, "#li"), "marker")!).FirstChild!;
        Assert.NotEqual("2px", bullet1.Style.GetComputedValue("word-spacing"));
        Assert.Same(bullet1.Style, bullet2.Style);
    }

    /// <summary>
    /// Lays out the document, applies a change, lays it out again, and checks that the box tree was patched (or built again when
    /// <paramref name="patched"/> is false, either when it is null), that it is the tree a build gives, and that the layout is the
    /// layout of a new tree.
    /// </summary>
    private static void AssertIncremental(Document document, Action change, bool? patched = true, Action? checkPatchedTree = null)
    {
        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        var patchCount = engine.BoxTreePatchCount;
        change();
        engine.EnsureLayout();
        if (patched is { } isPatched)
        {
            Assert.Equal(isPatched ? patchCount + 1 : patchCount, engine.BoxTreePatchCount);
        }

        Assert.Null(BoxTreeBuilder.DescribeDifference(engine.BoxTree!, BoxTree.Build(document)));
        checkPatchedTree?.Invoke();

        var dump = engine.Dump();
        engine.SetNeedsLayout(rebuildBoxTree: true);
        Assert.Equal(engine.Dump(), dump);
    }

    [Fact]
    public void ClassChange_PatchesTheBoxesOfTheElement()
    {
        var document = Parse("<style>.on { border: 1px solid } .on p { color: green } .flex { display: flex }</style><div id=a><p>a</p></div><div id=b><p>b</p><ul><li>x<li>y</ul></div>");
        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        var boxB = engine.BoxTree!.GetPrincipalBox(Find(document, "#b"));
        var boxA = engine.BoxTree.GetPrincipalBox(Find(document, "#a"));
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("on"), checkPatchedTree: () =>
        {
            // The boxes of the other elements are kept
            Assert.Same(boxB, engine.BoxTree!.GetPrincipalBox(Find(document, "#b")));
            Assert.NotSame(boxA, engine.BoxTree.GetPrincipalBox(Find(document, "#a")));
        });

        // The display of the element changes: the boxes of its parent are built again
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("flex"));
        Assert.Equal(Display.Block with { Inside = DisplayInside.Flex }, engine.BoxTree!.GetPrincipalBox(Find(document, "#a"))!.Display);
    }

    [Fact]
    public void InsertedAndRemovedChildren_PatchTheBoxesOfTheParent()
    {
        var document = Parse("<div id=a>a<span>b</span></div><table><tbody id=body><tr><td>1</td></tr></tbody></table><p id=p>c</p>");
        AssertIncremental(document, () => Find(document, "#a").AppendChild(document.CreateElement("p")));
        AssertIncremental(document, () =>
        {
            var row = document.CreateElement("tr");
            row.AppendChild(document.CreateElement("td")).TextContent = "2";
            Find(document, "#body").AppendChild(row);
        });
        AssertIncremental(document, () => Find(document, "#body").RemoveChild(Find(document, "#body").FirstChild!));
        AssertIncremental(document, () => ((Text)Find(document, "#p").FirstChild!).Data = "changed");
    }

    [Fact]
    public void DisplayChange_PatchesTheBoxesOfTheParent()
    {
        var document = Parse("<style>.none { display: none } .inline { display: inline }</style><div><p id=p>a</p><p>b</p></div>");
        AssertIncremental(document, () => Find(document, "#p").ClassList.Add("none"));
        AssertIncremental(document, () => Find(document, "#p").ClassList.Remove("none"));
        AssertIncremental(document, () => Find(document, "#p").ClassList.Add("inline"));
    }

    [Fact]
    public void ChangeInAnInlineBox_PatchesTheBoxesOfItsBlockContainer()
    {
        // The inline box is split around its block-level descendant by its block container
        var document = Parse("<style>.block { display: block }</style><p id=p>a <span id=s>b <b id=b>c</b> d</span> e</p>");
        AssertIncremental(document, () => Find(document, "#b").ClassList.Add("block"));
        AssertIncremental(document, () => Find(document, "#b").ClassList.Remove("block"));
    }

    [Fact]
    public void ListItems_KeepTheirCountersWhenTheyDoNotChange()
    {
        var document = Parse("<style>.on { color: green }</style><ol><li id=a>a<li>b<li>c</ol>");
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("on"));
    }

    [Fact]
    public void ListItemInserted_BuildsTheTreeAgain()
    {
        // The counters of the following list items change
        var document = Parse("<ol id=list><li>a<li>b</ol><ol start=5><li>c</ol>");
        AssertIncremental(document, () => Find(document, "#list").Prepend(document.CreateElement("li")), patched: false);
        Assert.Contains("\"3. \"", BoxTree.Dump(document.GetLayoutEngine().BoxTree!.Viewport!), StringComparison.Ordinal);
    }

    [Fact]
    public void Quotes_BuildTheTreeAgain()
    {
        var document = Parse("<style>.on { color: green }</style><q>a</q><p id=p>b</p>");
        AssertIncremental(document, () => Find(document, "#p").ClassList.Add("on"), patched: false);
    }

    [Fact]
    public void ChangeInASelect_PatchesTheBoxesAroundTheSelect()
    {
        var document = Parse("<style>.on { color: green }</style><div id=d><select><option id=o>a<option>b</select></div>");
        AssertIncremental(document, () => Find(document, "#o").ClassList.Add("on"));
    }

    [Fact]
    public void ChangeInARuby_PatchesTheBoxesAroundTheRuby()
    {
        // The ruby fixup wraps the intra-level white space (IsRubyWhiteSpace), and the layout hides the annotations whose text
        // is the text of their base
        var document = Parse("<style>.on { color: green }</style><div><ruby id=r><rb id=a>a</rb> <rb>b</rb><rt id=t>a</rt> <rt>y</rt></ruby> <span>c</span></div>");
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("on"));
        AssertIncremental(document, () => Find(document, "#t").TextContent = "x");
        AssertIncremental(document, () => Find(document, "#r").Append(document.CreateTextNode(" ")));
    }

    /// <summary>Applies a change and checks that the layout used the results of the previous passes (see <see cref="AssertIncremental"/>).</summary>
    private static void AssertLayoutReused(Document document, Action change)
    {
        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        var hits = engine.LayoutCacheHitCount;
        AssertIncremental(document, change, checkPatchedTree: () => Assert.True(engine.LayoutCacheHitCount > hits, "The layout did not use the results of the previous passes"));
    }

    [Fact]
    public void Layout_ReusesTheBoxesThatDidNotChange()
    {
        // The paragraphs participate in the block formatting context of the body: their margins collapse
        var document = Parse("<style>p { margin: 10px 0 } .big { font-size: 30px; margin: 20px 0 }</style><p>a</p><p id=p>b</p><p>c c c</p><div><p>d</p></div><p>e</p>");
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("big"));
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Remove("big"));
        AssertLayoutReused(document, () => ((Text)Find(document, "#p").FirstChild!).Data = "a longer text");
    }

    [Fact]
    public void Layout_FloatsAndClearance()
    {
        var document = Parse("<style>.f { float: left; width: 50px; height: 50px } .c { clear: both } .h { height: 80px }</style><p id=a>a</p><div class=f id=f></div><p id=b>b b b b b b b b b b</p><p class=c>c</p><p id=d>d</p>");
        AssertIncremental(document, () => Find(document, "#f").ClassList.Add("h"));
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("h"));
        AssertIncremental(document, () => Find(document, "#d").ClassList.Add("h"));
    }

    [Fact]
    public void Layout_TableCellsAndAlignment()
    {
        var document = Parse("<style>td { vertical-align: middle; height: 40px } .w { width: 200px }</style><p id=p>a</p><table><tr><td id=a>a<td>b<br>c</table>");
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("w"));
        AssertLayoutReused(document, () => Find(document, "#a").ClassList.Add("w"));
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Remove("w"));
    }

    [Fact]
    public void Layout_FlexItemsWithAutoMargins()
    {
        var document = Parse("<style>.flex { display: flex; width: 300px } .flex > div { margin: auto; width: 50px } .w { width: 100px !important }</style><p id=p>a</p><div class=flex><div id=a>a</div><div>b</div></div>");
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("w"));
        AssertLayoutReused(document, () => Find(document, "#a").ClassList.Add("w"));
    }

    [Fact]
    public void Layout_PositionedBoxes()
    {
        var document = Parse("<style>.rel { position: relative; top: 5px } .abs { position: absolute; right: 0; bottom: 0 } .sticky { position: sticky; top: 0 } .w { width: 100px }</style><div class=rel id=r><span class=abs>x</span><p id=p>a</p></div><div class=sticky id=s>s</div><p id=q>b</p>");
        AssertLayoutReused(document, () => Find(document, "#q").ClassList.Add("w"));
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("w"));
        _ = document.DefaultView!.ScrollTo(0, 20);
        AssertLayoutReused(document, () => Find(document, "#q").ClassList.Remove("w"));
    }

    [Fact]
    public void Layout_ScrollContainersAndPercentages()
    {
        var document = Parse("<style>.scroll { overflow: auto; height: 50px; width: 50% } .tall { height: 200px } .half { height: 50% }</style><div class=scroll><div id=a>a</div></div><div style='height: 100px'><div class=half id=h>h</div></div><p id=p>p</p>");
        AssertIncremental(document, () => Find(document, "#a").ClassList.Add("tall"));
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("tall"));
        AssertIncremental(document, () => Find(document, "#a").ClassList.Remove("tall"));
    }

    [Fact]
    public void Layout_BlockStepSizes()
    {
        // The space of the block step sizing inserted in the margins is aligned by the align-items of the parent (block-step-align: auto)
        var document = Parse("<style>.step { block-step-size: 30px } .end { align-items: end } .big { font-size: 30px }</style><div id=d><p id=p class=step>a</p><p id=q>b</p></div><p id=r>c</p>");
        AssertIncremental(document, () => Find(document, "#q").ClassList.Add("step"));
        AssertIncremental(document, () => Find(document, "#d").ClassList.Add("end"));
        AssertIncremental(document, () => Find(document, "#p").ClassList.Add("big"));
        AssertLayoutReused(document, () => Find(document, "#r").ClassList.Add("big"));
        AssertIncremental(document, () => Find(document, "#q").ClassList.Remove("step"));
    }

    [Fact]
    public void Layout_Framesets()
    {
        // Only the frame and frameset children of a frameset are rendered, in the grid of its rows and cols attributes
        var document = Parse("<frameset id=fs rows='100,*'><frame id=a><frameset id=inner cols='50%,*'><frame><frame id=b></frameset></frameset>");
        AssertIncremental(document, () => Find(document, "#fs").SetAttribute("rows", "200,*"), patched: null);
        AssertIncremental(document, () => Find(document, "#inner").SetAttribute("cols", "*,*,*"), patched: null);
        AssertIncremental(document, () => Find(document, "#inner").AppendChild(document.CreateElement("frame")), patched: null);
        AssertIncremental(document, () => Find(document, "#inner").AppendChild(document.CreateElement("div")).TextContent = "not rendered", patched: null);
        AssertIncremental(document, () => Find(document, "#b").SetAttribute("style", "display: inline"), patched: null);
        AssertIncremental(document, () => Find(document, "#fs").RemoveChild(Find(document, "#a")), patched: null);
    }

    [Fact]
    public void Layout_FormControlsSizedByTheirContents()
    {
        // field-sizing: content measures the placeholder (in the font of its ::placeholder box); a background devolves the control
        var document = Parse("<style>.content { field-sizing: content } .big::placeholder { font-size: 30px } .bg { background: yellow }</style><input id=i class=content placeholder=abc><textarea id=t class=content placeholder='a b c'></textarea><select id=s><option>option</select><p id=p>p</p>");
        AssertIncremental(document, () => Find(document, "#i").SetAttribute("placeholder", "a longer placeholder"), patched: null);
        AssertIncremental(document, () => Find(document, "#i").ClassList.Add("big"), patched: null);
        AssertIncremental(document, () => Find(document, "#t").SetAttribute("cols", "5"), patched: null);
        AssertIncremental(document, () => Find(document, "#s").ClassList.Add("bg"), patched: null);
        AssertIncremental(document, () => Find(document, "#t").ClassList.Add("bg"), patched: null);
        AssertLayoutReused(document, () => Find(document, "#p").ClassList.Add("bg"));
    }

#pragma warning disable CA5394 // Random is not used for security
    [Fact]
    public void Layout_RandomChanges_AreTheLayoutOfTheWholeDocument()
    {
        const string Style = """
            .c0 { display: none } .c1 { display: inline-block; width: 40px } .c2 { float: left; width: 30px; height: 20px }
            .c3 { margin: 15px 5px } .c4 { position: absolute; top: 3px } .c5 { display: flex } .c6 { font-size: 24px }
            .c7 { padding: 4px; border: 2px solid } .c8 { width: 50% } .c9 { clear: both; display: table }
            """;
        var html = new System.Text.StringBuilder("<style>" + Style + "</style>");
        for (var i = 0; i < 12; i++)
        {
            html.Append("<div id=d").Append(i).Append("><p>text ").Append(i).Append(" <span>inline</span></p><ul><li>a<li>b</ul></div>");
        }

        var document = Parse(html.ToString());
        var random = new Random(544);
        var previous = LayoutEngine.VerifyIncrementalLayout;
        LayoutEngine.VerifyIncrementalLayout = true;
        try
        {
            var engine = document.GetLayoutEngine();
            for (var i = 0; i < 60; i++)
            {
                engine.EnsureLayout();
                var target = Find(document, "#d" + random.Next(12).ToString(System.Globalization.CultureInfo.InvariantCulture));
                var element = random.Next(3) switch
                {
                    0 => target,
                    1 => target.FirstElementChild!,
                    _ => target.LastElementChild!.FirstElementChild!,
                };
                switch (random.Next(4))
                {
                    case 0:
                        element.ClassList.Toggle("c" + random.Next(10).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case 1:
                        element.AppendChild(document.CreateTextNode(" more"));
                        break;
                    case 2:
                        var span = document.CreateElement("span");
                        span.ClassName = "c" + random.Next(10).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        span.TextContent = "new";
                        element.Prepend(span);
                        break;
                    default:
                        if (element.LastChild is { } last)
                        {
                            element.RemoveChild(last);
                        }

                        break;
                }
            }

            engine.EnsureLayout();
            Assert.True(engine.LayoutCacheHitCount > 0);
        }
        finally
        {
            LayoutEngine.VerifyIncrementalLayout = previous;
        }
    }
#pragma warning restore CA5394
}
