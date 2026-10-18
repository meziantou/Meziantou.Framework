# Selector object model

The DOM methods take selectors as strings (`QuerySelector`, `QuerySelectorAll`, `Matches`, `Closest`), and
`CssStyleRule.SelectorText` returns the selector of a style rule as a string. The parsed form is available as a
read-only object model in the `Meziantou.Framework.WebPlatform.Css.Selectors` namespace. Use it to inspect selectors (for
example, to list the class names a style sheet uses) or to match a selector many times without parsing it again.

```csharp
using Meziantou.Framework.WebPlatform.Css.Selectors;

var selector = SelectorList.Parse("nav > a.active, .menu :is(.item, .link)"); // DomException "SyntaxError" when invalid
var links = document.QuerySelectorAll(selector);
var isActive = selector.Matches(element);       // same as element.Matches(selector)
var specificity = selector.Selectors[0].Specificity; // (0, 1, 2)
```

`SelectorList.Parse` and `SelectorList.TryParse` parse a selector as `querySelector` does
(https://dom.spec.whatwg.org/#scope-match-a-selectors-string). Namespace prefixes are not declared, so `svg|rect` is
invalid; `*|rect` and `|rect` are valid. The parsed selectors are immutable and cached, so they can be shared between
threads.

## Getting the selectors of a style sheet

| Member | Selector |
|---|---|
| `CssStyleRule.Selectors` | The selector of the style rule (the parsed form of `SelectorText`). It uses the `@namespace` rules of the style sheet. A nested style rule contains `NestingSelector` (`&`), which can be implicit. |
| `CssScopeRule.StartSelectors`, `CssScopeRule.EndSelectors` | The `<scope-start>` and `<scope-end>` selectors of `@scope`, or null when there are none. |

## Structure

| Type | Description |
|---|---|
| `SelectorList` | A comma-separated list of `ComplexSelector` (`Selectors`). `MaxSpecificity` is the highest specificity of the list. In the [forgiving selector lists](https://drafts.csswg.org/selectors-4/#typedef-forgiving-selector-list) of `:is()` and `:where()`, the invalid selectors are not in `Selectors` (they never match), but they are serialized as written (`:is(.a, :unknown)`); an invalid selector containing a bad token or an unterminated block makes the list invalid. |
| `ComplexSelector` | The `CompoundSelector` items from left to right (`Compounds`). `Combinators[i]` is the `Combinator` between `Compounds[i]` and `Compounds[i + 1]`. `RelativeCombinator` is the leading combinator of a relative selector (the arguments of `:has()`). |
| `CompoundSelector` | The `SimpleSelector` items, in source order (`Selectors`), including the pseudo-elements and the pseudo-classes that follow them. |
| `TypeSelector` | `div`, `svg|rect` or the universal selector `*` (`IsUniversal`, where `LocalName` is null). `Namespace` is the `NamespaceConstraint`; `Prefix` is the prefix as written. When the style sheet declares a default namespace, a compound selector without type selector (outside the arguments of the pseudo-classes) starts with an `Implicit` universal selector of the default namespace, which is not serialized ([CSS Namespaces](https://drafts.csswg.org/css-namespaces/#prefixes)). |
| `IdSelector`, `ClassSelector` | `#id` and `.class`. Escapes are resolved: `.a\.b` has the class name `a.b`. |
| `ClassPrefixSelector` | `.foo-*` ([Selectors 5](https://drafts.csswg.org/selectors-5/#class-prefix), tentative): an identifier ending with a hyphen (`Prefix`), immediately followed by `*`. It matches the elements with a class that starts with the prefix, followed by at least one character that is not a hyphen (`foo-bar`, not `foo--bar` nor `foo-`). The prefix is matched case-sensitively, also in quirks mode, where the classes are ASCII lowercased. Its specificity is the specificity of a class selector. |
| `AttributeSelector` | `[name]`, `[name=value]`… with `Operator`, `Value`, `CaseSensitivity` (`i` and `s`) and `Namespace`. |
| `PseudoClassSelector` | `Kind` (a `PseudoClass` value), `Name` as written in lowercase, and the arguments: `Argument` (the selector list of `:is()`, `:where()`, `:not()`, `:has()`, `:nth-child(An+B of S)`, `:host()`, `:host-context()`), `Nth` (the `AnB` value of `:nth-*()`), `Strings` (the ranges of `:lang()`, the direction of `:dir()`, the identifier of `:state()`, the types of `:active-view-transition-type()`) and `Levels` (`:heading()`). |
| `PseudoElementSelector` | `::before`, `::part(name)`… with `Name`, `IsFunction`, `Argument` (the compound selector of `::slotted()`, the selector list of `::cue()` and `::cue-region()`), `PartNames` (the names of `::part()`) and `ArgumentText` (the other arguments, serialized). The legacy forms (`:before`) are parsed as pseudo-elements. |
| `NestingSelector` | `&`. Its specificity is the specificity of the parent rule once the style sheet applies to a document. |

`ToString()` serializes each level as `SelectorText` does (https://drafts.csswg.org/cssom/#serializing-selectors), and
`Specificity` (https://drafts.csswg.org/selectors-4/#specificity-rules) is available on each level.

The pseudo-classes that can follow a pseudo-element depend on it: the user action pseudo-classes (`::before:hover`),
`:is()` and `:where()` (whose selectors that are not allowed after the pseudo-element are invalid), and the pseudo-classes
allowed by the pseudo-element. `::part()` ([CSS Shadow](https://drafts.csswg.org/css-shadow-1/#part)) can be followed by
the pseudo-classes that do not depend on the tree (not the structural pseudo-classes, `:has()`, `:scope` and `:host`),
`:not()` of these pseudo-classes, and the pseudo-elements other than `::part()` and `::slotted()`. `::slotted()` can only
be followed by `:is()` and `:where()` (without valid selectors) and by the tree-abiding pseudo-elements. In the argument
of `:host()`, `:host-context()` and `::slotted()`, the selectors of `:is()`, `:where()` and `:not()` must be compound
selectors. The scroll navigation pseudo-elements of [CSS Overflow 5](https://drafts.csswg.org/css-overflow-5/#scroll-navigation)
(`::scroll-marker`, `::scroll-marker-group`, `::scroll-button()`) can be followed by the user action pseudo-classes and by
`:not()` of the pseudo-classes they allow, `::scroll-marker` by `:target-current`, `:target-before` and `:target-after`,
and `::scroll-button()` by `:enabled` and `:disabled`. The argument of `::scroll-button()` is `*` or a direction (`up`,
`down`, `left`, `right`, `block-start`, `block-end`, `inline-start`, `inline-end`; `prev` and `next` are parsed and match
nothing), in ASCII lowercase in `ArgumentText`.

<a id="cue-pseudo-elements"></a>
The `::cue`, `::cue(selector)`, `::cue-region` and `::cue-region(selector)` pseudo-elements of [WebVTT](https://w3c.github.io/webvtt/#css-extensions)
style the cues and the regions rendered over a `video` element (the originating element). The argument is a selector
list (`PseudoElementSelector.Argument`) matched against the tree of WebVTT node objects of each cue: the internal node
objects are elements without namespace named `c`, `i`, `b`, `u`, `ruby`, `rt`, `v` and `lang`, with their classes, the
`voice` attribute of a voice object and the `lang` attribute of a language object; the root of the tree has no name,
matches `:root` and the universal selector, and has the identifier of the cue (`::cue(#id)`); `:lang()` uses the
applicable language of the objects. `::cue-region(#id)` matches the region with the identifier. The `:past` and `:future`
pseudo-classes match the node objects that precede or follow the timestamps of the cue text that the current playback
position has reached or not (they match nothing outside of `::cue()`). Only the properties that the specification
allows apply (the others are ignored): `color`, `opacity`, `visibility`, `text-decoration`, `text-shadow`, `outline`,
the background (on the cue background box for `::cue`), the font properties, `line-height`, `white-space`,
`text-combine-upright` and `ruby-position` (not for a `::cue()` selector with `:past` or `:future`), and the transition
and animation properties for `::cue()`.

The arguments of the pseudo-classes and pseudo-elements are selector lists too, so a visitor recurses into them:

```csharp
static void CollectClassNames(SelectorList selectors, HashSet<string> classNames)
{
    foreach (var complex in selectors.Selectors)
    {
        foreach (var compound in complex.Compounds)
        {
            foreach (var simple in compound.Selectors)
            {
                switch (simple)
                {
                    case ClassSelector classSelector:
                        classNames.Add(classSelector.ClassName);
                        break;
                    case PseudoClassSelector { Argument: { } argument }:
                        CollectClassNames(argument, classNames);
                        break;
                    case PseudoElementSelector { Argument: { } argument }:
                        CollectClassNames(argument, classNames);
                        break;
                }
            }
        }
    }
}

foreach (var rule in styleSheet.CssRules.OfType<CssStyleRule>())
{
    CollectClassNames(rule.Selectors, classNames);
}
```

## Matching

| Method | Equivalent to |
|---|---|
| `SelectorList.Matches(Element)`, `Element.Matches(SelectorList)` | `element.matches(selectors)` (`:scope` is the element) |
| `Element.Closest(SelectorList)` | `element.closest(selectors)`: the element or its closest ancestor that matches |
| `QuerySelector(SelectorList)`, `QuerySelectorAll(SelectorList)` on `Document`, `DocumentFragment` and `Element` | `querySelector(selectors)` and `querySelectorAll(selectors)` (`:scope` is the node the method is called on) |
| `QuerySelector<T>`, `QuerySelectorAll<T>` on `Document`, `DocumentFragment` and `Element` | The same, keeping the elements of type `T`: see [dom-helpers.md](dom-helpers.md#typed-queries) |
| `QuerySelector`, `QuerySelectorAll` on an `IEnumerable<Node>` | `querySelectorAll(selectors)` on each node of the sequence, without duplicates: see [dom-helpers.md](dom-helpers.md#queries-on-a-sequence-of-nodes) |

A selector that contains a pseudo-element never matches an element. In a shadow tree, the shadow host is in the tree for
the matching ([CSS Shadow](https://drafts.csswg.org/css-shadow-1/#host-element-in-tree)): `:host > p` matches the
children of the shadow root, and the ancestors of the host are not in the tree. The host is
[featureless](https://drafts.csswg.org/selectors-4/#featureless): it only matches `:host`, `:host()`, `:host-context()`
(and `:scope`/`&` when it is the scoping root), the logical combinations that contain them (`:is(:host)`,
`:where(div, :host)`, `:not(:not(:host))`; a `:not()` whose arguments cannot match a featureless element, such as
`:host:not(:hover)`, does not match) and `:has()` next to one of them (`:host:has(.a)`); the universal selector implied
by a default namespace does not apply to it.

A default `@namespace` adds an implied universal selector of that namespace to the compound selectors without type
selector, except to the subject of the selectors in the argument of a pseudo-class (`.a` in `:is(.container .a)`:
[Selectors](https://drafts.csswg.org/selectors-4/#matches)); an empty default namespace selects the elements without
namespace. In the parent selector that `&` stands for, a selector with a pseudo-element never matches and has no
specificity, and a `:has()` reached through `&` in the argument of another `:has()` never matches.

No link is visited for the selector APIs: `:link` matches every link and `:visited` matches nothing, whatever the history
of the browsing context (see [Visited links](computed-style.md#visited-links)).

The attribute values are compared ASCII case-insensitively by the `i` flag and by the HTML attributes whose values are
case-insensitive (the other characters are compared exactly: `a` followed by U+0308 matches `A` followed by U+0308, not
the precomposed U+00C4). `:heading` and `:heading()` use the computed heading level of the `h1`-`h6` elements, offset by
the `headingoffset` attributes of their shadow-including ancestors up to a `headingreset` attribute (at most 9).
`:lang()` follows the extended filtering of RFC 4647; a value whose subtag has more than 8 characters is not a language
tag and matches no range. `:placeholder-shown` matches the controls with a `placeholder` attribute (even empty) and an
empty value.

## User action pseudo-classes

There is no window, so the user action pseudo-classes follow the trusted events of the user input (the pointer events
fired by `BrowsingContext.Input`, see [User input](browsing-context.md#user-input), and the keyboard events dispatched by
the host, like the WPT test driver, see [Focus](browsing-context.md#focus)):

| Pseudo-class | Matches |
|---|---|
| `:hover` | The element designated by the pointer (the target of the last trusted pointer or mouse event of its document; the boundary events that leave an element designate their related target), its ancestors in the flat tree up to and including the first element of the top layer (a modal dialog, a showing popover, a fullscreen element: the ancestors of a top layer element do not match), and the labeled control of a designated `label`. |
| `:active` | The element being actively pointed at (the target of a trusted `pointerdown` or `mousedown`, until all the buttons are released, even when the event is canceled) or activated by the space key (between `keydown` and `keyup`, unless it is disabled), its ancestors in the flat tree up to and including the first element of the top layer, and the labeled control of an active `label`. |
| `:focus`, `:focus-within`, `:focus-visible` | The focused area of the top-level browsing context (see [Focus](browsing-context.md#focus)). `:focus-within` matches the ancestors of the focused element in the flat tree up to and including the first element of the top layer (and, as `:hover` and `:active`, not a select element and its ancestors for the contents of its open picker). |
| `:user-valid`, `:user-invalid` | The `input`, `textarea` and `select` elements whose user validity is true and that are candidates for constraint validation. The user validity becomes true when the user commits a change (a text control loses the focus after the user edited it, a check box or a radio button is clicked by the user), and for the controls of a form that is submitted (`requestSubmit()`, a submit button); the reset algorithm of the control sets it to false. |

The states are updated before the events are dispatched, so that the listeners of the events observe the new state.

## Scroll markers and scroll buttons

The pseudo-classes written after `::scroll-marker`, `::scroll-marker-group` and `::scroll-button()` match the state of the
pseudo-element, not the state of its originating element (the other pseudo-elements, e.g. `::before:hover`, match the
state of their originating element); see [Scroll markers and scroll buttons](layout.md#scroll-markers-and-scroll-buttons-csslayoutlayoutenginescrollmarkerscs)
for the boxes and the state.

| Selector | Matches |
|---|---|
| `:target-current` | The active scroll marker of a scroll marker group ([CSS Overflow 5](https://drafts.csswg.org/css-overflow-5/#active-scroll-marker)): an `a` element (HTML or SVG) of an element with `scroll-target-group: auto` whose `href` is a fragment of the document that indicates an element, or, after `::scroll-marker`, the pseudo-element. |
| `:target-before`, `:target-after` | The scroll markers of the group that precede or follow the active one. |
| `::scroll-button(up)`... | The physical directions select the button of the corresponding flow-relative direction in the writing mode of the originating element (`::scroll-button(left)` and `::scroll-button(inline-start)` are the same pseudo-element in a horizontal left-to-right element); `*` selects the four buttons. |
| `::scroll-button():enabled`, `:disabled` | Whether the originating element can be scrolled in the direction of the button (a button of an element that is not a scroll container is disabled). |
| `::column::scroll-marker` | The scroll marker of each column of a multi-column container ([CSS Multicol 2](https://drafts.csswg.org/css-multicol-2/#column-pseudo)), with the same pseudo-classes as `::scroll-marker`; `::column` accepts no pseudo-class. |
| `:hover`, `:active` after the three pseudo-elements | The box of the pseudo-element is designated by the pointer (the box at the point of the last trusted pointer or mouse event): its originating element matches `:hover` too; the group also matches when one of its markers does. |
| `:focus`, `:focus-visible`, `:focus-within` after `::scroll-marker` and `::scroll-button()` | The pseudo-element is focused: its scroll container is then `document.activeElement` and does not match `:focus` nor `:focus-visible`; it and its ancestors match `:focus-within`, and so does the `::scroll-marker-group` of a focused marker. |

The state of the scroll markers and of the scroll buttons is a snapshot of the layout taken by the rendering steps (like
the container scroll-state queries): `matches(":target-current")` and the computed styles reflect a scroll at the next
rendering update.

## View transition pseudo-elements

The pseudo-elements of [CSS View Transitions](view-transitions.md#pseudo-elements) (`::view-transition`,
`::view-transition-group()`, `::view-transition-image-pair()`, `::view-transition-old()`, `::view-transition-new()` and
`::view-transition-group-children()`) originate from the root element of a document with an active view transition. Their
argument is `*`, a view transition name, or classes of `view-transition-class` (`*.card`, `.card.big`, `name.card`);
`(*)` alone has a zero specificity, the other arguments the specificity of a type selector. The only pseudo-class that can
follow them is `:only-child`, matched in the tree of pseudo-elements (e.g. `::view-transition-new(*):only-child`: the
names without old image). `:active-view-transition` and `:active-view-transition-type(a, b)` match the root element while
the document has an active view transition (with one of the types); `none` and the names that start with `-ua-` are not
valid types.
