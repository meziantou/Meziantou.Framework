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
| [Input.Pinch(x, y, scale)](cref:Meziantou.Framework.WebPlatform.Dom.UserInput.Pinch) | A pinch of the touchpad at a position: a `wheel` event with `ctrlKey`, then the zoom |
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

[Input.Pinch(x, y, scale)](cref:Meziantou.Framework.WebPlatform.Dom.UserInput.Pinch) zooms the page as a touchpad pinch does. Two touch pointers that move apart or closer do the same,
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

For a pinch of the touch screen, create a touch pointer for each finger and move them. Each finger in contact is a
pointer with its own `pointerId` (the first one is the primary pointer), and the `touches` of the touch events are the
fingers in contact. When the fingers start to pinch, their pointers get `pointercancel`; the `scrollend` event of the
visual viewport is fired when the last finger is released.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/UserInputTests.cs;region=WebPlatform_TwoFingers;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>body { margin: 0 }</style>
    <p>Hello world</p>
    """);

var touchCounts = new List<int>();
document.AddEventListener("touchstart", e => touchCounts.Add(((TouchEvent)e).Touches.Count));

// Each touch pointer is a finger: Down() starts its contact at its position
var input = context.Input;
var finger1 = input.CreatePointer("touch");
var finger2 = input.CreatePointer("touch");
finger1.Move(300, 300);
finger1.Down();
finger2.Move(500, 300);
finger2.Down();

// The touches of a touch event are the fingers in contact
Assert.Equal([1, 2], touchCounts);

// The fingers move apart: the distance between them doubles, so the visual viewport is zoomed 2x, and the point
// under their middle (400, 300) moves under their new middle (500, 300)
finger2.Move(700, 300);
var visualViewport = document.DefaultView!.VisualViewport!;
Assert.Equal(2, visualViewport.Scale);
Assert.Equal((150, 150), (visualViewport.OffsetLeft, visualViewport.OffsetTop));

finger2.Up();
finger1.Up();
```
<!-- endref -->

The zoom is between 1 and 5. The `maximum-scale` of the viewport `meta` element lowers this limit, and a page with
`user-scalable=no` cannot be zoomed. On a mobile device ([ViewportOptions.IsMobile](cref:Meziantou.Framework.WebPlatform.Dom.ViewportOptions.IsMobile)), the pinch zoom multiplies the scale of the page.
While the page is zoomed in, the wheel, the keys, a touch pan and `scrollIntoView()` scroll the visual viewport to the edge
of the layout viewport first, then the layout viewport. `window.scrollTo()` and the other scroll methods of the window
only scroll the layout viewport.

The positions of the input stay in the layout viewport, so the coordinates of `GetBoundingClientRect()` can still be
clicked. To click a position of a screenshot taken while the page is zoomed in, convert it with
[Input.MapFromVisualViewport(x, y)](cref:Meziantou.Framework.WebPlatform.Dom.UserInput.MapFromVisualViewport).

## Drag and drop

A move of the mouse with the primary button pressed on a draggable element (`draggable="true"`, a link, an image) or on
the selection drags it, as in a browser: `dragstart` is fired at the element, then each move fires `drag`, `dragenter`,
`dragleave` and `dragover`, and the release fires `drop` (when the `dragover` event of the target was canceled) and
`dragend`. The pointer and mouse events are suppressed during the drag, and Escape cancels it. The events are trusted,
the `DataTransfer` object follows the modes of the specification (the data set in `dragstart` can only be read in
`drop`), and a text dropped in a text field or in editable content is inserted.

To drop files and folders from outside the page, as when the user drags them from the file manager, describe them with
a [DragData](cref:Meziantou.Framework.WebPlatform.Dom.DragData), then start the drag with
[Mouse.BeginDrag(data)](cref:Meziantou.Framework.WebPlatform.Dom.PointerInput.BeginDrag) or drop them at once with
[Input.Drop(x, y, data)](cref:Meziantou.Framework.WebPlatform.Dom.UserInput.Drop):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/DragAndDropTests.cs;region=WebPlatform_DropFiles;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>body { margin: 0 } #zone { width: 200px; height: 200px }</style>
    <div id="zone"></div>
    """);

var dropped = new List<string>();
var zone = document.GetElementById("zone")!;
zone.AddEventListener("dragover", e => e.PreventDefault()); // The zone accepts the drop
zone.AddEventListener("drop", e =>
{
    e.PreventDefault();
    foreach (var file in ((DragEvent)e).DataTransfer!.Files)
    {
        dropped.Add(file.Name);
    }
});

// The files and the folders that the user drags from the file manager (or DragData.AddFromDisk(path))
var data = new DragData();
data.AddFile("notes.txt", "hello"u8.ToArray());
data.AddDirectory("photos").AddFile("cat.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });

// The mouse enters the window with the button pressed, moves over the zone, and releases the button
var mouse = context.Input.Mouse;
mouse.Move(300, 300);
mouse.BeginDrag(data);
mouse.Move(100, 100);
mouse.Up();
Assert.Equal(["notes.txt", "photos"], dropped);

// The same drop in one call
context.Input.Drop(100, 100, data);
Assert.Equal(["notes.txt", "photos", "notes.txt", "photos"], dropped);
```
<!-- endref -->

| Member | Effect |
| --- | --- |
| `AddFile(name, content, type, lastModified)` | A file: its type is the type of its extension when you give none |
| `AddDirectory(name)` | A folder: add its files and subfolders to the [DragDataDirectory](cref:Meziantou.Framework.WebPlatform.Dom.DragDataDirectory) it returns |
| `AddFromDisk(path)` | A file or a folder of the disk, with its content (read when the method is called) |
| `SetString(type, data)` | A string, such as the `text/plain` of a text dragged from another application |
| `EffectAllowed` | The operations that the source allows (`all` by default) |

A drag from outside the page fires `dragenter`, `dragover`, `dragleave` and `drop`, but no `dragstart`, `drag` or
`dragend`. As in a browser, the page reads the files with `dataTransfer.files` and `items`, and the folders with the
File and Directory Entries API (`item.webkitGetAsEntry()`, `createReader().readEntries()`, `getFile()`,
`getDirectory()`) or with `item.getAsFileSystemHandle()`, which returns a handle to a copy of the dropped files: the page
can change the copy, never your files. A drop on a file input selects the files (all of them with `multiple`, the files
of the folders with `webkitdirectory`, with their `webkitRelativePath`) and fires `input` and `change`. A drop that the
page does not accept does nothing: a browser would open the file.

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
