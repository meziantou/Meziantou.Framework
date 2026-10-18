# Cascade and computed style

`Window.GetComputedStyle(element, pseudoElement)` ([CSSOM](https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle))
returns the resolved values of the properties of an element, computed by a [cascade](https://drafts.csswg.org/css-cascade-5/)
of the style sheets of the document. The values that depend on the layout are the used values computed by the layout engine
(see [Layout-dependent values](#layout-dependent-values) and [layout.md](layout.md)).

## Usage

The computed styles only exist for the documents of a [browsing context](browsing-context.md) (as specified, the
declarations are empty for an element that is not connected, not in the flat tree, or in a document without browsing
context):

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    // Optional: the virtual viewport and the preferences of the user (800x600 CSS pixels by default)
    Viewport = new ViewportOptions { Width = 1280, Height = 720, PreferredColorScheme = PreferredColorScheme.Dark },
});

var document = context.Navigate("https://example.com/", _ => html);
var style = document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!);
Console.WriteLine(style.Color);                         // rgb(0, 0, 0)
Console.WriteLine(style.GetPropertyValue("font-size")); // 16px
Console.WriteLine(style.GetPropertyValue("--my-var"));  // custom properties

var before = document.DefaultView.GetComputedStyle(element, "::before");
```

The returned declarations are read-only and live: they reflect the current state of the document and of its style sheets.
They list all the supported longhands in lexicographical order (the vendor-prefixed ones last), then the custom properties.
`Element.ComputedStyleMap()` returns the computed values as typed objects (CSS Typed OM): see [css-typed-om.md](css-typed-om.md).

`ViewportOptions` is used by the media queries (`@media`, the `media` attribute and `MediaList`, `CSSMediaRule.matches`,
`window.matchMedia()`) and the viewport-relative lengths (`vw`, `vh`, `vmin`...). It also has the preferences of the user:
the color scheme (`PreferredColorScheme`), the contrast (`PreferredContrast`), the reduced motion and transparency, and the
[forced colors mode](#forced-colors-mode) (`ForcedColors`, `ForcedColorsTheme`). The viewport of a nested browsing context
is the content box of its `iframe` laid out in the parent document (the layout of the parent document is computed when it is
out of date, as in the browsers; e.g. `width: 100%` is resolved), or 0x0 when the `iframe` is not being rendered (e.g.
`display: none`); the effective `zoom` of the `iframe` multiplies the `devicePixelRatio` of the nested document instead of
its viewport size. The media queries use the whole viewport (`width`, `height`, including the scrollbars), while the
[viewport-percentage lengths](https://drafts.csswg.org/css-values-4/#viewport-relative-lengths) subtract the scrollbar gutters
of the viewport that do not depend on its contents: `overflow: scroll` or `scrollbar-gutter: stable` (`both-edges` twice)
applied to the viewport (propagated from the root or the body element), with the `scrollbar-width` of the root element (the
scrollbars of `overflow: auto` are assumed not to exist). There is no browser interface that appears and disappears: the
small (`svh`...), large (`lvh`...) and dynamic (`dvh`...) viewports are the same, and `env(safe-area-inset-*)` is 0. The
`<meta name=viewport>` is ignored, as in the desktop browsers (no `initial-scale`, no `interactive-widget`).

`window.matchMedia()` returns a `MediaQueryList` whose `matches` is evaluated against the current environment of its
document. When the result changes (e.g. the `width` of the `iframe` changes), a `change` event (`MediaQueryListEvent`) is
fired at the list when the event loop updates the rendering, before the animation frame callbacks (see
[evaluate media queries and report changes](https://drafts.csswg.org/cssom-view/#evaluate-media-queries-and-report-changes)).
A list with event listeners is kept alive as long as its document. When the size of the viewport (or its
`devicePixelRatio`) changes, a `resize` event is fired at the window when the event loop updates the rendering, before the
`change` events, then at `window.visualViewport` when its size changed (see
[run the resize steps](https://drafts.csswg.org/cssom-view/#run-the-resize-steps); the size is tracked once the window or
the visual viewport has a `resize` event listener).

The media queries follow [Media Queries 5](https://drafts.csswg.org/mediaqueries-5/): an unknown media feature, an invalid
value (e.g. `(color: 1.0)`, `(resolution: -1dpi)`) or a function is a `<general-enclosed>`, which is kept and serialized as
specified (e.g. `(--foo: bar)`), but evaluates to unknown (it never matches, and neither does its negation). The `scan`
feature only applies to the `tv` devices: it never matches. The environment variables (`env()`) can be used in the values
of the media features (e.g. `(width > env(safe-area-inset-left))`). The `@custom-media` rules are parsed and exposed in the
CSSOM (`CSSCustomMediaRule`), but the custom media queries (`(--name)`) are not resolved: they evaluate to unknown. `window.screen` is the viewport of the top-level
browsing context (with a color depth of 24), and `window.devicePixelRatio` is `ViewportOptions.DevicePixelRatio` (multiplied by the effective `zoom` of the
`iframe` of a nested browsing context).

## Cascade

- **Style sheets**: the user agent style sheet of HTML (generated from the
  [rendering section](https://html.spec.whatwg.org/multipage/rendering.html) of the HTML Standard by `tools/Meziantou.Framework.WebPlatform.SpecGen`,
  including the rules that only apply in quirks mode, the rule that centers the text of the `th` elements whose parent
  has the initial `text-align`, and the rules of the [margin collapsing
  quirks](https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks) in quirks mode (the block-start or
  block-end margin of the elements with default margins, `p`, `h1`..., at the start or the end of a `body`, `td` or `th`
  element is 0, depending on their substantial siblings and children); the rules of the `select` elements that depend on their rendering
  (a drop-down box or a list box, with native or base appearance: `HtmlUserAgentStyleSheet.Select*`) are applied by the
  cascade when the condition of their block is true, see [the select elements](#select-elements)), the presentational hints (the CSS of the rendering section and the hints defined in prose: `width`,
  `height` and `aspect-ratio` (the `width` and `height` attributes of an `img` element are the ones of its dimension
  attribute source: the `source` element of its `picture` parent selected by the source selection, when it has one of these
  attributes), `hspace`, `vspace`, `bgcolor`, `bordercolor`, `color`, `border`, `frameborder`, the `align` attribute of
  the table rows and cells and of `div`, and the `center` element (`text-align`; they also [align their
  descendants](https://html.spec.whatwg.org/multipage/rendering.html#align-descendants): `LayoutStyle.DescendantAlignment`
  follows the inheritance chain of the styles, it is not a CSS property, and the layout aligns the block-level boxes in normal
  flow whose inline margins are not `auto` as with a safe `justify-self`; as in the browsers, an element with an `align`
  attribute of its own is aligned by its ancestors, the rendered legend of a fieldset is not, and the alignment does not
  cross a table: the browsers reset the `text-align` of the tables that inherit their `-webkit-center` value to `start`), the `value` attribute of `li` (`counter-set`), the `start` and `reversed`
  attributes of `ol` (`counter-reset`), body margin attributes, `font`...; like in the browsers, the default margins of
  the body element are in the user agent style sheet, and the `sub` and `sup` elements keep the inherited `line-height`
  (the HTML style sheet sets `line-height: normal` on them, which no browser does); the rules marked
  "[only if border is not equivalent to zero](https://html.spec.whatwg.org/multipage/rendering.html#magic-border-selector)"
  (the `outset` border of a `table[border]` and the `inset` 1px border of its cells) are in a layer of that name in the
  generated style sheet, and only match when the `border` attribute of the table is not zero: a value that cannot be parsed
  is not zero, as in the browsers), the user agent style
  sheet of [MathML Core](https://w3c.github.io/mathml-core/#user-agent-stylesheet) (generated from its appendix) and the
  presentational hints of the MathML attributes (`dir`, `mathcolor`, `mathbackground`, `mathsize`, `displaystyle`,
  `scriptlevel`, the `mathvariant` of `mi`, the sizes of `mspace` and `mpadded`), the rules of the [user agent style sheet of
  SVG](https://svgwg.org/svg2-draft/styling.html#UAStyleSheet) that the library implements (`overflow: hidden` on
  `svg:not(:root)`, `symbol`, `image`, `marker`, `pattern` and `foreignObject`, the size of the root `svg`, the `transform-origin`
  of the SVG elements, `xml:space`), the presentation attributes of SVG (a list of unitless numbers, e.g. `transform-origin="10 20"`,
  is in user units), the `marker` shorthand (its value for the three marker properties), `paint-order` (the specified order,
  serialized in its shortest form), the style sheets of
  the `style` elements and of the `xml-stylesheet` processing instructions (see
  [xml-stylesheet processing instructions](#xml-stylesheet-processing-instructions)), the constructed and adopted style
  sheets (the adopted style sheets of a shadow root are removed when it is adopted by another document, except between a
  document and the document of its template contents, as in browsers), and the `style` attributes. The disabled style sheets, the alternative style sheets (whose title is not the
  preferred style sheet set, or whose `alternate` pseudo-attribute is `yes`) and the style sheets whose media do not
  match are ignored (a change of the `media` attribute of a `style` element changes the media of its style sheet, as in the
  browsers). The style sheets of the `<link rel=stylesheet>` elements and of the `@import` rules are fetched with
  the `DocumentLoader` of the browsing context (see [Linked style sheets](#linked-style-sheets) and
  [Imported style sheets](#imported-style-sheets)); a layered `@import` whose conditions match declares its cascade
  layer even when its style sheet could not be fetched.
- **Sorting**: origin and importance, [context](https://drafts.csswg.org/css-cascade-5/#cascade-context) (shadow trees),
  element-attached styles, [cascade layers](https://drafts.csswg.org/css-cascade-5/#layering) (`@layer` blocks and
  statements, anonymous and nested layers; `@property` and `@function` rules of later layers win), specificity, [scope proximity](https://drafts.csswg.org/css-cascade-6/#cascade-proximity)
  and order of appearance. `!important`, `revert` and `revert-layer` are supported.
- **Rolling back the cascade** ([revert](https://drafts.csswg.org/css-cascade-5/#default)): `revert` rolls back to the
  previous origin (the presentational hints are part of the author origin), `revert-layer` to the previous cascade layer
  (the presentational hints are below the author layers, and the `!important` declarations of the lower layers of the
  origin are part of the rollback; a `revert-layer !important` rolls back to the layers below its own in the order of
  the normal declarations, whatever the importance of the declarations of the following layers), and `revert-rule` to the declarations of the other style rules, in the order of the
  cascade (across the layers, the shadow trees, `::part()` and `::slotted()` rules and the `style` attribute; the normal
  and `!important` declarations of the rule are both removed). The keywords chain (e.g. `revert-rule` rolling back to a
  `revert-layer`); a cycle (e.g. `revert-rule !important` and `revert-layer` in a higher layer) and a rollback without
  declaration behave like `unset`. A logical property that rolls back takes the place of its declaration among the
  physical properties of its group (`margin: 0; margin-block-start: revert` reverts the top margin). In a keyframe,
  `revert` and `revert-layer` are the underlying value of the animation (`revert-rule` is not supported in keyframes).
- **Conditional rules**: `@media` (Media Queries 5: media types, range syntax, `and`/`or`/`not`, all the media features
  evaluated against `ViewportOptions`), `@supports` (also in the `supports()` condition of `@import`; `font-format()`
  supports the formats that can be loaded: `collection`, `opentype`, `truetype`, `woff` and `woff2`, and `font-tech()`
  the technologies of the browsers: `features-opentype`, `color-COLRv0`, `color-COLRv1`, `color-sbix`, `color-CBDT`,
  `variations` and `palettes`), `@container` (see [Container queries](#container-queries)).
- **Scoping and nesting**: `@scope` (with or without prelude, scoping limits, `:scope` and `&`, nested `@scope` rules,
  the shadow host as scoping root in a shadow tree), `@import ... scope()` (the rules of the imported style sheet are
  scoped like a `@scope` rule), nested style rules and
  nested declarations (`&`), `@starting-style` (the starting style of the transitions, see
  [Animations and transitions](#animations-and-transitions)).
- **Shadow DOM**: the style sheets of a shadow tree apply to the elements of the tree; `:host`, `:host()` and
  `:host-context()` match the shadow host; `::slotted()` matches the elements assigned to the slots (through the slots
  assigned to other slots, but not the slots themselves), and its pseudo-elements (`::slotted(*)::before`); the elements
  inherit from their parent in the [flat tree](https://drafts.csswg.org/css-scoping/#flat-tree). Between the shadow
  trees, the [context](https://drafts.csswg.org/css-shadow-1/#shadow-cascading) follows the tree of trees: the
  `::slotted()` rules win over the `:host` rules of the shadow tree of the element (and lose for `!important`).
- **Shadow parts** ([CSS Shadow](https://drafts.csswg.org/css-shadow-1/#part)): `::part(name)` matches the elements of
  a shadow tree whose `part` attribute (`Element.Part`) contains the name, when its originating element is the shadow
  host, in the tree of the host (`x-button::part(label)`) or in the shadow tree (`:host::part(label)`). The `exportparts`
  attribute of a shadow host forwards the parts of its shadow tree to the tree of the host, under the same name or
  renamed (`exportparts="label, icon: button-icon"`), through any number of nested shadow trees; the part name lists and
  the forwarded part names are parsed as specified. A forwarded pseudo-element (`exportparts="::before: label"`) is
  styled by `::part(label)` (the pseudo-elements of any element can be forwarded, as the shadow trees of the user agent are
  not modeled). The pseudo-classes that follow `::part()` (`::part(label):focus`, `:state()`, `:is()`, `:not()`...)
  match the part, and the pseudo-elements that follow it (`::part(label)::before`) are its pseudo-elements. The rules of
  an outer tree win over the rules of the shadow tree for the normal declarations, and lose for `!important`. The `*`
  wildcard of the early drafts of `exportparts` is not supported (the specification does not define it).
- **Pseudo-elements**: the rules of the pseudo-elements (`::before`, `::after`, `::marker`, `::placeholder`,
  `::backdrop`, `::highlight(name)`...) apply to `GetComputedStyle(element, "::name")` (the `-webkit-` prefixed aliases,
  e.g. `::-webkit-file-upload-button`, are only supported in the selectors: their declarations are empty, as in
  browsers). The pseudo-elements inherit from their originating element, except the highlight pseudo-elements (see
  [Highlights](#highlights)) and the pseudo-elements of a [view transition](view-transitions.md#pseudo-elements)
  (`::view-transition-group(name)`...), which originate from the root element of the transition (the document element,
  or the element of a scoped view transition) and inherit from their parent in the view transition tree; the dynamic
  style sheet of an active view transition is a user-agent style sheet of its pseudo-elements. Only a [limited set of properties](https://drafts.csswg.org/css-pseudo-4/#marker-pseudo)
  applies to `::marker`: the font properties, `line-height`, `white-space`, `color`, `text-combine-upright`,
  `unicode-bidi`, `direction`, `content`, the animation and transition properties and, as in browsers, the inherited text
  and text decoration properties (`letter-spacing`, `text-transform`, `text-emphasis`, `text-shadow`...), `cursor` and
  the custom properties; the declarations of the other properties (e.g. `display`, `position` or `list-style`) are
  ignored. The user agent style sheet of [CSS Lists](https://drafts.csswg.org/css-lists/#ua-stylesheet) sets
  `unicode-bidi: isolate`, `font-variant-numeric: tabular-nums`, `white-space: pre` and `text-transform: none` on the
  markers. As in the browsers (their important user agent declarations), `text-indent`, `text-align` and
  `text-align-last` are not inherited by the markers (they have their initial value), and the computed `display` of
  `::marker` is `inline` for an inside marker and `inline-block` for an outside marker. Likewise, only the [properties of `::first-line`](https://drafts.csswg.org/css-pseudo-4/#first-line-styling)
  (the font, color, opacity, background and text decoration properties, the typesetting properties that apply to inline
  elements, `ruby-position`, `line-height`, the longhands of `vertical-align` and the custom properties) apply to
  `::first-line`, and to `::first-letter` with the margin, padding, border and `box-shadow` properties, `float` and the
  properties of the initial letters (`initial-letter`, `initial-letter-align`, `initial-letter-wrap`): the
  `display` of `::first-line` is `inline`, and the `display` of `::first-letter` is `inline`, or `block` when it floats.
  `::picker(select)` can be followed by the tree-abiding pseudo-elements (`::picker(select)::before`), by `::first-line`,
  `::first-letter` and `::backdrop`; `::before`, `::after`, `::backdrop`, `::first-line` and `::first-letter` are styled
  and rendered (the highlight pseudo-elements of the picker are not).
  The scroll navigation pseudo-elements of [CSS Overflow 5](https://drafts.csswg.org/css-overflow-5/#scroll-navigation):
  `GetComputedStyle(element, "::scroll-button(left)")` is the style of the button of that direction in the writing mode of
  the element (the same declarations as `::scroll-button(inline-start)` in a horizontal left-to-right element; the argument
  is ASCII case-insensitive, and `*`, `prev` and `next` give empty declarations); the declarations of `::scroll-marker`
  are empty when the marker is not generated (no `content`, or no `::scroll-marker-group` on the nearest scroll container),
  as in Chrome, and they reflect its state (`:target-current`, `:focus`...). The user agent style sheet styles a
  `::scroll-marker` as a link (`color: LinkText`, underlined, `cursor: pointer`) and the `::scroll-button()`
  pseudo-elements as the `button` element; the computed `contain` of `::scroll-marker-group` always has `layout`, and
  `size` unless the group is absolutely positioned. The size container queries of a `::scroll-marker-group` and of a
  `::scroll-button()` skip their originating element (their boxes are its siblings), unless it is the root element.

The rules are indexed by the id, class and type of the rightmost compound selector of their selector, so that only the
rules that may match an element are matched against it. The styles are computed on demand and cached until a change
invalidates them: then they are recomputed on the next access (see [Invalidation](#invalidation)).

## Invalidation

`StyleEngine.Version` is incremented by each change that may change the computed styles (the version the box tree and the
layout compare with, see [layout.md](layout.md#when-the-layout-runs)). The elements whose styles are discarded, and the
changes of the DOM, are recorded for the box tree (`StyleEngine.BoxTreeChanges.cs`, see
[layout.md](layout.md#incremental-update)): it is patched with the boxes of these elements; a change that discards all the
styles builds it again. Which cached styles are discarded depends on the change:

- **By element** (`StyleEngine.Invalidation.cs`): the changes of the DOM and of the states of the elements are recorded by
  the document (`Document.StyleInvalidations`: the attribute changes, the inserted and removed nodes, the text changes, the
  `:hover`, `:active`, `:focus`/`:focus-visible`/`:focus-within` and `:state()` changes, with the ancestors of a removed node
  that contained the designated, the actively pointed or the focused element), and the targets of the animations whose
  effects changed by the animations of the document (each frame, the targets of the time-dependent animations). The
  features of the selectors of the user agent, of the presentational hints and of the style sheets of the document
  (`StyleInvalidationFeatures`, collected once per rule set, like the [invalidation
  sets](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/css/invalidation/README.md)
  of the browsers) give the elements a change may affect: a class, an id or an attribute of the subject compound selector
  affects the element, of a compound followed by a descendant or child combinator its subtree, by a sibling combinator its
  following siblings (and their subtrees), in the argument of `:has()` the anchors (the ancestors, and their siblings for the
  sibling relative selectors), in the argument of `:nth-child(… of S)` its siblings; the insertion or the removal of a child
  affects its siblings when a selector uses a structural pseudo-class (`:first-child`, `:nth-child()`...) or a sibling
  combinator, its parent for `:empty`, its ancestors (and their siblings) for `:has()`, and its siblings when a declaration
  uses `sibling-index()` or `sibling-count()`; the descendants of the outermost element with [auto
  directionality](https://html.spec.whatwg.org/multipage/dom.html#auto-directionality) are affected by the text changes.
  The cascade of each affected element runs again: when its cascaded values are the same (and nothing else its style
  depends on changed: an animation, a visited link, `attr()` after an attribute change), the style is kept and only the
  styles of its pseudo-elements are discarded; otherwise the styles of the element, of its pseudo-elements and of its
  descendants are discarded (they inherit from it), and the styles of the removed and of the inserted nodes are discarded (a
  node moved by `moveBefore()` or removed and inserted again keeps its styles as its before-change styles). A change of the
  style of the root or body element discards all the styles (the viewport units depend on their scrollbar gutters).
- **All the styles**: a change of a style sheet (through the CSSOM: `Document.StyleSheetsVersion`, or a style sheet of
  the document added, removed or changed), of the environment (the viewport, the user preferences), of the fonts, of the
  visited links, of the snapshots of the scroll-state queries and of the scroll markers, and the changes that are not
  recorded: the other attribute changes (only the `class`, `id`, `style`, `data-*` and `aria-*` attributes are invalidated
  by element: the other attributes may change the states of other elements, e.g. `disabled`, `href`, `lang`, `dir`, or their
  presentational hints, e.g. the attributes of a `table` or of the `body` element), the insertion or the removal of the
  elements of the head of the document, of the root and body elements, of the listed form-associated elements, the forms,
  the labels and the options, the changes of the children of the `select`, `option`, `datalist`, `details`, `fieldset`,
  `label`, `textarea`, `style`, `object`, media, `picture`, `map` and `frameset` elements, the insertion or the removal of the
  SVG elements and the changes of their children (the style elements of SVG, the elements referenced by the `use` elements),
  an id change when a selector uses `:target`, a change of a `style` attribute that declares a timeline, the other state changes (`IncrementElementStateVersion()`: the checkedness,
  the value and the validity of the form controls, the dialogs, the popovers...), the animations without target, and more
  than 1,000 recorded changes (e.g. while the parser builds the document). The documents in quirks mode (rules of the user
  agent depend on other elements), with shadow trees, an active view transition, SMIL animations, WebVTT cues or named
  scroll-driven timelines and `animation-timeline` declarations (the timelines are looked up in the styles of other elements),
  and the style engines with an environment override (the printing, the minifier) always invalidate all their styles.

A style is computed for one version: the checks of the versions are suspended while `GetStyle` computes a style and its
ancestors (a change of the versions while it is computed is seen by the next read), a style computed by
the computation of another one is not computed again, and the styles of the descendants computed while the style of an element
is replaced by its style with the effects of its new animations are discarded: the styles kept by the invalidations by element
never inherit from a style of another generation. With the
`WEBPLATFORM_VERIFY_STYLE_INVALIDATION=1` environment variable (`StyleEngine.VerifyInvalidations`), each invalidation by element
checks that the cascade of each kept style is the same as a new cascade and that its parent is the style of its parent element
(an `InvalidOperationException` reports a missing invalidation): the web-platform-tests are run with it to check the features.

### Select elements

The rules of the rendering section of the HTML Standard for the `select` elements depend on how the element is rendered
(https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2: "The following styles are expected to apply
to select elements when they are being rendered [...]"), which a selector cannot express. `tools/Meziantou.Framework.WebPlatform.SpecGen` generates one
constant per condition (`HtmlUserAgentStyleSheet.SelectDropDown`, `SelectNativeAppearance`,
`SelectDropDownNativeAppearance`, `SelectBaseAppearance`, `SelectDropDownBaseAppearance`, `SelectListBoxBaseAppearance`),
and the cascade applies the rules whose condition is true (`CascadeRule.SelectCondition`, `StyleEngine.Select.cs`). The
library adds the rules of the rows of a list box with native appearance, which the specification leaves to the user
agent (`SelectRuleCondition.ListBoxNativeAppearance`: `min-block-size: 1lh` for the options, `font-weight: normal` for
the options of a group, the background of the selected options; see
[layout.md](layout.md#replaced-elements-csslayoutreplaced)):

- A select element is a *drop-down box* when it has no `multiple` attribute and its display size is 1 (0 too, as in the
  browsers), a *list box* otherwise (`StyleEngine.IsDropDownBox`).
- It has *base appearance* when the computed value of its `appearance` is `base-select` or `base`. The style of a select
  element is computed with the rules of the native appearance first, which gives its `appearance` (no conditional rule
  sets it), then again with the rules of the base appearance.
- Its *contents are rendered with base appearance* when it is a list box with base appearance, or a drop-down box with
  base appearance whose `::picker(select)` has base appearance too: the rules of the options (`select option`, `select
  optgroup`, `option::checkmark`...) apply then.
- The children of a drop-down box other than its button (its first child element when it is a `button`) inherit from the
  `::picker(select)` pseudo-element, the select popover they are slotted in. The library adds the rules of the popovers of
  the user agent style sheet for it (`display: none` unless the select element matches `:open`, `position: fixed`...),
  which the rules of `::picker(select)` of the specification override, and `color: inherit` for a select element with base
  appearance (the system colors that the library gives to the form controls, as the browsers, are for the native
  appearance).
- `display: inherit` on a child of a select element computes to `contents`, as in the browsers: the child inherits from
  the slot of the shadow tree it is assigned to.
- Not in the pinned specification, so not applied (some tests expect them from Chrome): the inherited `text-shadow` of a
  select element with base appearance (the rule `input, select, button, textarea { text-shadow: initial }` has no
  condition), `cursor: default`, and the dimmed label of a disabled `optgroup`.

### Form controls with base appearance

The other form controls opt into the [basic appearance](https://drafts.csswg.org/css-forms-1/#appearance) with
`appearance: base`. The rules of the Basic Appearance User Agent Stylesheet of CSS Form Control Styling
(`StyleEngine.FormControls.cs`: the appendix of the editor's draft of 2026-09-17, the revision of the pinned specification
data; a later revision adds `box-sizing: border-box` to the pseudo-elements and the cursors) are conditional rules of the
user agent style sheet, as the ones of the select element (`SelectRuleCondition.ControlBaseAppearance`): the style of an
`input`, `textarea`, `button`, `meter`, `progress`, `fieldset` or `legend` element is computed first with the other rules,
which gives its `appearance`, then again with these rules when it is `base`; they apply to the pseudo-elements of such a
control too ("these pseudo-elements always inherit `appearance` from their originating element": `::checkmark`,
`::slider-track`, `::slider-fill`, `::slider-thumb`, `::color-swatch`, `::file-selector-button`). They are added after the
other rules of the user agent, which they override at equal specificity. The rule of the text inputs (`border-radius:
4px`) "must apply to textareas and textual inputs only". The library adds the intrinsic inline size of `meter` and
`progress` (see [layout.md](layout.md#replaced-elements-csslayoutreplaced)). The rules of the parts of the controls with
primitive appearance, which the specifications leave to the user agent (the sizes of the track, of the fill and of the
thumb of the slider-like controls, the colors of the progress bar), have the opposite condition
(`ControlNativeAppearance`). Not done: the rules of the pseudo-elements that are not generated (`::field-content`,
`::picker-icon` of the inputs...).

## Linked style sheets

The style sheets of the `<link rel=stylesheet>` elements of the documents of a browsing context are fetched with
`BrowsingContextOptions.DocumentLoader` (see [browsing-context.md](browsing-context.md#the-document-loader)), as specified
by [the stylesheet link type](https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet):
`DocumentRequest.Destination` is `RequestDestination.Style`, `DocumentRequest.Initiator` is the `link` element, and the
request is a CORS request when the element has a `crossorigin` attribute. Without a loader, or in a document without
browsing context, nothing is fetched (`HTMLLinkElement.sheet` is null).

- The style sheet is fetched when the element is connected and its `rel` contains `stylesheet`, it has no `disabled`
  attribute, its `type` is missing or `text/css`, and its `media` matches the environment. It is fetched again when
  `href`, `crossorigin` or `referrerpolicy` changes (not when an attribute is set to the same value), and it is removed
  when the element is removed or no longer links to a style sheet. A change of `media` or `title` changes the media or
  the title of the style sheet (an empty title is null).
- As in the browsers (memory cache), a style sheet already fetched by a link element of the document (same URL, mode
  and credentials) is not fetched again: the style sheet of the element is created synchronously, and its `load` event
  is fired by a task.
- An [alternative style sheet](https://html.spec.whatwg.org/multipage/links.html#the-link-is-an-alternative-stylesheet)
  (`rel="alternate stylesheet"`) is only fetched when it has a title, and it does not apply (the style sheet sets cannot
  be selected), unless the element is [explicitly enabled](https://html.spec.whatwg.org/multipage/semantics.html#explicitly-enabled)
  (its `disabled` attribute was removed, e.g. `link.Disabled = false`): then it is a regular style sheet, whatever its
  `rel`. Clones are not explicitly enabled.
- The request URL modifiers of the `url()` apply to the request (see [Request URL modifiers](#request-url-modifiers)).
- The response must be `text/css` (any type is accepted for a same-origin response in quirks mode, except for a
  `data:` URL, whose origin is opaque), with an ok status. As in the browsers, a response without a valid Content-Type
  is sniffed as a style sheet, unless it has the `X-Content-Type-Options: nosniff` header. It is decoded as an
  imported style sheet (byte order mark, `charset` parameter, `@charset` rule, encoding of the document), and its
  relative URLs are resolved against its response URL. Its `@import` rules are fetched. As in browsers, a response
  that is not a style sheet (a network error, an error status, another MIME type) creates an empty style sheet (it is
  in `Document.StyleSheets`), and the `error` event is fired.
- The rules of a style sheet that is not [CORS-same-origin](https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cors-same-origin)
  cannot be accessed (the origin-clean flag): `CssRules`, `InsertRule()` and `DeleteRule()` throw a `SecurityError` for
  a style sheet of another origin fetched without `crossorigin`, of a network error, or whose response went through
  another origin (`DocumentResource.RedirectUrls`, even when it is redirected back to the origin of the document).
- The `load` event is fired at the element once the style sheet and its imported style sheets are fetched, the `error`
  event when one of them could not be fetched. The element delays the load event of its document meanwhile.
- The style sheet of an element created by the parser (whose media matches, and that is not an alternative style sheet)
  is a [style sheet that is blocking scripts](https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet)
  until its `load` or `error` event: the parser pauses at the next script until then, so that the scripts see the style
  sheet (`Document.StyleSheets`, `GetComputedStyle()`).

`<link rel=preload as=style>` fetches a style sheet in advance: the first `<link rel=stylesheet>` of the same request
(URL, mode and credentials) uses its response without fetching it again (see
[Preloads](browsing-context.md#preloads)). The `integrity` attribute of a `<link rel=stylesheet>` is the integrity
metadata of its request: a style sheet that does not match it is a network error.

## Imported style sheets

The style sheets of the `@import` rules are fetched with `BrowsingContextOptions.DocumentLoader` (see
[browsing-context.md](browsing-context.md#the-document-loader)): `DocumentRequest.Destination` is `RequestDestination.Style`
and `DocumentRequest.Initiator` is the `style` element. Without a loader, nothing is fetched (`CssImportRule.StyleSheet` is
null), except the `data:` URLs, which are processed by the library. A document without a browsing context that was created
by a window (e.g. `createHTMLDocument()`) fetches its imports with the loader of that window, as the browsers do, and its
`style` elements fire their `load` and `error` events.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = DocumentLoader.Create() });
var document = context.Navigate("https://example.com/", _ => "<style>@import 'theme.css';</style>");

// The style sheets are loaded asynchronously by the HTTP loader
await context.EventLoop.RunUntilIdleAsync();
```

As specified by [fetch an @import](https://drafts.csswg.org/css-cascade-5/#fetch-an-import):

- The URL is resolved against the URL of the style sheet that contains the rule (the base URL of the document for a
  `style` element), and the relative URLs of an imported style sheet (`url()`, nested `@import`) against its response URL.
  A style sheet that imports itself (directly or not) is not fetched again.
- The style sheets of the constructed style sheets are never fetched, nor the ones whose `supports()` condition is false.
  The style sheets are fetched whatever their media queries: the media queries are evaluated by the cascade.
- The request URL modifiers of the `url()` apply to the request (see [Request URL modifiers](#request-url-modifiers)).
- The response must be `text/css` (any type is accepted for a same-origin response in quirks mode, except for a
  `data:` URL, whose origin is opaque, and a response without a valid Content-Type is sniffed as a style sheet, unless
  it has the `X-Content-Type-Options: nosniff` header). The encoding is determined by the byte order mark, the
  `charset` parameter, the `@charset` rule, then the encoding of the document.
- The rules of the imported style sheet replace the `@import` rule in the cascade, in the layer of `layer` / `layer(name)`.
- `CssImportRule.StyleSheet`, `StyleSheet.ParentStyleSheet`, `CssStyleSheet.OwnerRule` and `StyleSheet.Href` expose the
  imported style sheets (they are not in `Document.StyleSheets`). The `ParentStyleSheet` of the style sheet of a removed
  `@import` rule is null, as in browsers. The rules of a style sheet imported from another origin cannot be accessed:
  `CssRules`, `InsertRule()` and `DeleteRule()` throw a `SecurityError`.
- The `load` event of the `style` element (`error` when a style sheet cannot be fetched) and the `load` event of the
  document wait for the imported style sheets. A style sheet is parsed when its `style` element changes if its text
  contains `@import` and the browsing context has a loader (otherwise it is parsed lazily, when it is first used); when its
  content changes, the pending fetches are canceled. The style sheet of a `style` element created by the parser is a
  style sheet that is blocking scripts until its imported style sheets are fetched.

### Request URL modifiers

The [request URL modifiers](https://drafts.csswg.org/css-values-5/#request-url-modifiers) of a `url()` or `src()` apply
to the request of its resource: the `@import` style sheets, the sources of the font faces and the images of the computed
styles.

- `cross-origin(anonymous)` makes the request a CORS request (`DocumentRequest.Mode` is `RequestMode.Cors`) whose
  credentials are only included for the same origin, and `cross-origin(use-credentials)` a CORS request with the
  credentials. A cross-origin style sheet or font must pass the CORS check (`Access-Control-Allow-Origin`, and
  `Access-Control-Allow-Credentials: true` with `use-credentials`), and the rules of a style sheet that passes it can be
  accessed (`CssRules`).
- `integrity("<metadata>")` is the integrity metadata of the request (`DocumentRequest.Integrity`): the body of the
  response must match it ([Subresource Integrity](https://w3c.github.io/webappsec-subresource-integrity/), SHA-256,
  SHA-384 and SHA-512), else it is a network error. A cross-origin response of a no-cors request never matches a hash.
- `referrer-policy(<policy>)` is the referrer policy of the request (`DocumentRequest.ReferrerPolicy`), instead of the one
  of the document, and determines `DocumentRequest.Referrer`.

```css
@import url("https://cdn.example/theme.css" cross-origin(anonymous) integrity("sha384-..."));
@font-face { font-family: Brand; src: url("https://fonts.example/brand.woff2" referrer-policy(no-referrer)); }
```

## xml-stylesheet processing instructions

The [`xml-stylesheet` processing instructions](https://drafts.csswg.org/cssom/#requirements-on-user-agents-implementing-the-xml-stylesheet-processing-instruction)
of the prolog (the children of the document that precede the document element) are fetched with the `DocumentLoader`
like the imported style sheets (`DocumentRequest.Initiator` is null), when the document has a browsing context:

- The pseudo-attributes `href` (resolved against the base URL of the document), `type` (missing or `text/css`), `media`,
  `title`, `alternate` and `charset` (the encoding used when neither the response nor the style sheet declares one, then
  the encoding of the document) are supported. The `href` of the style sheet is the response URL.
- `ProcessingInstruction.Sheet` and `Document.StyleSheets` expose the style sheet once it is fetched. It is removed, and
  the pending fetch is canceled, when the node leaves the prolog or its data changes (then it is fetched again).
- The style sheet of a processing instruction created by the XML parser is a [style sheet that is blocking scripts](https://html.spec.whatwg.org/multipage/semantics.html#a-style-sheet-that-is-blocking-scripts)
  until it is fetched: the parser pauses at the next script (inline or not) until then. The style sheets delay the `load`
  event of the document.

## Computed values

- Inheritance, initial values and the CSS-wide keywords (`initial`, `inherit`, `unset`, `revert`, `revert-layer`), the
  `all` shorthand.
- Custom properties: `var()` with fallbacks (a `<declaration-value>`: a `!` or a `;` at the top level of the fallback makes
  the declaration invalid), cycles (the properties of a cycle are invalid), invalid values at computed-value
  time, a CSS-wide keyword that results from the substitution (`revert`, `revert-layer` and `revert-rule` roll back the
  cascade from the declaration, e.g. `var(--unknown, revert-layer)`), and the registered custom properties (see [Registered
  custom properties](#registered-custom-properties)). An empty value (`--x: ;`) is serialized as a space, as is a value that
  only contains whitespace after substitution (`var(--x)`, a custom function whose result is empty), but not an empty
  `if()` branch. The substituted
  values are serialized token by token: the comments around a substituted value are dropped, and an empty comment separates
  the tokens that would otherwise be parsed as one ([serialization](https://drafts.csswg.org/css-syntax-3/#serialization),
  e.g. `var(--a)var(--b)` is `a/**/b`); in the value of another property, it is a space (`1px 2px`). The values that come
  from `attr()` are [attr()-tainted](https://drafts.csswg.org/css-values-5/#attr-security), as is a `var()` whose name
  or value is (the taint of a custom property is inherited), an `if()` whose value is or whose evaluated conditions use a
  tainted value (e.g. `style(--x: attr(data-x type(*)))`, or a tainted `--x`), a `random-item()` whose key is, and a custom
  function whose result is (the parameters, default values and local variables keep their taint, also in the called
  functions): a tainted value used in a `url()`, `src()` or as the string
  of `image-set()` makes the declaration invalid at computed-value time. `env()` (the insets are `0px`, the indices of the two-dimensional variables are not
  supported: their fallback is used), `attr()` (its name and type can be given by substitution functions; without type, or
  with `raw-string`, the value is a string, an empty string when the attribute is missing and there is no fallback; with
  `number` or a unit, it is a number; with `type()`, whose `<syntax>` cannot contain `<url>`, the arbitrary substitution
  functions of the value are substituted, then it must match the syntax, and the values of the data types are serialized as
  specified values, e.g. `3EM` is `3em`; an attribute that is read while its value is being substituted is a cycle: the
  `attr()` functions of the cycle use their fallback, the outermost one included; invalid arguments, such as an unknown
  `type()` syntax, make it invalid whatever its fallback; in a `::part()` rule, the attributes are the ones of the shadow
  host that the rule matches), `inherit()` and `ident()` (the concatenation of its strings, idents and
  integers, e.g. `ident("--item" calc(2 * 3))` is `--item6`) are substituted, and so are the custom functions (see
  [Custom functions](#custom-functions)). `if()` substitutes the value of its first branch whose condition is true
  (`style()` queries the custom properties of the element itself, or the local variables of the custom function that
  contains it, `media()` and `supports()`, whose declarations accept the [quirky lengths](https://drafts.csswg.org/css-values-4/#deprecated-quirky-length)
  and [colors](https://drafts.csswg.org/css-color-4/#quirky-color) in quirks mode, as `@supports` does (they are not
  otherwise implemented), combined with `and`, `or` and `not`; `else` is always true, and a condition
  that cannot be parsed is false); when no condition is true, the value is invalid at computed-value time. `random-item()`
  substitutes one of its items, chosen with the random base value of its key. A value that comes from `attr()`, directly or
  through a custom property or an `if()` condition, is attr-tainted: the property is invalid at computed-value time when
  the value contains a URL (`url()`, `src()`, a string of `image-set()`).
- Lengths are absolute (`px`), the angles, times, frequencies and resolutions use their canonical unit (`deg`, `s`, `hz`,
  `dppx`), the math functions (`calc()`, `min()`, `clamp()`, `round()`, trigonometric functions...) are simplified, and the
  URLs are absolute. The font-relative units use the metrics of the fonts selected by the font matching (see
  [Font matching in docs/layout.md](layout.md#font-matching)): `ex` and `cap` are the x-height and the cap height of the
  [first available font](https://drafts.csswg.org/css-fonts-4/#first-available-font) (0.5em and the ascent when the font
  does not define them), `ch` and `ic` the advances of "0" and "水" in the fonts that render them (in the inline axis: the
  advance height in vertical text when the glyph is upright; 0.5em and 1em when no font has the glyph), and `lh` the
  used line height: `line-height: normal` is the ascent plus the descent plus the line gap of the first available font
  (with the overrides of its `@font-face` rule), each rounded to whole pixels as in Blink (except for the tiny fonts). The `r*` units use the fonts of the root element, the media queries and
  the `sizes` attribute the default font (Tinos, see [Bundled fonts](layout.md#bundled-fonts)). The computed values are
  computed again when a web font is loaded. The container query length units (`cqw`, `cqh`, `cqi`, `cqb`, `cqmin`,
  `cqmax`) use the size of the nearest eligible size container when it is known (see [Container queries](#container-queries)),
  the small viewport size otherwise. The result of a math function is clamped to the range of the property
  ([range checking](https://drafts.csswg.org/css-values-4/#calc-range), e.g. `flex-shrink: calc(-1)` computes to `0` and
  `column-count: calc(0)` to `1`); a top-level calculation that is `NaN` computes to zero, and an infinite one to the
  largest finite value of a single-precision number. The percentages of a property that accepts a `<length-percentage>`
  are not resolved in the math functions (`min(10%, 20%)` is kept). The font-relative lengths of the `font-*` properties
  refer to the font of the parent. The math functions of CSS Values 5 are supported: `progress()` (with `no-clamp`),
  `calc-mix()` (the weights are normalized), `calc-size()` (kept as is: it is resolved at used time) and `random()`: the
  random base value is cached by the document for its caching key, made of the `<dashed-ident>`, the user agent
  `<random-ua-ident>` (`auto`, `property-scoped` and `property-index-scoped` are replaced at parse time by
  `ua-<property>-<index>`, e.g. `random(0px, 10px)` is `random(element-scoped ua-width-1, 0px, 10px)`, also in the
  conditions of the `if()` functions, where the index counts all the `random()` functions of the value) and the element
  when `element-scoped` is specified. In a custom function, the `random()` functions of the typed values (the result of a
  function with a return type, the typed parameters and their local variables) are evaluated in the function: the property
  of their key is the name of the function followed by `result` or the name of the parameter (e.g. `ua---f-result-1`), and
  the element of `element-scoped` is the "hypothetical element" of the invocation (each invocation gets its own value); a `random()` whose arguments are percentages that cannot be resolved computes to
  `random(fixed <base value>, ...)`. `sibling-index()` and `sibling-count()` are integers, and are kept in the specified
  values; they, `random()` and `random-item()` are invalid in the descriptors of the at-rules and in `@page`.
- The descriptors of the at-rules are parsed with their grammar, and with the constraints defined in prose: in
  `@counter-style`, a `<counter-style-name>` is not `none`, the lower bound of a `range` is not higher than its upper
  bound, the weights of `additive-symbols` are strictly decreasing, and the weight of `pad` and `additive-symbols` is
  serialized before the symbol; in `@font-face`, a range of `font-weight` or `font-width` whose bounds are equal is
  serialized as a single value, and an oblique angle of zero is `normal`.
- `zoom` computes to a number: a percentage is converted, and the legacy `normal` keyword and `0` compute to `1` (as in the
  browsers).
- Colors are computed (named colors, system colors in light and dark color schemes or of the palette of the
  [forced colors mode](#forced-colors-mode), `light-dark()`, `color-mix()` in the
  sRGB, linear sRGB, XYZ, Lab, LCH, Oklab and Oklch color spaces, `contrast-color()`, which is white or black, whichever has
  the higher WCAG 2.1 contrast ratio); the resolved value of a color is its used value (`currentcolor` is resolved). The
  relative color syntax is kept as specified. `light-dark()` of images computes to the image of the used color scheme
  (`none` is `image(rgba(0, 0, 0, 0))`).
- The color conversions follow the sample code of CSS Color 4, except the transfer function of `rec2020`: the piecewise
  function of ITU-R BT.2020 (as in all the browsers and in the reftests), not the pure 2.4 gamma of BT.1886 of the current
  draft (which only the newer testharness tests use). The computed and used colors are not gamut mapped (they keep the
  components outside of their gamut); the painted colors are (see [rendering](rendering.md#display-list)).
- HDR colors ([CSS Color HDR](https://drafts.csswg.org/css-color-hdr-1/)): `ictcp()`, `jzazbz()`, `jzczhz()` and the
  `rec2100-pq`, `rec2100-hlg` and `rec2100-linear` color spaces of `color()` are converted like the other color spaces
  (also in the relative colors), with HDR Reference White at 203 cd/m². The actual color of `color-hdr()` depends on the HDR
  headroom of the display, which is not exposed: its computed value keeps the function with the computed colors, as do the
  `color-mix()` and relative colors that contain it. Its parsing is not fully defined
  ([csswg-drafts#13597](https://github.com/w3c/csswg-drafts/issues/13597)): an omitted headroom is 0, and the function is
  invalid when its two headrooms are the same. A color parsed without an element (e.g. the value of `<input type=color>`)
  uses the color of an SDR display (a headroom of 0). `dynamic-range-limit-mix()` computes to the weighted sum of its
  keywords (a mix whose percentages sum to zero is the initial value, `no-limit`).
- Serialization (specified and computed values): a quoted family name of `font-family` is serialized as identifiers
  when they round-trip (`'Lucida Grande'` is `Lucida Grande`, but `"serif"`, `"34J"` and `"A  B"` stay quoted), and
  the default `decimal` counter style of `counter()` and `counters()` is omitted, the `<position>`s of `mask-position`,
  `object-position`, `offset-position`, `offset-anchor` and of the basic shapes start with the horizontal component (`top`
  is `center top`, `y-start` is `center y-start`; the computed logical keywords `x-start`, `x-end`, `y-start` and `y-end`
  are the sides of the writing mode and the direction of the element, e.g. `x-start` is `100%` in `direction: rtl`), and
  the `mask` shorthand omits the initial values of a layer (one box sets `mask-origin` and
  `mask-clip`, and `mask` resets `mask-border`). In a declaration block, the longhands
  are not serialized as a shorthand when a declaration of their logical property group with a flow-relative mapping is
  between them, and `setProperty()` moves a declaration after the declarations of its logical property group that have
  another mapping logic (https://drafts.csswg.org/cssom/#set-a-css-declaration).
- MathML: `math-depth` (`auto-add` adds 1 when the inherited `math-style` is `compact`, `add()` adds to the inherited
  value) and `font-size: math`, which scales the inherited font size by the `scriptPercentScaleDown` and
  `scriptScriptPercentScaleDown` of the OpenType MATH table of the inherited first available font for the first two levels
  (0.71 per level otherwise, https://w3c.github.io/mathml-core/#the-math-script-level-property). The float of a child of a
  MathML box whose display is `math`/`block math` does not blockify its display (`float` does not apply there).
- The default font size of the `monospace` generic family is 13px, as in the browsers (16px for the other families): when
  `font-family` is `monospace` alone, a `font-size` keyword (declared or inherited) is mapped with that default (`medium` is
  13px, the table of Blink and WebKit), and a size that is relative to the one of the parent (a percentage, `em`, `ex`...,
  `larger`, `smaller`, or inherited from such a size) is scaled by 13/16 when the parent does not use the monospace family
  (and by 16/13 from a monospace parent to another family). The absolute lengths, the sizes relative to an absolute length
  and the units relative to the root element (`rem`...) are not changed (`ComputedStyle.AdjustFontSizeToGenericFamily`,
  `FontSizeOrigin`). E.g. `pre` and `code` are 13px in a 16px text, `monospace, monospace` keeps 16px.
- Specific rules: the `font-size` keywords and relative sizes, `font-weight` (`bolder`, `lighter`), `font-width`,
  `line-height`, the blockification of `display` (root element, floats, absolutely positioned elements, flex and grid items,
  and the children of a `display: layout()` box unless the `childDisplay` of its registered layout is `normal`,
  whose parent box is the box of the nearest ancestor that is not `display: contents`; an `inline` (`run-in`) box whose
  `writing-mode` differs from the one of its parent box is an `inline-block` (`block`),
  [CSS Writing Modes](https://drafts.csswg.org/css-writing-modes-4/#block-flow);
  the multi-keyword values are serialized in their shortest form (`layout(name)` and `inline layout(name)` of the
  [CSS Layout API](layout.md#layout-api-containers-csslayoutlayoutapi-csslayoutapi) keep the case of the name), a vertical `-webkit-box` whose lines are clamped by
  `line-clamp` or `-webkit-line-clamp` is a `flow-root`, the `math` display of an element that is not a MathML element is
  `inline` or `block`, `contents` computes to `none` on a MathML element and on the replaced elements and form controls of
  HTML (`br`, `wbr`, `meter`, `progress`, `canvas`, `embed`, `object`, `audio`, `iframe`, `img`, `video`, `frame`,
  `frameset`, `input`, `textarea`, `select`), an `audio` element without `controls` computes to `none` when scripting is
  enabled (it is [not exposing a user interface](https://html.spec.whatwg.org/multipage/rendering.html#embedded-content-rendering-rules)), and a `textarea` is an `inline-block`), `overflow` (the resolved value of `visible` is `auto` when the
  other axis is neither `visible` nor `clip`), `overflow-clip-margin` (the default `padding-box` and a zero length are omitted),
  the box alignment properties (`first baseline` is `baseline`, `justify-items: legacy` takes the inherited legacy position
  or is `normal`, `place-content: baseline` sets `justify-content` to `start`), the gaps (clamped to zero),
  the filter functions of `filter` and `backdrop-filter` (default arguments, percentages as numbers, amounts clamped),
  the shorthands `white-space` (the legacy keywords and the combinations of `white-space-collapse` and `text-wrap-mode`) and
  `vertical-align` (`baseline-source`, `alignment-baseline`, `baseline-shift`), `text-emphasis-style` (the omitted shape is
  `circle`, or `sesame` in vertical writing modes), the SVG 1.1 values of `writing-mode` (`lr`, `lr-tb`, `rl`, `rl-tb` are
  `horizontal-tb`, `tb` and `tb-rl` are `vertical-rl`), the legacy `sideways-right` of `text-orientation` (`sideways`),
  `text-combine-upright: digits` (`digits 2` by default, `none` when a math function is outside of 2–4), the
  `text-decoration` shorthand (its `currentcolor` is omitted),
  `box-shadow` (a longhand, as in the browsers: the color first, four lengths, then `inset`), `box-shadow-offset` (two
  equal offsets are one length), the `corner-*-shape` keywords (the equivalent `superellipse()`: `round` is
  `superellipse(1)`, `bevel` is `superellipse(0)`...), the corner shorthands (`corner`, `corner-top`, `corner-top-left` and
  the logical ones: the grammar of the browsers and of WPT, newer than webref: the corners are separated by slashes and
  each one is `normal` or a radius and a shape, e.g. `corner: 10px bevel / normal`; a corner without radius whose shape is
  round is serialized as `normal`), `border-shape` (the default reference boxes are omitted:
  `half-border-box` for a single shape, `border-box` for the outer shape and `padding-box` for the inner one; two
  identical shapes are one shape; the radii of `circle()` and `ellipse()` can be `closest-corner` and `farthest-corner`
  in all the properties that take a basic shape: for one radius of an ellipse, the distance to the corner, as in Blink), the side shorthands of `border-radius` (`border-top-radius`,
  `border-block-start-radius`..., in their shortest form), `outline-width` (it does not
  depend on `outline-style`; the computed `outline` has its three components), `outline-color` (`auto` computes to
  `currentcolor` unless `outline-style` is `auto`), `caret-color` (the used value of `auto` is `currentcolor`), the
  `border-image` shorthand (expanded and serialized in its shortest form),
  the easing functions (`step-end` is `steps(1)`, `step-start` is `steps(1, start)`, the default position `end` of
  `steps()` is omitted, and the number of steps is at least 1, 2 with `jump-none`; the x values of `cubic-bezier()` are
  clamped to [0, 1]; the computed `linear()` has an input percentage for each stop: a stop with two percentages is split,
  the first and last default to `0%` and `100%`, and the others are spread evenly), `animation-range` (the default offsets
  `0%` and `100%` of a timeline range name are omitted; the omitted end is the timeline range name of the start), the
  resolved value of `animation-duration: auto` (`0s` when `animation-timeline` is `auto`), the `animation` and
  `transition` shorthands (the components in their canonical order, without their initial value; `animation` resets
  `animation-timeline` and `animation-range`),
  `float`, the border and outline widths (snapped, zero when the style is `none`; `hairline` is one device pixel), `text-align: match-parent`
  (`text-align-last: match-parent` computes to the computed value of the parent, `auto` when the parent has `auto`),
  `letter-spacing` (`normal` computes to `0px`; "for legacy reasons, a computed letter-spacing of zero yields a resolved
  value (getComputedStyle() return value) of normal", also in `computedStyleMap()` as Chrome and Safari), `text-fit` (the
  default `consistent` keyword is omitted, as Blink),
  `opacity`, the positions (`center top` is `50% 0%`, `right 20%` is `80%` and `right -10px` is `calc(100% + 10px)`), the
  gradients (the positions are computed, the default values `at center`, `farthest-corner`, `ellipse` and `from 0deg` are
  omitted, the negative radii are clamped; only the colors of the `-webkit-` prefixed gradients and of `-webkit-gradient()`
  are computed, as in the browsers), `content: normal` of `::before` and `::after`, `text-shadow` (a
  color, `currentcolor` when omitted, and three lengths), the track lists of `grid-template-rows` and
  `grid-template-columns` (the lengths are absolutized, `repeat()` and the line names are kept; as in the browsers, a
  `repeat(auto-fill | auto-fit)` with intrinsic sizes, which is tentative, computes to `none` when the element is not a grid
  lanes container), the basic shapes of `clip-path`, `shape-outside` and `offset-path` (the default arguments and
  reference box are omitted, the positions are offsets, the radii are clamped to zero, `rect()` and `xywh()` are the
  equivalent `inset()`; in `shape()`, the control points are canonical positions or coordinate pairs without their default
  reference, the options of `arc` are in the order of the grammar, and the logical and physical keywords of the positions and
  of `hline`/`vline` are percentages), `offset-rotate: reverse` (`auto 180deg`), `mask-size` (a single size is completed with `auto`,
  the negative sizes are clamped to zero), the request URL modifiers of `url()` and `src()` (serialized in the order
  `cross-origin()`, `integrity()`, `referrer-policy()`; see [Request URL modifiers](#request-url-modifiers)), and the fragment-only URLs
  (`url("#foo")`), which are not resolved, `image-set()` (a string is a `url()`, the resolution is in `dppx`, `1dppx`
  when omitted), `paint()` (the function is kept as specified: the name of the paint is an identifier, not a color
  keyword, and the arguments are computed with the syntaxes of the paint class when the image is drawn, see
  [Paint worklet images](rendering.md#paint-worklet-images)), `clip` (the legacy `rect()` without commas is serialized with commas), the `<position-area>` of
  `position-area` and `position-try-fallbacks` (two identical keywords are one keyword: `start start` is `start`), the
  counters (the omitted integer of `counter-increment` is `1`, of `counter-reset` and `counter-set` `0`, except for a
  `reversed()` counter; `none` is not a counter name nor a counter style name), `symbols()` (the default `symbolic` type is
  omitted; the `alphabetic` and `numeric` types need two symbols), the `list-style` shorthand (a `none` sets
  `list-style-image` and `list-style-type` when they are not otherwise set; the initial values are omitted), the color
  stops with two positions of the computed gradients (two color stops: `red 10% 20%` is `rgb(255, 0, 0) 10%,
  rgb(255, 0, 0) 20%`), the `size` descriptor of `@page` (lowercase keywords, the page size first, without the default
  `portrait`: `letter portrait` is `letter`), `color-scheme` (`only` is last; `normal` and `only` are not color scheme
  names), `scroll-snap-type` (the default `proximity` is omitted), `scroll-snap-align` (two identical alignments are
  one), the `font-synthesis` shorthand (of `font-synthesis-weight`, `-style`, `-small-caps` and `-position`; `oblique-only`
  sets `font-synthesis-style`), `font-family` (an unquoted family name cannot contain a generic family keyword:
  `cursive serif` is invalid), `font-language-override` (a string of 1 to 4 ASCII characters) and the `override-colors`
  descriptor of `@font-palette-values` (absolute colors: no `currentcolor`, system colors nor `light-dark()`). The
  page-margin rules (`@top-center`...) accept the properties of `@page` and those of the
  generated content and of the text (`content`, `counter-*`, `width`, `height`, `text-*`, `vertical-align`...).
  Also the `<integer>` properties (the
  math functions are rounded, e.g. `reading-order: calc(34.1)` is `34`), `text-justify: distribute` (a parse-time alias of
  `inter-character`), `overflow: overlay` (a legacy value alias of `auto`), the legacy shorthands `page-break-before`, `page-break-after` and `page-break-inside` (`always` is
  `page`; they never serialize a declaration block, and are empty when the `break-*` value has no legacy equivalent, e.g.
  `column`), and the `font` shorthand (it resets `font-size-adjust`, `font-kerning`, `font-feature-settings`,
  `font-language-override`, `font-optical-sizing` and `font-variation-settings`, and is not serialized when one of them is
  not initial).
- Logical properties share their computed value with the physical properties of their logical property group, mapped with
  the `writing-mode` and the `direction` of the element.

## Forced colors mode

`ViewportOptions.ForcedColors` enables the [forced colors mode](https://drafts.csswg.org/css-color-adjust-1/#forced) (the
high contrast themes of the operating systems) in the documents of the browsing context and of its nested browsing
contexts: the user agent enforces a color palette on the pages, which they can detect and opt out of.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions
    {
        ForcedColors = true,

        // Optional: the palette. The default is ForcedColorsTheme.Light, or ForcedColorsTheme.Dark when
        // PreferredColorScheme is dark (the system colors of the color schemes)
        ForcedColorsTheme = ForcedColorsTheme.Dark with
        {
            Canvas = Color.Black,
            CanvasText = Color.Yellow,
            LinkText = Color.Cyan,
        },
    },
});
```

- **Palette**: a `ForcedColorsTheme` gives the values of the [system colors](https://drafts.csswg.org/css-color-4/#css-system-colors)
  (`Canvas`, `CanvasText`, `LinkText`, `VisitedText`, `ActiveText`, `ButtonFace`, `ButtonText`, `ButtonBorder`, `Field`,
  `FieldText`, `Highlight`, `HighlightText`, `SelectedItem`, `SelectedItemText`, `Mark`, `MarkText`, `GrayText`,
  `AccentColor`, `AccentColorText`; `System.Drawing.Color` values, whose alpha is ignored). In forced colors mode the system
  color keywords (and the deprecated ones, which map to them) have the colors of the palette whatever the color scheme of
  the element.
- **Media features**: `(forced-colors: active)` matches. `prefers-contrast` is the contrast of the palette instead of
  `ViewportOptions.PreferredContrast`: `more` when the [contrast ratio](https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio)
  of `CanvasText` on `Canvas` is at least 7, `less` when it is at most 2.5 (the thresholds of Chromium), `custom`
  otherwise. `prefers-color-scheme` is `dark` when the Lab lightness of `Canvas` is below 33%, `light` above 67%, and
  `ViewportOptions.PreferredColorScheme` between them.
- **`forced-color-adjust`** (`auto | none | preserve-parent-color`, inherited): the colors of an element are forced when its
  computed value is `auto`. As specified, the user agent style sheet sets `preserve-parent-color` on the `svg` elements and
  `auto` on the `foreignObject` elements (the colors of the SVG content are kept). With `preserve-parent-color`, a `color`
  that inherits from the parent (no cascaded value, `currentcolor`, `inherit`...) computes to the used color of the parent.
  A highlight pseudo-element follows its originating element (the property does not apply to it).
- **Computed values** of an element with forced colors: `box-shadow` and `text-shadow` are `none`, `color-scheme` is
  `light dark`, `scrollbar-color` is `auto`, `accent-color` is `auto` unless it is a system color, `background-image` is
  `none` unless the value contains a `url()` (a gradient is removed), and `font-variant-emoji: normal | unicode` is `text`.
  The computed values of the colors are not changed: they are inherited and returned by `computedStyleMap()`.
- **Used values**: a color that is not a system color is forced to a system color when it is used (painted). A color is a
  system color when the declaration it comes from (the cascaded value of the element after the substitution of `var()`, or
  of the ancestor it is inherited from) is a system color keyword and no animation changes it; `color-mix()` of system
  colors, the relative colors and the animated colors are forced.
  - `color` is forced to the color the element has without author style sheet: the system color that the user agent style
    sheet gives to it or to its nearest ancestor (`LinkText` for a link, `ButtonText` in a button, `FieldText` in a text
    field, `MarkText` in `mark`, `GrayText` for a placeholder), `CanvasText` otherwise.
  - `background-color` is forced to the system color that the user agent style sheet gives to the background of the element
    (e.g. `ButtonFace`, `Field`, `Mark`), or else to the color [paired](https://drafts.csswg.org/css-color-4/#css-system-colors)
    with the forced `color` (`Canvas` for `CanvasText` and the link colors, `ButtonFace` for `ButtonText`...), with the alpha
    of the original color: a transparent background stays transparent.
  - `border-*-color` and `outline-color` (other than `auto`) are forced to the system color that the user agent style
    sheet gives to them (`ButtonBorder` for a button), or else to the forced `color`: a transparent border becomes visible.
  - `text-decoration-color`, `text-emphasis-color`, `column-rule-color`, `row-rule-color`, `caret-color`,
    `-webkit-text-fill-color`, `-webkit-text-stroke-color`, and the SVG colors `fill`, `stroke` (the color or the fallback
    color; `none` and the paint servers are kept), `stop-color`, `flood-color` and `lighting-color` are forced to the forced
    `color`.
  - The highlight pseudo-elements (`::selection`, `::highlight()`, `::target-text`...) whose originating element has forced
    colors: a background that is not transparent is `Highlight` (`Mark` for the target text, `Canvas` for the spelling and
    grammar errors), opaque, and the color of the text is then `HighlightText` (`MarkText` for the target text; the forced
    color of the originating element for the spelling and grammar errors, and for a highlight without background); the
    unstyled selection is `HighlightText` on `Highlight`.
- **Resolved values**: `getComputedStyle()` returns the forced colors of the properties whose resolved value is the used
  value (`color`, `background-color`, `border-*-color`, `outline-color`, `caret-color`), and the computed values of the
  other properties (their `currentcolor` is the forced color).
- The painting also adds backplates behind the text: see [rendering.md](rendering.md#forced-colors-mode).

The color of a visited link is forced to `VisitedText` (see [Visited links](#visited-links): for the painting only). Not
implemented: the colors of the native appearance of the form controls (the frame of a checkbox, the track of a slider or of a progress bar keep their colors:
only the text, the background and the borders of the controls are forced), and `-webkit-tap-highlight-color`.

## Visited links

No link is visited by default: every link matches `:link`, and the rendering of a document does not depend on the documents
loaded before it. `BrowsingContextOptions.VisitedLinks` gives the browsing contexts a history of visited links
([Selectors](https://drafts.csswg.org/selectors-4/#link)):

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    // The visited links of the session (the store can be shared by several browsing contexts, or implemented by the host)
    VisitedLinks = new InMemoryVisitedLinkStore(),
});
```

- **History**: a `VisitedLinkStore` answers `IsVisited(VisitedLink)` and receives the navigations of the browsing contexts
  in `Add(VisitedLink)`; its `Version` changes when its answers may have changed (the styles are computed again). An entry
  is the triple of the partitioned history of [Selectors, appendix C](https://drafts.csswg.org/selectors-4/#visited-partitioning):
  the URL of the link (with its fragment), the origin of the document that contains the link and the site of its top-level
  document. The navigations to a document, the fragment navigations and the URL changes of `history.pushState()` and
  `history.replaceState()` are recorded for the destination (its URL, the origin of the document and the site of its
  top-level document, when the document is a top-level document or has the origin of its top-level document) and, when a
  document started the navigation (a link, a form, a script), for that document (the same URL, its origin and the site of
  its top-level document). The traversals, the reloads, the prerendered documents that are not activated and the `about:`,
  `blob:`, `data:` and `javascript:` URLs are not recorded. `InMemoryVisitedLinkStore` matches the whole triple: a link is
  only visited in the documents that could observe its navigation (`new InMemoryVisitedLinkStore(partitioned: false)`
  matches the URL alone, as the browsers did before they partitioned their history). A document whose origin is opaque
  (e.g. a `file:` or `data:` document) has no visited link.
- **Privacy**: Selectors lets the user agents choose how they protect the history; as the browsers do (and as the tests of
  the Web Platform Tests expect), the scripts never observe which links are visited. The computed style of an element is
  computed as if every link was unvisited: `:visited` never matches for `getComputedStyle()`, `computedStyleMap()`,
  `matches()`, `querySelector()`, the layout, the hit testing, the transitions and the animations, and
  `elementFromPoint()`.
- **Visited style**: an element in a visited link (the link itself, and the elements that inherit from it, up to another
  link) has a second style, computed on demand by the painting: its cascade is the cascade of the element where the link
  matches `:visited` and not `:link` (also in `:is()`, `:not()`, `:where()`, `&` and `@scope`; the body `vlink` attribute
  replaces `link`), and it inherits from the visited style of the parent. Only the nearest link among the inclusive
  ancestors of the subject of a selector can match `:visited`: the links matched after a sibling combinator or in the
  argument of `:has()` are unvisited, as in the browsers. Only the colors come from the visited style: `color`,
  `background-color`, `border-*-color`, `outline-color`, `column-rule-color`, `row-rule-color`, `text-decoration-color`,
  `text-emphasis-color`, `caret-color`, `-webkit-text-fill-color`, `-webkit-text-stroke-color`, and the colors of `fill` and
  `stroke`; the other properties and the custom properties of the `:visited` rules are ignored (the values of the style of
  the element are used, e.g. by a `var()` of a visited color).
- **Painted colors**: the painted color is the visited color with the alpha of the unvisited color (a link cannot be made
  to appear or disappear), and a fully transparent visited `background-color` is ignored. `currentcolor` is the painted
  color in every property (gradients, shadows, SVG `stop-color`, `flood-color` and `lighting-color`...). `fill` and
  `stroke` keep the paint of the unvisited style when it is not a color (`none`, a paint server); the visited style has a
  single color for the column and row rules (as in Chromium, a list of colors in a `:visited` rule is `currentcolor`),
  which replaces each color of the unvisited list. The highlight pseudo-elements (`a:visited::selection`), the forced
  colors (`VisitedText`), the animations and the important declarations apply to the visited style as to the style of the
  element.

Not implemented: the tainting of a canvas by an SVG filter that uses a visited color (the canvas does not support the
references to SVG filters).

## Custom functions

The `@function` rules of [CSS Mixins](https://drafts.csswg.org/css-mixins-1/#defining-custom-functions) define custom
functions, called by a `<dashed-function>` (`--name(arguments)`) wherever `var()` is allowed:

```css
@function --negative(--value <length>: 0px) returns <length> {
  result: calc(-1 * var(--value));
}
div { margin-left: --negative(1em); }
```

- **Rules**: the parameters (with a type, `<length>` or `type(<length> | auto)`, and a default value that must match it),
  the return type, the local variables (custom properties) and the `result` descriptor, the conditional rules of the body
  (`@media`, `@supports`, `@container`). `!important` and the other declarations are invalid in the body. The
  `CSSFunctionRule` exposes the name, the parameters (`getParameters()`) and the return type, and the runs of declarations
  are `CSSFunctionDeclarations` rules whose `style` is a `CSSFunctionDescriptors`. The `@function` rule of the strongest
  cascade layer wins, then the last one; the name is a tree-scoped name: it is looked up in the style sheets of the tree
  of the declaration, then in the trees of the shadow hosts up to the document, and the references of the body are looked
  up from the tree of the `@function` rule.
- **Evaluation**: the arguments are substituted in the calling context (an argument containing commas is written in a `{}`
  block), then the function body applies to a hypothetical child of the calling context. An argument that does not match
  the type of its parameter is replaced by the default value (a default value sees the previous parameters); a missing
  argument without default value, or too many arguments, makes the call invalid. `var()` refers to the local variables,
  then to the parameters, then to the variables of the calling context (the local variables and the parameters of the
  calling functions, then the custom properties of the element). In a local variable, `initial` is the value of the
  parameter, `inherit` (and `inherit()`) the value in the calling context, and the other CSS-wide keywords are invalid.
  The typed parameters and the result are computed with their type (relative lengths resolve against the element); the
  CSS-wide keywords of an untyped result are left unresolved. The conditional rules are evaluated for the element.
- **Cycles**: a function that calls itself (directly, through a local variable, a default value or another function),
  and the local variables in a cycle, are invalid, even when they are not used by the result; a cycle through a custom
  property of the element invalidates the custom property.

## Mixins

The `@mixin` rules of [CSS Mixins](https://drafts.csswg.org/css-mixins-1/#defining-mixins) define mixins, applied in a
style rule by an `@apply` rule. Mixins are experimental: the library implements the model of the browsers and of the
web-platform-tests, where the declarations and the rules that are applied are in an `@result` rule of the mixin body
(the specification no longer defines it):

```css
@mixin --bordered(--color <color>: black) {
  --width: 2px;                   /* a local variable */
  @result {
    border: var(--width) solid var(--color);
    &:hover { @contents; }        /* the contents block of the @apply rule */
  }
}
button { @apply --bordered(red) { color: red; } }
```

- **Rules**: the mixin body contains the local variables (the custom properties; the other declarations, `!important`
  and the style rules are invalid), the `@result` rules and the conditional rules (`@media`, `@supports`, `@container`)
  that contain them. An `@result` rule accepts the contents of a style rule (but not `@layer`), and the `@contents` rules.
  `@apply --name;`, `@apply --name(arguments)` and `@apply --name(arguments) { contents }` are only valid in a style rule
  (or a nested group rule); an argument containing a top-level `!` (e.g. `!important`) makes it invalid. The parameters
  are the ones of `@function` (the parentheses are optional when there is none). The CSSOM rules are `CSSMixinRule`
  (`name`, `getParameters()`, `contents`), `CSSApplyStatementRule` and `CSSApplyBlockRule` (`name`, `getArguments()`),
  `CSSContentsStatementRule` and `CSSContentsBlockRule`, and the `@result` rule is a `CSSGroupingRule`; the runs of
  declarations of the mixin body and of the blocks are `CSSNestedDeclarations` rules.
- **Application**: the rules of the `@result` rules of the mixin (whose `@media` and `@supports` conditions are true;
  the `@container` conditions are evaluated for each element) replace the `@apply` rule, in its style rule and cascade
  layer: the nested rules are relative to the style rule, and the declarations have its specificity. A `@contents` rule
  is replaced by the contents block of the `@apply` rule, or else by its own fallback block. The `@mixin` rule of the
  strongest cascade layer wins, then the last one; the name is a tree-scoped name (like `@function`). An `@apply` rule
  does nothing when the mixin is unknown, when it has more arguments than the mixin has parameters, or when it applies a
  mixin that is already being applied (a cycle).
- **Evaluation**: the values of the declarations of the mixin are substituted for each element in a frame like the one
  of a custom function: `var()` refers to the local variables, then to the parameters, then to the variables of the
  mixin whose `@result` rule applies it, then to the custom properties of the element. The arguments are substituted in
  the calling context, and a missing argument without default value is the guaranteed-invalid value. The custom functions
  called by the values do not see the variables of the mixin, and the variables are not custom properties of the element.

The `@private` rule (`CSSPrivateRule`, in a style rule) is parsed and serialized (its declarations are
`--name <type>?: value`), but its private properties are not applied.

## Registered custom properties

The custom properties can be registered with a syntax, an inherit flag and an initial value ([CSS Properties and Values
API](https://drafts.css-houdini.org/css-properties-values-api/)), by `CSS.registerProperty()` or by a `@property` rule:

```csharp
// In a script: CSS.registerProperty({ name: '--size', syntax: '<length>', inherits: false, initialValue: '0px' })
Css.RegisterProperty(document, new PropertyDefinition { Name = "--size", Syntax = "<length>", Inherits = false, InitialValue = "0px" });
Console.WriteLine(style.GetPropertyValue("--size")); // e.g. 20px for 2em
```

- **Registration**: the properties registered by `CSS.registerProperty()` (the `[[registeredPropertySet]]` of the document;
  `Css.RegisterProperty(definition)` uses the document of the current global object) win over the `@property` rules,
  and the last valid `@property` rule wins (in layer order). The `@property` rules of the shadow trees are registered
  globally (the rules of the document win). `CSS.registerProperty()` throws a `SyntaxError` when the name is not a custom
  property name, the syntax is invalid or the initial value is invalid, and an `InvalidModificationError` when the
  property is already registered. A `@property` rule with an invalid descriptor is dropped.
- **Syntax**: the [syntax strings](https://drafts.css-houdini.org/css-properties-values-api/#syntax-strings) are parsed
  as specified: the universal syntax `*`, the supported data types (`<length>`, `<number>`, `<percentage>`,
  `<length-percentage>`, `<string>`, `<color>`, `<image>`, `<url>`, `<integer>`, `<angle>`, `<time>`, `<resolution>`,
  `<transform-function>`, `<custom-ident>`, the pre-multiplied `<transform-list>`) and the identifiers (case-sensitive,
  not a CSS-wide keyword or `default`), with the `+` and `#` multipliers, combined with `|`. A value matches the first
  component that accepts it.
- **Initial value**: it is required unless the syntax is universal (as in the browsers and the web-platform-tests; the
  current draft makes it optional), it must match the syntax, not be a CSS-wide keyword, and be computationally
  independent: no `var()` or `attr()`, and no font-relative unit (`em`, `rem`, `lh`...) unless the syntax is universal.
  The viewport units are allowed. The `inherits` member of `PropertyDefinition` is `true` by default, as in the current
  draft (the browsers require it).
- **Computed values**: a value that does not match the syntax (after the substitution of `var()`) is invalid at
  computed-value time: the property is unset (inherited or initial value). The values are computed per component of the
  lists: absolute lengths, canonical units, math functions simplified (`<integer>` is rounded), colors computed
  (`currentcolor` is resolved, also in `color-mix()` and the relative colors), absolute URLs (relative to the style sheet
  of the declaration), identifiers and strings as specified, the transform functions with absolute lengths (serialized
  in camel case, e.g. `translateX(10px)`). `var()` substitutes the computed value of a registered property.
- **Cycles**: the registered properties of a `var()` cycle are unset. A registered property whose value uses
  font-relative units (`em`, `ex`, `cap`, `ch`, `ic`, `lh`; `rem`... on the root element) referenced by `font-size`
  (`lh` by `line-height`), directly or through the custom properties of the element, forms a
  [dependency cycle](https://drafts.css-houdini.org/css-properties-values-api/#dependency-cycles-via-relative-units):
  the property, the registered property and the custom properties between them are invalid at computed-value time.
- **Animations**: the registered properties are interpolated by their syntax (the lists `+` and `#` item by item when
  they have the same length), the unregistered custom properties discretely.
- **Not supported**: the Typed OM reification (`computedStyleMap()`), and the `ident()` function.

## Fonts

`Document.Fonts` is the [font source](https://drafts.csswg.org/css-font-loading/#font-source) of the document (the CSS
Font Loading API): a `FontFaceSet` of the CSS-connected `FontFace` objects of the `@font-face` rules of the style sheets
of the document, in document order, followed by the font faces added by `Add()` (the font faces of the rules that do
not apply, e.g. in a `@media` or `@supports` rule whose condition is false, are in the set but the font matching ignores
them). The attributes of a CSS-connected font
face reflect the descriptors of its rule in both directions; it is no longer CSS-connected when its rule is removed or
its `src` changes.

Loading a font face (`FontFace.Load()`, `FontFaceSet.Load()`) fetches its sources in order with the `DocumentLoader` of
the browsing context (`RequestDestination.Font`, a CORS request: a cross-origin font must allow the origin with its
`Access-Control-Allow-Origin` header; the `data:` URLs are processed by the library) until one of them is a valid font
file: an OpenType or TrueType font, a font collection, a WOFF or a WOFF2 file, decoded and validated by the managed font
reader (see [Fonts in docs/layout.md](layout.md#fonts)); like the browsers, a file whose structure is invalid is
rejected, even when it starts with a font signature. The request URL modifiers of a source apply to its request (see
[Request URL modifiers](#request-url-modifiers)). The `local()` sources are the bundled fonts (see
[Bundled fonts](layout.md#bundled-fonts)), matched by their full name or PostScript name (e.g. `local(Ahem)`,
`local("Tinos Bold")`): the fonts installed on the machine are never used. A font face created from binary data is
loaded when its data is a valid font file.

```csharp
var face = new FontFace("My Font", "url(my-font.woff2) format(woff2)");
document.Fonts.Add(face);
await document.Fonts.Load("16px 'My Font'");
```

`FontFaceSet.Ready` completes once the document is loaded, has no
[pending layout operations which might cause the user agent to request a font](https://drafts.csswg.org/css-font-loading/#fontfaceset-pending-on-the-environment)
and no font face is loading. When the document is loaded, the
font faces used by its text (the first family of the `font-family` of an element with text that has font faces in the
set, also in the shadow trees) and the first available font of the parent of an element with `font-size: math` (its
`MATH` table scales the font size, also for an element without text) are loaded, as when the user agent renders the document: then at each rendering opportunity after the tree or the
styles change, and when `Ready` is read while such a rendering is pending. The font faces are also loaded when the font
matching needs them: the first available font of the font-relative units (`ex`, `ch`...) and of the line boxes, the font
of each character, the next family of the list when a font face fails to load or does not have a character (see
[Font matching](layout.md#font-matching)); while a font face
loads, the next families are used, and the computed styles are computed again once it is loaded. These font faces are
only known by the layout, so a document that is rendered (it has a rendering backend, or its layout was already computed)
and whose set has a font face that is not loaded yet updates its layout before the set is switched to loaded: when the
document is loaded, when the last loading font face is loaded or fails (the layout depends on it), and when `Ready` is
read while it is resolved (as Blink). The set stays `loading`, and `Ready` pending, while the font faces requested by
that layout load: the scripts and the screenshots that wait for `document.fonts.ready` see the text with its web fonts.
The font faces of the `@font-face` rules of the shadow trees (tree-scoped, see [Font matching](layout.md#font-matching))
are not in the set, but their loads are tracked by it: a rendered document with shadow roots updates its layout too, and
`Ready` waits for the faces it requests. The documents without unloaded font face, and the ones that are not rendered,
get no extra layout. `Check()` and `Load()`
select the font faces of each family with the font matching algorithm (the `font-width`, `font-style` and `font-weight`
descriptors and their ranges, and the `unicode-range` of the faces). `Check()` returns whether the font faces that match a
font are loaded, and the `loading`, `loadingdone` and `loadingerror` events are fired as specified. When several
`@font-face` rules define the same font face (the same family, style, weight, stretch and unicode-range), only the one in
the last [cascade layer](https://drafts.csswg.org/css-cascade-5/#layering) (the unlayered rules win), then the last one,
matches a font. The `font-feature-settings` descriptor of a font face is applied by the text shaping (see
[layout.md](layout.md#text-shaping)); `FontFace.features`, `variations` and `palettes` are not implemented.

A worker has its own font source (`self.fonts`, `WorkerGlobalScope.Fonts`): a `FontFaceSet` without CSS-connected font
face, which holds the `FontFace` objects that the scripts of the worker create and add to it. The font faces of a worker
are fetched with the environment of the worker (its URL as base URL and referrer, its content security policies, the
`DocumentLoader` of its browsing context) from a task of its event loop, and they are only used on the thread of the
worker. The set is never pending on the environment: it is `loaded`, and `ready` is resolved, unless one of its font
faces is loading. The text of the `OffscreenCanvas` objects of the worker uses the font faces of the set (see
[rendering.md](rendering.md)): a font face that is not loaded yet starts to load when a text needs it, and the next
family of the list is used meanwhile.

The [`@font-feature-values`](https://drafts.csswg.org/css-fonts-4/#font-feature-values) rules are parsed
(`CssFontFeatureValuesRule`): their feature value blocks (`@stylistic`, `@styleset`, `@character-variant`, `@swash`,
`@ornaments`, `@annotation`, `@historical-forms`) are merged by type into `CssFontFeatureValuesMap` objects (a name and
its non-negative integers; the declarations whose number of values is not valid for the type are ignored, and
`Set()` throws an `InvalidAccessError` for them), serialized in the order of their first block. The feature values of the
rules that apply give the features of `font-variant-alternates` (the strongest cascade layer wins, see
[layout.md](layout.md#text-shaping)). `CSS.supports("at-rule(@swash)")` recognizes the feature value blocks.

The [`@font-palette-values`](https://drafts.csswg.org/css-fonts-4/#font-palette-values) rules (`CssFontPaletteValuesRule`)
that apply define the palettes that `font-palette` names for the color glyphs of the fonts of their `font-family`
descriptor (see [layout.md](layout.md#color-fonts)).

## Highlights

`Document.Highlights` (`CSS.highlights` in the scripts) is the [highlight registry](https://drafts.csswg.org/css-highlight-api-1/#highlight-registry)
of the document: a map of the custom highlights (`Highlight`, a set of ranges with a `Priority` and a `Type`) by name. As
for the JavaScript `Map` and `Set`, the entries are enumerated in insertion order (replacing the highlight of a name keeps
its position), and an enumeration sees the changes made while it is in progress.

```csharp
var range = document.CreateRange();
range.SelectNodeContents(document.QuerySelector("p")!);
document.Highlights.Set("search", new Highlight(range) { Priority = 1 });

var style = document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!, "::highlight(search)");
```

The registered highlights do not change the computed styles; `highlightsFromPoint()` hit tests the client rectangles of their
ranges (see [Geometry](#geometry)). The target text of the document (`::target-text`) is given by the
[text directives](https://wicg.github.io/scroll-to-text-fragment/) of the URL it is navigated to (`#:~:text=[prefix-,]start[,end][,-suffix]`,
several separated by `&`): the ranges of the directives that are found in the rendered text of the document, whether the
document is allowed to scroll to the first one or not (see [Text directives](browsing-context.md#text-directives) for
the search, the target element and the restrictions). The highlight pseudo-elements (`::selection`, `::search-text`, `::target-text`,
`::spelling-error`, `::grammar-error` and `::highlight(name)`) follow the [highlight cascade](https://drafts.csswg.org/css-pseudo-4/#highlight-cascade):

- Only the [applicable properties](https://drafts.csswg.org/css-pseudo-4/#highlight-styling) are cascaded: `color`,
  `background-color`, `text-decoration` and its longhands, `text-underline-position`, `text-underline-offset`,
  `text-shadow`, `stroke-color`, `fill-color`, `stroke-width` and the custom properties. The other properties have the
  computed values of the originating element (e.g. `font-size` and `line-height`, which resolve the `em` and `lh` units).
- The applicable properties without a cascaded value (or with `inherit` or `unset`) inherit from the same highlight
  pseudo-element of the parent element in the flat tree, whether they are inherited properties or not. The highlight
  pseudo-elements of the root element inherit `currentcolor` as `color`, and the initial values. The custom properties
  inherit from the originating element.
- `currentcolor` is the computed value of `color` when it is specified or inherited, and its used value is the color of
  the originating element (there are no highlight overlays below it).
- `::highlight(*)` matches every custom highlight, with a zero specificity (`::highlight(\*)` is the highlight named `*`).
- No pseudo-class can follow a highlight pseudo-element (`::selection:hover` is invalid), except `:current` (also in
  `:not()`) after `::search-text`. There is no active search result: `::search-text:current` never matches, and
  `::search-text:not(:current)` matches.

## Container queries

`@container` rules ([CSS Conditional 5](https://drafts.csswg.org/css-conditional-5/#container-queries)) are evaluated
for each element: the query container of each `<container-condition>` is the nearest ancestor in the flat tree (an
inclusive ancestor of the originating element for a pseudo-element) whose `container-name` matches and whose
`container-type` supports all the features of the query. A condition that contains an unknown feature or a
`<general-enclosed>` selects no container (it is unknown). The `CssContainerRule` exposes the conditions as specified
(`conditions`, `containerName`, `containerQuery`, `conditionText`).

- **Style queries** (`style()`): the computed value of a property (custom or standard, including the shorthands) on the
  container is compared with the value computed with respect to the container; `style(--x)` is true when the value is
  not the initial value. The range syntax (`style(--x > 10px)`) compares numbers, percentages, lengths, angles, times,
  frequencies and resolutions. `var()`, `attr()` and `env()` are substituted with the values of the container. Two colors
  are equal when they are equivalent ([comparing colors](https://drafts.csswg.org/css-color-4/#comparing-color-values)):
  the powerless hue of an achromatic `lch()` or `oklch()` color is missing, and the colors of different color spaces are
  compared in Oklab.
- **Size queries** (`width`, `height`, `inline-size`, `block-size`, `aspect-ratio`, `orientation`) and the
  [container-relative lengths](https://drafts.csswg.org/css-conditional-5/#container-lengths) (`cqw`, `cqh`, `cqi`, `cqb`,
  `cqmin`, `cqmax`): the size of the content box of the container computed by the layout (the scrollbars and the scrollbar
  gutters are not part of it; in the coordinates of the container, divided by its effective zoom). The layout lays out a
  size container before its contents (its size does not depend on them: size containment), then computes the styles of
  its descendants again when they were computed with another size, and builds their boxes again, before laying them out
  (the [style and layout interleaving](https://drafts.csswg.org/css-contain-3/#size-container), see
  [layout.md](layout.md#size-container-queries)). The block size of a `container-type: size` container with an `auto`
  block size is its `contain-intrinsic-size` (0 by default) with its min/max sizes; the block size of an `inline-size`
  container cannot be queried. `getComputedStyle()`, `getAnimations()` and `matchContainer()` compute the layout first when
  the element is in a size container (a forced layout).

  Without layout (the document has no browsing context), or before the first layout of the container, the size is only
  known when the own properties of the container determine it:
  - an absolute `width` or `height`, a percentage of a known size, or a `calc()` of them (with `box-sizing`, the
    paddings, the borders and `min-`/`max-` sizes applied; the containing block of an absolutely positioned box is the
    padding box of its nearest positioned, transformed, filtered or contained ancestor, or the viewport);
  - an `auto` inline size of a block-level box in normal flow whose parent is a block container: it fills the content box
    of its containing block (the viewport for the root element), unless the box establishes an independent formatting
    context and a float may precede it;
  - an `auto` size in an axis with size containment (`container-type: size`, `contain: size`): zero.

  Otherwise (flex and grid items, tables, `auto` sizes of absolutely positioned boxes, shrink-to-fit widths,
  content-based heights...), the size is unknown: the size features evaluate to unknown, and the container query length
  units use the small viewport size. The containers without a principal box (`display: none` or `contents`) and
  the boxes to which layout containment does not apply (inline boxes, tables, internal table and ruby boxes, SVG elements
  other than the outermost `svg`) are not eligible: their size features are unknown, and the length units skip them.
- **Scroll-state queries** (`scroll-state()`): the features (`stuck`, `snapped`, `scrollable`, `scrolled`) of a
  `container-type: scroll-state` container are evaluated against the snapshot of its state taken by the rendering steps
  (after the style and layout update of each frame; the styles are then updated again in the same frame when it changed),
  or by the layout pass that lays out the container for the first time (see docs/layout.md "Scroll-state container queries"):
  a change of the scroll position or of the layout is seen by the styles from the next frame, not by a `getComputedStyle()`
  call made before it. Without browsing context (no layout), the state of every feature is `none`. The `scrolled`
  feature reflects the relative scrolls (`scrollBy()` and the scrolls of the user).
- **Anchored queries** (`anchored()`, [CSS Anchor Positioning 2](https://drafts.csswg.org/css-anchor-position-2/#anchored-container-queries)):
  the `fallback` feature of a `container-type: anchored` container (which has style containment) is evaluated against the
  option of `position-try-fallbacks` applied by its last layout (`none` before its first layout, for the base styles and
  without browsing context); the layout computes again the styles of the descendants that used another option (see
  docs/layout.md "Anchor positioning"), so `getComputedStyle()` sees the option of the current layout.

`Element.matchContainer()` (tentative, proposed in [csswg-drafts#13551](https://github.com/w3c/csswg-drafts/pull/13551))
returns a `ContainerQueryList` that evaluates a comma-separated list of `<container-condition>`s for the element, like a
`@container` rule: `matches` is true when the element is connected and one of the conditions is true, and `query` is the
serialization of the conditions (the empty string when one of them is invalid). A `change` event is fired at the list when
the result changes, when the event loop updates the rendering (after the `change` events of the media query lists), unless
the document of the element is not fully active. A list with event listeners is kept alive as long as its element.

## Layout-dependent values

The [resolved value](https://drafts.csswg.org/cssom/#resolved-values) of some properties is their used value, from the
fragment tree of the layout engine (see [layout.md](layout.md#resolved-values-csslayoutlayoutresolvedvaluescs)): reading
one of them computes the layout when it is out of date (a forced layout); the other properties never do. The used values
are divided by the [effective zoom](https://drafts.csswg.org/css-viewport/#effective-zoom) of the element, as in the
browsers (e.g. `width: auto` with `zoom: 2` and `margin: 0 10px` in a 800px wide viewport is `380px`). When the element
does not generate a box (`display: none`, `display: contents`, in a replaced element, in skipped contents), or when the
property does not apply to its box, the computed value is returned:

- `width` and `height` (and `inline-size`, `block-size`): the used size in pixels of the content box, or of the border box
  with `box-sizing: border-box` (the table box of a table, not the table wrapper box), including the `calc-size()` values,
  the intrinsic size keywords, the min/max constraints and the animated sizes (the size of the whole box for a box split by
  the columns of a multi-column container). The scrollbar gutters are part of the
  content box, so that the value round-trips (`width: 100px; overflow: scroll` is `100px`). They do not apply to the
  non-replaced inline boxes (`width: 50%` on a `span` is returned as is).
- The margins and the paddings (only when they are `auto` or contain a percentage: a length is its used value): the
  percentages are resolved against the inline size of the containing block, and the `auto` margins are their used values:
  for an in-flow block-level box in the inline axis of its block container, the distance between its border box and the
  content box of the container (so it includes the space taken by the floats beside it, as in the browsers, see
  [csswg-drafts#9174](https://github.com/w3c/csswg-drafts/issues/9174)); for an absolutely positioned box, the margins
  resolved by the positioned layout; 0 for the floats and the atomic inlines. As in the browsers, the margin of an
  over-constrained box is its specified value (not the space left, [csswg-drafts#2328](https://github.com/w3c/csswg-drafts/issues/2328)).
  The margins do not apply to the internal table boxes other than the captions, the paddings to the ones other than the cells.
- The insets (`top`, `right`, `bottom`, `left`) of a positioned element: the used value in pixels (see
  [Positioned layout in docs/layout.md](layout.md#positioned-layout-csslayoutpositioned)): for a relatively positioned box,
  an `auto` inset is the negation of the opposite one (0 when both are `auto`); for an absolutely positioned box, an `auto`
  inset is the distance from the edge of the containing block to the margin edge of the box (from its static position when
  both are `auto`); the specified value in pixels when the box is over-constrained; for a sticky positioned box, the
  percentages are resolved against the scrollport and `auto` is returned as is. A static element returns the computed value.
- `transform-origin`, `perspective-origin` and the percentages of the translations of `transform`: they are resolved in
  pixels against the reference box (`transform-box`: the border box, or the content box for `content-box` and `fill-box`;
  for an SVG element, its object or stroke bounding box (the object bounding box when it or one of its descendants has
  `vector-effect: non-scaling-stroke`), or the size of its nearest SVG viewport for `view-box`, the
  viewport of the outermost `svg` element being its content box). The non-replaced inline boxes are not transformable:
  the percentages are returned as is.
- `grid-template-rows` and `grid-template-columns` of a grid container: the used track sizes recorded by the grid layout
  (see [Grid layout in docs/layout.md](layout.md#grid-layout-csslayoutgrid)), in pixels, every track listed (implicit and
  explicit, without `repeat()`, the collapsed repetitions of `auto-fit` as `0px`) with the explicitly-assigned names of the
  lines; a subgridded axis returns `subgrid` and the names explicitly defined on the subgrid for each of its lines
  (`subgrid [a] [] []`). Without layout (no browsing context, an element that is not a grid container or generates no box), the
  computed value is returned. The `grid-template` shorthand is serialized with the computed track lists (the used sizes of
  the implicit tracks cannot be represented with the strings of `grid-template-areas`), but a track list of `none`, which
  is the used sizes of the implicit tracks, as in Blink.
- The accepted @position-try properties (the insets, margins, sizes, min/max sizes, `justify-self`, `align-self`,
  `position-anchor`, `position-area`) of an anchor-positioned box (an absolutely positioned box with an anchor function,
  `position-area` or `position-try-fallbacks`): the values of the position option the layout chose, with the anchor functions
  resolved to lengths (`anchor()`, `anchor-size()`: the style and layout interleaving of
  [CSS Anchor Positioning](https://drafts.csswg.org/css-anchor-position-1/#interleaving), see
  [Anchor positioning in docs/layout.md](layout.md#anchor-positioning-csslayoutpositionedanchorpositioningcs)), also for
  `computedStyleMap()`. The anchor functions of the other boxes compute to their fallback value (or to the initial value of
  the property when they have none).
- `line-height`: a number is returned in pixels (`number × font-size`), `normal` is returned as is.
- `min-width` and `min-height`: `auto` is `0px`, except for the flex and grid items and the boxes with an aspect ratio.
- `scale`, `translate` and `rotate` are returned in their shortest form (the percentages of `scale` are numbers, a `rotate`
  axis parallel to `x`, `y` or `z` is a keyword or is omitted). Their specified values are serialized in the same form
  (`scale: 100% 100% 1` is `1`, `translate: 100px 0px 0px` is `100px`, `rotate: -1 0 0 90deg` is `x -90deg`; the math
  functions are kept), a single value of `transform-origin` or `perspective-origin` is completed with `center`, and the
  transform functions are canonicalized (`scale(250%)` is `scale(2.5)`, `rotate(0)` is `rotate(0deg)`, `translate(0)` is
  `translate(0px)`; the names of `scaleX()`... and `skewX()`... are lowercase, as
  `css/css-transforms/parsing/transform-valid.html` expects).
- `transform-origin` without its z offset when it is zero, and `border-spacing` with a single length when both spacings
  are equal (the shortest forms, as in the browsers).
- `aspect-ratio` with the second number of its ratio (`16` is `16 / 1`), the `size` shorthand with one length when both are
  equal, `stroke-dasharray` with commas (`10px 20%` is `10px, 20%`), the `font` shorthand with the `<font-width-css3>`
  keyword of a percentage font width (`50%` is `ultra-condensed`, also in a `calc()`), and the `local()` sources of
  `@font-face` with a string (as in the browsers). A `src` entry whose `format()` string is not a format string is dropped
  (a `FontFace` without valid source rejects with a `SyntaxError`). `mask` also resets `mask-border-mode`, and `columns`
  resets `column-wrap`. The grammars of the browsers are used where they differ from the specifications: `column-span` has no
  `auto`, `column-width` no percentage, and a text shadow has no spread nor `inset`.
- A zero dimension added to a percentage is dropped from a computed math function (`calc(10% + 0px)` is `10%`, as in the
  browsers), and the negative blur radius of a `text-shadow` is clamped to `0px`.
- The math functions of the computed values are evaluated with the full precision of the units converted by the
  serialization of the specified value (`calc(100dpi - 20dpi * sign(1em))` is `0.833333dppx`), and are clamped to the
  range of the type, also in the track sizes and in `stroke-dasharray` (`calc(-5%)` is `0%`). The numbers of
  `stroke-width` and `stroke-dashoffset` (and the math functions that resolve to a number) are lengths in pixels.

`computedStyleMap()` returns the computed values (it only computes the layout for the elements in a size container, whose
styles depend on the sizes of the containers, see [Container queries](#container-queries), and for the accepted @position-try
properties of the anchor-positioned boxes).

Without layout (the document has no browsing context, or the element is not connected, e.g. `ComputedStyle.GetResolvedValue`
called on the style engine of a parsed document), the used values that are known without layout are returned (`BoxSizes`):
an absolute length, a percentage of a known size, or the inline size of a block-level box in normal flow whose width is
`auto`, which fills its containing block; the percentages of the margins and paddings and of the transform origins when
the size they refer to is known. The size queries of the [container queries](#container-queries) use these sizes too.

The painting does not change the computed or the resolved values: the paint-only properties (colors, backgrounds, borders,
`opacity`, `filter`...) are read from the computed styles when the document is painted into a display list (see
[docs/rendering.md](rendering.md); the display list is rasterized by a rendering backend, e.g. the screenshots of the
`Meziantou.Framework.WebPlatform.Imaging` package), and a change of one of them only invalidates the painting with the
layout.

## Geometry

The geometry of [CSSOM View](https://drafts.csswg.org/cssom-view/) is computed from the fragment tree of the layout engine (see
[layout.md](layout.md#cssom-view-geometry-and-hit-testing-csslayouthittesting)): reading it computes the layout when it is
out of date (a forced layout). The block layout (with the margin collapsing and the quirks of the quirks mode), the inline
layout (line breaking, bidi reordering, vertical alignment, the CSS Text features: the tab stops, `letter-spacing` not applied at
the end of a line, `text-justify`, `text-spacing-trim`, `text-autospace`, `hanging-punctuation`, `text-wrap-style`, the
automatic hyphenation (`hyphens: auto`, with the hyph-utf8 dictionaries) and its limits, `white-space-trim`, and `text-box-trim`), the floats (with the clearance, the line boxes and the block
formatting context roots placed next to them, and the float areas of `shape-outside` from the basic shapes, the boxes and,
with a rendering backend, the images),
the intrinsic sizes (the shrink-to-fit widths, the intrinsic size contributions, the keywords `min-content`, `max-content`,
`fit-content`, `fit-content()` and `stretch`, `calc-size()`, size containment and `contain-intrinsic-size` with the last
remembered sizes), the containment (`contain`, `content-visibility`), the sizing of
the replaced elements, of the form controls and of `aspect-ratio`, the positioning (relative, absolute, fixed, sticky),
the overflow and scrolling, the flex layout (with the legacy `-webkit-box`), the grid layout (with the subgrids), the table layout
(automatic and fixed, the collapsing borders, the captions, `visibility: collapse`) and the box alignment (`justify-self` of the
block-level and absolutely positioned boxes, `align-self` of the absolutely positioned boxes, `align-content` of the block
containers and of the table cells, with their overflow alignment) and the writing modes (vertical and sideways text, the
orthogonal flows, the central baseline of vertical text, `text-combine-upright: all`, the principal writing mode propagated from
the `body` element to the root element and the viewport) and the multi-column layout (the column balancing, `column-fill`,
`column-span: all`, `column-height`/`column-wrap`, and the fragmentation of the content into the columns: `break-before`,
`break-after`, `break-inside`, `orphans`, `widows`, `box-decoration-break`) are implemented; the paged media,
the fragmentation of a nested multi-column container are not yet (the boxes of these
layouts are laid out as blocks); the MathML Core layout and the SVG layout (the text layout, the geometry of the shapes, the
transforms, the `foreignObject` elements, see [SVG layout](layout.md#svg-layout-svg)) are implemented (see [MathML layout](layout.md#mathml-layout-csslayoutmathml)). An element has boxes when it is
[being rendered](https://html.spec.whatwg.org/multipage/rendering.html#being-rendered) (see
[box generation](layout.md#box-generation-query)): it is connected and in the flat tree, its document has a browsing
context, neither it nor one of its ancestors has `display: none`, it does not have `display: contents`, it is not in the
contents of a replaced element or a form control, and it is not a never-rendered SVG element (`defs`, `symbol`...) or in one.

- `Element.getClientRects()` returns the border box of each fragment of the principal box of the element (an inline box has
  one per line, and the blocks split out of it are included; a box split by the columns of a multi-column container has one
  per column; a table returns its table wrapper box, which contains its captions, as in the browsers), with the
  transforms of the element and of its ancestors (the bounding box of the transformed box; the `perspective` of the parent
  element applies to the transforms of its children; the transforms of the boxes of a 3D rendering context
  (`transform-style: preserve-3d`) accumulate in 3D and are flattened in the plane of the parent of its root, the other 3D
  transforms are flattened at each box), in the coordinates of the viewport
  (CSS pixels of the root: not divided by the `zoom` of the element, as in the browsers). The element of an SVG document
  fragment returns its bounding box (`getBBox()`, see below) transformed to the box of its outermost `svg` element (the
  transforms of the element and of its ancestors, the viewports of the nested `svg` elements, the `viewBox`); the CSS boxes
  of the content of a `foreignObject` are transformed the same way. `Range.getClientRects()` returns the glyph cells of the
  selected characters of an SVG text.
  `getBoundingClientRect()` is the union of the rectangles (the empty ones are ignored unless they all are), or a zero
  rectangle without box. `Range.getClientRects()` returns the boxes of the elements contained in the range (whose parent is
  not) and the rectangle of the selected characters of each text fragment (their advance, the ascent and descent of the
  font); a collapsed range in a text gives a rectangle without width at the caret position.
- `offsetTop`/`offsetLeft` are the position of the first box relative to the padding edge of the first box of the
  `offsetParent` (relative to the initial containing block when it is the body element or null, as in the browsers), without
  the transforms and the scroll positions; `offsetWidth`/`offsetHeight` the size of the bounding box of the fragments; they
  are divided by the effective zoom of the element and rounded. `HTMLImageElement.x`/`y` are the position of the first box
  relative to the initial containing block (not divided by the zoom, as in the browsers).
- The hit testing (`document.elementFromPoint()`, `document.elementsFromPoint()`, the targets of the pointers of
  `BrowsingContext.Input`) walks the boxes in the reverse of the painting order of CSS 2 (the stacking contexts by `z-index`, the
  positioned boxes, the floats, the inline content, the top layer above everything), maps the point through the scroll
  positions and the inverse of the transforms (of the accumulated 3D transforms in a 3D rendering context, where the nearest
  box at the point is hit, the root of the context being one of its planes), clips it to the overflow clip edge (with its
  rounded corners when the box has a `border-radius` and clips both axes), the `clip` rectangle and the `clip-path` (basic shapes,
  geometry boxes, `clipPath` elements) of the ancestors and of the box, and tests the border box (with the rounded corners of
  `border-radius`, shaped by `corner-shape`) or the text fragment; the masks are not taken into account. A box with `visibility` other than `visible`,
  `pointer-events: none`, a box showing its back face with `backface-visibility: hidden`, or an inert element is not hit;
  the `area` of an image map is hit instead of the image; the
  elements of an SVG document fragment are hit with their geometry (the fill and the stroke of the shapes, with the dashes,
  the joins and the caps, depending on `pointer-events`, `visibility`, `fill` and `stroke`; the glyph cells of the texts,
  whose innermost `tspan`/`textPath`/`a` is hit; the bounding box of the images and of the elements with
  `pointer-events: bounding-box`; the content referenced by a `use`, which is hit; the boxes of a `foreignObject`), clipped by
  their `clip-path` (a `clipPath` element or a basic shape in their SVG boxes); a block
  split out of an inline box is in the inline box (the band of its anonymous block). `elementsFromPoint()` lists the elements
  of the boxes (not of the texts), retargeted out of the shadow trees, then the document element. A point outside of the
  viewport returns null (an empty list).
- `document.caretPositionFromPoint()` returns the position in the text under the point (the closest boundary of the clusters),
  else in the closest text of the line of the element under the point, before or after a replaced element, at the start of
  an SVG element or of another element; in a text field or a `textarea`, the offset in its value, from the line boxes of
  its inner editor (see [text controls](layout.md#replaced-elements-csslayoutreplaced): the line whose block range contains
  the point, else the closest one, and the closest boundary of the clusters along it, at the scroll position of the value).
  `CaretPosition.getClientRect()` is the caret rectangle (as tall as the line box in a text control).
- `Selection.modify()` moves the selection over the line boxes (see [caret navigation](layout.md#caret-navigation-csslayoutcaret)):
  by characters (the grapheme clusters of the rendered text; `left`/`right` are visual in bidi text), words (UAX #29 word
  boundaries: to the end of the word forward, to its start backward), sentences, lines and paragraphs (keeping the inline
  coordinate of the caret), and to the line, sentence, paragraph and document boundaries, in the editing host of the caret.
  The arrow keys, Home and End move the caret of the editable content and of the text controls the same way (in a text
  control, the value is scrolled to reveal the caret, which fires `scroll` at the control), and focusing an
  editing host places the caret at its start. `Selection.toString()` returns the rendered text of the range (see `innerText`).
- `CSS.highlights.highlightsFromPoint()` returns the custom highlights whose ranges have a client rectangle
  (`Range.getClientRects()`) that contains the point, by descending priority.
- The `GeometryUtils` methods of `Element`, `Text`, `CSSPseudoElement` and `Document` (`getBoxQuads()`,
  `convertQuadFromNode()`, `convertRectFromNode()`, `convertPointFromNode()`) use the same mappings: the quads of the margin,
  border, padding or content box of each fragment, relative to the viewport or to the first box of a node (the parent element
  of a text, the viewport of a document and, as in Blink, of a pseudo-element without box), through the `iframe`s of the
  same-origin documents (a `SecurityError` otherwise; a `NotFoundError` for an element without box). As in the browsers,
  `convertPointFromNode()` keeps the `z` and `w` of the point.
- `Element.pseudo()` (https://drafts.csswg.org/css-pseudo-4/#CSSPseudoElement-interface) returns the `CSSPseudoElement` of
  a tree-abiding pseudo-element that is not element-backed (`::before`, `::after`, `::marker`, `::placeholder`,
  `::backdrop`, `::checkmark`, `::picker-icon`, `::column`, the scroll navigation pseudo-elements, the view transition
  pseudo-elements), whether or not it is generated, and null for the other selectors (`::first-line`, the highlight
  pseudo-elements, `::part()`, `::details-content`, `::picker()`...). The same object represents the same pseudo-element of an
  element (it is kept by the element). A `::scroll-button()` of a physical direction is the button of the flow-relative
  direction it maps to in the writing mode of the element when `pseudo()` is called (its `selectorText` is the flow-relative
  one). The sub-pseudo-elements are the `::marker` of `::before` and `::after` (`CSSPseudoElement.pseudo()`), which has no
  box (it is an anonymous box of its pseudo-element), and the view transition pseudo-elements, which are the
  sub-pseudo-elements of their parent in a view transition tree without nested group (`::view-transition` >
  `::view-transition-group()` > `::view-transition-image-pair()` > `::view-transition-old()`/`::view-transition-new()`,
  also when they are returned by the `pseudo()` of the element).
- `UIEvent.pseudoTarget` (https://github.com/w3c/uievents/pull/413, implemented by Blink): the pointer events, the
  compatibility mouse events, `click`, `auxclick`, `dblclick` and `contextmenu` of a pointer that hits the box of a
  pseudo-element represented by a `CSSPseudoElement` have it as their `pseudoTarget` (their target is its originating
  element), except the boundary events (`*over`, `*out`, `*enter`, `*leave`). The `pseudoTarget` of a mouse event and of a
  click is null when the pseudo-element has no box in the box tree when it is fired (e.g. a listener of the press hid it and a
  layout was computed), and the one of a click is the nearest common ancestor of the pseudo-elements of the press and of the
  release. As specified, `pseudoTarget` is null outside of the dispatch and for the listeners the originating element is
  retargeted for (out of its shadow tree). The `pseudoTarget` of the events of the touches, of the keyboard and of the
  focus (e.g. a focused `::scroll-marker`), and of the animation and transition events, is null.
- `MouseEvent.pageX`/`pageY` add the scroll position of the window of the event; `offsetX`/`offsetY` are relative to the
  padding edge of the target (the outermost `svg` element for an SVG element), computed while the event is dispatched and kept
  afterwards, rounded except for the pointer events other than `click`, `auxclick` and `contextmenu`; `layerX`/`layerY` are
  relative to the nearest positioned ancestor. The clicks fired by the user agent (`click()`, the keyboard activation) have
  no position (0).
- `checkVisibility({ contentVisibilityAuto: true })` returns false when an ancestor with `content-visibility: auto` skips its
  contents: it is not relevant to the user (see [content-visibility](layout.md#containment-and-content-visibility): it was
  not close to the viewport at the last rendering update, and neither it nor its contents are focused, selected or in the
  top layer).
- The skipped contents of `content-visibility` (`hidden`, `auto` when not relevant to the user, and the contents of a closed
  `details`) are laid out when a geometry API reads them (the box that skips them keeps its size containment), but they are
  not hit tested, `scrollIntoView()` does not scroll to the skipped contents of `content-visibility: hidden` (it makes the
  `content-visibility: auto` ancestors relevant first), and they do not contribute to `innerText`.
- `SVGGraphicsElement.getBBox()` computes the [bounding box](https://svgwg.org/svg2-draft/coords.html#BoundingBoxes) of the
  geometry: the basic shapes (from the computed values of the geometry properties: `x`, `cx`, `r`, `width`...; the
  percentages are relative to the viewport of the nearest `svg` element, the content box of the outermost one from the
  layout), the paths (the exact extrema of the Bézier curves and of the arcs), the texts (the glyph cells of the text
  layout), the containers (`g`, `svg`, `a`, `switch`: the union of the children in the user space, with their transform
  and the `viewBox` of the nested `svg` elements), `use` (the referenced element, translated by `x` and `y`), `image` (an
  `auto` size is the default sizing of its natural size once loaded) and `foreignObject`. With `{ stroke: true }`, the
  stroke is included (`stroke-width`, `stroke-linecap`, `stroke-linejoin` and `stroke-miterlimit`). An element that is
  not rendered (not connected, `display: none`, in `defs`, `clipPath`, `mask`, `symbol`..., or outside of an `svg` element) or whose geometry is empty
  (e.g. a `rect` with a zero width) has a zero bounding box and does not contribute to its parent. The `markers` and
  `clipped` options are ignored.
- `ResizeObserver` notifies the size changes of the content box, the border box or the device pixel content box of the
  observed elements (one size per fragment, in the inline and block axes of the element, divided by its `zoom` except the
  device pixels), from the layout updated by the rendering steps of the event loop: the observations are gathered and
  broadcast depth after depth after the layout, and the notifications that would loop are reported as an error ("ResizeObserver
  loop completed with undelivered notifications.") and delivered by the next frame. The elements without box and the
  non-replaced inline elements have a zero size, the SVG elements without CSS box their bounding box (`getBBox()`, so the
  SVG text has no size), and the elements in the skipped contents of `content-visibility` are not observed (see
  [Resize observers](layout.md#resize-observers-csslayoutresizeobservationscs)).
- `IntersectionObserver` notifies the changes of the intersection of the observed elements with the viewport of the
  top-level document, a document or an element (root margin, scroll margin, thresholds, and the visibility of
  `trackVisibility` without the ink overflow), computed from the layout by the rendering steps of the event loop after the
  resize observers: the target is clipped by the overflow clips and the clip paths (the bounding box of their basic shape) of
  its ancestors and by the viewports of the nested documents (see
  [Intersection observers](layout.md#intersection-observers-csslayoutintersectiongeometrycs)). The `loading="lazy"` images
  and iframes are loaded when they are 1250px from the viewport or from the scrollport of a scroll container.
- The `layout-shift` performance entries (`LayoutShift`, Layout Instability API) report the movements of the visible boxes
  and text nodes between two rendering updates, once a `PerformanceObserver` of the window observes them: the layout is
  compared frame by frame from then on (the transforms, the scrolls and the newly appeared boxes are not shifts), and the
  sources are the 5 nodes with the largest impact (see [Layout shifts](layout.md#layout-shifts-csslayoutlayoutshifttrackercs)).
- The `paint`, `largest-contentful-paint` and `element` performance entries (Paint Timing, Largest Contentful Paint, Element
  Timing) report the paints found in the display list painted by the rendering updates, once a script of the window observes
  or reads them (see [Paint timing](rendering.md#paint-timing)).
- `getTotalLength()` and `getPointAtLength()` use the path equivalent to the shape (SVG 2: a rectangle starts at `(x + rx, y)`,
  a circle and an ellipse at `(cx + rx, cy)`, clockwise); `isPointInFill()` (with `fill-rule`) and `isPointInStroke()` (the
  stroke width, the joins, the miter limit, the caps, the dashes scaled by `pathLength`, `vector-effect: non-scaling-stroke`)
  test the point in the user space of the element, whatever its paint, `visibility` and `pointer-events`. An empty path makes
  `getPointAtLength()` throw an `InvalidStateError`.
- `getCTM()` is the transform to the nearest viewport element (the transforms of the element and of its ancestors up to the
  nearest `svg`, whose `x`/`y` and `viewBox` are included); `getScreenCTM()` adds the CSS layout of the outermost `svg` (its
  content box, its CSS transforms and the ones of its ancestors, the scroll positions); without layout, it is `getCTM()`. The
  transform of an SVG element is its `translate`, `rotate`, `scale` and `transform` properties (the `transform` attribute,
  `patternTransform`, `gradientTransform` are their presentation attribute) around `transform-origin` in the reference box
  of `transform-box`.
- The `SVGTextContentElement` methods (`getNumberOfChars()`, `getComputedTextLength()`, `getSubStringLength()`,
  `getStartPositionOfChar()`, `getEndPositionOfChar()`, `getExtentOfChar()`, `getRotationOfChar()`, `getCharNumAtPosition()`,
  `selectSubString()`) read the SVG text layout (see [SVG layout](layout.md#svg-layout-svg)): the addressable characters are the
  UTF-16 code units left by the white space processing, in the user space of the `text` element; they return 0 (or throw an
  `IndexSizeError`) for an element that is not rendered.
- `SVGLength.value`, `convertToSpecifiedUnits()`... of an attribute resolve the relative units for its element: its font
  (`em`, `ex`, `ch`, `ic`, `cap`, `lh`, and the root ones), its SVG viewport for the percentages (the width, the height or
  the normalized diagonal depending on the attribute), the viewport units; a detached length uses a 16px font and cannot
  convert its percentages.
- `window.innerWidth` and `window.innerHeight` are the size of the viewport, including its scrollbars (`ViewportOptions`,
  or the content box of the `iframe` from the layout of the parent document). `window.outerWidth` and `window.outerHeight`
  have the size of the viewport of the top-level browsing context, `screenX` and `screenY` are 0, and `moveTo()`,
  `resizeTo()`... do nothing. `window.visualViewport` (there is no pinch zoom) is the layout viewport without its classic
  scrollbars, from the layout: its `width` and `height` are the `clientWidth` and `clientHeight` of the root element, its
  `scale` is 1, its `offsetLeft`/`offsetTop` 0 and its `pageLeft`/`pageTop` the scroll position of the viewport; it fires
  `resize` events when its size changes (e.g. the classic scrollbars of the viewport appear), never `scroll` nor `scrollend`
  events. `window.viewport.segments` is a single segment, the rectangle of the viewport.
- `HTMLImageElement.width` and `height` are the rendered size of the content box of an image that is being rendered (the
  natural size and the `width`/`height` attributes otherwise), and `x` and `y` the position of its first box relative to
  the initial containing block, from the layout (see [Replaced elements](layout.md#replaced-elements-csslayoutreplaced));
  `sizes=auto` uses the rendered width of the image. The images that use `srcset` or `picture` [react to the changes of the
  environment](https://html.spec.whatwg.org/multipage/images.html#img-environment-changes) (the size of the viewport, the
  device pixel ratio): when the style engine sees another environment, a task selects their image source again
  (`HtmlImageElement.ReactToEnvironmentChanges`; when only the pixel density changes, the image data is kept) and fires `load`.
  The rendering updates check the environment of the documents that have such images (`StyleEngine.CheckEnvironment`), so
  that they react when nothing reads the styles (e.g. an `iframe` resized by its document).
- The scrolling members read the layout (a forced layout; see [Overflow and scrolling](layout.md#overflow-and-scrolling-csslayoutscrolling)):
  `scrollTop`, `scrollLeft`, `window.scrollX`/`scrollY` (`pageXOffset`/`pageYOffset`) are the scroll positions of the scroll
  containers and of the viewport (negative when the scrollable overflow extends leftwards or upwards: `direction: rtl`,
  `vertical-rl`), `scroll()`, `scrollTo()`, `scrollBy()` (of the elements and of the window) and `scrollIntoView()`
  (`block`, `inline`, `container`, `scroll-margin`, `scroll-padding`) scroll them, instantly or smoothly
  (`behavior: "smooth"` or `scroll-behavior: smooth`: an animation of 300 ms over the frames of the event loop), and return a
  promise that settles when the scroll completes or is aborted (it resolves with `undefined`, as in the specification).
  `scroll` and `scrollend` events are fired by the rendering steps. `clientWidth`/`clientHeight` are the size of the
  padding box without the scrollbars, `clientTop`/`clientLeft` the borders and a scrollbar on the left, `scrollWidth`/
  `scrollHeight` the size of the scrolling area (rounded to integers; the viewport for the root element, the body element in
  quirks mode). The `offset*`, `client*` and `scroll*` sizes of a table are the ones of its table wrapper box (with its captions,
  without border), as in the browsers. The classic scrollbars take 15px by default (`ViewportOptions.ScrollbarThickness`, 0 for overlay
  scrollbars). `focus()` scrolls the element into view (`preventScroll`), the fragment navigations scroll the target into view,
  and the history traversals restore the scroll position of the viewport (`history.scrollRestoration`).
- The scroll snap containers (`scroll-snap-type`, see [Scroll snapping](layout.md#scroll-snapping-csslayoutscrollingsnapcontainercs-snapselectionstrategycs-layoutenginescrollsnapcs))
  snap the programmatic scrolls: `scrollTo()`, `scroll()`, `scrollLeft`/`scrollTop` and `scrollIntoView()` (with the focus and
  the fragment navigations) to the closest snap position, `scrollBy()` to the snap position ahead in its direction
  (`scroll-snap-stop: always` is not passed over), using `scroll-snap-align`, `scroll-margin` and `scroll-padding`
  (`proximity` snaps within a third of the snapport). The containers re-snap after each layout change (to the same snap
  targets, the focused one, then the targeted one first), and fire `scrollsnapchanging` and `scrollsnapchange` (`SnapEvent`).
  The scroll containers and the viewport are scrolled to their initial scroll target (`scroll-initial-target`) until a scroll
  operation scrolls them.
- The scroll anchoring (`overflow-anchor`, see [Scroll anchoring](layout.md#scroll-anchoring-csslayoutscrollingscrollanchorcs-layoutenginescrollanchoringcs))
  adjusts the scroll positions of the viewport and of the scroll containers after each layout change (forced layouts included)
  so that their anchor node (the focused editable element, else the deepest visible box in tree order) does not move; the
  changes of the positioning properties of the anchor path and the elements that become or stop being absolutely positioned
  suppress the adjustment. The adjustment fires a `scroll` event (and `scrollend`) at the next frame.
- The members defined from the computed styles follow the specification: `offsetParent`, `scrollParent` (the containing
  blocks are approximated by the positioned ancestors and the ancestors that establish a containing block for the fixed
  positioned boxes: transforms, filters, layout or paint containment, including `content-visibility`; the ancestors in the
  shadow trees the element is not in are skipped, as in the browsers: when the fixed containing block of a slotted fixed
  element is skipped, the search continues from it as for an element that is not fixed; an element in the top layer has
  no offset parent),
  `document.scrollingElement`, `checkVisibility()` and `currentCSSZoom`.

## Rendered text

`HTMLElement.innerText` and `outerText` return the [rendered text](https://html.spec.whatwg.org/multipage/dom.html#rendered-text-collection-steps)
of an element that is being rendered (otherwise its `textContent`), computed from the cascade:

- The boxes come from the computed value of `display`: the block-level boxes and the table captions are separated by a
  line break, `p` by two, the table cells by a tab and the table rows by a line break; `display: none` and
  `content-visibility: hidden` hide their contents, `visibility` hides the text of the element (not of its visible
  descendants). The contents of the replaced elements and form controls (`img`, `iframe`, `video`, `canvas` when
  scripting is enabled, an `object` that does not show its fallback content, `textarea`, `input`...), the text of a
  `select` other than its options, the contents of a closed `details` element (except its
  summary), the white space between the table parts, and the SVG text outside the text content elements are not rendered.
- The text follows the [white space processing](https://drafts.csswg.org/css-text/#white-space-processing) of
  `white-space-collapse` (`collapse`, `preserve`, `preserve-breaks`, `break-spaces`...) and `text-transform` (with the
  Turkish and Azeri case mappings, and the `::first-line` and `::first-letter` pseudo-elements).
- The rendered text is computed from the computed styles, not from the layout: as in the browsers, the soft wraps add no
  line break, and the `::first-line` of a block is its text up to its first forced line break (not its first laid-out line).

## Animations and transitions

The computed values include the effects of the CSS animations, the CSS transitions and the animations created by script
([Web Animations](https://drafts.csswg.org/web-animations-1/)), see [Animations](animations.md) for the .NET API.

- **Origins**: the [transition origin](https://drafts.csswg.org/css-cascade-5/#cascade-origin-transition) wins over all
  the declarations; the [animation origin](https://drafts.csswg.org/css-cascade-5/#cascade-origin-animation) wins over
  the normal declarations, but not over the `!important` declarations. The animated values are inherited (e.g. `em`
  relative to an animated `font-size`).
- **SVG animations** (SMIL, see [Animations](animations.md#svg-animations-smil)): the animated value of a presentation
  attribute that is only a property (e.g. `fill`) overrides the normal declarations of the style sheets and of the `style`
  attribute, and it is the underlying value of the CSS animations and transitions; the animated value of an attribute
  reflected by an SVG DOM object (e.g. `width`, `d`, `transform`) replaces the attribute as the presentation attribute.
- **Interpolation** ([CSS Values 4](https://drafts.csswg.org/css-values-4/#interpolation)): by computed value type
  (lengths, percentages and their mix as `calc()`, numbers, integers, angles, colors, ratios...), with the
  [animation types](https://drafts.csswg.org/css-values-4/#animation-type) of the specifications and a discrete fallback.
  The legacy sRGB colors are interpolated in sRGB, the other colors in Oklab, with premultiplied alpha. The repeatable
  lists use the least common multiple of their lengths; the transform lists pair their functions (the identity function
  that pads the shorter list is the function of the other list, so that `none` to `translateX(100px)` stays a
  `translateX()`; a zero translation takes the type, length or percentage, of the other value), or are interpolated as
  decomposed matrices (2D or 3D, with quaternions; the 2D matrices are decomposed into a translation, a rotation, a
  skew and a scale like the 3D ones, as in Blink and Gecko, since the
  [2D decomposition](https://drafts.csswg.org/css-transforms-1/#decomposing-a-2d-matrix) of the specification does not
  round-trip the matrices that have a skew); `translate`, `rotate` (the rotations around different axes by their
  quaternions, a rotation of more than half a turn being the equivalent rotation in the other direction, like the
  quaternion of its matrix) and `scale`, the shadow lists, the filter lists,
  `d` and `path()` (with the same reference box), the basic shapes with the same function (the omitted offsets and radii of
  `inset()` and the rounding of `polygon()` are restored before, `CssValueNormalizer`; a `circle()` or an `ellipse()` with a
  keyword radius is discrete), `aspect-ratio` (by logarithm), the `corner-*-shape` properties (the superellipse parameters by their
  normalized superellipse half corner, [CSS Borders 4](https://drafts.csswg.org/css-borders-4/#corner-shape-interpolation):
  `round` to `bevel` at 60% is `superellipse(0.36)`), the grid track lists (item by
  item when the lists have the same number of tracks: the arguments of two `minmax()` are combined one by one, and two
  `repeat()` with the same integer count and the same number of tracks are combined track by track,
  [CSS Grid](https://drafts.csswg.org/css-grid-2/#repeat-interpolation); the lists are discrete otherwise, also with an
  automatic repetition, as in the browsers), `visibility`, `display` and `content-visibility` follow their own rules. The sizing properties interpolate and add
  `calc-size()` values with compatible bases, and the sizing keywords with lengths when `interpolate-size` is
  `allow-keywords` ([CSS Values 5](https://drafts.csswg.org/css-values-5/#interpolate-calc-size)): the result is a
  `calc-size()` resolved by the layout. Like in browsers, the images,
  `grid-auto-columns`, `grid-auto-rows` and `initial-letter` are interpolated discretely, and `stroke-miterlimit` by
  computed value ([SVG 2](https://svgwg.org/svg2-draft/painting.html#StrokeMiterlimitProperty)). `font-palette` is
  interpolated with `palette-mix()` in Oklab ([CSS Fonts 4](https://drafts.csswg.org/css-fonts-4/#font-palette-prop)):
  `light` to `dark` at 30% is `palette-mix(light 70%, dark 30%)` (the default color space is omitted, as in the computed
  value of a specified `palette-mix()`); the value is the first palette at 0% and before, the second one at 100% and after,
  and the palettes are not additive.
- **Composition**: `add` and `accumulate` (the composite operation of the effects and of the keyframes, and the
  iteration composite operation); the values out of the range of the property (e.g. an accumulated color channel above
  255) are clamped once interpolated. `stroke-dasharray` is not additive. The interpolation of a percentage and a dimension keeps both
  terms (`100%` to `0px` at 30% is `calc(70% + 0px)`), and a color that depends on `currentcolor` (`color-mix()`, a relative
  color) is interpolated with the color that `currentcolor` stands for.
- **CSS animations** ([CSS Animations](https://drafts.csswg.org/css-animations-1/)): the `@keyframes` rules (tree-scoped,
  the last one of the highest cascade layer wins) with their `animation-timing-function` and `animation-composition`
  declarations, the implicit keyframes at `0%` and `100%` (the underlying value), the animation properties (the matching
  of the running animations by name, `animation-play-state`, the values overridden by script are kept), the
  `CSSAnimation` objects and the `animationstart`, `animationiteration`, `animationend` and `animationcancel` events. An
  element that is not rendered (`display: none` on it or an ancestor) has no animations; an animation of `display` does
  not cancel itself.
- **CSS transitions** ([CSS Transitions](https://drafts.csswg.org/css-transitions-1/)): a transition starts when the
  computed value of a property changes between the before-change style and the after-change style, with reversing and
  shortening, `transition-behavior: allow-discrete`, the starting style of `@starting-style` (when the element had no
  style, or was not rendered), the `CSSTransition` objects and the `transitionrun`, `transitionstart`, `transitionend`
  and `transitioncancel` events. A transition of `display` to `none` keeps the element rendered until it ends. The starting
  style inherits from the after-change style of the parent. A value inherited from a transitioning ancestor does not start a
  transition, unless the element did not inherit it before the change (its declaration changed to `inherit`).
- **Before-change styles**: the styles are computed lazily, so when the document declares transitions or animations, the
  styles of the elements are kept when they are invalidated (they are the before-change styles), and the styles of all the
  elements are computed at the next style read or animation frame (the ones that were not invalidated are reused, see
  [Invalidation](#invalidation)), so that the transitions start and the animations are created like in a browser.
- **Timelines**: the document timeline, and the scroll and view timelines of
  [Scroll-driven Animations](https://drafts.csswg.org/scroll-animations-1/) (`animation-timeline: scroll()`, `view()`,
  the names declared by `scroll-timeline` and `view-timeline`, `timeline-scope`, `animation-range`): their animated values
  depend on the layout (the scroll positions sampled by the last rendering update, see
  [animations.md](animations.md#scroll-driven-animations)). A computed style never computes the layout for them.

## Limitations

- The serialization of some shorthands from their computed longhands (e.g. `border-radius`, `vertical-align`) is
  the empty string, like their specified values (see the serialization of the shorthands). The computed `font` omits the
  values equal to `normal` (`400` for the weight), and is the empty string when it cannot represent the longhands (a
  `font-variant` other than `small-caps`, a font width that is not a keyword).
- The animations and the transitions follow the virtual clock of the event loop (see
  [Animations and transitions](#animations-and-transitions)). The user action pseudo-classes (`:hover`, `:active`, `:focus`...) follow
  the trusted events that simulate the user input (see [User action pseudo-classes](selectors.md#user-action-pseudo-classes)).
