---
title: Locators and assertions
description: Find the elements as a user does, act on them once they are actionable, and assert their state until it is the expected one.
---

A [Locator](cref:Meziantou.AspNetCore.Testing.Locator) works like a Playwright locator. It is a query that runs again
each time you use it, so it still finds the element after a component renders it again. The actions wait until the
element can receive the input, and the assertions of
[Expectations.Expect](cref:Meziantou.AspNetCore.Testing.Expectations.Expect) retry until they pass. Together they
remove the waits and the timing issues from the tests.

## Find elements

Create locators from a page, from a rendered component (it searches only the elements of the component), or from
another locator (it searches the descendants of its elements):

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/LocatorTests.cs;region=Locator_Queries;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var page = await host.RenderAsync(builder => builder.AddMarkupContent(0, """
    <h1>Users</h1>
    <label for="search">Search users</label>
    <input id="search" placeholder="Name or email">
    <label>Subscribe <input type="checkbox" checked></label>
    <button aria-pressed="true">Bold</button>
    <img alt="Portrait of Ada">
    <p data-testid="user">Ada Lovelace <button>Delete</button></p>
    """));

// A text matches a substring, ignoring the case, unless it is exact
Assert.Equal("Users", page.GetByRole("heading", "users").TextContent);
Assert.Equal(0, page.GetByRole("heading", "users", new() { Exact = true }).Count);

// The states of the accessibility tree
Assert.Equal("Users", page.GetByRole("heading", options: new() { Level = 1 }).TextContent);
Assert.Equal("Bold", page.GetByRole("button", options: new() { Pressed = true }).TextContent);
Assert.True(page.GetByLabel("subscribe").IsChecked);

// The labels, the attributes and the texts
Assert.Equal("search", page.GetByLabel("Search users").GetAttribute("id"));
Assert.Equal("search", page.GetByPlaceholder("email").GetAttribute("id"));
Assert.Equal("img", page.GetByAltText("Portrait").GetElement().LocalName);
Assert.Equal("p", page.GetByText("Ada Lovelace").GetElement().LocalName);
await page.GetByTestId("user").GetByRole("button", "Delete").ClickAsync();
```
<!-- endref -->

| Method | Finds the elements |
| --- | --- |
| [GetByRole](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByRole) | With an ARIA role, an accessible name and the states of [GetByRoleOptions](cref:Meziantou.AspNetCore.Testing.GetByRoleOptions) (`Checked`, `Disabled`, `Expanded`, `Pressed`, `Selected`, `Level`) |
| [GetByText](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByText) | The smallest elements that contain a text: an element is found when none of its children contains the text |
| [GetByLabel](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByLabel) | Labelled by `aria-labelledby`, `aria-label` or a `label` element |
| [GetByPlaceholder](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByPlaceholder), [GetByAltText](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByAltText), [GetByTitle](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByTitle) | With a `placeholder`, `alt` or `title` attribute |
| [GetByTestId](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByTestId) | With a test identifier: `data-testid`, or the attribute of [TestPageSettings.TestIdAttribute](cref:Meziantou.AspNetCore.Testing.TestPageSettings.TestIdAttribute) |
| [Locator](cref:Meziantou.AspNetCore.Testing.LocatorScope.Locator) | That match a CSS selector |

Prefer the queries that a user sees, the role and the text, to the CSS selectors: they don't break when the markup
changes, and they check that the page is accessible.

- A text matches a substring, ignoring the case and collapsing the white space. Pass `exact: true` (or
  [GetByRoleOptions.Exact](cref:Meziantou.AspNetCore.Testing.GetByRoleOptions.Exact)) to match the whole text, with the
  case, or a `Regex`. The test identifier is always compared as is.
- [GetByRole](cref:Meziantou.AspNetCore.Testing.LocatorScope.GetByRole) ignores the elements hidden from the
  accessibility tree (`aria-hidden="true"`, `visibility: hidden`), unless
  [IncludeHidden](cref:Meziantou.AspNetCore.Testing.GetByRoleOptions.IncludeHidden) is true. The role of an element that
  is not rendered (`display: none`) is `none`: find it with a CSS selector. The other queries find the hidden elements
  too.
- The queries search the open shadow roots of the page, so the internals of web components can be found.

## Narrow, filter and combine

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/LocatorTests.cs;region=Locator_Filters;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var page = await host.RenderAsync(builder => builder.AddMarkupContent(0, """
    <ul>
      <li>Ada Lovelace <button>Delete</button></li>
      <li>Grace Hopper <button>Delete</button></li>
      <li>Alan Turing</li>
    </ul>
    """));
var users = page.GetByRole("listitem");
Assert.Equal(3, users.Count);

// Search inside the elements of a locator, filter them by text or by descendant, or pick one
await users.Filter(new() { HasText = "grace" }).GetByRole("button", "Delete").ClickAsync();
Assert.Equal(2, users.Filter(new() { Has = page.GetByRole("button", "Delete") }).Count);
Assert.Equal("Alan Turing", users.Filter(new() { HasNot = page.GetByRole("button") }).TextContent);
Assert.Equal("Alan Turing", users.Last.TextContent);
Assert.Equal("Ada Lovelace Delete", users.First.TextContent);

// Combine locators
Assert.Equal(2, page.GetByRole("button").And(page.GetByText("Delete")).Count);
Assert.Equal(5, users.Or(page.GetByRole("button")).Count);
```
<!-- endref -->

- [Filter](cref:Meziantou.AspNetCore.Testing.Locator.Filter) keeps the elements that contain a text (`HasText`,
  `HasNotText`), that contain an element of another locator (`Has`, `HasNot`, evaluated from each element), or that are
  visible (`Visible`).
- [First](cref:Meziantou.AspNetCore.Testing.Locator.First), [Last](cref:Meziantou.AspNetCore.Testing.Locator.Last) and
  [Nth](cref:Meziantou.AspNetCore.Testing.Locator.Nth) pick one element; a negative index counts from the end.
- [And](cref:Meziantou.AspNetCore.Testing.Locator.And) keeps the elements that both locators find, and
  [Or](cref:Meziantou.AspNetCore.Testing.Locator.Or) the elements that either finds, in document order.
- [GetAll](cref:Meziantou.AspNetCore.Testing.Locator.GetAll) returns a locator per element, to iterate the rows of a list.

## Act on the elements

An action finds the element again before each attempt, and waits until it is actionable:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/LocatorTests.cs;region=Locator_AutoWait;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();

// Render without waiting: the controls are disabled, or not rendered, for a while
var form = host.Render<DelayedForm>();

// The click waits until the button is enabled, the fill until the field is rendered,
// and the selection until the select element has the option (by value or by label)
await form.GetByRole("button", "Save").ClickAsync();
await form.GetByLabel("Name").FillAsync("Ada");
await form.GetByLabel("Color").SelectOptionAsync("Red");

await Expect(form.GetByRole("status")).ToHaveTextAsync("Saved 1 times");
await Expect(form.Locator("#name")).ToHaveTextAsync("Ada");
await Expect(form.Locator("#color")).ToHaveTextAsync("r");
```
<!-- endref -->

| Check | Actions |
| --- | --- |
| The locator finds exactly one element | All |
| Visible: a non-empty box, not `visibility: hidden` | Click, double click, hover, tap, fill, clear, select an option, check, uncheck, drag |
| Stable: its box is the same in consecutive animation frames | Click, double click, hover, tap, check, uncheck, drag |
| Receives the input: no other element covers it (hit testing) | Click, double click, hover, tap, check, uncheck, drag |
| Enabled: not a disabled form control, no `aria-disabled="true"` | Click, double click, tap, fill, clear, select an option, check, uncheck |
| Editable: not `readonly` | Fill, clear |

- An element out of the viewport is scrolled into it first.
- [FillAsync](cref:Meziantou.AspNetCore.Testing.Locator.FillAsync), [CheckAsync](cref:Meziantou.AspNetCore.Testing.Locator.CheckAsync)
  and [SelectOptionAsync](cref:Meziantou.AspNetCore.Testing.Locator.SelectOptionAsync) act on the control of a `label`
  when the element is inside the label: `page.GetByText("Accept the terms").CheckAsync()` checks the check box of the
  label.
- [SelectOptionAsync](cref:Meziantou.AspNetCore.Testing.Locator.SelectOptionAsync) selects the options by value or by
  label, and waits until the `select` element has them.
- [CheckAsync](cref:Meziantou.AspNetCore.Testing.Locator.CheckAsync) clicks only when the element is not checked yet,
  then checks that the click checked it.

The action fails after [DefaultTimeout](cref:Meziantou.AspNetCore.Testing.TestPage.DefaultTimeout) (10 seconds): with an
[ElementNotFoundException](cref:Meziantou.AspNetCore.Testing.ElementNotFoundException) when the locator finds no
element, and with an [ElementNotInteractableException](cref:Meziantou.AspNetCore.Testing.ElementNotInteractableException)
that gives the reason (`it is disabled`, `<div id="overlay"> receives the input`...) otherwise. Set
[ClickOptions.Timeout](cref:Meziantou.AspNetCore.Testing.ClickOptions.Timeout) for one click, or
[ClickOptions.Force](cref:Meziantou.AspNetCore.Testing.ClickOptions.Force) to skip the checks.

A locator that finds more than one element fails at once with a
[StrictModeViolationException](cref:Meziantou.AspNetCore.Testing.StrictModeViolationException), which lists the elements:
make the locator more specific, or pick one with `First`, `Last` or `Nth`.

## Assert the state of the page

Import the assertions with `using static Meziantou.AspNetCore.Testing.Expectations;`. Each assertion runs the event
loop of the page until it passes, and fails with an
[ExpectationFailedException](cref:Meziantou.AspNetCore.Testing.ExpectationFailedException) after the timeout:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/LocatorTests.cs;region=Locator_Assertions;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var counter = await host.RenderAsync<Counter>();
await counter.GetByRole("button", "Click me").ClickAsync();

// The assertions run the event loop until they pass, and fail after the timeout of the page
await Expect(counter.GetByRole("status")).ToHaveTextAsync("Current count: 1");
await Expect(counter.GetByRole("button", "Click me")).ToBeEnabledAsync();
await Expect(counter.GetByRole("button")).ToHaveCountAsync(1);
await Expect(counter.GetByText("Current count: 2")).Not.ToBeVisibleAsync();
```
<!-- endref -->

| Assertions of [LocatorAssertions](cref:Meziantou.AspNetCore.Testing.LocatorAssertions) | Pass when |
| --- | --- |
| `ToBeAttachedAsync`, `ToBeVisibleAsync`, `ToBeHiddenAsync` | The locator finds an element, a visible element, no element or a hidden element |
| `ToBeEnabledAsync`, `ToBeDisabledAsync`, `ToBeCheckedAsync`, `ToBeEditableAsync`, `ToBeFocusedAsync`, `ToBeEmptyAsync` | The element has the state |
| `ToHaveTextAsync`, `ToContainTextAsync` | The text of the element is, or contains, a text (with collapsed white space) or matches a `Regex` |
| `ToHaveValueAsync`, `ToHaveAttributeAsync`, `ToHaveClassAsync`, `ToHaveCssAsync` | A field, an attribute, the classes or a computed style have a value |
| `ToHaveRoleAsync`, `ToHaveAccessibleNameAsync`, `ToHaveAccessibilitySnapshotAsync` | The accessibility tree of the element is the expected one |
| `ToHaveCountAsync` | The locator finds a number of elements |

`Not` negates an assertion: `Expect(locator).Not.ToBeVisibleAsync()`. The assertions on an element fail, negated or
not, while the locator finds no element. [PageAssertions](cref:Meziantou.AspNetCore.Testing.PageAssertions) checks the
URL and the title of a page: `await Expect(page).ToHaveUrlAsync("/profile")`.

## Read the elements now

A locator also reads the page without waiting:
[GetElement](cref:Meziantou.AspNetCore.Testing.Locator.GetElement) returns its element (the DOM element of WebPlatform),
[GetElements](cref:Meziantou.AspNetCore.Testing.Locator.GetElements) and
[Count](cref:Meziantou.AspNetCore.Testing.Locator.Count) its elements, and
[TextContent](cref:Meziantou.AspNetCore.Testing.Locator.TextContent),
[InputValue](cref:Meziantou.AspNetCore.Testing.Locator.InputValue),
[IsVisible](cref:Meziantou.AspNetCore.Testing.Locator.IsVisible) or
[IsChecked](cref:Meziantou.AspNetCore.Testing.Locator.IsChecked) the state of its element. `Find`, `FindAll` and
`FindOrDefault` of the pages and of the components return the elements that match a CSS selector now. Since every
action waits until the page is idle, these reads are reliable after an action. Use the assertions for a result that
comes later (a timer, a message from the server).
