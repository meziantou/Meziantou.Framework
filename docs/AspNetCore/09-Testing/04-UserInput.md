---
title: User input
description: Click, type, tap, swipe, scroll and drag the elements of a component like a user.
---

The actions send trusted input events to the page, with the default actions of a browser: the focus moves, the text is
typed in the fields, the forms are submitted, the page scrolls. Before an action, the element must be actionable, as in
Playwright: in the document, visible, stable, enabled, and the element under the pointer. An element covered by
another one (a dialog, an overlay) does not get the click, and the action fails with an
[ElementNotInteractableException](cref:Meziantou.AspNetCore.Testing.ElementNotInteractableException) that
names the element that covers it. See [Locators and assertions](03-Locators.md#act-on-the-elements) for the checks of
each action.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Forms;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var form = await host.RenderAsync<PersonForm>();

await form.GetByRole("button", "Save").ClickAsync();
Assert.False(form.Instance.Submitted);
Assert.Equal("The Name field is required.", form.Find(".validation-message").TextContent);

await form.GetByLabel("Name").FillAsync("Ada");
await form.GetByLabel("Subscribe").CheckAsync();
await form.GetByLabel("Country").SelectOptionAsync("fr");
await form.GetByRole("button", "Save").ClickAsync();

Assert.True(form.Instance.Submitted);
Assert.Equal("Ada", form.Instance.Person.Name);
Assert.True(form.Instance.Person.Subscribe);
Assert.Equal("fr", form.Instance.Person.Country);
// The snapshot shows the state of the field: Blazor sets the value as a property, not as an attribute
Assert.Equal("""<input id="name" name="Person.Name" class="modified valid" value="Ada">""", form.GetByLabel("Name").GetMarkup());
```
<!-- endref -->

| Method | Input |
| --- | --- |
| `ClickAsync`, `DoubleClickAsync`, `RightClickAsync` | The mouse moves to the center of the element (or to `ClickOptions.Position`), then presses and releases a button, with `ClickOptions.Modifiers` |
| `HoverAsync` | The mouse moves over the element |
| `TapAsync`, `SwipeAsync` | A finger: the pointer, touch and compatibility mouse events |
| `WheelAsync` | The mouse wheel: the `wheel` event, then the scrolling |
| `DragAndDropAsync` | The HTML drag and drop of an element on another one |
| `FocusAsync` | Focuses the element |
| `TypeAsync`, `PressAsync` | Focuses the element, then the keys of a US keyboard |
| `FillAsync`, `ClearAsync` | Replaces the text of a field, then leaves it (the `change` event of `@bind`). The keyboard cannot type in a `date`, `time`, `datetime-local`, `month`, `week`, `color` or `range` input: their value is set, then `input` and `change` fire, as in Playwright |
| `CheckAsync`, `UncheckAsync`, `SetCheckedAsync` | Clicks a check box or a radio button when needed |
| `SelectOptionAsync` | Selects options of a `select` element by value or by label, then fires `input` and `change` |

Each action waits until the components are idle, so the result of an asynchronous event handler is there when it
returns. To check the page while the handler is still running (a loading indicator), click with
[ClickOptions.NoWaitAfter](cref:Meziantou.AspNetCore.Testing.ClickOptions.NoWaitAfter), then wait for the result with
an assertion of `Expect`.

The methods are on the [locators](cref:Meziantou.AspNetCore.Testing.Locator), which find their element again before
each attempt, on [PageInput](cref:Meziantou.AspNetCore.Testing.PageInput) (`host.Input`), and on the elements, as
extension methods of [ElementExtensions](cref:Meziantou.AspNetCore.Testing.ElementExtensions).
`host.Input.Raw` is the low-level input of WebPlatform: positions in CSS pixels, pointer down and up, keys down and up.

The handlers get the event arguments of Blazor (`MouseEventArgs`, `PointerEventArgs`, `KeyboardEventArgs`,
`TouchEventArgs`, `ChangeEventArgs`...) built from the DOM events, with their positions and their modifiers.
[CustomEventArgs](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.CustomEventArgs) creates the
arguments of the custom event types.
