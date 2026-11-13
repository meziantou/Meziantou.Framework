---
title: User input
description: Move the mouse, click, tap, scroll and type in a page as a user does, with the trusted events and the default actions of a browser.
---

There is no window and no user: your application plays the user with [BrowsingContext.Input](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Input). The library fires the
trusted events that a browser fires (pointer, mouse, touch, wheel, keyboard, input) and runs their default actions: the
focus, the clicks, the text typed in the fields, the form submissions, the scrolling, the drag and drop.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/UserInputTests.cs;region=WebPlatform_UserInput;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>body { margin: 0 } button { display: block; width: 100px; height: 40px }</style>
    <button>Save</button>
    <input id="name">
    """);

var clicks = 0;
document.QuerySelector("button")!.AddEventListener("click", _ => clicks++);

// The positions are in CSS pixels, in the viewport: the element at the position gets the events
var input = context.Input;
input.Mouse.Move(50, 20);
input.Mouse.Down();
input.Mouse.Up();
Assert.Equal(1, clicks);

// The keys go to the focused element
var name = (HtmlInputElement)document.GetElementById("name")!;
name.Focus();
input.Keyboard.Type("Ada");
input.Keyboard.Press("Backspace");
Assert.Equal("Ad", name.Value);
```
<!-- endref -->

## Pointers

The positions are in CSS pixels, in the viewport of the top-level browsing context: the element at the position (found by
the hit testing of the layout) gets the events, also in the document of an `iframe`.

| Member | Effect |
| --- | --- |
| `Input.Mouse` | The mouse: `Move(x, y)`, `Down(button)`, `Up(button)`. A press and a release make a `click` (`dblclick` for a second click, `contextmenu` for the secondary button) |
| `Input.CreatePointer("touch")`, `CreatePointer("pen")` | A finger or a pen, with the same methods; a moving touch pans the page, and two touches that move pinch it |
| `Input.Wheel(x, y, deltaX, deltaY)` | A turn of the wheel at a position: the `wheel` event, then the scroll |
| `Input.Pinch(x, y, scale)` | A pinch of the touchpad at a position: a `wheel` event with `ctrlKey`, then the zoom |
| `Input.Modifiers` | The modifier keys ([InputModifiers.Shift](cref:Meziantou.Framework.WebPlatform.Dom.InputModifiers.Shift), [Control](cref:Meziantou.Framework.WebPlatform.Dom.InputModifiers.Control)...) of the next events |
| `Input.HitTest(x, y)` | The element at a position, without firing anything |

The pen and touch events can be given to `navigator.ink` (the [Delegated Ink Trail API](https://wicg.github.io/ink-enhancement/)):
`requestPresenter()` and `updateInkTrailStartPoint()` validate their arguments as in Chromium (a trusted pointer event, a
valid CSS color, a diameter greater than 0), but the engine does not draw the predicted trail: the page draws its own
trail in its next frame.

A press of the primary button puts the caret at the pointer, and a drag selects the text from the press to the pointer
(with Shift, a press extends the selection). A double click selects a word and a triple click a paragraph. The same
works in the text fields. The `selectstart` and `selectionchange` events are fired, and nothing is selected in an element
whose `user-select` is `none`.

To click an element, click the center of its [GetBoundingClientRect()](cref:Meziantou.Framework.WebPlatform.Dom.Element.GetBoundingClientRect). `Input.MapToTopLevel(document, x, y)` converts a
position of the viewport of an `iframe` to the top-level viewport.

## Pinch zoom

`Input.Pinch(x, y, scale)` zooms the page as a touchpad pinch does. Two touch pointers that move apart or closer do the same,
unless `touch-action` prevents it (for instance, `pan-x pan-y`). As in a browser, the pinch zoom does not change the
layout. It magnifies the visual viewport of the top-level document: `visualViewport.scale` grows, its size shrinks, and
its `offsetLeft` and `offsetTop` move it in the layout viewport. Its `resize`, `scroll` and `scrollend` events are fired,
and the screenshots of the viewport show the magnified area.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/UserInputTests.cs;region=WebPlatform_SelectAndPinchZoom;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>body { margin: 0 } p { margin: 0; font-size: 20px }</style>
    <p>Hello world</p>
    """);

// A double click selects the word under the pointer (a drag selects from the press to the pointer)
var input = context.Input;
input.Mouse.Move(5, 10);
input.Mouse.Down();
input.Mouse.Up();
input.Mouse.Down();
input.Mouse.Up();
Assert.Equal("Hello", document.GetSelection()!.ToString());

// A pinch of the touchpad zooms the visual viewport 2x around the center of the 800x600 viewport: the screenshots
// show the area (200, 150)-(600, 450), magnified
input.Pinch(400, 300, 2);
var visualViewport = document.DefaultView!.VisualViewport!;
Assert.Equal(2, visualViewport.Scale);
Assert.Equal((200, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));

// The positions of the input stay in the layout viewport: map a position of a screenshot to click it
Assert.Equal((210, 155), input.MapFromVisualViewport(20, 10));
```
<!-- endref -->

The zoom is between 1 and 5. The `maximum-scale` of the viewport `meta` element lowers this limit, and a page with
`user-scalable=no` cannot be zoomed. On a mobile device ([ViewportOptions.IsMobile](cref:Meziantou.Framework.WebPlatform.Dom.ViewportOptions.IsMobile)), the pinch zoom multiplies the scale of the page.
While the page is zoomed in, the wheel, the keys, a touch pan and `scrollIntoView()` scroll the visual viewport to the edge
of the layout viewport first, then the layout viewport. `window.scrollTo()` and the other scroll methods of the window
only scroll the layout viewport.

The positions of the input stay in the layout viewport, so the coordinates of `GetBoundingClientRect()` can still be
clicked. To click a position of a screenshot taken while the page is zoomed in, convert it with
`Input.MapFromVisualViewport(x, y)`.

## Keyboard

`Input.Keyboard` sends the keys to the focused element, with the key values of the browsers (a character, or the name of
a key such as `Enter`, `Tab`, `Backspace`, `ArrowLeft`, `Shift`) and the layout of a US keyboard:

| Method | Effect |
| --- | --- |
| `Type(text)` | Presses and releases the key of each character |
| `Press(key)` | Presses and releases a key |
| `Down(key)`, `Up(key)` | Presses or releases a key: hold `Shift` or [Control](cref:Meziantou.Framework.WebPlatform.Dom.InputModifiers.Control) while pressing other keys |
| `InsertText(text)` | Inserts a text as an input method does, without keyboard events |

The default actions run: the text is inserted in the text fields and the editable content, Tab moves the focus, Enter
and Space activate the focused element (Enter submits the form of a text field), the arrow keys move the caret, and
Escape closes the dialogs and the popovers. Outside of the editable content, the arrow keys, `PageUp`, `PageDown`, Space,
`Home` and `End` scroll the focused scroll container (or the viewport). As in the browsers, a scroll container that the
user can scroll and that has no focusable descendant can be focused by Tab and by `focus()` (not by a click).

Tab follows the `reading-flow` property of the flex, grid and grid lanes containers: the visual order of the items
instead of the order of the document. A grid lanes container (`display: grid-lanes`) accepts `source-order`,
`grid-lanes-order` (the `order` property, then the order of the document), `grid-lanes-track` (one track after the
other, each from its start edge) and `grid-lanes-flow` (across the tracks, row by row of items). The way
`grid-lanes-flow` groups the items into rows is not specified yet: the library groups them as Chromium does.

## Pending input

A long script can call `navigator.scheduling.isInputPending()` to yield when the user is waiting. The library dispatches
the input you send at once, so nothing is pending by default. When your application queues input before sending it (for
instance from a UI thread while a script runs), answer the question with
[BrowsingContextOptions.PendingInput](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.PendingInput):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_PendingInput;format=md-fenced-code-block;dedent=true -->
```csharp
var pendingInput = new Queue<string>(["pointermove", "click"]);
var options = new BrowsingContextOptions
{
    PendingInput = new PendingInputOptions
    {
        // The input events that the host has not dispatched yet; the continuous events (e.g. pointermove) are
        // only considered when includeContinuous is true
        IsInputPending = (callbackContext, includeContinuous) => pendingInput.Any(type => includeContinuous || type is not ("pointermove" or "mousemove" or "wheel" or "touchmove")),
    },
};
```
<!-- endref -->

The callback runs while the script runs, on the thread of the event loop: return quickly. Return false for the input of
a cross-origin frame, and when you are not sure the input targets the window. The documents that are not fully active
always get false.

## Without input

[HtmlElement.Click()](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.Click), [Focus()](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.Focus) and [DispatchEvent()](cref:Meziantou.Framework.WebPlatform.Dom.EventTarget.DispatchEvent) act on an element directly, as the same methods of a script: the
events they fire are not trusted, and no hit testing is done. Use them when you do not need to simulate a user.

To automate pages with a higher-level API (locators, auto-waiting, assertions), drive the engine with Playwright over the
Chrome DevTools Protocol: see [DevTools protocol](../08-DevTools/01-Overview.md).
