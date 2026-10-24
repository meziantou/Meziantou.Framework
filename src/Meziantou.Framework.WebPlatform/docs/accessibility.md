# Accessibility

`Element.ComputedRole` and `Element.ComputedName` return the role and the accessible name that an accessibility tree
exposes for an element: the values of the WebDriver [Get Computed Role](https://w3c.github.io/webdriver/#get-computed-role)
and [Get Computed Label](https://w3c.github.io/webdriver/#get-computed-label) commands. They are .NET members (not exposed to
the scripts), useful to check the accessibility of generated HTML.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions());
var document = context.Navigate("https://example.com/", _ => """
    <label>Email <input type="email" id="email"></label>
    <button aria-label="Close"><svg></svg></button>
    """);

var input = document.GetElementById("email")!;
Console.WriteLine(input.ComputedRole); // textbox
Console.WriteLine(input.ComputedName); // Email
```

## Role

The [computed role](https://w3c.github.io/core-aam/#roleMappingComputedRole) is:

- the first valid token of the `role` attribute ([WAI-ARIA](https://w3c.github.io/aria/), DPUB-ARIA and Graphics ARIA
  roles; the abstract roles are ignored, `region` and `form` need an accessible name), normalized like the browsers
  (`img` is `image`, `presentation` is `none`, `directory` is `list`);
- otherwise the role of `ElementInternals` (the default semantics of a custom element);
- otherwise the implicit role of [HTML-AAM](https://w3c.github.io/html-aam/#html-element-role-mappings) (e.g. `heading`,
  `link` for an `a` with `href`, `banner` for a `header` that is not in sectioning content or `main` (`sectionheader` otherwise), `listitem` for an `li` in a
  list, `combobox` or `listbox` for a `select`) and [SVG-AAM](https://w3c.github.io/svg-aam/), `generic` for the elements
  without specific semantics (`div`, `span`...); an element with a `popover` attribute whose implicit role is `generic`
  or that has none maps to `group` ([the minimum role of a popover](https://w3c.github.io/html-aam/#att-popover)).

As in the browsers, an element that is not rendered because of `display: none` (on it or on an ancestor, e.g. a popover
that is not showing) is not in the accessibility tree: its computed role is `none`. The elements hidden by
`visibility: hidden` or `aria-hidden="true"` keep their role, and so do all the elements without a browsing context.

The presentational roles (`none`, `presentation`) are ignored on the focusable elements and the elements with global
ARIA attributes ([conflict resolution](https://w3c.github.io/aria/#conflict_resolution_presentation_none)), and the items of
a presentational list are presentational. The empty string means that the element has no role (e.g. `head`, `br`).

## Accessible name

The [accessible name computation](https://w3c.github.io/accname/) uses, in order: `aria-labelledby` (and the
`ariaLabelledByElements`), the value of an embedded control (in the name of another element), `aria-label`, the text
alternatives of [HTML-AAM](https://w3c.github.io/html-aam/#accessible-name-computations-by-html-element) (the associated
`label` elements, `alt`, the `value` of the buttons, `placeholder`, `caption`, `legend`, `figcaption`, the SVG `title`),
the name from content (for the roles that support it, and in the recursive traversals: the flat tree with the
relocations of `aria-owns`, the `::marker`, `::before` and `::after` content with its CSS counters, including the
reversed counters of `<ol reversed>` whose initial value is computed from the list items, `text-transform`), the
name from the first heading for the `dialog`, `alertdialog` and `article` roles (tentative), then `title`. A node is part
of a name only once (e.g. an element referenced by `aria-labelledby` and also reached as content). The white space of the
result is collapsed and trimmed.

The hidden nodes (`display: none`, `visibility: hidden`, `aria-hidden="true"`) are not part of the names, except when they
are referenced by `aria-labelledby`. An element owned with `aria-owns` is not hidden by the `aria-hidden` of its DOM
ancestors; `aria-owns` is ignored on a hidden element and when it references an element that is not rendered. Without a
browsing context, there is no computed style: only the `hidden` and `aria-hidden` attributes hide the nodes, and there are
no markers nor generated content.
