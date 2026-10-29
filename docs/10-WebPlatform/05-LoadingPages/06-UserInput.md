---
title: User input
description: Move the mouse, click, tap, scroll and type in a page as a user does, with the trusted events and the default actions of a browser.
---

There is no window and no user: your application plays the user with `BrowsingContext.Input`. The library fires the
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
| `Input.CreatePointer("touch")`, `CreatePointer("pen")` | A finger or a pen, with the same methods; a moving touch pans the page |
| `Input.Wheel(x, y, deltaX, deltaY)` | A turn of the wheel at a position: the `wheel` event, then the scroll |
| `Input.Modifiers` | The modifier keys (`InputModifiers.Shift`, `Control`...) of the next events |
| `Input.HitTest(x, y)` | The element at a position, without firing anything |

To click an element, click the center of its `GetBoundingClientRect()`. `Input.MapToTopLevel(document, x, y)` converts a
position of the viewport of an `iframe` to the top-level viewport.

## Keyboard

`Input.Keyboard` sends the keys to the focused element, with the key values of the browsers (a character, or the name of
a key such as `Enter`, `Tab`, `Backspace`, `ArrowLeft`, `Shift`) and the layout of a US keyboard:

| Method | Effect |
| --- | --- |
| `Type(text)` | Presses and releases the key of each character |
| `Press(key)` | Presses and releases a key |
| `Down(key)`, `Up(key)` | Presses or releases a key: hold `Shift` or `Control` while pressing other keys |
| `InsertText(text)` | Inserts a text as an input method does, without keyboard events |

The default actions run: the text is inserted in the text fields and the editable content, Tab moves the focus, Enter
and Space activate the focused element (Enter submits the form of a text field), the arrow keys move the caret, and
Escape closes the dialogs and the popovers.

## Without input

`HtmlElement.Click()`, `Focus()` and `DispatchEvent()` act on an element directly, as the same methods of a script: the
events they fire are not trusted, and no hit testing is done. Use them when you do not need to simulate a user.

To automate pages with a higher-level API (locators, auto-waiting, assertions), drive the engine with Playwright: see
[Playwright](../08-Playwright/01-Overview.md).
