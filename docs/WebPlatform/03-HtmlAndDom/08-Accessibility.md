---
title: Accessibility
description: Read the role and the accessible name of the elements, as an accessibility tree exposes them, to check generated HTML.
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

## With and without a browsing context

The computation uses the computed styles: the generated content of `::before` and `::after`, `display: none` and
`visibility: hidden`. Load the document in a browsing context, as above, to get the names a browser gives. In a document
parsed by [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser), only the `hidden` and `aria-hidden` attributes hide the nodes, and there is no generated
content.
