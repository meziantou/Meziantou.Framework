using global::Jint;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>
/// The CSS Animation Worklet API (https://drafts.css-houdini.org/css-animation-worklet-1/): the animation worklet,
/// <c>registerAnimator()</c> and the worklet animations, whose animators set the local times of the effects.
/// </summary>
public sealed class AnimationWorkletTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly List<(GlobalScope Global, string Text)> _messages = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    /// <summary>Loads a document whose animation worklet runs a module; the script of the document runs once the module is added.</summary>
    private Document Load(string module, string html, string script, WebPlatformFeatures? features = null)
    {
        _loader.Resources["https://example.com/animator.js"] = new DocumentResource(module) { Headers = [new("Content-Type", "text/javascript")] };
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            Features = features,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            ConsoleMessage = (global, message) => _messages.Add((global, message.Text)),
        });

        var document = context.Navigate("https://example.com/page.html", _ => "<!DOCTYPE html><body>" + html + "<script>if (CSS.animationWorklet) CSS.animationWorklet.addModule('animator.js').then(() => { document.title = 'added'; " + script + " }).catch(e => document.title = 'error ' + e)</script>");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Advances the clock and runs the tasks and the frames that are due.</summary>
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

    private IEnumerable<string> WorkletMessages => _messages.Where(message => message.Global is AnimationWorkletGlobalScope).Select(message => message.Text);

    private static string Opacity(Document document, string id = "target") => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).Opacity;

    [Fact]
    public void Animator_SetsTheLocalTimeOfTheEffect()
    {
        var document = Load("""
            registerAnimator('half', class {
              constructor(options) { this.time = options.time; }
              animate(currentTime, effect) { effect.localTime = this.time; }
            });
            """, "<div id=target></div>", """
            window.animation = new WorkletAnimation('half', new KeyframeEffect(document.getElementById('target'), [{ opacity: 0 }, { opacity: 1 }], { duration: 1000 }), document.timeline, { time: 250 });
            document.body.dataset.result = [animation.animatorName, animation.playState, animation instanceof Animation, animation.timeline === document.timeline].join();
            animation.play();
            """);

        Assert.Equal("added", document.Title);
        Assert.Empty(_errors);
        Assert.Equal("half,idle,true,true", document.Body!.GetAttribute("data-result"));

        // The effect has no local time until the animator sets it, whatever the current time of the animation
        Assert.Equal("1", Opacity(document));
        Advance(50);
        Assert.Equal("0.25", Opacity(document));

        // The local time is the one the animator set once the animation is finished, and the effect is removed when it is canceled
        Advance(2000);
        Assert.Equal("finished", _engine.Evaluate(document.DefaultView!, "animation.playState").ToString());
        Assert.Equal("0.25", Opacity(document));
        _engine.Evaluate(document.DefaultView!, "animation.cancel()");
        Assert.Equal("1", Opacity(document));
    }

    [Fact]
    public void Animator_GetsTheCurrentTimeAndTheTimingOfTheEffect()
    {
        var document = Load("""
            registerAnimator('passthrough', class {
              animate(currentTime, effect) {
                effect.localTime = currentTime;
                this.log ??= console.log([typeof currentTime, effect.getTiming().duration, effect.getComputedTiming().progress, effect.constructor.name].join());
              }
            });
            """, "<div id=target></div>", """
            window.animation = new WorkletAnimation('passthrough', new KeyframeEffect(document.getElementById('target'), [{ opacity: 0 }, { opacity: 1 }], { duration: 1000 }));
            animation.play();
            """);

        Assert.Equal("added", document.Title);
        Advance(516);
        Assert.Empty(_errors);
        var opacity = double.Parse(Opacity(document), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(opacity, 0.4, 0.55);
        Assert.Contains("number,1000,0,WorkletAnimationEffect", WorkletMessages);
    }

    [Fact]
    public void GroupEffect_SetsTheLocalTimesOfTheChildren()
    {
        var document = Load("""
            registerAnimator('group', class {
              animate(currentTime, effect) {
                const [first, second] = effect.getChildren();
                first.localTime = 200;
                second.localTime = 800;
              }
            });
            """, "<div id=target></div><div id=other></div>", """
            const keyframes = [{ opacity: 0 }, { opacity: 1 }];
            window.animation = new WorkletAnimation('group', [new KeyframeEffect(document.getElementById('target'), keyframes, 1000), new KeyframeEffect(document.getElementById('other'), keyframes, 1000)]);
            animation.play();
            """);

        Assert.Equal("added", document.Title);
        Advance(50);
        Assert.Empty(_errors);
        Assert.Equal("0.2", Opacity(document));
        Assert.Equal("0.8", Opacity(document, "other"));
    }

    [Fact]
    public void StatefulAnimator_KeepsItsStateWhenItIsMigrated()
    {
        // The animator instances are created again in another global scope every few frames: a stateful animator gets the
        // state it returned, a stateless one starts again; the local time of the effect is kept
        var document = Load("""
            registerAnimator('stateful', class {
              constructor(options, state = { count: 0 }) { this.count = state.count; }
              animate(currentTime, effect) { effect.localTime = ++this.count; }
              state() { return { count: this.count }; }
            });
            registerAnimator('stateless', class {
              constructor(options, state = { count: 0 }) { this.count = state.count; }
              animate(currentTime, effect) { effect.localTime = ++this.count; this.previous ??= console.log('new instance'); }
            });
            """, "<div id=target></div><div id=other></div>", """
            const keyframes = [{ opacity: 0 }, { opacity: 1 }];
            window.stateful = new WorkletAnimation('stateful', new KeyframeEffect(document.getElementById('target'), keyframes, 100000));
            window.stateless = new WorkletAnimation('stateless', new KeyframeEffect(document.getElementById('other'), keyframes, 100000));
            stateful.play();
            stateless.play();
            """);

        Assert.Equal("added", document.Title);
        Advance(1000);
        Assert.Empty(_errors);
        var stateful = _engine.Evaluate(document.DefaultView!, "stateful.effect.getComputedTiming().localTime").AsNumber();
        var stateless = _engine.Evaluate(document.DefaultView!, "stateless.effect.getComputedTiming().localTime").AsNumber();
        Assert.InRange(stateful, 50, 70);
        Assert.InRange(stateless, 1, 12);
        Assert.True(WorkletMessages.Count(message => message == "new instance") >= 3);
        Assert.Equal(2, _messages.Where(message => message.Text == "new instance").Select(message => message.Global).Distinct().Count());
    }

    [Fact]
    public void RegisterAnimator_ValidatesTheClass()
    {
        var document = Load("""
            const log = [];
            const check = (name, ctor) => { try { registerAnimator(name, ctor); log.push('ok'); } catch (e) { log.push(e.name); } };
            const valid = class { animate() {} };
            check('not an ident', valid);
            check('a', valid);
            check('a', valid);
            check('b', class { });
            check('c', () => {});
            check('d', class { animate() {} state() {} });
            console.log([...log, typeof WorkletAnimationEffect, typeof WorkletGroupEffect, typeof WorkletAnimation, typeof window, Object.prototype.toString.call(globalThis)].join());
            """, "", """
            const log = [];
            for (const create of [() => new WorkletAnimation('unknown'), () => new WorkletAnimation('a'), () => new WorkletAnimation('d', null, null), () => new WorkletAnimation('a', null, document.timeline, { f() {} })]) {
              try { const animation = create(); log.push(animation.effect + ' ' + (animation.timeline === document.timeline)); } catch (e) { log.push(e.name); }
            }
            document.body.dataset.result = log.join();
            """);

        Assert.Equal("added", document.Title);
        Assert.Equal(2, WorkletMessages.Count());
        Assert.All(WorkletMessages, message => Assert.Equal("TypeError,ok,NotSupportedError,TypeError,TypeError,ok,function,function,undefined,undefined,[object AnimationWorkletGlobalScope]", message));
        Assert.Equal("TypeError,null true,null false,DataCloneError", document.Body!.GetAttribute("data-result"));
    }

    [Fact]
    public void DisabledFeature_HidesTheAnimationWorklet()
    {
        var document = Load("", "<script>document.title = [typeof Worklet, typeof CSS.animationWorklet, 'animationWorklet' in CSS, typeof WorkletAnimation].join()</script>", "", new WebPlatformFeatures { AnimationWorkletEnabled = false });
        Assert.Equal("function,undefined,false,undefined", document.Title);
    }
}
