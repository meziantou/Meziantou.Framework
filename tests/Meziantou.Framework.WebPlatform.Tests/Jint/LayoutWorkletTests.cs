using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>
/// The CSS Layout API (https://drafts.css-houdini.org/css-layout-api-1/): the layout worklet, <c>registerLayout()</c> and the
/// layout of the <c>display: layout()</c> boxes by the registered classes.
/// </summary>
public sealed class LayoutWorkletTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly List<(GlobalScope Global, string Text)> _messages = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    /// <summary>Loads a document whose layout worklet runs a module, added before the load event.</summary>
    private Document Load(string module, string html, WebPlatformFeatures? features = null)
    {
        _loader.Resources["https://example.com/layout.js"] = new DocumentResource(module) { Headers = [new("Content-Type", "text/javascript")] };
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            ConsoleMessage = (global, message) => _messages.Add((global, message.Text)),
        });

        var document = context.Navigate("https://example.com/page.html", _ => "<!DOCTYPE html><body style='margin: 0'>" + html + "<script>if (CSS.layoutWorklet) CSS.layoutWorklet.addModule('layout.js').then(() => document.title = 'added', e => document.title = 'error ' + e)</script>");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private IEnumerable<string> WorkletMessages => _messages.Where(message => message.Global is LayoutWorkletGlobalScope).Select(message => message.Text);

    private static string Rect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return FormattableString.Invariant($"{rect.X},{rect.Y} {rect.Width}x{rect.Height}");
    }

    [Fact]
    public void Layout_PositionsTheFragmentsOfTheChildren()
    {
        // The children are laid out with the constraints of the layout, in a row, in the reverse order
        var document = Load("""
            registerLayout('row', class {
              static inputProperties = ['--gap'];
              static childInputProperties = ['--grow'];
              async intrinsicSizes() {}
              async layout(children, edges, constraints, styleMap) {
                const gap = parseInt(styleMap.get('--gap').toString());
                const fragments = await Promise.all(children.map(child => child.layoutNextFragment({ fixedInlineSize: 20 * parseInt(child.styleMap.get('--grow').toString()) })));
                let offset = constraints.fixedInlineSize - edges.inlineEnd;
                let blockSize = 0;
                for (const fragment of fragments) {
                  offset -= fragment.inlineSize;
                  fragment.inlineOffset = offset;
                  fragment.blockOffset = edges.blockStart;
                  offset -= gap;
                  blockSize = Math.max(blockSize, fragment.blockSize);
                }
                return { autoBlockSize: blockSize + edges.block, childFragments: fragments };
              }
            });
            """, """
            <div id=container style="display: layout(row); width: 200px; padding: 5px; border: 1px solid; --gap: 10; --grow: 1">
              <div id=a style="height: 30px; --grow: 1"></div>
              text
              <div id=b style="height: 50px; --grow: 2"></div>
              <div id=abs style="position: absolute; width: 5px; height: 5px"></div>
            </div>
            """);

        Assert.Equal("added", document.Title);
        Assert.Empty(_errors);
        Assert.Equal("0,0 212x62", Rect(document, "container"));
        Assert.Equal("186,6 20x30", Rect(document, "a"));
        Assert.Equal("106,6 40x50", Rect(document, "b"));

        // The static position of an absolutely positioned child is the padding edge of the box
        Assert.Equal("1,1 5x5", Rect(document, "abs"));
    }

    [Fact]
    public void Layout_FallsBackToTheFlowLayout()
    {
        // Before the registration, and when the callback fails, the box is laid out as a block container
        var document = Load("""
            registerLayout('throws', class { async intrinsicSizes() {} async layout() { throw new Error('layout failed'); } });
            registerLayout('pending', class { async intrinsicSizes() {} layout() { return new Promise(() => {}); } });
            registerLayout('invalid', class { async intrinsicSizes() {} async layout() { return 42; } });
            registerLayout('foreign', class { async intrinsicSizes() {} async layout(children) { this.child ??= children[0]; return { childFragments: [await this.child.layoutNextFragment({})] }; } });
            """, """
            <style>.box { width: 100px } .box > div { height: 40px }</style>
            <div id=unregistered class=box style="display: layout(unknown)"><div></div></div>
            <div id=throws class=box style="display: layout(throws)"><div></div></div>
            <div id=pending class=box style="display: layout(pending)"><div></div></div>
            <div id=invalid class=box style="display: layout(invalid)"><div></div></div>
            """);

        Assert.Equal("added", document.Title);
        foreach (var id in (ReadOnlySpan<string>)["unregistered", "throws", "pending", "invalid"])
        {
            Assert.Equal(40, document.GetElementById(id)!.GetBoundingClientRect().Height);
        }

        Assert.Contains(_errors, error => error.Message.Contains("layout failed", StringComparison.Ordinal));
        Assert.Equal("layout(throws)", document.DefaultView!.GetComputedStyle(document.GetElementById("throws")!).Display);
    }

    [Fact]
    public void IntrinsicSizes_AreTheSizesOfTheCallback()
    {
        var document = Load("""
            registerLayout('sized', class {
              async intrinsicSizes(children, edges) {
                const sizes = await Promise.all(children.map(child => child.intrinsicSizes()));
                return { minContentSize: sizes[0].minContentSize + edges.inline, maxContentSize: sizes[0].maxContentSize + 100 + edges.inline };
              }
              async layout() { return { autoBlockSize: 10 }; }
            });
            """, """
            <div id=min style="display: layout(sized); width: min-content; padding: 0 3px"><div style="width: 30px"></div></div>
            <div id=max style="display: layout(sized); width: max-content; padding: 0 3px"><div style="width: 30px"></div></div>
            <div id=inline style="display: inline layout(sized)"><div style="width: 30px"></div></div>
            """);

        Assert.Equal("added", document.Title);
        Assert.Empty(_errors);
        Assert.Equal(36, document.GetElementById("min")!.GetBoundingClientRect().Width);
        Assert.Equal(136, document.GetElementById("max")!.GetBoundingClientRect().Width);

        // An inline-level layout API container is sized as an atomic inline: its fit-content size
        Assert.Equal(130, document.GetElementById("inline")!.GetBoundingClientRect().Width);
    }

    [Fact]
    public void ManualSizing_UsesTheSizesOfTheResult()
    {
        var document = Load("""
            registerLayout('manual', class {
              static layoutOptions = { sizing: 'manual' };
              async intrinsicSizes() {}
              async layout(children, edges, constraints) {
                console.log([constraints.fixedInlineSize, constraints.fixedBlockSize].join());
                return { inlineSize: constraints.fixedInlineSize ?? 70, blockSize: 30 };
              }
            });
            """, """
            <div id=auto style="display: layout(manual); height: 400px"></div>
            <div id=sized style="display: layout(manual); width: 150px; height: 400px"></div>
            <div><div id=float style="display: layout(manual); float: left; height: 400px"></div></div>
            """);

        Assert.Equal("added", document.Title);
        Assert.Empty(_errors);

        // The sizes of the style are not used: only the stretch-fit inline size that the block formatting context forces on a box
        // whose inline size is auto is fixed
        Assert.Equal("0,0 800x30", Rect(document, "auto"));
        Assert.Equal("0,30 70x30", Rect(document, "sized"));
        Assert.Equal("0,60 70x30", Rect(document, "float"));
        Assert.Contains("800,", WorkletMessages);
        Assert.Contains(",", WorkletMessages);
    }

    [Fact]
    public void ChildDisplay_BlockifiesTheChildren()
    {
        var document = Load("""
            const layout = class { async intrinsicSizes() {} async layout(children) { console.log(this.constructor.label + ' ' + children.length); return {}; } };
            registerLayout('block', class extends layout { static label = 'block'; });
            registerLayout('normal', class extends layout { static label = 'normal'; static layoutOptions = { childDisplay: 'normal' }; });
            """, """
            <div id=block style="display: layout(block)"><span id=a>a</span> text <span>b</span><div></div></div>
            <div id=normal style="display: layout(normal)"><span id=b>a</span> text <span>b</span><div></div></div>
            """);

        Assert.Equal("added", document.Title);
        _ = document.GetElementById("block")!.GetBoundingClientRect();
        Assert.Equal("block", document.DefaultView!.GetComputedStyle(document.GetElementById("a")!).Display);
        Assert.Equal("inline", document.DefaultView!.GetComputedStyle(document.GetElementById("b")!).Display);

        // Each blockified child and each run of text is a child; the inline-level children are in one root inline box
        Assert.Contains("block 4", WorkletMessages);
        Assert.Contains("normal 2", WorkletMessages);
    }

    [Fact]
    public void Data_IsClonedBetweenTheLayouts()
    {
        var document = Load("""
            registerLayout('parent', class {
              async intrinsicSizes() {}
              async layout([child]) {
                const fragment = await child.layoutNextFragment({ data: { size: 25 } });
                return { autoBlockSize: fragment.data.doubled, childFragments: [fragment] };
              }
            });
            registerLayout('child', class {
              async intrinsicSizes() {}
              async layout(children, edges, constraints) { return { autoBlockSize: constraints.data.size, data: { doubled: constraints.data.size * 2 } }; }
            });
            """, """
            <div id=parent style="display: layout(parent); width: 100px"><div id=child style="display: layout(child)"></div></div>
            """);

        Assert.Equal("added", document.Title);
        Assert.Empty(_errors);
        Assert.Equal(50, document.GetElementById("parent")!.GetBoundingClientRect().Height);
        Assert.Equal(25, document.GetElementById("child")!.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Layout_RunsAgainWhenAnInputPropertyChanges()
    {
        var document = Load("""
            registerLayout('sized', class {
              static inputProperties = ['--size'];
              async intrinsicSizes() {}
              async layout(children, edges, constraints, styleMap) { return { autoBlockSize: parseInt(styleMap.get('--size').toString()) }; }
            });
            """, """
            <div id=box style="display: layout(sized); --size: 10"></div>
            """);

        var box = (HtmlElement)document.GetElementById("box")!;
        Assert.Equal(10, box.GetBoundingClientRect().Height);
        box.Style.SetProperty("--size", "35");
        Assert.Equal(35, box.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Layout_DoesNotRunTheMicrotasksOfTheDocument()
    {
        // The layout runs the microtasks of the worklet until the promise of the callback is settled, not the ones of the window
        var document = Load("""
            registerLayout('async', class {
              async intrinsicSizes() {}
              async layout([child]) { await null; const fragment = await child.layoutNextFragment(); await null; return { autoBlockSize: 20, childFragments: [fragment] }; }
            });
            """, """
            <div id=box style="display: layout(async)"><div></div></div>
            """);

        Assert.Equal("added", document.Title);
        _engine.Evaluate(document.DefaultView!, "let resolved = false; Promise.resolve().then(() => resolved = true); document.body.dataset.result = document.getElementById('box').offsetHeight + ' ' + resolved;");
        Assert.Equal("20 false", document.Body!.GetAttribute("data-result"));
        Assert.Empty(_errors);
    }

    [Fact]
    public void RegisterLayout_ValidatesTheClass()
    {
        var document = Load("""
            const log = [];
            const check = (name, ctor) => { try { registerLayout(name, ctor); log.push('ok'); } catch (e) { log.push(e.name); } };
            const valid = class { intrinsicSizes() {} layout() {} };
            check('', valid);
            check('a', valid);
            check('a', valid);
            check('b', class { layout() {} });
            check('c', class { intrinsicSizes() {} });
            check('d', class extends valid { static layoutOptions = { sizing: 'unknown' }; });
            check('e', class extends valid { static layoutOptions = 42; });
            check('f', class extends valid { static inputProperties = 42; });
            check('g', class extends valid { static inputProperties = ['--a', 'WIDTH', 'unknown-property']; });
            console.log(log.join());
            """, "");

        Assert.Equal("added", document.Title);
        Assert.Equal(2, WorkletMessages.Count());
        Assert.All(WorkletMessages, message => Assert.Equal("TypeError,ok,InvalidModificationError,TypeError,TypeError,TypeError,TypeError,TypeError,ok", message));
    }

    [Fact]
    public void GlobalScope_ExposesTheLayoutInterfaces()
    {
        var document = Load("""
            console.log([typeof registerLayout, typeof LayoutChild, typeof LayoutFragment, typeof LayoutConstraints, typeof LayoutEdges, typeof IntrinsicSizes, typeof FragmentResult,
              typeof CSSUnitValue, typeof window, typeof registerPaint, Object.prototype.toString.call(globalThis)].join());
            try { new FragmentResult(); } catch (e) { console.log(e.name); }
            """, "<script>document.body.dataset.result = [typeof LayoutChild, typeof FragmentResult, CSS.layoutWorklet instanceof Worklet, CSS.supports('display', 'layout(foo)'), CSS.supports('display', 'layout()')].join()</script>");

        Assert.Equal("added", document.Title);
        Assert.Equal("undefined,undefined,true,true,false", document.Body!.GetAttribute("data-result"));
        Assert.Equal(4, WorkletMessages.Count());
        Assert.Equal(2, WorkletMessages.Count(message => message == "function,function,function,function,function,function,function,function,undefined,undefined,[object LayoutWorkletGlobalScope]"));

        // A FragmentResult is the result of the layout that is running
        Assert.Equal(2, WorkletMessages.Count(message => message == "InvalidStateError"));
        Assert.Same(document.LayoutWorklet, document.LayoutWorklet);
    }

    [Fact]
    public void DisabledFeature_HidesTheLayoutWorklet()
    {
        var document = Load("registerLayout('a', class { intrinsicSizes() {} layout() { return { autoBlockSize: 50 }; } });", """
            <div id=box style="display: layout(a)"><div style="height: 10px"></div></div>
            <script>document.body.dataset.result = [typeof Worklet, typeof CSS.layoutWorklet, 'layoutWorklet' in CSS].join()</script>
            """, new WebPlatformFeatures { LayoutWorkletEnabled = false });

        Assert.Equal("function,undefined,false", document.Body!.GetAttribute("data-result"));

        // The box is a block container, even when a layout is registered with the .NET API
        _ = document.LayoutWorklet.AddModule("layout.js");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(10, document.GetElementById("box")!.GetBoundingClientRect().Height);
    }
}
