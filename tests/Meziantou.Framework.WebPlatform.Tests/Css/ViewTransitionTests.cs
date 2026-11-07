using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>CSS View Transitions (https://drafts.csswg.org/css-view-transitions-1/, https://drafts.csswg.org/css-view-transitions-2/).</summary>
public sealed class ViewTransitionTests : IDisposable
{
    private const string Page = """
        <!DOCTYPE html>
        <style>
          body { margin: 0 }
          #target { width: 100px; height: 50px; background: red; view-transition-name: target }
          #target.moved { margin-left: 200px; width: 200px; background: green }
        </style>
        <div id=target></div>
        """;

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, WebPlatform.Scripting.ScriptEngine? scriptEngine = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, ScriptEngine = scriptEngine, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering updates of the given number of frames.</summary>
    private void AdvanceFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1000d / 60));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static string GetComputed(Document document, string pseudoElement, string property) =>
        document.DefaultView!.GetComputedStyle(document.DocumentElement!, pseudoElement).GetPropertyValue(property);

    private static string? GetErrorName(Task task) => (task.Exception?.InnerException as DomException)?.Name;

    [Fact]
    public void Lifecycle_CapturesTheOldStateThenCallsTheCallbackAndAnimates()
    {
        var document = Load(Page);
        var target = document.GetElementById("target")!;
        var called = 0;
        var transition = document.StartViewTransition(() =>
        {
            called++;
            target.ClassList.Add("moved");
            return null;
        });

        // The old state is captured by the next rendering update: the callback is not called synchronously
        Assert.Same(transition, document.ActiveViewTransition);
        Assert.Same(document.DocumentElement, transition.TransitionRoot);
        Assert.Equal(0, called);
        Assert.False(transition.Ready.IsCompleted);

        AdvanceFrames();
        Assert.Equal(1, called);
        Assert.True(transition.UpdateCallbackDone.IsCompletedSuccessfully);
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        Assert.False(transition.Finished.IsCompleted);

        // The group has the geometry of the new element, and animates from the geometry of the old one
        Assert.Equal("absolute", GetComputed(document, "::view-transition", "position"));
        Assert.Equal("-ua-view-transition-group-anim-target", GetComputed(document, "::view-transition-group(target)", "animation-name"));
        Assert.Equal("-ua-view-transition-fade-out, -ua-mix-blend-mode-plus-lighter", GetComputed(document, "::view-transition-old(target)", "animation-name"));
        Assert.Equal("-ua-view-transition-fade-in, -ua-mix-blend-mode-plus-lighter", GetComputed(document, "::view-transition-new(target)", "animation-name"));
        Assert.Equal("isolate", GetComputed(document, "::view-transition-image-pair(target)", "isolation"));
        Assert.Equal("100px", GetComputed(document, "::view-transition-group(target)", "width"));
        Assert.Equal("matrix(1, 0, 0, 1, 0, 0)", GetComputed(document, "::view-transition-group(target)", "transform"));

        // The pseudo-elements of the root and of the target: a group animation and two image animations each
        var animations = document.GetAnimations();
        Assert.Equal(10, animations.Count);
        Assert.Contains(animations, animation => animation.Effect is WebPlatform.Animations.KeyframeEffect { PseudoElement: "::view-transition-group(target)" });

        // In the middle of the animations (250ms), the group is between the geometry of the old element and the one of the new element
        AdvanceFrames(8);
        Assert.False(transition.Finished.IsCompleted);
        var width = double.Parse(GetComputed(document, "::view-transition-group(target)", "width")[..^2], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(width, 120, 190);

        // The pseudo-elements are removed once the animations are finished
        AdvanceFrames(12);
        Assert.True(transition.Finished.IsCompletedSuccessfully);
        Assert.Null(document.ActiveViewTransition);
        Assert.Empty(document.GetAnimations());
        Assert.Equal("none", GetComputed(document, "::view-transition-group(target)", "animation-name"));
    }

    [Fact]
    public void BoxesOfThePseudoElements_AreLaidOutInTheSnapshotContainingBlock()
    {
        var document = Load(Page + "<style>::view-transition-group(*) { animation-play-state: paused }</style><div style='height: 2000px'></div>");
        _ = document.DefaultView!.ScrollTo(0, 100);
        var transition = document.StartViewTransition(() =>
        {
            document.GetElementById("target")!.ClassList.Add("moved");
            return null;
        });
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        var engine = document.GetLayoutEngine();
        var root = Assert.Single(engine.GetFragments(document.DocumentElement!, "view-transition"));
        Assert.True(root.Box.IsViewTransitionRoot);
        Assert.Equal((800d, 600d), (root.Size.Width, root.Size.Height));

        // The layer does not scroll with the viewport: its position in the document is the scroll position
        Assert.Equal(100, root.GetOffsetInRoot().Y);

        // The images are replaced boxes whose natural size is the size of the border box of the element
        var old = Assert.Single(engine.GetFragments(document.DocumentElement!, "view-transition-old(target)"));
        Assert.Equal((100d, 50d), (old.Size.Width, old.Size.Height));
        var rootImage = Assert.Single(engine.GetFragments(document.DocumentElement!, "view-transition-new(root)"));
        Assert.Equal((800d, 600d), (rootImage.Size.Width, rootImage.Size.Height));
    }

    [Fact]
    public void Painting_TheCapturedElementsArePaintedByThePseudoElements()
    {
        var document = Load(Page + """
            <style>
              ::view-transition-group(*) { animation-duration: 100s }
              ::view-transition-old(*) { animation: none; opacity: 1 }
              ::view-transition-new(*) { animation: none; opacity: 0 }
            </style>
            """);
        var target = document.GetElementById("target")!;
        var transition = document.StartViewTransition(() =>
        {
            target.ClassList.Add("moved");
            return null;
        });
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        // Without rendering backend, the snapshot of the old state is its painting: the red box at the origin
        var list = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rect (0, 0, 100x50) fill #ff0000", list, StringComparison.Ordinal);

        // The captured element is not hit: the pseudo-elements of its originating element are
        Assert.Same(document.DocumentElement, document.ElementFromPoint(250, 10));
    }

    [Fact]
    public void SkipTransition_CallsTheCallbackAndRejectsReady()
    {
        var document = Load(Page);
        var called = 0;
        var transition = document.StartViewTransition(() =>
        {
            called++;
            return null;
        });
        transition.SkipTransition();
        Assert.Null(document.ActiveViewTransition);
        Assert.Equal("AbortError", GetErrorName(transition.Ready));
        Assert.Equal(0, called);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, called);
        Assert.True(transition.UpdateCallbackDone.IsCompletedSuccessfully);
        Assert.True(transition.Finished.IsCompletedSuccessfully);
    }

    [Fact]
    public void StartViewTransition_SkipsTheActiveTransition()
    {
        var document = Load(Page);
        var first = document.StartViewTransition();
        var second = document.StartViewTransition();
        Assert.Equal("AbortError", GetErrorName(first.Ready));
        Assert.Same(second, document.ActiveViewTransition);

        AdvanceFrames();
        Assert.True(first.Finished.IsCompletedSuccessfully);
        Assert.True(second.Ready.IsCompletedSuccessfully);
    }

    [Fact]
    public void DuplicateNames_SkipTheTransition()
    {
        var document = Load(Page + "<div style='view-transition-name: target'>x</div>");
        var transition = document.StartViewTransition();
        AdvanceFrames();
        Assert.Equal("InvalidStateError", GetErrorName(transition.Ready));
        Assert.True(transition.Finished.IsCompletedSuccessfully);
    }

    [Fact]
    public void FailedCallback_RejectsThePromises()
    {
        var document = Load(Page);
        var transition = document.StartViewTransition(() => Task.FromException(new InvalidOperationException("failed")));
        AdvanceFrames();
        Assert.IsType<InvalidOperationException>(transition.UpdateCallbackDone.Exception?.InnerException);
        Assert.IsType<InvalidOperationException>(transition.Ready.Exception?.InnerException);
        Assert.IsType<InvalidOperationException>(transition.Finished.Exception?.InnerException);
        Assert.Null(document.ActiveViewTransition);
    }

    [Fact]
    public void PendingCallback_SuppressesTheRenderingUntilItSettles()
    {
        var document = Load(Page);
        var completion = new TaskCompletionSource();
        var transition = document.StartViewTransition(() => completion.Task);
        AdvanceFrames();
        Assert.True(document.ViewTransitionRenderingSuppressed);

        // The animation frame callbacks wait, and the hit testing targets the document element
        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        AdvanceFrames(3);
        Assert.Equal(0, frames);
        Assert.Same(document.DocumentElement, document.ElementFromPoint(10, 10));

        completion.SetResult();
        AdvanceFrames(2);
        Assert.False(document.ViewTransitionRenderingSuppressed);
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        Assert.Equal(1, frames);
    }

    [Fact]
    public void CallbackTimeout_SkipsTheTransition()
    {
        var document = Load(Page);
        var transition = document.StartViewTransition(() => new TaskCompletionSource().Task);
        AdvanceFrames();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("TimeoutError", GetErrorName(transition.Ready));
        Assert.False(transition.Finished.IsCompleted);
    }

    [Fact]
    public void Selectors_NamesClassesAndOnlyChild()
    {
        var document = Load(Page + """
            <style>
              #target { view-transition-class: card big }
              ::view-transition-group(*) { animation-play-state: paused; outline-style: solid; outline-width: 1px }
              ::view-transition-group(target) { outline-width: 2px }
              ::view-transition-group(*.card) { z-index: 1 }
              ::view-transition-group(.card.big) { order: 2 }
              ::view-transition-group(target.small) { order: 3 }
              ::view-transition-old(*):only-child { color: rgb(1, 2, 3) }
              ::view-transition-new(*):only-child { color: rgb(4, 5, 6) }
              :root:active-view-transition { --active: yes }
              :root:active-view-transition-type(slide, fade) { --type: yes }
            </style>
            <div id=old style="view-transition-name: old">old</div>
            """);
        var transition = document.StartViewTransition(new StartViewTransitionOptions
        {
            Types = ["fade"],
            Update = () =>
            {
                document.GetElementById("old")!.Remove();
                var added = document.CreateElement("p");
                added.SetAttribute("style", "view-transition-name: new");
                document.Body!.AppendChild(added);
                return null;
            },
        });
        Assert.Equal("yes", document.DefaultView!.GetComputedStyle(document.DocumentElement!).GetPropertyValue("--active"));
        Assert.Equal("yes", document.DefaultView.GetComputedStyle(document.DocumentElement!).GetPropertyValue("--type"));
        transition.Types.Delete("fade");
        Assert.Equal("", document.DefaultView.GetComputedStyle(document.DocumentElement!).GetPropertyValue("--type"));

        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        // A name is more specific than *, and the classes of view-transition-class are matched
        Assert.Equal("2px", GetComputed(document, "::view-transition-group(target)", "outline-width"));
        Assert.Equal("1px", GetComputed(document, "::view-transition-group(root)", "outline-width"));
        Assert.Equal("1", GetComputed(document, "::view-transition-group(target)", "z-index"));
        Assert.Equal("2", GetComputed(document, "::view-transition-group(target)", "order"));
        Assert.Equal("auto", GetComputed(document, "::view-transition-group(root)", "z-index"));

        // :only-child matches the image of a pair without the other image
        Assert.Equal("rgb(1, 2, 3)", GetComputed(document, "::view-transition-old(old)", "color"));
        Assert.Equal("rgb(4, 5, 6)", GetComputed(document, "::view-transition-new(new)", "color"));
        Assert.NotEqual("rgb(1, 2, 3)", GetComputed(document, "::view-transition-old(target)", "color"));
        Assert.Equal("-ua-view-transition-fade-out", GetComputed(document, "::view-transition-old(old)", "animation-name"));
        Assert.Equal("-ua-view-transition-fade-in", GetComputed(document, "::view-transition-new(new)", "animation-name"));
    }

    [Fact]
    public void GeneratedNames_AutoAndMatchElement()
    {
        var document = Load("""
            <!DOCTYPE html>
            <style>
              :root { view-transition-name: none }
              div { view-transition-name: match-element; width: 10px; height: 10px }
              ::view-transition-group(*) { animation-play-state: paused }
            </style>
            <div></div><div id=second style="view-transition-name: auto"></div>
            """);
        var transition = document.StartViewTransition();
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        var pseudoElements = document.GetAnimations().Select(animation => ((WebPlatform.Animations.KeyframeEffect)animation.Effect!).PseudoElement!).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(6, pseudoElements.Count);
        Assert.All(pseudoElements, pseudoElement => Assert.Contains("(-ua-", pseudoElement, StringComparison.Ordinal));
        Assert.Contains("::view-transition-group(-ua-id-second)", pseudoElements);

        // The pseudo-elements are in the order of the view transition tree
        Assert.StartsWith("::view-transition-group(", pseudoElements[0], StringComparison.Ordinal);
        Assert.StartsWith("::view-transition-old(", pseudoElements[1], StringComparison.Ordinal);
        Assert.StartsWith("::view-transition-new(", pseudoElements[2], StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenDocument_SkipsTheTransition()
    {
        var document = Load(Page);
        document.UpdateVisibilityState(visible: false);
        var transition = document.StartViewTransition();
        Assert.Equal("InvalidStateError", GetErrorName(transition.Ready));
        Assert.Null(document.ActiveViewTransition);
    }

    [Fact]
    public void ViewTransitionRule_IsExposedByTheObjectModel()
    {
        var document = Load("<style>@view-transition { navigation: auto; types: slide \\31 23; unknown: 1 } @view-transition { navigation: invalid }</style>");
        var rules = document.StyleSheets[0].CssRules;
        var rule = Assert.IsType<WebPlatform.Css.Om.CssViewTransitionRule>(rules[0]);
        Assert.Equal("auto", rule.Navigation);
        Assert.Equal(["slide", "123"], rule.Types);
        Assert.Equal("@view-transition { navigation: auto; types: slide \\31 23; }", rule.CssText);
        Assert.Equal("@view-transition { }", rules[1].CssText);
    }

    [Fact]
    public void CrossDocumentTransition_PageSwapAndPageReveal()
    {
        using var engine = TestScriptEngines.CreateDisposable();
        var pages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["https://example.com/old"] = """
                <!DOCTYPE html>
                <style>@view-transition { navigation: auto; types: forward }</style>
                <div style="view-transition-name: hero; width: 100px; height: 100px; background: red"></div>
                <script>
                  onpageswap = e => { top.name = [e.activation.navigationType, new URL(e.activation.entry.url).pathname, e.viewTransition !== null, [...e.viewTransition.types]].join(); };
                </script>
                """,
            ["https://example.com/new"] = """
                <!DOCTYPE html>
                <style>@view-transition { navigation: auto } ::view-transition-group(*) { animation-play-state: paused }</style>
                <div style="view-transition-name: hero; width: 200px; height: 200px; background: green"></div>
                <script>
                  onpagereveal = e => {
                    document.title = [e.viewTransition !== null, document.activeViewTransition === e.viewTransition].join();
                    e.viewTransition.ready.then(() => { document.title += ',' + getComputedStyle(document.documentElement, '::view-transition-old(hero)').position; });
                  };
                </script>
                """,
        };
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, ScriptEngine = engine, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        context.Navigate("https://example.com/old", url => pages[url.Href]);
        _contexts.EventLoop.RunUntilIdle();
        AdvanceFrames();

        context.Navigate("https://example.com/new", url => pages[url.Href]);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("push,/new,true,forward", context.Window.Name);

        // The new document is revealed by its first rendering update: the inbound transition shows the image of the old document
        AdvanceFrames(2);
        var document = context.Document;
        Assert.Equal("true,true,absolute", document.Title);
        Assert.NotNull(document.ActiveViewTransition);
        var old = Assert.Single(document.GetLayoutEngine().GetFragments(document.DocumentElement!, "view-transition-old(hero)"));
        Assert.Equal((100d, 100d), (old.Size.Width, old.Size.Height));
    }

    [Fact]
    public void Script_StartViewTransitionAndPromises()
    {
        using var engine = TestScriptEngines.CreateDisposable();
        var document = Load(Page + """
            <script>
              const log = [];
              const transition = document.startViewTransition({ update: () => { log.push('update'); target.className = 'moved'; }, types: ['a', 'b'] });
              log.push(transition instanceof ViewTransition, [...transition.types].join('+'), document.activeViewTransition === transition);
              transition.updateCallbackDone.then(() => log.push('done'));
              transition.ready.then(() => log.push('ready:' + document.getAnimations().length));
              transition.finished.then(() => { log.push('finished'); document.title = log.join(); });
            </script>
            """, engine);
        AdvanceFrames(20);
        Assert.Equal("true,a+b,true,update,done,ready:10,finished", document.Title);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scoped view transitions (https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions)
    // ----------------------------------------------------------------------------------------------------------------

    private const string ScopedPage = """
        <!DOCTYPE html>
        <style>
          body { margin: 0 }
          #scope { margin: 20px; width: 200px; height: 100px; border: 5px solid black; padding: 10px; overflow: hidden }
          #part { width: 50px; height: 50px; background: red; view-transition-name: part }
          #part.moved { margin-left: 100px; background: green }
          #outside { width: 50px; height: 50px; view-transition-name: outside }
          ::view-transition-group(*) { animation-play-state: paused }
        </style>
        <div id=scope><div id=part></div></div>
        <div id=outside></div>
        """;

    private static string GetComputed(Element element, string pseudoElement, string property) =>
        element.NodeDocument.DefaultView!.GetComputedStyle(element, pseudoElement).GetPropertyValue(property);

    [Fact]
    public void Scoped_TheTransitionIsHostedByTheElement()
    {
        var document = Load(ScopedPage);
        var scope = document.GetElementById("scope")!;
        var part = document.GetElementById("part")!;
        var view = document.DefaultView!;
        Assert.Null(scope.ActiveViewTransition);
        Assert.Equal("none", view.GetComputedStyle(scope).GetPropertyValue("view-transition-scope"));

        var transition = scope.StartViewTransition(() =>
        {
            part.ClassList.Add("moved");
            return null;
        });
        Assert.Same(scope, transition.TransitionRoot);
        Assert.Same(transition, scope.ActiveViewTransition);
        Assert.Null(document.ActiveViewTransition);

        // The root element has view transition scoping, the default name "root" and layout containment (the computed value of
        // contain does not change) for the duration of the transition, and matches :active-view-transition
        Assert.Equal("all", view.GetComputedStyle(scope).GetPropertyValue("view-transition-scope"));
        Assert.Equal("root", view.GetComputedStyle(scope).GetPropertyValue("view-transition-name"));
        Assert.Equal("none", view.GetComputedStyle(scope).GetPropertyValue("contain"));
        Assert.True(scope.Matches(":active-view-transition"));
        Assert.False(document.DocumentElement!.Matches(":active-view-transition"));

        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        // Only the root element and its descendants are captured: the pseudo-elements originate from the root element
        Assert.Equal("absolute", GetComputed(scope, "::view-transition", "position"));
        Assert.Equal("-ua-view-transition-group-anim-part", GetComputed(scope, "::view-transition-group(part)", "animation-name"));
        Assert.Equal("-ua-view-transition-group-anim-root", GetComputed(scope, "::view-transition-group(root)", "animation-name"));
        Assert.Equal("none", GetComputed(scope, "::view-transition-group(outside)", "animation-name"));
        Assert.Equal("none", GetComputed(document, "::view-transition-group(part)", "animation-name"));

        // The geometry is relative to the padding box of the root element: the border box of the root element starts before it,
        // the part is after the padding (the animations of the groups are paused at the geometry of the old state)
        Assert.Equal("matrix(1, 0, 0, 1, -5, -5)", GetComputed(scope, "::view-transition-group(root)", "transform"));
        Assert.Equal("230px", GetComputed(scope, "::view-transition-group(root)", "width"));
        Assert.Equal("matrix(1, 0, 0, 1, 10, 10)", GetComputed(scope, "::view-transition-group(part)", "transform"));

        // The ::view-transition box covers the padding box of the root element, and is not clipped by it
        var box = document.GetLayoutEngine().GetFragments(scope, "view-transition");
        var fragment = Assert.Single(box);
        Assert.Equal(new WebPlatform.Css.Layout.PhysicalRect(25, 25, 220, 120), fragment.GetRectInRoot());

        // The captured elements are painted by the pseudo-elements, which are hit as their originating element
        Assert.Same(scope, document.ElementFromPoint(40, 40));

        transition.SkipTransition();
        Assert.Null(scope.ActiveViewTransition);
        Assert.Equal("none", view.GetComputedStyle(scope).GetPropertyValue("view-transition-scope"));
        Assert.Equal("none", view.GetComputedStyle(scope).GetPropertyValue("view-transition-name"));
        Assert.Empty(document.GetLayoutEngine().GetFragments(scope, "view-transition"));
    }

    [Fact]
    public void Scoped_TransitionsRunConcurrentlyWithTheOneOfTheDocument()
    {
        var document = Load(ScopedPage);
        var scope = document.GetElementById("scope")!;
        var global = document.StartViewTransition();
        var scoped = scope.StartViewTransition();
        Assert.Same(global, document.ActiveViewTransition);
        Assert.Same(global, document.DocumentElement!.ActiveViewTransition);
        Assert.Same(scoped, scope.ActiveViewTransition);

        AdvanceFrames();
        Assert.True(global.Ready.IsCompletedSuccessfully);
        Assert.True(scoped.Ready.IsCompletedSuccessfully);

        // View transition scoping: the names in the root element of the scoped view transition are not discovered by the
        // transition of the document, and the two transitions have their own "root" group
        Assert.Equal("-ua-view-transition-group-anim-outside", GetComputed(document, "::view-transition-group(outside)", "animation-name"));
        Assert.Equal("none", GetComputed(document, "::view-transition-group(part)", "animation-name"));
        Assert.Equal("-ua-view-transition-group-anim-part", GetComputed(scope, "::view-transition-group(part)", "animation-name"));
        Assert.Equal("800px", GetComputed(document, "::view-transition-group(root)", "width"));
        Assert.Equal("230px", GetComputed(scope, "::view-transition-group(root)", "width"));

        // Starting a transition on the element skips its active transition, not the one of the document
        var replacement = scope.StartViewTransition();
        Assert.True(scoped.Finished.IsCompletedSuccessfully);
        Assert.Same(replacement, scope.ActiveViewTransition);
        Assert.Same(global, document.ActiveViewTransition);

        // On the document element, the method is the one of the document
        var other = document.DocumentElement.StartViewTransition();
        Assert.True(global.Finished.IsCompletedSuccessfully);
        Assert.Same(other, document.ActiveViewTransition);
        Assert.Same(document.DocumentElement, other.TransitionRoot);
    }

    [Fact]
    public void Scoped_ViewTransitionScopeHidesTheNames()
    {
        var document = Load(ScopedPage + "<style>#scope { view-transition-scope: all; view-transition-name: scope }</style>");
        var scope = document.GetElementById("scope")!;
        var transition = document.StartViewTransition();
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        // Neither the element nor its descendants are discovered by a transition that originates outside of it
        Assert.Equal("none", GetComputed(document, "::view-transition-group(scope)", "animation-name"));
        Assert.Equal("none", GetComputed(document, "::view-transition-group(part)", "animation-name"));
        Assert.Equal("-ua-view-transition-group-anim-outside", GetComputed(document, "::view-transition-group(outside)", "animation-name"));
        transition.SkipTransition();

        // The element itself and its descendants are discovered by its own transition
        var scoped = scope.StartViewTransition();
        AdvanceFrames();
        Assert.True(scoped.Ready.IsCompletedSuccessfully);
        Assert.Equal("-ua-view-transition-group-anim-scope", GetComputed(scope, "::view-transition-group(scope)", "animation-name"));
        Assert.Equal("-ua-view-transition-group-anim-part", GetComputed(scope, "::view-transition-group(part)", "animation-name"));
    }

    [Fact]
    public void Scoped_ElementsThatCannotHostATransitionSkipIt()
    {
        var document = Load(ScopedPage + "<span id=inline>text</span><div id=hidden style='display: none'></div>");
        var called = 0;
        foreach (var element in new[] { document.GetElementById("inline")!, document.GetElementById("hidden")!, document.CreateElement("div") })
        {
            var transition = element.StartViewTransition(() =>
            {
                called++;
                return null;
            });
            Assert.Equal("InvalidStateError", GetErrorName(transition.Ready));
            Assert.Null(element.ActiveViewTransition);
        }

        // The update callbacks are still called
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(3, called);

        // The root element is removed before the old state is captured, or hidden by the update callback
        var scope = document.GetElementById("scope")!;
        var removed = scope.StartViewTransition();
        scope.Remove();
        AdvanceFrames();
        Assert.Equal("InvalidStateError", GetErrorName(removed.Ready));
        Assert.True(removed.Finished.IsCompletedSuccessfully);

        document.Body!.AppendChild(scope);
        var hidden = scope.StartViewTransition(() =>
        {
            scope.SetAttribute("style", "display: none");
            return null;
        });
        AdvanceFrames();
        Assert.Equal("InvalidStateError", GetErrorName(hidden.Ready));
        Assert.Null(scope.ActiveViewTransition);
    }

    [Fact]
    public void Scoped_TheRenderingIsSuppressedWithinTheRootElement()
    {
        var document = Load(ScopedPage + "<style>#scope { width: fit-content; height: auto; overflow: visible } #outside { background: blue }</style>");
        var scope = document.GetElementById("scope")!;
        var part = document.GetElementById("part")!;
        var outside = document.GetElementById("outside")!;
        var pending = new TaskCompletionSource();
        var transition = scope.StartViewTransition(() =>
        {
            part.Remove();
            outside.SetAttribute("style", "background: lime");
            return pending.Task;
        });
        AdvanceFrames(2);
        Assert.False(transition.UpdateCallbackDone.IsCompleted);

        // The frame of the old state of the root element is displayed (the removed part), the rest of the document is rendered,
        // and the root element keeps its size (it does not collapse without its contents)
        var list = document.GetDisplayList()!.Dump();
        Assert.Contains("fill #ff0000", list, StringComparison.Ordinal);
        Assert.Contains("fill #00ff00", list, StringComparison.Ordinal);
        Assert.DoesNotContain("fill #0000ff", list, StringComparison.Ordinal);
        Assert.Equal(80, scope.GetBoundingClientRect().Width);
        Assert.Equal(80, scope.GetBoundingClientRect().Height);
        Assert.Equal(120, outside.GetBoundingClientRect().Top);

        // The hit testing in the root element targets it; the document is not suppressed
        Assert.False(document.ViewTransitionRenderingSuppressed);

        pending.SetResult();
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        transition.SkipTransition();
        Assert.Equal(30, scope.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Scoped_Script_ElementStartViewTransition()
    {
        using var engine = TestScriptEngines.CreateDisposable();
        var document = Load(ScopedPage + """
            <script>
              const log = [];
              const transition = scope.startViewTransition({ update: () => { log.push('update'); part.className = 'moved'; }, types: ['a'] });
              log.push(transition.transitionRoot === scope, scope.activeViewTransition === transition, document.activeViewTransition === null, scope.matches(':active-view-transition-type(a)'));
              transition.ready.then(() => {
                log.push('ready:' + document.getAnimations().filter(a => a.effect.target === scope).map(a => a.effect.pseudoElement).filter(p => p.includes('group')).join('+'));
                transition.skipTransition();
              });
              transition.finished.then(() => { log.push('finished', scope.activeViewTransition === null); document.title = log.join(); });
            </script>
            """, engine);
        AdvanceFrames(5);
        Assert.Equal("true,true,true,true,update,ready:::view-transition-group(root)+::view-transition-group(part),finished,true", document.Title);
    }

    [Fact]
    public void Selectors_NegatedOnlyChildAfterAPseudoElement()
    {
        var document = Load(Page + """
            <style>
              :root { view-transition-name: none }
              ::view-transition-group(*) { animation-play-state: paused }
              ::view-transition-group(target):not(:only-child) { outline: 1px solid red }
              ::view-transition-group(target):only-child { outline: 2px solid green }
            </style>
            """);
        var transition = document.StartViewTransition();
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        Assert.Equal("2px", GetComputed(document, "::view-transition-group(target)", "outline-width"));
        transition.SkipTransition();

        document.DocumentElement!.SetAttribute("style", "view-transition-name: root");
        transition = document.StartViewTransition();
        AdvanceFrames();
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        Assert.Equal("1px", GetComputed(document, "::view-transition-group(target)", "outline-width"));
    }
}
