# View transitions

The library implements [CSS View Transitions Level 1](https://drafts.csswg.org/css-view-transitions-1/) (the same-document
transitions of `document.startViewTransition()`) and, from [Level 2](https://drafts.csswg.org/css-view-transitions-2/), the
transition types, `view-transition-class`, the `auto` and `match-element` names, the nested groups
(`view-transition-group`), the scoped view transitions (`element.startViewTransition()`, `view-transition-scope`) and the
cross-document transitions (`@view-transition`, `pageswap`, `pagereveal`). A transition
shows the snapshots of the old state of the named elements and the live images of their new state in a tree of
pseudo-elements, which is styled, animated (see [animations.md](animations.md)), laid out (see [layout.md](layout.md))
and painted (see [rendering.md](rendering.md)) like the rest of the document.

## Usage

```csharp
var clock = new FakeTimeProvider(); // Microsoft.Extensions.TimeProvider.Testing
using var context = new BrowsingContext(new BrowsingContextOptions
{
    TimeProvider = clock,
    RenderingBackend = new ImagingRenderingBackend(), // the snapshots are rasterized by the backend
});
var document = context.Navigate("https://example.com/", _ => """
    <style>#card { view-transition-name: card; width: 100px; height: 100px; background: red }</style>
    <div id=card></div>
    """);
context.EventLoop.RunUntilIdle();

// The old state is captured at the next rendering opportunity, then the callback updates the document
var transition = document.StartViewTransition(() =>
{
    document.GetElementById("card")!.SetAttribute("style", "margin-left: 300px; background: green");
    return null; // or a Task: the new state is captured when it completes
});

clock.Advance(TimeSpan.FromMilliseconds(20));
context.EventLoop.RunUntilIdle();
await transition.Ready; // the pseudo-elements exist, their animations are about to start

var style = context.Window.GetComputedStyle(document.DocumentElement!, "::view-transition-group(card)");
Console.WriteLine(style.GetPropertyValue("transform")); // the position of the group, animated from the old one to the new one

using var frame = context.TakeScreenshot(); // a frame of the transition

clock.Advance(TimeSpan.FromMilliseconds(300));
context.EventLoop.RunUntilIdle();
await transition.Finished; // the animations finished: the pseudo-elements are removed
```

The script API is the same: `document.startViewTransition(callback | { update, types })`, `document.activeViewTransition`,
`element.startViewTransition(…)`, `element.activeViewTransition`, `ViewTransition` (`updateCallbackDone`, `ready`,
`finished`, `skipTransition()`, `types`, `transitionRoot`, `waitUntil()`) and `ViewTransitionTypeSet`.

## Lifecycle

- `StartViewTransition()` skips the active transition of the document (its `Ready` task fails with an `AbortError`) and
  schedules a rendering opportunity. A transition started in a hidden document is skipped with an `InvalidStateError`, and
  the one of a document that is not fully active (e.g. the document of a removed iframe) with an `AbortError`, without
  calling the callback (no task of such a document runs).
- **Capture of the old state** ("perform pending transition operations", in the rendering update after the layout): the
  elements whose `view-transition-name` is not `none` are captured in paint order (the elements in the skipped contents of
  `content-visibility` and the fragmented boxes are ignored; two elements with the same name skip the transition). The
  rendering of the document is then suppressed: its animation frame callbacks and its animations wait, the hit testing
  targets the document element, and the painting returns the frame of the old state.
- **Update callback**: called in a task. The transition is skipped with a `TimeoutError` when the task (or the promise) it
  returns is not settled after 4 seconds (the timeout of the browsers), and with its reason when it fails. The callback is
  always called, also when the transition is skipped before.
- **Activation**: the new state is captured (the same elements, or other ones with the same names), the pseudo-element tree
  is created and styled, and `Ready` completes. A change of the size of the snapshot containing block, a duplicate name or
  an element that is no longer rendered skips the transition with an `InvalidStateError`.
- **Animation**: each rendering update computes the geometry of the new elements again. The transition ends when no
  animation of its pseudo-elements is running or paused (and no `waitUntil()` promise is pending): as in the browsers, the
  pseudo-elements are removed and `Finished` completes in a task that follows the frame (the specification does it in the
  frame).
- `SkipTransition()` removes the pseudo-elements at once; `Finished` follows `UpdateCallbackDone`.
- As the specification says, the `finished` promise is marked as handled, and so is the `ready` promise when the update
  callback fails (Chrome reports them as unhandled rejections).

## Names and captures

- `view-transition-name`: `none`, a name, `auto` (the id of the element when it has one, else its identity) or
  `match-element` (the identity of the element); the generated names start with `-ua-` and cannot be selected by name.
  The root element is named `root` by the user agent style sheet. The pseudo-elements that generate boxes (e.g.
  `::backdrop`) can be named too. A named element is a stacking context, and its used `transform-style` is `flat`.
- `view-transition-class`: the classes matched by the pseudo-element selectors (the ones of the new element when there is
  one).
- `view-transition-group` (`normal`, `contain`, `nearest`, a name): the group of the element is nested in the
  `::view-transition-group-children()` of the group of an ancestor
  (https://drafts.csswg.org/css-view-transitions-2/#resolve-the-used-group-name); its transform is then relative to the
  padding box of its container, and the children container has the border widths, the radii and the corner shapes of the
  element (animated from the old ones), so that `overflow: clip` clips the nested groups as the element does.
- **Geometry**: the size of the border box of the element (the snapshot containing block for the root element), and the
  matrix that maps it from the origin of the snapshot containing block to its position, with the transforms and the
  scroll positions of its ancestors. The `transform` of a group is this matrix relative to the center of the group (its
  default `transform-origin`).
- **Image of the old state**: the stacking context of the element is painted into a display list without its transform and
  without the clips and the effects of its ancestors, with its own effects (opacity, filters, clips) and without its
  descendants that are captured too, then rasterized by the [rendering backend](rendering.md#rendering-backends) at the
  device pixel ratio (multiplied by the scale of the transform of the element, up to 8). The bitmap covers the ink overflow
  (the bounds of the display list), limited to 4096 device pixels per side around the part of the element that is in the
  viewport; its natural size is the size of the border box. The image of the root element is the whole document (the
  canvas background and the top layer) in the snapshot containing block, over the canvas color of a top-level document (as
  in the browsers, so it is opaque). Without rendering backend, the image is the display list itself.
- **Image of the new state**: live. The element is painted in place of the content of its `::view-transition-new()`
  pseudo-element each time the document is painted, with the same rules.
- While the transition is animating, the captured elements and their contents are not painted in the document and are
  not hit: a point in a pseudo-element hits its originating element, the document element.

## Pseudo-elements

```
::view-transition
├─ ::view-transition-group(name)
│  ├─ ::view-transition-image-pair(name)
│  │  ├─ ::view-transition-old(name)
│  │  └─ ::view-transition-new(name)
│  └─ ::view-transition-group-children(name)   (the nested groups)
└─ …
```

- **Selectors**: `::view-transition-*(name)`, `(*)` (zero specificity), `(.class)`, `(*.class)`, `(name.class1.class2)`,
  followed by `:only-child` or `:not(:only-child)` (an image without the other image, a group without sibling);
  `:active-view-transition` and `:active-view-transition-type(a, b)` match the root element of a document with an active
  transition and the root element of a scoped transition (`none` and the names that start with `-ua-` are not valid types).
- **Styles**: the pseudo-elements originate from the root element and inherit from their parent in the tree.
  `getComputedStyle(root, "::view-transition-group(name)")` returns the style of any name (the pseudo-elements that do not
  exist have the styles of the user agent and author style sheets, without animations). The global user agent style sheet
  of the specification is part of the user agent style sheet; the dynamic style sheet of a transition (the size, the
  transform, the writing mode, the direction, the blend mode, the backdrop filter and the color scheme of the groups, the
  `-ua-view-transition-group-anim-*` keyframes from the old geometry, the cross-fade of the images with
  `mix-blend-mode: plus-lighter`, the isolation of the image pairs) is a user-agent sheet generated when the transition is
  activated and at each frame. The `-ua-*` keyframes can be used by the author style sheets.
- **Animations**: the CSS animations and transitions of the pseudo-elements, `element.animate(…, { pseudoElement })` and
  `document.getAnimations()` (in the order of the tree) work as for `::before`; they are canceled when the tree is removed.
- **Layout**: `::view-transition` is a box of the top layer painted after it, whose containing block is the snapshot
  containing block: the viewport with its scrollbar gutters, which does not scroll (there is no user agent interface that
  shows and hides). The images are replaced boxes (`object-fit`, `object-position`, `object-view-box` apply; their ink
  overflow is painted outside of their box unless they clip their overflow). As in the browsers, `::view-transition`
  generates a box during the whole animation, even without group.
- **Painting**: the images are aligned to the device pixels as the elements are in the document (an image displayed at its
  natural size keeps the fractional part of the position of its element, with the translations of the element and of its
  ancestors: a box that is only translated is snapped at its translated position), and the isolation of an image pair whose
  images are not blended creates no layer, nor does the `backdrop-filter` animation of a group.

## Scoped view transitions

`Element.StartViewTransition()` starts a transition whose root element (`TransitionRoot`) is the element
(https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions): it animates the subtree of the element, and
several transitions (one per element, and the one of the document) run at the same time. On the document element, the
method is `Document.StartViewTransition()`.

```csharp
var card = document.GetElementById("card")!;
var transition = card.StartViewTransition(() =>
{
    card.ClassList.Add("expanded");
    return null;
});
// card.ActiveViewTransition == transition; document.ActiveViewTransition is null
var style = context.Window.GetComputedStyle(card, "::view-transition-group(root)");
```

- **Root element**: the element must be rendered and qualify for layout containment (not a non-atomic inline box, an
  internal table box other than a cell, an internal ruby box): else the transition is skipped with an `InvalidStateError`
  (the update callback is still called), also when the element is removed, moved to another document, hidden
  (`display: none`) or loses its box type before the old state is captured, when the callback returns, or during the
  animation. Starting a transition on an element skips its active transition (`AbortError`), not the ones of the other
  elements. For the duration of the transition, the element has layout containment (so it is a stacking context and the
  containing block of its descendants; the computed value of `contain` does not change), matches `:active-view-transition`
  and `:active-view-transition-type()`, and two rules of the user agent style sheet give it
  `view-transition-scope: all` (`!important`) and the default name `root` (an author `view-transition-name`, e.g. `none`,
  replaces it).
- **Names**: a scoped transition only discovers the names of its root element and of its flat tree descendants.
  `view-transition-scope: all` hides the names of an element and of its descendants from the transitions that originate
  outside of it (the transition of the document, the ones of its ancestors); the root element of an active scoped
  transition has it. As the specification says, a transition whose old state captures an element that another active
  transition already captured skips that other transition with an `AbortError` (Chrome skips the new transition instead).
- **Order**: the pending transitions are processed in the order of their creation at each rendering update, so the update
  callbacks are called in this order (as Chrome does; the specification lists the transition of the document, then the
  ones of the elements in tree order).
- **Rendering suppression**: between the capture of the old state and the end of the update callback, the rendering is
  only paused within the root element: the frame of its old state is painted in place of its stacking context (with its
  current transform), the hit testing in its contents targets it, and it has size containment with the size of its old
  state, so the changes of its contents do not move the rest of the document (as in Chrome). The animation frame
  callbacks and the animations of the document keep running.
- **Pseudo-element tree**: it originates from the root element (`#card::view-transition-group(name)`,
  `getComputedStyle(card, "::view-transition-new(name)")`, `card.animate(…, { pseudoElement })`); each transition has its own
  dynamic style sheet and keyframes, so two transitions can use the same names. The `::view-transition` box is the last
  child of the box of the root element, absolutely positioned in its padding box (the snapshot containing block of the
  transition: the geometry of the groups is relative to it, and its size can change during the transition). It is a
  stacking context painted after the stacking context of the root element, above all its contents: the transform of the
  root element applies to it, its effects (opacity, filters, clip path, masks) do not, it is not scrolled nor clipped by
  the root element and it does not extend its scrollable overflow. The image of the root element itself (its `root`
  group) does not contain the pseudo-elements; the pseudo-elements of a nested scoped transition are painted in the
  images of the elements that contain its root element.

## Cross-document transitions

A navigation between two same-origin documents that both opt in with `@view-transition { navigation: auto }` is animated
(https://drafts.csswg.org/css-view-transitions-2/#cross-document-view-transitions):

- `CssViewTransitionRule` (`CSSViewTransitionRule`: `navigation`, `types`); the last rule of the strongest cascade layer
  that is not in a conditional rule that does not match applies.
- **Old document**: the `pageswap` event (`PageSwapEvent`: `activation`, the entries and the type of the navigation for a
  same-origin navigation, and `viewTransition`) is fired at its window before it is unloaded, for every navigation that
  replaces a document (except the initial `about:blank`). When the document opts in, is visible, and the navigation is
  not a reload, its old state is captured right after the event (the specification waits for its next rendering
  opportunity; nothing renders in between) and given to the new document. A handler that delays the navigation for more
  than 4 seconds, or that calls `skipTransition()`, cancels the transition.
- **New document**: the `pagereveal` event (`PageRevealEvent`: `viewTransition`) is fired by its first rendering update
  once its body exists, or its parser stopped. When the document opts in, `viewTransition` is the inbound transition (its
  `updateCallbackDone` is already resolved), which is activated after the event: the new state is captured and the
  animation starts. A rendering opportunity is only scheduled for the reveal when a `pagereveal` listener is added or when
  the navigation has a transition; the other documents are revealed by their first rendering update.

## Feature toggle

`WebPlatformFeatures.ViewTransitionsEnabled` (see [browsing-context.md](browsing-context.md)) hides
`document.startViewTransition()`, `document.activeViewTransition`, `element.startViewTransition()`,
`element.activeViewTransition`, `ViewTransition`, `ViewTransitionTypeSet` and `CSSViewTransitionRule` from the scripts, and disables the cross-document transitions (the events are still fired, with a
null `viewTransition`). The properties and the selectors are still parsed.

## Not supported

- The automatic nesting of the groups of a scoped transition in the group of its root element (a tentative behavior of
  Chrome, csswg-drafts#13420): the groups are only nested by `view-transition-group`, and they are not clipped by the root
  element.
- A root element whose box is a table: the `::view-transition` box is a child of its table grid box.
- The back/forward cache. A new document is revealed by its first rendering update once it is no longer
  [render-blocked](browsing-context.md#render-blocking) (`blocking="render"`, `<link rel="expect">`); a prerendered
  document is revealed once it is activated, so a navigation that activates it can have a view transition. A document
  that is hidden before it is revealed has no inbound view transition, and a document that is still render-blocked has
  no outbound view transition (it was never revealed). The documents are otherwise revealed lazily, so a document that
  navigates before its first rendering update still has an outbound view transition.
- The cross-origin redirects of a navigation are not tracked: a same-origin navigation through a cross-origin redirect
  has a transition and an activation.
- The view transition names are not tree-scoped: a name declared in a shadow tree is a name of the document, and the
  `::part()` rules do not style the pseudo-elements.
- The images are 8-bit premultiplied bitmaps: a translucent snapshot can differ by 2 levels from the element.
- An inline box fragmented on several lines is captured as its first fragment.
