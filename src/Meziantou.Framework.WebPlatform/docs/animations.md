# Animations

The library implements [Web Animations](https://drafts.csswg.org/web-animations-1/) (with the iteration composite
operation of [Web Animations 2](https://drafts.csswg.org/web-animations-2/)),
[CSS Animations](https://drafts.csswg.org/css-animations-1/), [CSS Transitions](https://drafts.csswg.org/css-transitions-1/)
and the [SVG animations](#svg-animations-smil) (SMIL).
The animated values are part of the computed styles (see
[Animations and transitions](computed-style.md#animations-and-transitions)): the animations change the values returned by
`getComputedStyle()`, the layout and the painting of the frames, and the events fired at the elements.
The [view transitions](view-transitions.md) animate the pseudo-elements that show the old and the new state of a document
with these animations.

## Usage

The animations progress with the clock of the event loop of the [browsing context](browsing-context.md): the document
timeline (`Document.Timeline`) is the time of the last animation frame. With a fake `TimeProvider`, the time is
controlled by the test:

```csharp
var clock = new FakeTimeProvider(); // Microsoft.Extensions.TimeProvider.Testing
using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock });
var document = context.Navigate("https://example.com/", _ => "<div id=target></div>");
var target = document.GetElementById("target")!;

var animation = target.Animate(
    [
        new Keyframe { Properties = { ["opacity"] = "0" } },
        new Keyframe { Properties = { ["opacity"] = "1" } },
    ],
    new KeyframeAnimationOptions { Duration = 1000, Easing = "ease-in-out", Fill = FillMode.Forwards });

animation.CurrentTime = 500; // seek: the value at the middle of the animation
var style = context.Window.GetComputedStyle(target);
Console.WriteLine(style.GetPropertyValue("opacity")); // 0.5

// Or let the animation run: the frames run when the clock advances
animation.Play();
clock.Advance(TimeSpan.FromSeconds(1));
context.EventLoop.RunUntilIdle();
```

The `Animation` objects of an element include its CSS animations (`CssAnimation`, with `AnimationName`) and transitions
(`CssTransition`, with `TransitionProperty`):

```csharp
foreach (var running in target.GetAnimations(new GetAnimationsOptions { Subtree = true }))
{
    Console.WriteLine($"{running.PlayState} {running.CurrentTime}");
}

var all = document.GetAnimations(); // the animations of the document, in composite order
```

## Web Animations

- `Element.Animate()`, `Element.GetAnimations()`, `Document.GetAnimations()`, `ShadowRoot.GetAnimations()`.
- `Animation`: the playback control (`Play()`, `Pause()`, `Finish()`, `Cancel()`, `Reverse()`, `UpdatePlaybackRate()`),
  `StartTime`, `CurrentTime`, `PlaybackRate`, `PlayState`, the `Ready` and `Finished` tasks, the `finish`, `cancel` and
  `remove` events, the replaced animations (`ReplaceState`, `Persist()`) and `CommitStyles()` (the endpoints of the
  active interval are included, so the final value of a finished animation is committed; as in the browsers, a committed
  `transform` is written in the shortest form of its functions and keeps their names: `scaleX(1.5)`, `translate(75px)`
  for a zero y translation, `scale(1.5)` for equal scales).
- `KeyframeEffect`: the timing (`GetTiming()`, `GetComputedTiming()`, `UpdateTiming()`: delays, fill modes, iterations,
  directions, easing functions), the keyframes (`GetKeyframes()`, `SetKeyframes()`: offsets, per-keyframe easing and
  composite operations, shorthands expanded to their longhands, logical properties resolved), the target element or
  pseudo-element (`::before`, `::after`, `::marker`, the `::view-transition-*()` pseudo-elements of an active
  [view transition](view-transitions.md)), `Composite` and `IterationComposite`.
- `DocumentTimeline` (with an origin time). The timeline is inactive in a document without browsing context.

The script API is the same: `element.animate()`, `new Animation()`, `new KeyframeEffect()`, `document.timeline`...

## Events

The events are queued by the animation frames and dispatched in the order of their scheduled time, then of the
composite order of their animations:

- `AnimationEvent`: `animationstart`, `animationiteration`, `animationend` and `animationcancel`.
- `TransitionEvent`: `transitionrun`, `transitionstart`, `transitionend` and `transitioncancel`.
- `AnimationPlaybackEvent`: `finish`, `cancel` and `remove` (at the `Animation` objects).

## Scroll-driven animations

[Scroll-driven Animations](https://drafts.csswg.org/scroll-animations-1/) drive the animations with the scroll position
of a scroll container: their timelines are progress-based (their times are percentages, their duration is 100%). They need
the layout engine (see [Scroll-driven timelines in layout.md](layout.md#scroll-driven-timelines-csslayoutscrolltimelinegeometrycs)).

```csharp
var scroller = document.GetElementById("scroller")!;
var timeline = new ScrollTimeline(new ScrollTimelineOptions { Source = scroller, Axis = ScrollAxis.Block });
var animation = target.Animate(keyframes, new KeyframeAnimationOptions { Timeline = timeline, RangeStart = "20%", RangeEnd = "80%" });

scroller.ScrollTop = 200;
context.EventLoop.RunUntilIdle(); // the timelines are sampled by the rendering steps
Console.WriteLine(timeline.CurrentTime); // CSSUnitValue: 50%
```

- `ScrollTimeline` (a scroll progress timeline: `source`, `axis`) and `ViewTimeline` (a view progress timeline: `subject`,
  `startOffset`, `endOffset`, the `inset` of the options). A timeline is inactive (its current time is null) when its source
  is not a scroll container in its axis, has no scrollable overflow (a scroll timeline), or its subject has no box. The
  current time is the one of the last rendering update (https://drafts.csswg.org/scroll-animations-1/#event-loop); a
  timeline created by script is sampled at once.
- The CSS timelines: `animation-timeline: scroll()` (`nearest`, `root`, `self`, the axis) and `view()` (the axis and the
  inset) create one anonymous timeline per animation (kept while the arguments do not change); the named timelines are
  declared by `scroll-timeline` and `view-timeline` (the later names win, the scroll timelines win over the view timelines),
  their names are tree-scoped, and are looked up by the [target timeline](https://drafts.csswg.org/scroll-animations-1/#target-timeline)
  algorithm (the element and its ancestors, then the last declaration in flat tree order in the scope of `timeline-scope`
  or of the root element). An unresolved name is an inactive timeline, `null` in the object model.
- The animation attachment ranges: `animation-range` and the `rangeStart`/`rangeEnd` of `Animation` and of `animate()`
  (`normal`, an offset, or a named timeline range — `cover`, `contain`, `entry`, `exit`, `entry-crossing`, `exit-crossing`,
  `scroll` — with an offset; the script API only accepts the absolute lengths; the lengths of `animation-range` are zoomed by
  the animation target, the ones of `view-timeline-inset` by the subject). The timing of the effect is normalized to the
  attachment range (Web Animations 2): an `auto` duration fills it (divided by the iterations), the times keep their
  proportions; `getComputedTiming()` returns percentages.
- The timeline offsets of the keyframes: `@keyframes` selectors such as `entry 10%`, and the `offset` of a keyframe given
  as a string or a `TimelineRangeOffset`: their computed offset is resolved from the timeline (null when it cannot be).
- Web Animations 2: the auto-aligned start time, the playback control on a progress-based timeline (setting the timeline,
  play, pause, the playback rate that flips the start time), `CSSNumberish` percentages for the start and current times,
  the [timeline boundary](https://drafts.csswg.org/web-animations-2/#at-timeline-boundary) rule.
- `WebPlatformFeatures.ScrollDrivenAnimationsEnabled` hides `ScrollTimeline`, `ViewTimeline` and the range attributes;
  the scroll and view timelines of the styles are then no timeline, so the layout is never computed for them.

## Animation worklet

The [CSS Animation Worklet API](https://drafts.css-houdini.org/css-animation-worklet-1/) lets a script compute the local
times of the effects of an animation: a `WorkletAnimation` is an `Animation` whose effects are driven by an *animator*, an
instance of a class that a module of the animation worklet registers (`CSS.animationWorklet.addModule()`,
`Document.AnimationWorklet`; see [Worklets](scripting.md#worklets)).

```js
// animator.js
registerAnimator('spring', class {
  constructor(options, state) { this.stiffness = options.stiffness; this.position = state?.position ?? 0; }
  animate(currentTime, effect) { effect.localTime = currentTime * this.stiffness; }
  state() { return { position: this.position }; } // optional: a stateful animator
});

// document
await CSS.animationWorklet.addModule('animator.js');
const animation = new WorkletAnimation('spring', new KeyframeEffect(target, keyframes, 1000), document.timeline, { stiffness: 0.5 });
animation.play();
```

- **Registration** (`AnimationWorkletGlobalScope.RegisterAnimator`): the name is an identifier, the class a constructor
  whose prototype has an `animate` function; the animator is stateful when the prototype has a `state` function. The
  document keeps the stateful flag of each name (the document animator definitions).
- **`WorkletAnimation`** (`new WorkletAnimation(animatorName, effects, timeline, options)`): the animator must be
  registered (else a `TypeError`); the timeline defaults to the timeline of the document of the current global object (a
  `ScrollTimeline` is a valid timeline); the options are structured cloned. It has the playback control, the play state,
  the current time, the playback rate, the promises and the events of an `Animation`, and `animatorName`. With a sequence
  of effects, the animation drives each effect individually (the `WorkletGroupEffect` of the draft): the first effect is
  `animation.effect`, the other ones belong to the animation too.
- **Local times**: the current time of a worklet animation does not determine the local time of its effects
  (`AnimationEffect.LocalTime`): an effect has no local time (it is not in effect) until the animator sets one, and keeps the
  last one the animator set, also once the animation is finished. An idle (canceled) animation has no local time.
- **Running the animators** ([run animators](https://drafts.css-houdini.org/css-animation-worklet-1/#run-animators),
  `AnimationWorklet.UpdateAnimator`): when the animations of a document are updated for a frame ("update animations and
  send events", before the animation frame callbacks), the animator instance of each worklet animation that the frame may
  change is created (the animation is no longer idle: its class is constructed with the options) or removed (it is idle),
  and `animate(currentTime, effect)` is invoked in the current global scope of the worklet: `currentTime` is the current
  time of the animation in milliseconds (the percentage of a progress-based timeline; null when unresolved), `effect` a
  `WorkletAnimationEffect` (`getTiming()`, `getComputedTiming()` at its local time, `localTime`) or a `WorkletGroupEffect`
  (`getChildren()`). The local times are synced to the effects right after the callback (the global scopes do not run in
  parallel), and the styles of the targets are computed again when one changed; the local times set by a callback that
  throws are ignored.
- **Migration** ([migrate an animator instance](https://drafts.css-houdini.org/css-animation-worklet-1/#migrating-animator)):
  the worklet has two global scopes, and the animator instances are moved to the other one every 12 frames, so that the
  stateless animators cannot rely on their state: the class is constructed again with the options, and with the structured
  clone of the value that `state()` returned for a stateful animator (undefined otherwise). The animator effects are new
  objects that keep the local times of the effects. An instance whose `state()` throws, or returns a value that cannot be
  cloned, is removed until the animation is idle again.
- **Feature toggle**: `WebPlatformFeatures.AnimationWorkletEnabled` hides `CSS.animationWorklet` and `WorkletAnimation`.

The draft is followed where the tests of the former Chromium implementation differ: a worklet animation finishes as any
animation (its current time is limited to the end of its effect), its start time is set when its pending play task runs,
playing an effect without keyframes or creating a group without effect does not throw, and an unregistered animator name
is a `TypeError`.

## SVG animations (SMIL)

[SVG Animations](https://svgwg.org/specs/animations/) (with the timing model of
[SMIL Animation](https://www.w3.org/TR/2001/REC-smil-animation-20010904/)): the `animate`, `set`, `animateMotion` (with
`mpath`) and `animateTransform` elements animate the attributes and the properties of their target element (the element
referenced by `href`, or their parent).

```csharp
var clock = new FakeTimeProvider();
using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock });
var document = context.Navigate("https://example.com/", _ => """
    <svg id="svg" width="200" height="100">
      <rect id="target" width="10" height="10" fill="red">
        <animate attributeName="width" from="10" to="110" dur="4s" fill="freeze" />
        <animate attributeName="fill" from="red" to="blue" begin="target.click" dur="1s" />
      </rect>
    </svg>
    """);
context.EventLoop.RunUntilIdle(); // the timeline of the document fragment begins with the load event

var svg = (SvgSvgElement)document.GetElementById("svg")!;
var rect = (SvgRectElement)document.GetElementById("target")!;

// Let the animations run with the clock...
clock.Advance(TimeSpan.FromSeconds(1));
context.EventLoop.RunUntilIdle();

// ... or seek the timeline: the animations are sampled at once
svg.PauseAnimations();
svg.SetCurrentTime(2);
Console.WriteLine(rect.Width.AnimVal.Value); // 60 (the base value, the attribute, is still 10)
Console.WriteLine(context.Window.GetComputedStyle(rect).GetPropertyValue("width")); // 60px
```

- **Timelines**: each SVG document fragment (an outermost `svg` element) has a timeline that begins when the load event of
  the document is fired (when the element is inserted, afterwards), follows the clock of the event loop, and is
  controlled by `SvgSvgElement.PauseAnimations()`, `UnpauseAnimations()`, `AnimationsPaused()`, `GetCurrentTime()` and
  `SetCurrentTime()` (a time set before the load event is the time the timeline begins at). The animations are sampled
  by the animation frames (the same frames as the other animations) and when a timeline is seeked. A document without
  browsing context (a parsed document, an SVG image) has no clock: its timelines are at 0 until they are seeked, so an SVG
  image shows its animations at the begin of their timeline.
- **Timing**: the `begin` and `end` lists (offsets, syncbase values `id.begin`/`id.end`, events `id.click` — the event
  targets are the referenced element or the target element —, repeat values `id.repeat(2)`, `accessKey(c)`,
  `indefinite`), `dur`,
  `repeatCount`, `repeatDur`, `min`, `max`, `restart` and `fill`. The intervals are resolved as in SMIL: an interval that
  did not begin is resolved again when the instance lists change, the end of the current interval is computed again, a
  begin time ends the current interval (`restart="always"`), and the elements that depend on an interval (syncbase
  values) get their instance times from it. The times are integers (microseconds), so the ends of the intervals are
  exact. An `accessKey(c)` value occurs when a `keydown` event of the key is dispatched in the document.
- **`SVGAnimationElement`**: `TargetElement`, `GetStartTime()` (it throws without current interval), `GetCurrentTime()`,
  `GetSimpleDuration()`, `BeginElement()`, `BeginElementAt()`, `EndElement()`, `EndElementAt()`, and the
  `beginEvent`, `endEvent` and `repeatEvent` events (`TimeEvent`, whose `Detail` is the iteration; `OnBegin`, `OnEnd`,
  `OnRepeat`), queued as tasks. When a timeline is seeked, only the changes of active state fire events (the intervals
  that are skipped fire nothing).
- **Values**: `from`/`to`/`by`/`values`, `keyTimes`, `keySplines`, `calcMode` (`discrete`, `linear`, `paced`, `spline`),
  `additive` and `accumulate`, to animations, and the sandwich model (the animations of a target attribute are applied in
  the order of the begin of their interval, then in document order, on top of the base value). An animation with an
  invalid value has no effect.
  - The attributes reflected by an SVG DOM object are animated with the type of their values: lengths (combined in user
    units; the result has the unit of the nearest value), numbers, integers, number pairs, lists of lengths, numbers and
    points, `viewBox`, angles (`orient`), and the values that are not additive (enumerations, booleans, strings,
    `preserveAspectRatio`, `class`, `href`), which are animated discretely. The path data (`d` of `path`, `path` of
    `textPath`) is interpolated when the two paths have the same commands. `animateTransform` animates the transform
    lists (`transform`, `gradientTransform`, `patternTransform`); an additive animation appends its transform. The
    animated value is `animVal` (the `baseVal` and the attribute do not change); the geometry, the painting and the
    presentation attribute of the property (e.g. `width`, `d`, `transform`) use it. An animated `class` is matched by
    the selectors.
  - The other presentation attributes (`fill`, `opacity`, `visibility`...) are animated as properties, with the
    interpolation of the CSS values: as in the browsers, the animated value overrides the declarations of the style
    sheets and of the `style` attribute, except the important ones, and it is the underlying value of the CSS animations
    and transitions. The colors are interpolated as CSS colors (with premultiplied alpha, as in Firefox).
  - `animateMotion`: the `path` attribute, the `mpath` child or the `from`/`to`/`by`/`values` points, `keyPoints`,
    `rotate` (`auto`, `auto-reverse`, an angle) and the paced `calcMode` by default; the supplemental transform is applied
    after the transform of the element.
- The animation elements in an element whose conditional processing attributes fail, or that are not the child rendered
  by their `switch` parent, are not timed.
- The `load` event is fired at the outermost `svg` elements of a document before the load event of the window.
- `WebPlatformFeatures.SvgAnimationsEnabled` disables the animation elements (they have no effect and fire no event) and
  hides `TimeEvent`.

## Not supported

- The temporal media fragments of the SVG images (`#t=`) and the `discard` element of the SMIL animations.
- The animation triggers, and the percentage delays of the effects (a Web Animations 2 proposal).
- The named timelines declared by pseudo-elements, and the sticky positioned inline boxes that contain the block-level box
  of the subject of a view progress timeline (the sticky block boxes and the sticky subjects are accounted for, see
  [layout.md](layout.md)).
- The group and sequence effects of Web Animations 2 (`GroupEffect`, `SequenceEffect`, the effect tree members) and
  the `playbackRate` member of the timing.
- The values that depend on layout are not interpolated (e.g. a percentage of a translation that needs the matrix
  fallback when the size of the reference box is not known: the interpolation is discrete).
