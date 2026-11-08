---
title: Accessibility
description: Read the role, the accessible name and the states of the elements, and capture the accessibility tree, to check generated HTML.
---

[Element.ComputedRole](cref:Meziantou.Framework.WebPlatform.Dom.Element.ComputedRole) and [Element.ComputedName](cref:Meziantou.Framework.WebPlatform.Dom.Element.ComputedName) return the role and the accessible name that the accessibility tree of a
browser exposes for an element: the values that the WebDriver commands *Get Computed Role* and *Get Computed Label*
return. Use them to check that generated HTML is accessible:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/AccessibilityTests.cs;region=WebPlatform_Accessibility;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <label>Email <input type="email" id="email"></label>
    <button id="close" aria-label="Close"><svg></svg></button>
    <img id="logo" src="logo.png">
    """);

// The role and the accessible name that an accessibility tree exposes, as WebDriver reports them
var email = document.GetElementById("email")!;
Assert.Equal(("textbox", "Email"), (email.ComputedRole, email.ComputedName));

var close = document.GetElementById("close")!;
Assert.Equal(("button", "Close"), (close.ComputedRole, close.ComputedName));

// An image without alt attribute has no name: a check of generated HTML can report it
var logo = document.GetElementById("logo")!;
Assert.Equal(("image", ""), (logo.ComputedRole, logo.ComputedName));
```
<!-- endref -->

## Role

The role is the first valid role of the `role` attribute (`img` is reported as `image`, `presentation` as `none`), else
the role given by [ElementInternals](cref:Meziantou.Framework.WebPlatform.Dom.ElementInternals) for a custom element, else the implicit role of the element: `heading`, `link` for
an `a` with an `href`, `listitem`, `textbox`, `combobox`, `generic` for a `div` or a `span`... An empty string means
that the element has no role (`head`, `br`).

An element that is not rendered (`display: none` on it or on an ancestor) is not in the accessibility tree: its role is
`none`. The elements hidden by `visibility: hidden` or `aria-hidden="true"` keep their role.

## Accessible name

The name comes, in order, from `aria-labelledby`, `aria-label`, the native text alternatives (the associated `label`,
`alt`, the `value` of a button, `placeholder`, `caption`, `legend`, `figcaption`, the SVG `title`), the content of the
element for the roles that take their name from it (buttons, links, headings...), then `title`. The whitespace is
collapsed and trimmed. The hidden nodes are not part of a name, unless `aria-labelledby` references them.

## Properties and states

[Element.GetAccessibilityProperties](cref:Meziantou.Framework.WebPlatform.Dom.Element.GetAccessibilityProperties) returns everything the accessibility tree exposes for an element, computed when you call it:

- `Role` and `Name`: the values of `ComputedRole` and `ComputedName`.
- `Description`: the text of the elements referenced by `aria-describedby`, else `aria-description`, else the `title`
  attribute when it is not the name.
- `Checked` and `Pressed`: `True`, `False` or `Mixed`, or `null` when the role has no such state. A checkbox without
  `aria-checked` is not checked; an indeterminate checkbox is `Mixed`. A button is a toggle button only when it has
  `aria-pressed`.
- `Expanded` and `Selected`: `aria-expanded` and `aria-selected`, or the selectedness of an `option`.
- `Disabled`: a disabled form control (including by a disabled `fieldset`, `optgroup` or `select`), or
  `aria-disabled="true"` on the element or an ancestor.
- `Level`: the level of a heading, a list item, a row or a tree item. For `h1` to `h6`, it is the computed heading
  level, which adds the `headingoffset` attributes of the ancestors up to a `headingreset`: an `h1` in
  `<section headingoffset="1">` has level 2. `aria-level` takes precedence, and an element with `role="heading"` has
  level 2 by default.
- `IsHidden`: whether the element is excluded from the tree (`display: none`, `visibility: hidden`, or
  `aria-hidden="true"` on it or an ancestor).

## Accessibility tree

[Element.CaptureAccessibilityTree](cref:Meziantou.Framework.WebPlatform.Dom.Element.CaptureAccessibilityTree) captures the tree rooted at an element. The result is a snapshot: change the
document, and capture the tree again to see the change. Each [AccessibilityNode](cref:Meziantou.Framework.WebPlatform.Dom.AccessibilityNode) has its DOM `Node`, its
`Properties`, its `Parent` and its `Children`. [ToAriaSnapshot](cref:Meziantou.Framework.WebPlatform.Dom.AccessibilityNode.ToAriaSnapshot) writes the tree in the YAML format of
the [aria snapshots of Playwright](https://playwright.dev/docs/aria-snapshots), so you can compare it with an expected
text:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/AccessibilityTests.cs;region=WebPlatform_AccessibilityTree;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => """
    <main>
      <section headingoffset="1">
        <h1>Sign in</h1>
        <label>Email <input type="email" placeholder="you@example.com"></label>
        <label><input type="checkbox" checked> Remember me</label>
        <button disabled>Continue</button>
      </section>
    </main>
    """);

// The properties of one element
var button = document.QuerySelector("button")!.GetAccessibilityProperties();
Assert.Equal(("button", "Continue", true), (button.Role, button.Name, button.Disabled));

// The level of a heading takes headingoffset into account
var heading = document.QuerySelector("h1")!.GetAccessibilityProperties();
Assert.Equal(2, heading.Level);

// A snapshot of the tree, in the format of the aria snapshots of Playwright
var tree = document.Body!.CaptureAccessibilityTree()!;
Assert.Equal("""
    - main:
      - heading "Sign in" [level=2]
      - text: Email
      - textbox "Email":
        - /placeholder: you@example.com
      - checkbox "Remember me" [checked]
      - text: Remember me
      - button "Continue" [disabled]
    """, tree.ToAriaSnapshot());
```
<!-- endref -->

The tree is built as Playwright builds its snapshots:

- A hidden element is excluded with its whole subtree. `CaptureAccessibilityTree` returns `null` for a hidden element.
- The elements without a role (`label`, `br`...), the presentational elements and the generic elements (`div`, `span`...)
  are not nodes: their content belongs to their parent.
- The adjacent texts are merged into one `text` node, with the white space collapsed. A text that repeats the name of
  its parent is dropped (the text of a button named by its content). A text field shows its value.
- The children follow the flat tree (the shadow trees and the slotted nodes), with the relocations of `aria-owns`.

The snapshot uses the names and the states of `GetAccessibilityProperties`, so it differs from the snapshot of
Playwright where the specifications and Playwright disagree:

- The rows and the cells of a table are not named by their content (Playwright writes `row "Cell 1 Cell 2"`).
- An `article`, a `dialog` and an `alertdialog` are named by their first heading.
- A disabled `fieldset` is disabled itself, not only its form controls.
- The level of a heading takes `headingoffset` into account.

## With and without a browsing context

The computation uses the computed styles: the generated content of `::before` and `::after`, `display: none` and
`visibility: hidden`. Load the document in a browsing context, as above, to get the names a browser gives. In a document
parsed by [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser), only the `hidden` and `aria-hidden` attributes hide the nodes, and there is no generated
content.
