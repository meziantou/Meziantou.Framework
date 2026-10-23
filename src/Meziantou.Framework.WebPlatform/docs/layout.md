# Layout engine

Internal architecture of the layout engine (epic #371). The layout is internal: it is only exposed through the web APIs
(CSSOM View, `getComputedStyle`, the observers...). The code is in `src/Meziantou.Framework.WebPlatform/Css/Layout/`
(namespace `Meziantou.Framework.WebPlatform.Css.Layout`). Units are CSS px, as `double`.

Each section describes a layer of the engine and what the other layers can rely on. The painting of the fragments is described
in the [Painting](#painting-csslayoutpainting) section and in [rendering.md](rendering.md).

## Typed style (`Css/Layout/Style/`)

`ComputedStyle` (`Css/Cascade/ComputedStyle.cs`) stores the computed values as strings, computed lazily. The layout reads
the same properties many times per pass, so it reads them through a typed view: `ComputedStyle.Layout` returns the
`LayoutStyle` of the style.

### Lifetime and cost

- The view is created on first access and cached on the style (one field of `ComputedStyle`). A `ComputedStyle` is
  immutable once computed: a DOM, style sheet, environment or animation change creates new styles (see `StyleEngine`), so
  the view needs no invalidation and has no global state.
- The properties that are often read alone are converted one by one, on first access, and memoized with a bit mask:
  `Display`, `Position`, `Float`, `Clear`, `BoxSizing`, `OverflowX`/`OverflowY`, `WritingMode`, `Direction`,
  `TextOrientation`, `UnicodeBidi`, `Visibility`, `ContentVisibility`, `Contain`, `ContainerType`, `WhiteSpaceCollapse`,
  `TextWrapMode`, `TextTransform`, `VerticalAlign` (the inline boxes do not need the other properties of `Text`),
  `Zoom`/`EffectiveZoom`, `FontSize`, `LineHeight`, the containing block flags and `IsMultiColumnContainer`. Reading `Display` does not compute any other property (e.g. `innerText` only reads display,
  visibility, white-space-collapse, text-transform and content-visibility).
- The properties that a formatting context reads together are converted by groups, each a class allocated on first
  access, which computes all its properties at once:

  | Group | Properties |
  |---|---|
  | `Box` (`BoxModelStyle`) | `width`, `height`, `min-*`, `max-*`, margins, paddings, border widths (used: 0 when the style is `none`/`hidden`) and styles, `aspect-ratio`, `contain-intrinsic-*`, `interpolate-size` |
  | `Insets` (`InsetStyle`) | `top`, `right`, `bottom`, `left`, `z-index` |
  | `Text` (`TextLayoutStyle`) | `text-align-all`, `text-align-last`, `text-justify`, `text-indent` (+ `hanging`, `each-line`), `text-wrap-style`, `white-space-trim`, `word-break`, `overflow-wrap`, `line-break`, `hyphens`, `hyphenate-character`, `letter-spacing`/`word-spacing` (used px, percentages of the font size resolved), `tab-size`, `vertical-align` (`alignment-baseline`, `baseline-shift`, `baseline-source`), `list-style-position`, `text-combine-upright`, `text-spacing-trim`, `text-autospace` (flags; `normal`/`auto` = `ideograph-alpha ideograph-numeric`), `word-space-transform` (`none`, `space` or `ideographic-space`; `auto-phrase`: `WordSpaceTransformAutoPhrase`), `hanging-punctuation` (flags), `hyphenate-limit-chars` (`HyphenateLimitChars`, 0 = `auto`), `hyphenate-limit-lines` (null = `no-limit`), `hyphenate-limit-last: always`, `text-box-trim`, `text-box-edge` (`TextEdge` over/under metrics; `auto` from `line-fit-edge`) |
  | `Font` (`FontLayoutStyle`) | `font-family` (names and generic families), `font-weight`, `font-width`, `font-style`, `font-size-adjust`, `font-kerning`, `font-synthesis-*`, `font-optical-sizing`, `font-feature-settings`/`font-variation-settings` (computed strings) |
  | `Flex` (`FlexLayoutStyle`) | `flex-direction`, `flex-wrap` (+ `balance`), `flex-line-count`, `flex-grow`, `flex-shrink`, `flex-basis`, `order`, the legacy `-webkit-box-*` properties |
  | `Alignment` (`AlignmentStyle`) | `align-*`, `justify-*` (keyword, `safe`/`unsafe`, `legacy`), `row-gap`, `column-gap` |
  | `Grid` (`GridLayoutStyle`) | `grid-template-rows`/`-columns` (`GridTrackList`: segments of track sizes and line names, `repeat()`, `subgrid`), `grid-template-areas` (cells and named areas), `grid-auto-rows`/`-columns`, `grid-auto-flow`, the placement properties (`GridLine`), the grid lanes properties (`grid-lanes-direction`, `grid-lanes-pack`, `flow-tolerance`) |
  | `Table` (`TableLayoutStyle`) | `table-layout`, `border-collapse`, `border-spacing`, `empty-cells`, `caption-side` |
  | `Fragmentation` (`FragmentationStyle`) | `column-count`, `column-width`, `column-fill`, `column-span`, `column-height`, `break-*`, `orphans`, `widows`, `box-decoration-break` |
  | `GapDecorations` (`GapDecorationStyle`) | the gap decorations of the column and row gaps (`GapRuleStyle`): the lists of `*-rule-width` and `*-rule-style` (`GapValueList<T>`: the integer repeaters expanded, the auto repeater), `*-rule-break`, `*-rule-visibility-items`, the four `*-rule-inset-*` longhands (`RuleInset`: a length-percentage or `overlap-join`), `rule-overlap`; `LayoutStyle.HasGapRules` is a cheap test (a rule style is declared and not `none`) |
  | `Overflow` (`OverflowStyle`) | `overflow-clip-margin-*`, `scrollbar-gutter`, `scrollbar-width`, `text-overflow`, `max-lines` (the number of lines and `auto`), `block-ellipsis` (`TextOverflowSide`), `scroll-behavior`, `scroll-margin-*`, `scroll-padding-*` |
  | `Replaced` (`ReplacedStyle`) | `object-fit`, `object-position`, `image-orientation` |

  The properties that have no typed accessor are read with `LayoutStyle.Style.GetComputedValue()`; add them to a group
  (or as a memoized field when they are read alone) rather than parsing strings in the layout code.
- The conversions reuse the parsers of the cascade (`CssParser`, `CssCalc`, `CssValueComputer.SplitComponents`); the
  common values (`10px`, `50%`, keywords) are parsed without tokenizing. There is no second value parser.

### Values

- `LengthPercentage`: a length in px plus an optional percentage (`calc(50% + 10px)` is stored as both parts), resolved
  with `Resolve(basis)` or `TryResolve(basis?)` (null when the value has a percentage and the basis is indefinite). A math
  function that is not such a sum (`min(10px, 50%)`, `clamp()`...) is a `CalcLength`, evaluated by `CssCalc` with the
  basis at used time. `HasPercentage` distinguishes `0%` from `0px` (e.g. a percentage height with an indefinite basis
  behaves as `auto`). Non-negative properties (paddings, sizes) clamp their math functions at used time
  ([calc-range](https://drafts.csswg.org/css-values-4/#calc-range)); NaN is 0 and the infinite values are clamped to
  `CalcLength.MaxLength`.
- `LengthPercentageAuto` (margins, insets, `scroll-padding`), `Size` (`SizeKind`: `auto`, `none`, a length-percentage,
  `min-content`, `max-content`, `fit-content`, `fit-content()`, `stretch`, `contain`, `content` for `flex-basis`, and
  `calc-size()` whose function is kept for the intrinsic sizing). The anchor functions are resolved by the layout of the
  anchor-positioned boxes (see [Anchor positioning](#anchor-positioning-csslayoutpositionedanchorpositioningcs)): an unresolved
  one is the `auto`/`none` of the property. `LayoutStyle.Anchor` (`AnchorPositioningStyle`) holds the properties of the anchor
  positioning (`anchor-name`, `anchor-scope`, `position-anchor`, `position-area` as a `PositionAreaValue`, `position-try-*`,
  `position-visibility`).
- `Display`: the components of the computed `display` (`DisplayBox`, `DisplayOutside`, `DisplayInside`,
  `DisplayInternal`, `IsListItem`) with the classifications of CSS Display: `IsBlockLevel`, `IsInlineLevel`,
  `IsInlineBox`, `IsAtomicInline`, `IsBlockContainer`, `IsFlexContainer`, `IsGridContainer`, `IsTable`,
  `IsInternalTableBox`... The computed value is already blockified (root, floats, absolutely positioned boxes, flex and
  grid items).
- Enums for the keywords (`PositionKind`, `FloatKind`, `OverflowKind`, `WritingMode`, `WhiteSpaceCollapse`...), flags for
  the keyword sets (`Containment`, `ContainerType`, `TextTransform`, `FontSynthesis`, `WhiteSpaceTrim`), `Sides<T>` for
  the four physical sides. The logical properties are already mapped to the physical ones by the cascade; the logical ↔
  physical mapping of the geometry belongs to the fragment tree.
- Used values that need no layout are resolved by the view: the used `overflow-x`/`overflow-y` (visible becomes auto
  when the other axis scrolls; clip stays clip, as in the browsers: `overflow: hidden clip` is a scroll container in one
  axis), `HasTransform` (`transform`, `translate`, `rotate` or `scale`), the border widths of `none`/`hidden` borders (0), `normal` spacings (0),
  `EstablishesFixedPositionContainingBlock` (transforms, perspective, `preserve-3d`, filters, layout or paint containment
  including `content-visibility`, `will-change`) and `EstablishesAbsolutePositionContainingBlock` (positioned, root,
  `will-change: position`), `HasSizeContainment(inlineAxis)`, `HasLayoutContainment`, `HasPaintContainment`, `HasStyleContainment`.

### Zoom

The lengths of the view are used values: the computed lengths (which do not include `zoom`) are multiplied by the
[effective zoom](https://drafts.csswg.org/css-viewport/#effective-zoom) of the element (`EffectiveZoom`, the product of
the `zoom` of the element and of its flat tree ancestors; a highlight pseudo-element has the zoom of its originating
element). The percentages are not zoomed (their basis is). `FontSize` is the used font size (computed × zoom), and
`UsedLineHeight` the used line height (`normal` is the ascent + descent + line gap of the first available font, see
[Font matching](#font-matching)). A `CalcLength` evaluates its function with the unzoomed basis and zooms the result (the math
functions of lengths are homogeneous).

The values exposed by the APIs in the coordinate space of the element (e.g. the resolved `width` of `getComputedStyle`,
the size of a query container) are divided by the effective zoom: see `BoxSizes`.

### Consumers

- `BoxSizes` (`Css/Cascade/BoxSizes.cs`): the sizes known without layout, used when there is no layout (a document without
  browsing context: the resolved values of `getComputedStyle`), and by the size container queries before the first layout
  of a container. It computes in used (zoomed) px with the typed view, caches the content box sizes on the view, and
  returns sizes divided by the effective zoom.
- `ComputedStyle.GetResolvedValue`, `CssomViewHelpers` (effective zoom, fixed containing blocks) and `RenderedText`
  (`innerText`) read the typed display, visibility, white space and text-transform.

## Box tree (`Css/Layout/BoxTree/`)

The box tree ([CSS Display](https://drafts.csswg.org/css-display/#box-generation)) is built from the flat tree and the
computed styles by `BoxTreeBuilder` (`BoxTree.Build(document)`). It is the input of the formatting contexts. Building it
reads only the styles (`ComputedStyle.Layout`): no geometry. The tree reflects the DOM and the styles when it was built
(the text runs keep the string of their text node): it is patched or built again when they change (the `LayoutEngine`
decides when, see [Incremental update](#incremental-update)).

### Boxes

- `LayoutBox` (abstract): `Style`, the used `Display` (after the fixups), `Element` (the element of a principal box, the
  originating element of a pseudo-element box, the parent element of a text run, null for the anonymous boxes),
  `PseudoElement` (`before`, `after`, `marker`, `first-letter`, `details-content`, `backdrop`, and the scroll navigation
  controls `scroll-marker-group`, `scroll-marker`, `scroll-button(block-start)`...), `Parent`/`PreviousSibling`/
  `NextSibling`, `IsOutOfFlow` (absolutely positioned (the boxes of the top layer included), or floating in a block/inline
  formatting context; never the table grid box of a positioned table, its wrapper is), and
  `LayoutState`, an object slot for the per-box state of the formatting contexts.
- `ContainerBox`: the children (`FirstChild`, `LastChild`, `Children` enumerates without allocation) and a `Kind`:
  - `Viewport`: the root, the initial containing block (its style is the style of the root element, for the writing mode: the
    principal writing mode, see [Writing modes](#writing-modes)).
  - `Normal`: the box of an element or a pseudo-element, laid out according to its display type.
  - `Replaced`: an atomic box whose content is outside of CSS (replaced elements, form controls, the outermost `svg`, the
    images of the generated content with `GeneratedImage`); no children, except the `foreignObject` boxes of an `svg`,
    the boxes of the cues of a `video`, the inner editor and the `::placeholder` box of a text control, and the contents
    box of a list box or of a slider-like control (see [Replaced elements](#replaced-elements-csslayoutreplaced)).
    As in the browsers, a checkbox or a radio button with `appearance: none` is not replaced: it is a regular inline-block
    box with its `::before` and `::after` pseudo-elements (it has no natural size: as in Blink, it is empty unless the author
    sizes it). A file upload control (`input type=file`) is not replaced either: it is an inline-block box that holds the
    box of its button and its text; nor are some controls with base appearance (`appearance: base`), which hold the boxes
    of their parts.
  - `TableWrapper` (the principal box of a table, a block container whose children are the captions and the grid) and
    `TableGrid` (the table box, whose children are the row groups, rows, columns and column groups).
  - `Anonymous`: the anonymous boxes of the fixups (`IsAnonymous` is true for every box without element: the anonymous
    tables too). Their style comes from `StyleEngine.CreateAnonymousStyle(parentStyle)`: it inherits from the parent box,
    the other properties are initial.

  `ChildrenAreInline` tells whether a block container has inline-level children (an inline formatting context) or
  block-level children; out-of-flow boxes and an outside marker may be mixed with both. `IsBlockContainer`,
  `IsInlineBox`, `IsAtomicInline` and `EstablishesIndependentChildrenLayout` (flex, grid, table, math: floats do not
  float there) classify the box. `SkipsContents` (`content-visibility: hidden`, as the `::details-content` box of a closed
  `details`): the boxes of the contents exist but are not laid out, painted or hit tested. `FirstLineStyle` is the style of
  the `::first-line` of a block container when a rule styles it (no box: the inline layout applies it to the first
  formatted line). `IsOutsideMarker`: an outside `::marker`, an inline-block positioned by the inline layout against the
  first line box.
- `TextRun`: the text of a DOM text node (`Node`, `Data`, `Start`, `Length`, `Text` as a span), the text of the generated
  content (`Kind = Generated`), the line break of a `br` (`LineBreak`, `"\n"`) or the break opportunity of a `wbr`
  (`WordBreakOpportunity`, U+200B). Its style is the style of its parent element (the first letter has the
  `::first-letter` style). A text node has one run, or several when its first letter is in a `::first-letter` box
  (`NextRunOfNode`); the offsets are the ones of the text node (Range rects, caret and selection map back to the DOM with
  them). A text run is the anonymous inline box of CSS 2: there is no other anonymous inline box. The white space is not
  processed: the inline layout does it (`IsCollapsibleWhiteSpace` tells whether a run is only collapsible white space).
- `BoxTree`: `Viewport`, `GetPrincipalBox(element)` (the table wrapper of a table, the first part of a split inline),
  `GetPseudoElementBox(element, name)`, `GetFirstTextRun(text)`, and `Dump()` (one box per line: kind/display, element,
  flags; used by the snapshot tests of `BoxTreeTests`).

### Generation

- `display: none` generates nothing; `display: contents` is replaced by its `::before`, its children and its `::after`.
  The computed display is already blockified (root, floats, absolutely positioned boxes, flex and grid items).
- The children are the flat tree children: the shadow tree of a host, the assigned nodes of a slot (or its fallback).
- `display: list-item` generates a `::marker` first (its `content`, else `list-style-image` when it is a valid image, else
  the marker string of `list-style-type`: a string, or the prefix of the counter style, the representation of the
  `list-item` counter and the suffix of the counter style), then `::before`, the children and `::after` (generated when
  their `content` is not `none`/`normal`). An image that cannot be loaded is not a valid image
  ([CSS Lists §3.1](https://drafts.csswg.org/css-lists-3/#image-marker)): the boxes are built again when its fetch fails
  (`CssImageFetching.IsInvalidImage`). A `::before` or `::after` pseudo-element that is a list item has a marker too: an
  anonymous box of the pseudo-element that inherits from it, with the declarations of the user agent style sheet for
  `::marker` (it cannot be styled: there is no `::before::marker` rule). The content lists give text runs (strings, `counter()`, `counters()` with their counter style, the
  quotes with their nesting depth in tree order; `quotes: auto` uses the quotation marks of the content language of the
  element, `LanguageQuotes`: the delimiters of the Unicode CLDR, `tests/Meziantou.Framework.WebPlatform.SpecData/cldr`, generated by `tools/Meziantou.Framework.WebPlatform.SpecGen`, looked up by
  the longest prefix of the subtags of the language tag, the English marks by default) and replaced boxes (images); the
  alternative text after `/` is ignored. `content: <image>` on an element replaces it by the image (also on an `img` element,
  whatever its `src` and its `alt`, as in the browsers, and on an `input type=image`).
- **Counters** (`Css/Cascade/CssCounters.cs`, [CSS Lists §4](https://drafts.csswg.org/css-lists-3/#auto-numbering)): the
  counters of the boxes are computed once for a box tree, in the tree order of the flat tree. A box inherits the counters of
  its preceding sibling, or of its parent, with the values of the preceding box in tree order; `counter-reset` replaces a
  counter of the box or of a preceding sibling (the `::marker`, `::before` and `::after` boxes are children of the box of
  their element) and nests the others. An element with `display: contents` has no box: its counter properties have no
  effect, and the boxes of its contents are children of the box of its parent (they inherit the counters from each other
  across the element); a `::before` or `::after` pseudo-element with `display: none` does not change the counters either. A `counter-increment` that would overflow the 32-bit integers is ignored, as in the browsers (the
  specification clamps the value). The initial value of a `reversed()` counter without value is computed from the
  increments of the elements of its scope (not from the ones of the pseudo-elements).
- **Counter styles** (`Css/Cascade/CounterStyle.cs`, `CounterStyleResolver.cs`, `PredefinedCounterStyles.cs`,
  [CSS Counter Styles 3](https://drafts.csswg.org/css-counter-styles-3/)): `list-style-type`, `counter()` and `counters()`
  take a counter style name or a `symbols()` function.
  - A `CounterStyle` is an algorithm (`cyclic`, `fixed`, `symbolic`, `alphabetic`, `numeric`, `additive`, and the algorithms
    of the longhand East Asian styles and of `ethiopic-numeric`) with its symbols, negative sign, prefix, suffix, ranges,
    padding and fallback style. `CounterStyle.Generate` returns null when the style cannot represent a value (out of its
    range, no symbol for it, or a representation longer than 120 UTF-16 code units for the systems that repeat symbols);
    `CounterStyleResolver.Generate` then uses the fallback style, and `decimal` when the fallback styles loop. The padding
    counts grapheme clusters, the negative sign included. The marker string is the prefix, the representation and the
    suffix; `counter()` and `counters()` only use the representation.
  - The predefined styles are a style sheet of `@counter-style` rules parsed once (`PredefinedCounterStyles`, the rules of
    the specification), plus the styles that have their own algorithm: the Chinese, Japanese and Korean longhand styles
    use the optional extended algorithm of the specification (groups of four digits, up to 10^16 exclusive, then
    `cjk-decimal`; the counters are 32-bit integers), `cjk-ideographic` is `trad-chinese-informal`. The symbols of
    `disclosure-open` and `disclosure-closed` depend on the writing mode and on the direction of the box (the closed
    triangle points to the inline-end direction, the open one to the block-end direction), also for the styles that
    extend them.
  - The `@counter-style` rules are collected for each tree with the other at-rules (`CascadeRuleCollector`, the rule of the
    strongest cascade layer wins, then the last one; the rules that do not define a counter style are ignored: a system
    without the symbols it needs, an `extends` system with symbols). A name is a tree-scoped reference
    (`StyleEngine.FindCounterStyle`): it is looked up from the tree of the style sheet of the declaration of
    `list-style-type` or `content` (an inherited value keeps its tree; the tree of the element for a `style` attribute),
    then in the trees of the shadow hosts, then in the predefined styles; the `extends` and `fallback` names of a rule are
    looked up from the tree of the rule, the ones of a predefined style in the predefined styles. An unknown name is
    `decimal`, and the rules that extend each other in a cycle extend `decimal`. The predefined names are ASCII
    case-insensitive (lowercased where they are used), the other names are case-sensitive; `decimal`, `disc`, `square`,
    `circle`, `disclosure-open` and `disclosure-closed` cannot be redefined. The integers of the descriptors may be math
    functions (computed with the initial font size). The image symbols are not supported, as in the browsers: the rule
    defines no counter style. `speak-as` is parsed and has no effect.
  - The counter styles are created from the rules by the `CounterStyleResolver` of the `CssCounters` of each box tree. The
    setters of `CSSCounterStyleRule` ignore the values that are invalid, that change the algorithm of the system, or that
    would make the rule define no counter style; a change of a rule changes the style version, so the next layout uses it.
- `::first-letter` boxes are created after the tree is built, for the block containers that have a `::first-letter` rule:
  the first typographic letter unit of the first formatted line (descending into the first in-flow block child, even
  when it has no line: the content after it is not on the first formatted line; and into the inline boxes, stopping at an
  atomic inline, a line break or a preserved line feed), with the preceding and following punctuation, is moved to an
  inline box (a block when it floats); the text run is split. The
  [first-letter text](https://drafts.csswg.org/css-pseudo-4/#first-letter-pattern) (`ScanFirstLetterText`) is the
  punctuation of any category and the spaces (Zs, except U+3000) that precede the letter, the letter unit, then the
  punctuation other than the opening punctuation and the dashes, with the typographic spaces before it (not the word
  separators). The letter unit is the extended grapheme cluster of a letter, a number or a symbol (as Chrome): its
  combining marks, and the Indic conjuncts (UAX #29 rule GB9c); a control character, which is rendered as a symbol; and
  the digraph IJ when the content language is Dutch. When the punctuation and the letter are not in the same text run
  (the quote of a `::before`, the text of an inline box), the box wraps the part of the first run only, as in the
  browsers (the specification leaves it to the user agent): the punctuation alone, when a letter follows it in another
  run. The `::first-letter` is
  inside the innermost element that contains the letter (the fictional tag sequence of
  [CSS Pseudo §3.5](https://drafts.csswg.org/css-pseudo-4/#first-letter-pseudo)): its style inherits from the style of
  this element (an inline box, a `display: contents` element, a descendant block) or of the `::before`/`::after`
  pseudo-element whose generated content has the letter, not from the block container (`ComputedStyle.WithParent`). When its `initial-letter` is
  not `normal` (and it does not float), the box is an initial letter box (`ContainerBox.InitialLetter`): a block container
  with a used style (`InitialLetter.CreateBoxStyle`), placed by the inline layout (see
  [Inline formatting context](#inline-formatting-context-csslayoutinline)).
- A `br` or `wbr` element is a line break (opportunity) whatever its `display` (other than `none` and `contents`), as in the
  browsers: its box is an inline box.
- The `details` children: the summary, then the box of the [`::details-content`](https://drafts.csswg.org/css-pseudo-4/#details-content-pseudo)
  pseudo-element (the second slot of the element) with the other children, which inherit from it. The box has the computed
  `display` of the pseudo-element (`block` in the user agent style sheet; with `contents` the children are children of the
  box of the `details`, with `none` they have no box) and `SkipsContents` with `content-visibility: hidden` (the user agent
  style sheet while the element is closed: `details:not([open])::details-content`, which the author can override). Its
  `::before`, `::after`, `::first-line` and `::first-letter` are generated (the styles `details-content::before`...,
  `StyleEngine.DetailsContentPrefix`); `content-visibility: auto` is not applied to it.
- An `embed` element that has neither a `src` nor a `type` attribute represents nothing: as in the browsers, it has no box.
- A `button` element ([button layout](https://html.spec.whatwg.org/multipage/rendering.html#button-layout)) keeps a `flex`,
  `grid`, `inline-flex` or `inline-grid` display; its other display types behave as `inline-block` (inline outer display)
  or `flow-root` (the box display is changed, not the computed value).
- An outermost `svg` is a replaced box; its rendered `foreignObject` descendants are flow-root children of it. The other
  SVG elements generate no CSS box.
- The top layer ([CSS Position 4](https://drafts.csswg.org/css-position-4/#top-layer)): an element rendered in the top layer
  (in the top layer of the document, `Document.TopLayerElementsForRendering`: the modal dialogs, the popovers, the fullscreen
  elements, with a computed `overlay` of `auto`) whose ancestors generate boxes is not built at its place in the tree: its
  boxes are children of the viewport box, after the root box, in the order of the top layer, preceded by the box of its
  `::backdrop` (when its `content` is not `none`; the UA style sheet has `::backdrop { position: fixed; inset: 0 }`); they
  are marked `IsInTopLayer` (out of flow, absolutely positioned). The cascade computes `position` to `absolute` (unless it is
  `fixed`) and `display: contents` to `block` for the elements rendered in the top layer. Adding or removing an element of
  the top layer invalidates the styles and rebuilds the box tree.
- [`overlay`](https://drafts.csswg.org/css-position-4/#overlay) is set by the user agent rules
  `* { overlay: none !important }` and `overlay: auto !important` for the elements in the top layer
  (`ComputedStyle.HasUserAgentImportantOverlay`: the author declarations and the animations do not change it, the
  transitions do). Hiding a popover and closing a modal dialog request the removal of the element from the top layer: it
  stays in the top layer, pending removal (`Document.TopLayerElements` and `:popover-open` no longer see it), and is rendered
  in the top layer while its `overlay` transitions to `none` (`transition: overlay 1s allow-discrete`, with `display` for an
  exit animation); the rendering update removes the elements pending removal whose `overlay` is `none` or that are not
  rendered (`Document.ProcessTopLayerRemovals`). An element added to the top layer with an `overlay` transition is rendered at
  its place until its `overlay` becomes `auto`. The removal of an element from the document and the fullscreen and select
  pickers remove it immediately.
- The view transition layer ([CSS View Transitions](view-transitions.md)): the boxes of the pseudo-element tree of the
  animating view transition of the document. The box of `::view-transition` is the last child of the viewport box
  (`ContainerBox.IsViewTransitionRoot`), after the top layer: it is absolutely positioned in the snapshot containing block
  (the viewport with its scrollbar gutters, which does not scroll), and it is a stacking context painted last. The groups,
  the image pairs and the children containers are boxes of their computed display (absolutely positioned by the user
  agent style sheet); the `::view-transition-old()` and `::view-transition-new()` images are replaced boxes whose natural
  size is the size of the border box of their element. The box tree is built again when the tree of pseudo-elements or
  the dynamic view transition style sheet changes. The `::view-transition` box of a
  [scoped view transition](view-transitions.md#scoped-view-transitions) is the last child of the box of its root element
  (`ContainerBox.IsScopedViewTransitionRoot`), absolutely positioned in its padding box: the root element has layout
  containment for the duration of the transition (the used containment of `LayoutStyle.Contain`; size containment with
  the size of its old state while its rendering is suppressed), the box is not scrolled by the root element and does not
  extend its scrollable overflow.
- The pseudo-element styles are only computed when a rule may style them (`StyleEngine.MayHavePseudoElementRules(element,
  name)`, which checks the user agent rules of the element, the author rules by name and the animations).
- The scroll navigation controls ([CSS Overflow 5](https://drafts.csswg.org/css-overflow-5/#scroll-navigation),
  `BoxTreeBuilder.ScrollControls.cs`): the `::scroll-marker-group` of a scroll container and the `::scroll-button()` boxes are
  siblings of the box of their originating element, the `::scroll-marker` boxes are children of the group: see
  [Scroll markers and scroll buttons](#scroll-markers-and-scroll-buttons-csslayoutlayoutenginescrollmarkerscs).

### Fixups

Run bottom-up when the children of a box are built (`FixupContainer`), in this order (a
[layout API container](#layout-api-containers-csslayoutlayoutapi-csslayoutapi) whose layout is registered only wraps its
runs of text and inline-level boxes in anonymous blocks, as a flex container: `BoxTreeBuilder.LayoutApi.cs`):

1. Block-level ruby containers ([block ruby](https://drafts.csswg.org/css-ruby-1/#block-ruby): `display: block ruby`, and the
   ruby containers blockified as floats, absolutely positioned boxes, flex items or children of a MathML box): the box becomes
   a block container whose only child is an anonymous inline ruby container with its children. Then the ruby inlinification
   of the in-flow block-level children of the ruby containers and internal ruby boxes.
2. Table fixup ([CSS Tables 3](https://drafts.csswg.org/css-tables-3/#fixup-algorithm)): white space runs between or at
   the edges of table parts removed, the non-column children of a column group removed, missing child wrappers
   (anonymous rows and cells), missing parents (anonymous rows around cells, anonymous tables or inline tables, with
   their wrapper, around misparented proper table children), captions moved to the wrapper (by `caption-side`).
3. Ruby anonymous boxes: an anonymous ruby container around the misparented bases/annotations, anonymous ruby bases (or
   annotations in an annotation container) around the other content with the white space around it; the white space at the
   start and at the end of the ruby containers and of the level containers, and the inter-level white space are removed; the
   intra-level white space (white space only between two bases, or between two annotations) is wrapped in an anonymous base
   (annotation) flagged `IsRubyWhiteSpace` ([white space](https://drafts.csswg.org/css-ruby-1/#anon-gen-interpret-space)). The
   level containers are not generated (the ruby layout pairs the bases and the annotations).
4. By formatting context: a block container splits its inline children that contain in-flow block-level boxes (CSS 2
   block-in-inline: the parts are linked by `Continuation`/`PreviousContinuation`, the first part keeps the start edges
   and the last one the end edges; consecutive blocks separated by collapsible white space or out-of-flow boxes do not
   split again), then wraps each sequence of inline-level content in an anonymous block when it has in-flow block-level
   children (a sequence of collapsible white space and out-of-flow boxes is not wrapped: the white space is dropped and
   the out-of-flow boxes stay children of the block). The blocks and the out-of-flow boxes between the parts of a split
   inline box are children of the block container: `ContainerBox.SplitInlineParent` is the (innermost) inline box they were
   split out of, for its relative offset and as their containing block. A flex or grid container wraps each sequence of
   text runs (and inline-level boxes) in an anonymous block item, unless it is only white space.
5. Run-in boxes ([CSS Display §2.1.3](https://drafts.csswg.org/css-display-3/#run-in-layout), `FixupRunIns`, on the children
   of a block container once its inline boxes are split): a run-in box with `display: flow` inlinifies its contents (a
   block child becomes an inline-block, a table an inline-table, a nested run-in an inline box; the inline boxes in it are
   not split around blocks). A run-in sequence (the consecutive run-in boxes, with the white space and the out-of-flow
   boxes between them) immediately followed by a block box that does not establish a formatting context is inserted at
   the start of this box, after its marker (in the anonymous block of its first line when it has block-level children);
   the white space and the out-of-flow boxes between the sequence and the block box do not prevent it and stay where they
   are. Otherwise the sequence and the inline-level content that follows it (up to the next block-level box or run-in
   sequence) are wrapped in an anonymous block, with the inside marker of the container when the sequence follows it.
   The sequences are those of the tree before any of them moves: a sequence inserted in a block box that starts with
   run-in boxes does not join their sequence (it gets a line of its own). The run-in boxes are then inline-level boxes;
   only the layout changes, not the inheritance. This is the model of CSS Display 3, not the one of the CSS 2.1 drafts
   that most of the WPT run-in tests check (a run-in box that contains a block, or that is not followed by a block,
   became a block box): see the `TEST BUG` reasons of `css/css-display/run-in`.

### Box generation query

`BoxGeneration.Get(element)` / `Get(style)` (`BoxGeneration.cs`) answers "does the element generate boxes / is it being
rendered" from the computed styles only, without building the tree, with the same rules as the builder:
`NoStyle` (not rendered: `ComputedStyleDeclarations.IsRendered` is false), `DisplayNone`, `ReplacedContents` (with the
replaced ancestor), `NotRenderedSvg`, `NotGenerated` (a `::before`/`::after` with `content: none`, a `::marker` of a
non-list item), `Contents`, `Box`; plus `InSkippedContents`. `BoxGeneration.IsReplaced(element, style)` is the list of
the replaced elements. Every "being rendered" helper uses it: `ComputedStyleDeclarations.IsBeingRendered` (CSSOM View,
the iframe viewport, embed/object loading, image `sizes=auto`), `FocusModel.IsBeingRendered` (with the canvas fallback
content, the skipped contents and `visibility`), `RenderedText` (innerText), `BoxSizes` and `ComputedStyle` (sizes known
without layout, `min-width: auto`), `SvgBoundingBox` (`getBBox()`) and `Animation.commitStyles()`.

### Incremental update

`BoxTree.TryPatch` (`BoxTreeBuilder.Incremental.cs`) updates the tree after a change instead of building it again: the
style engine records the changes since the tree was built (`BoxTreeChanges`, `StyleEngine.BoxTreeChanges.cs`): the elements
whose styles or pseudo-element styles were discarded by the [invalidation by element](computed-style.md#invalidation), and
the elements whose attributes, children or texts changed (the log of the document). The boxes of each changed element and of
its descendants are built again in a temporary parent and replace its previous principal box; the other boxes are kept,
with the state the layout attached to them (see [Layout result cache](#layout-result-cache)). In a rebuilt subtree, the
boxes of the elements that did not change (neither them nor a descendant) are kept too: the build appends a placeholder
with their kind, style and display (the fixups of their parent give the same boxes), which they replace (`TryKeep`).

- The root of a rebuilt subtree is the changed element or its nearest ancestor whose boxes can be built alone: an HTML element
  whose principal box is one box of the element (not an inline box, which its block container splits around its block-level
  descendants, nor a run-in box, nor a box of the top layer) and whose parent box keeps it as it is (not a ruby container, a
  MathML box nor a run-in box, whose fixups change the display of their children; not a layout API container). The new
  boxes must be one box of the same kind, display and positioning, so that the fixups of the parent box (the anonymous
  boxes around the inline-level boxes, the table fixup...) are the same; otherwise the parent element is the root. The
  legends and the summaries, and the contents of the `select`, `datalist`, `option` and `optgroup` elements, are built with
  their ancestors; a change of the root element, or of the styles of the body element (propagated to the viewport), builds
  the whole tree. The changes inside a subtree that is not rendered (`display: none`) are ignored.
- The counters of the previous build are kept (`BoxTree.Counters`): the patch fails when the counter operations of the
  rebuilt boxes (`counter-reset`, `counter-increment`, `counter-set`, the list items, the style containment) differ from
  the ones of the previous boxes, or when a new element or pseudo-element reads the counters (its marker, `counter()`).
- The trees that use quotes (their depth continues from the previous content), run-in boxes, `::first-letter` boxes, scroll
  navigation controls, the top layer, view transitions, shadow trees, or counter operations on pseudo-elements without box
  are built again (`BoxTree.IsPatchable`), as are the changes that are not known element by element (the style sheets, the
  environment, the fonts, the changes of the state of the form controls...) and `SetNeedsLayout(rebuildBoxTree: true)`.
- The styles that the tree derives from the computed styles (the anonymous boxes, the symbols of the markers, the fieldset
  frames and contents, the inner editors and the placeholders of the text controls, the parts of the form controls) are kept
  with their style (`ComputedStyle.GetDerivedStyle`): the boxes built again share them with the kept boxes, and so do the
  builds of the tree while the style engine keeps the styles (the anonymous styles of the root element are created by each
  build: the principal writing mode is set by the build).
- `WEBPLATFORM_VERIFY_INCREMENTAL_LAYOUT=1` (`LayoutEngine.VerifyIncrementalLayout`) builds the tree again after each patch
  and throws when it differs from the patched tree (`BoxTreeBuilder.DescribeDifference`: the boxes, their flags and texts,
  the styles, the boxes by node and the lists of the tree). `LayoutEngine.BoxTreePatchCount` counts the patches (tests).

### Not done yet

- The `::marker` of an inline-level list item (`display: inline list-item`) and the anonymous button content box are not
  generated yet; the marker of a `::before`/`::after` list item cannot be styled. The counter properties and `quotes` do not
  apply to `::marker`.

## Layout pass and fragment tree (`Css/Layout/`, `Css/Layout/Fragments/`, `Css/Layout/Geometry/`)

The layout turns the box tree into a fragment tree ([CSS Fragmentation](https://drafts.csswg.org/css-break-4/#fragment)):
the output that the geometry APIs, the observers and the painting read.

### When the layout runs

- `LayoutEngine` is the layout of a document (`Document.GetLayoutEngine()`, created on first use; `LayoutEngineOrNull` does
  not create it). `EnsureLayout()` is the forced layout: it computes the layout when it is out of date and returns the root
  fragment (or null when the document is not rendered: no browsing context, or the root element generates no box).
  Nothing else lays out a document: a document that never reads the geometry is never laid out (and pays nothing: no box
  tree, no fragments).
- The invalidation of the layout is version-based: `StyleEngine.Version` is incremented each time computed styles are
  invalidated (the DOM, the state of the elements, the style sheets, the environment including the viewport size, the
  animated values, the fonts: `FontSelector.Version`; the styles themselves are invalidated element by element when the
  change allows it, see [computed-style.md](computed-style.md#invalidation)). The box tree is then patched with the boxes of
  the elements that changed, or built again (see [Incremental update](#incremental-update)), then the tree is laid out
  again: the boxes that did not change are not laid out again when their constraint space is the same (see
  [Layout result cache](#layout-result-cache)). `SetNeedsLayout()` (`Document.InvalidateLayout()`) marks the layout out of date for a
  change that does not change the styles: an image, a poster frame, a CSS image or the SVG document of an `embed`/`object` is
  loaded (its natural size), or the `width`, `height` or `viewBox` of the root of that SVG document changes; the box tree is then reused, unless `rebuildBoxTree` is set (a broken `img` starts or stops
  being rendered as its alternative text, an `object` starts or stops showing its fallback content); the results of the
  previous passes are not used by the next pass. `NeedsLayout` tells whether the next read recomputes the layout,
  `LayoutCount` counts the passes (tests).
- The first `EnsureLayout()` registers the document with its event loop (`DomEventLoop.AddDocumentWithLayout`): the
  rendering steps ([update the rendering](https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering)) then
  update its layout after the styles (after the animation frame callbacks, the CSS animations and the CSS resources, before
  the focus fixup), when they run for another reason (the scroll steps run before, after the resize steps: see
  [Overflow and scrolling](#overflow-and-scrolling-csslayoutscrolling)): this is where the observers read the layout
  ([Resize observers](#resize-observers-csslayoutresizeobservationscs), [Intersection observers](#intersection-observers-csslayoutintersectiongeometrycs)).
  The layout step never runs for a document whose geometry was never read and that has no resize or intersection observer
  nor lazy element (`LayoutEngine.RequestRenderingUpdates()` registers a document without computing its layout).
- A geometry read during a layout pass returns the tree being computed (no re-entrancy).
- The layout suspends the checks of the style versions while it runs (`StyleEngine.SuspendUpdates`): it reads the styles of
  one version. Otherwise, a change of a version (e.g. `Document.StyleSheetsVersion`) in the middle of the box tree
  construction would start a new generation of styles and lose the styles computed so far as the before-change styles of
  the transitions.
- The documents of the `iframe`s are laid out with the size of their viewport (the initial containing block is the
  `MediaEnvironment` of the nested browsing context: the content box of the `iframe` laid out in the parent document, see
  [Viewports](#viewports-layoutengineviewportcs)). Each document has its own engine and fragment tree; a nested document is
  laid out only when its own geometry is read, and its environment updates the layout of the parent document.

### Layout result cache

`LayoutResultCache` (as the layout result cache of Blink's LayoutNG) keeps the fragment of the last layout of each box with
the box (`ContainerBox.LayoutResult`, and `MeasureLayoutResult` for the layouts that measure the intrinsic block size),
with its constraint space: `context.LayoutBox(box, space)` returns it again, without laying out the box and its contents,
when the box is laid out in the same constraint space by a later pass. The intrinsic inline sizes and the measured
intrinsic block sizes (`IntrinsicSizesCache`, `IntrinsicBlockSizeCache`) are kept from one pass to the next the same way.

- **Invalidation.** A patch of the box tree drops the results of the boxes that contain the replaced boxes (the ancestors of
  a change are laid out again, the other boxes keep their results, see [Incremental update](#incremental-update));
  so do a change of `SkipsContents` or `LaysOutSkippedContents` (content-visibility) and a change of the scrollbars displayed
  by a scroll container. The results are also keyed on the size of the initial containing block and of the viewport. Every
  other change that the cache does not track drops all the results (`LayoutEngine.LayoutCacheGeneration` is incremented):
  `SetNeedsLayout()` (an image is loaded...) and a box tree built again.
- **Block formatting contexts.** A box that participates in the block formatting context of its parent is laid out
  relative to the current position of the context (`BlockFormattingContextState`, a new state for each layout of the root):
  its result is used when the context has no float (the layout then does not depend on the position), the same adjoining
  margins before the box, no line clamping, text group alignment, column spanners, nor margins separated by clearance. The
  changes of the state made by its layout are recorded (`BlockFormattingContextState.Operation`: the advances, the adjoined,
  collapsed, discarded and restored margins) and made again when the result is used; a layout that restores a saved state
  (the look-ahead of the margins of the first children) is not kept.
- **Not kept.** The results of the layouts that have effects outside of their fragments needed by the next passes
  (`LayoutContext.PreventLayoutCaching`, for the box and its ancestors): the size containers (their sizes are recorded for
  the container queries), the grid containers (their track sizes), the boxes laid out by a layout API class, the
  multi-column containers and the fragmentation, the videos with cues, the scroll containers whose classic scrollbars size
  their intrinsic sizes; the fragments with absolutely positioned descendants whose containing block is an ancestor (laid
  out by it); the boxes that receive the outside marker of their list item or whose margins are trimmed. No result is used
  by the printing, nor in a document with anchor-positioned boxes (the anchor queries find the fragments laid out by the
  pass: the pass that lays out the first one is computed again without the cache), scroll navigation controls,
  `contain-intrinsic-size: auto` boxes or an active view transition (`LayoutEngine.CanUseLayoutCache`).
- **Fragments used again.** A fragment is returned at most once per pass. The parents set the offsets of their child
  fragments and change some of their properties (the used margins of the flex and grid items and of the absolutely
  positioned boxes, `IsHiddenForPaint`, the alignment of the contents of a table cell, the sticky offsets): the state of a
  fragment when it was laid out is restored when it is used again (`BoxFragment.PrepareForReuse`).
- **Verification.** With `WEBPLATFORM_VERIFY_INCREMENTAL_LAYOUT=1`, each pass that used results of the previous passes is
  computed again without them (`LayoutContext.UsesLayoutCache` false) and an `InvalidOperationException` is thrown when the
  fragment trees differ (`LayoutResultCache.DescribeDifference`: the kinds, boxes, offsets and sizes of the fragments, the
  edges, baselines and scrollbars of the box fragments, the ranges of the text fragments). `WEBPLATFORM_DISABLE_LAYOUT_CACHE=1`
  disables the cache. `LayoutEngine.LayoutCacheHitCount` counts the results used (tests).
- **Algorithms.** The cache is outside of the formatting contexts: a new input of a layout algorithm must be in the
  constraint space (or make the layout not kept with `PreventLayoutCaching`), and a new change of the state of the block
  formatting context must be recorded as an operation.

### Formatting contexts

`LayoutContext` is the state of a pass (the engine, the `MediaEnvironment`, the initial containing block size, quirks mode,
reusable buffers). `context.LayoutBox(box, space)` lays out a box with the algorithm of its formatting context:
`FormattingContext.GetKind(box)` classifies the box (`Block`, `Inline`, `Flex`, `Grid`, `Table`, `Ruby`, `Math`,
`Replaced`) and `FormattingContext.For(box)` returns the algorithm (stateless singletons). The ruby containers and the internal
ruby boxes are inline boxes (`Display.IsInlineBox`) laid out by the inline layout of their block container (see
[Inline formatting context](#inline-formatting-context-csslayoutinline)). Each other kind has its arm in `For`
(the flex containers and `-webkit-box` are laid out by `FlexFormattingContext`, see [Flex layout](#flex-layout-csslayoutflex),
the grid containers and the grid lanes containers by `GridFormattingContext`, see [Grid layout](#grid-layout-csslayoutgrid) and
[Grid lanes](#grid-lanes-csslayoutgridgridlayoutalgorithmlanescs), the table wrapper boxes by
`TableWrapperFormattingContext` and the table grid boxes by `TableFormattingContext`, see [Table layout](#table-layout-csslayouttable),
the multi-column containers by `MultiColumnFormattingContext`, see
[Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation),
the `display: layout()` boxes whose layout is registered by `LayoutApiFormattingContext`, see
[Layout API containers](#layout-api-containers-csslayoutlayoutapi-csslayoutapi)).

- The input is a `ConstraintSpace`, in the writing mode of the containing block: available inline size, available block
  size (null when indefinite), the percentage bases (the containing block size; the block size is null when indefinite),
  and the border-box sizes imposed by the parent (`FixedInlineSize`/`FixedBlockSize`, for the flex and grid items and the
  table cells; `IsFixedBlockSizeIndefinite` makes an imposed block size indefinite for the percentages of the contents),
  `AutoInlineSizeIsFitContent` (an `auto` inline size is the fit-content size: the cross size of a flex item) and
  `MeasuresIntrinsicBlockSize` (the preferred block size behaves as `auto`: a layout that measures
  `BoxFragment.IntrinsicBlockSize`, the automatic block size before the min/max sizes). `ConstraintSpace.ForContentBox` builds
  the space of the children of a content box. `ContentArea.MinBlockSize`/`MaxBlockSize` give the content-box min/max block
  sizes of a box whose block size is indefinite (a column flex container clamps its main size with them).
- The output is the `BoxFragment` of the box; the parent formatting context sets its offset.
- `FormattingContext.Layout` is the common sizing of a box (CSS 2.1 [§10.3.3](https://drafts.csswg.org/css2/#blockwidth) and
  [§10.6.3](https://drafts.csswg.org/css2/#normal-block)): used paddings and margins (percentages of the inline size of the
  containing block), the preferred size (`box-sizing`, percentages of an indefinite size behave as `auto`), the min/max sizes,
  the `auto` inline margins of a block-level box in normal flow (centering, over-constrained: the inline-end margin is
  recomputed), orthogonal flows (the available inline size is the block size of the containing block, else the smallest of
  its fixed maximum block size, `ConstraintSpace.OrthogonalFallbackInlineSize`, the fixed inner size of the nearest scroll
  container (capped by its fixed maximum size and floored by its fixed minimum size, `GetScrollportFallbackSize`: found by
  walking the ancestors, Blink and WebKit do not use it) and the initial containing block; the percentages of the inline size
  of an orthogonal flow resolve against that fallback when the block size of the containing block is indefinite). The values of the sizing properties are resolved by `AxisSizing` (lengths, percentages, the intrinsic
  size keywords, `stretch`, `calc-size()`: see [Intrinsic sizing](#intrinsic-sizing)). An `auto` inline size fills the
  available space (`GetAutomaticInlineSize`, overridden by the replaced boxes), or is the fit-content size of the floats,
  atomic inlines, absolutely positioned boxes, outside markers, buttons and orthogonal flows; an `auto` block
  size is the size of the contents (`GetAutomaticBlockSize`). `ConstraintSpace.StretchInlineSize`/`StretchBlockSize` (in the
  axes of the containing block) replace an `auto` size by a stretch-fit border-box size (the absolutely positioned boxes,
  before the min/max sizes). At the end, `PositionedLayout.SetRelativeOffset` sets the relative offset of the fragment and
  `PositionedLayout.LayoutOutOfFlowDescendants` lays out the absolutely positioned boxes whose containing block is the box
  (see [Positioned layout](#positioned-layout-csslayoutpositioned)). The
  [preferred aspect ratio](https://drafts.csswg.org/css-sizing-4/#aspect-ratio) applies to every box (see
  [Aspect ratio](#aspect-ratio)). The contents are laid out
  by `LayoutContents` in a `ContentArea` (the content box: its offset in the border box, its inline size and its block size
  when definite), unless the box skips its contents. A formatting context with another sizing algorithm overrides `Layout`.
- `LayoutContext.OnSizeContainerLaidOut` is the hook of the style and layout interleaving of the size container queries
  ([CSS Contain 3](https://drafts.csswg.org/css-contain-3/#size-container)): it is called when the content box of a box with
  `container-type: size | inline-size` is sized, before its contents are laid out (see
  [Size container queries](#size-container-queries)).

### Size container queries

The size container queries and the container-relative lengths ([CSS Conditional 5](https://drafts.csswg.org/css-conditional-5/#container-queries))
use the size of the content box of the container computed by the layout: the style and the layout are interleaved
([CSS Contain 3](https://drafts.csswg.org/css-contain-3/#size-container)). A size container is laid out before its contents
(size containment makes its size independent of them), then the styles of its descendants are computed with its size, then
its contents are laid out.

- **Sizes** (`LayoutEngine`): `LayoutContext.OnSizeContainerLaidOut(box, content)` records the size of the content box of the
  container (physical, zoomed px; without the scrollbar gutters) with the number of the pass; the block size is known when
  it is definite, or when the box has size containment in the block axis (`container-type: size`: the `auto` block size
  is computed before the contents from `contain-intrinsic-size` with the min/max sizes, `FormattingContext.Layout`). The
  sizes are kept from one pass to the next (`TryGetLaidOutContainerSize`, which does not compute the layout): during a
  pass, a container that is not laid out yet has the size of the previous pass; the entries of the containers that the
  pass did not lay out are removed at its end. `TryGetContainerSize` computes the layout first.
- **Evaluation** (`ContainerQueries.GetContainerContentSize`, used by the size features and the `cq*` units): the size from
  the layout divided by the effective zoom of the container, else (no entry: before the first layout of the container, or
  a document without browsing context) the size known without layout (`BoxSizes`). The engines with an environment
  override (the minifier) never use the layout. Each size used is recorded by the `StyleEngine` for the current
  generation of styles (`RecordQueriedContainerSize`, cleared when all the styles are invalidated: the sizes used by the
  styles kept by an invalidation by element are kept with them), with a flag when two styles used different sizes of the
  same container.
- **Interleaving** (`OnSizeContainerLaidOut`): when the recorded sizes differ from the laid out size
  (`StyleEngine.IsQueriedWithAnotherSize`), `StyleEngine.InvalidateQueryContainerDescendants` discards the computed styles
  of the flat tree descendants of the container and of its pseudo-elements (they are computed again on demand, in the same
  generation: the layout runs under `SuspendUpdates`; the before-change styles of their first computation are put back,
  and a style kept from a previous generation by an invalidation by element becomes the before-change style, so that the
  transitions compare the new styles with the styles before the change, the transitions started by the discarded
  styles are canceled (they were not sampled yet: no event; `CssTransition.TransitionGeneration` is compared with the
  generation recorded before the first computation), and the sizes recorded for the inner containers are forgotten), then `BoxTree.RebuildContents(box)` (`BoxTreeBuilder.RebuildContents`) builds the children of
  the container again: `::marker`, `::before`, the flat tree children, `::after` and the fixups, the `::first-letter` boxes
  of the container, its descendants and its ancestors whose first letter was inside, the quotes continue from the depth
  recorded at the start of the contents; the boxes of the elements of the top layer in the container (viewport children)
  are updated in place (the box object is kept because the viewport may have recorded it as an out-of-flow candidate).
  The container keeps its box and its style (its style does not depend on its own size). The viewport reads the next
  sibling after laying out a child, as the root box may update the top layer boxes after it.
- **No loops**: the size of a container does not depend on the styles of its descendants (size containment; only the
  inline axis for `inline-size`, whose block size cannot be queried), so the descendants are styled again at most once
  per layout of the container. A box laid out several times in a pass (e.g. next to floats: first in the whole width, then
  in the space beside the floats; the scrollbar passes; the measures of the intrinsic sizes) styles its descendants again
  for each size. A layout of the same size does not compute the styles again.
- **Forced layout**: the computed values are computed lazily (a `cq*` length queries the container when the property is
  read), so the APIs that expose the styles compute the layout first when the element (or the originating element of a
  pseudo-element) is in a size container: `getComputedStyle()` and `computedStyleMap()` (`ComputedStyleDeclarations.GetStyle`,
  through `ContainerQueries.GetStyleWithContainerLayout`), `matchContainer()` (`ContainerQueryList.matches`, also evaluated
  by the rendering steps), and `getAnimations()` (`Document.FlushAnimationStyles` updates the layout when the styles queried a
  container size, so the animations selected by `@container` rules are created).
- Not done: the boxes of an element of the top layer that had no box before the restyle are not added; a stale
  out-of-flow candidate of the viewport is laid out when a box of the top layer changes of kind.

### Scroll-state container queries (`LayoutEngine.ScrollStateQueries.cs`, `Scrolling/ScrollStateSnapshot.cs`)

The container scroll-state queries ([CSS Conditional 5](https://drafts.csswg.org/css-conditional-5/#scroll-state-container):
`scroll-state(stuck | snapped | scrollable | scrolled)` on a `container-type: scroll-state` container) are evaluated against a
snapshot of the state of the container, not against the live layout, to avoid the layout cycles
([updating scroll state](https://drafts.csswg.org/css-conditional-5/#updating-scroll-state)).

- **Snapshot** (`ScrollStateSnapshot`: four `PhysicalEdges` sets, all physical): `stuck` = the sign of the sticky offset of the
  first fragment of the container (a positive offset comes from the `top`/`left` inset, a negative one from `bottom`/`right`);
  `snapped` = the axes in which the container is `ScrollState.Snap.TargetX`/`TargetY` of the nearest scroll container fragment
  ancestor (the viewport for the root fragment); `scrollable` = the edges past which the scroll position can move, for the axes
  whose overflow is `auto`/`scroll` (`hidden` is not reachable by the user); `scrolled` = `ScrollState.RelativeScrollDirections`,
  the direction of the last relative scroll that moved, per axis (recorded by `RecordRelativeScroll` from
  `CssomViewScrolling.ScrollElementBy`, i.e. `scrollBy()` on an element or the window, from the clamped and snapped
  destination: a `scrollBy()` at an edge changes nothing; the absolute scrolls are ignored). The scrolling box of the root
  element is the viewport (its overflow is propagated); an element whose overflow is propagated to the viewport (the body) is
  not a scroll container.
- **Containers**: `IndexFragments` collects the first fragments of the principal boxes whose style has
  `ContainerType.ScrollState` (`_scrollStateContainers`). The snapshots that are not none are kept by element
  (`_scrollStateSnapshots`); `GetScrollStateSnapshot(element)` never computes the layout.
- **When** (the snapshot post-layout state steps, [whatwg/html#11613](https://github.com/whatwg/html/pull/11613),
  [CSSOM View](https://drafts.csswg.org/cssom-view/#post-layout-snapshot)): `UpdateRendering` runs `UpdateScrollStateSnapshots`
  once after the style and layout update that precedes each gathering of the resize observations; when a snapshot changed, the
  styles and the layout are updated again before the resize observers ("if any snapshotted state changed, then continue"). So a
  scroll or a layout change is seen by the styles at the next rendering update, a forced style update (`getComputedStyle()`)
  in between still sees the previous state, and the resize observer callbacks of the same frame see the new state.
  Additionally, a container laid out for the first time (inserted, made a scroll-state container, or the first layout) is
  snapshotted at the end of the layout pass (`TakeInitialScrollStateSnapshots`, as the browsers: a script that reads the styles
  before the first frame sees its initial state); `getComputedStyle()` lays out the document when an ancestor of the element is
  a scroll-state container (`ContainerQueries.GetStyleWithContainerLayout`), so this snapshot is taken.
- **Restyle**: a change of the snapshots increments `ScrollStateSnapshotVersion`, which the `StyleEngine` includes in its
  versions: a new generation of styles (the transitions compare with the styles before the change), then a new box tree and a
  new layout (no incremental restyle). Only when a style evaluated a scroll-state feature (`OnScrollStateQueried`, which also
  registers the document for the rendering steps): the snapshots of a document without scroll-state queries are kept up to date
  without restyle. `StyleEngine.MayHaveScrollStateQueries` (a rule in a `@container` rule with a scroll-state query,
  `CascadeRuleSet.HasScrollStateQueries`) makes the rendering steps lay out a document that never read its geometry. The
  scrolls and the layout changes request a rendering update once the document queried a scroll state.
- **Evaluation** (`ContainerQueryContext.EvaluateScrollStateFeature`): a boolean context is true when the state is not none;
  the logical values (`block-start`..., `block`/`inline`, also for `snapped`) use the writing mode and the direction of the query
  container; `snapped: both` requires both axes. The state is none without browsing context, for the engines with an
  environment override, and before the first snapshot.
- The `scrolled` feature reflects the relative scrolls: `scrollBy()` and the scrolls of the user (the wheel, a panning
  touch and the keys, see [User input](browsing-context.md#user-input)); the smooth relative scrolls record their direction when they start.

The block layout is described in [Block formatting context](#block-formatting-context-csslayoutblock), the inline layout in
[Inline formatting context](#inline-formatting-context-csslayoutinline), the replaced boxes in
[Replaced elements](#replaced-elements-csslayoutreplaced).

### Aspect ratio

`FormattingContext.Layout` applies the [preferred aspect ratio](https://drafts.csswg.org/css-sizing-4/#aspect-ratio) of a box
(not an inline box nor an internal table box): the `aspect-ratio` property, or the natural aspect ratio of a replaced
element (`GetNaturalAspectRatio`) for `auto` and `auto && <ratio>` (a degenerate ratio behaves as `auto`). It is an
`AspectRatioTransfer` between the border-box sizes: the ratio applies to the content box (`auto`, `auto && <ratio>`) or to
the box of `box-sizing` (`<ratio>`), so the paddings and borders outside of that box are removed before the transfer.

- The block size is resolved first (when definite): an `auto` inline size is then transferred from the preferred block size
  (before min/max, "the resolved preferred size in the ratio-determining axis"), else it comes from `GetAutomaticInlineSize`.
  An `auto` block size is transferred from the used inline size, and is definite for the contents (their percentages
  resolve against it); it is not `auto` for the margin collapsing (css-sizing-4 §4.2.1). A replaced element whose inline
  size is its natural inline size uses its natural block size instead (CSS 2.1 §10.6.2), unless `aspect-ratio` is an
  explicit `<ratio>` (`GetNaturalBlockSize`).
- [Min/max transfers](https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers): the definite min/max sizes of the
  ratio-determining axis are transferred to the `auto` axis (capped/floored by its own sizes) and applied with its min/max
  sizes (`ClampBorderBoxSize(..., transferredMin, transferredMax)`); an intrinsic size keyword (`width: max-content`) is an
  indefinite preferred size too, so they apply to it. For a replaced element with natural sizes, this gives
  the constraint table of [CSS 2.1 §10.4](https://drafts.csswg.org/css2/#min-max-widths).
- [Automatic minimum](https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum): a non-replaced box that is not a scroll
  container in the ratio-dependent axis, with `min-*: auto`, grows to its contents (block axis: the block size of its
  contents; inline axis: its min-content size, `BlockFormattingContext.ComputeIntrinsicInlineSizes`), capped by its
  maximum size. The rows of a grid container whose block size comes from its aspect ratio are aligned (`align-content`) in the
  grown size (`ContentArea.BlockSizeGrowsToContents`). The intrinsic size keywords of the minimum and maximum block sizes of
  such a box are the size given by the ratio, not the block size of its contents (`min-height: min-content` does not make it
  grow, [csswg-drafts#5032](https://github.com/w3c/csswg-drafts/issues/5032)).
- A definite block size limited by an intrinsic size keyword of the minimum or maximum block size (`height: 200px;
  max-height: max-content`) is only known once the contents are laid out: the box is laid out first in the specified size,
  apart from the block formatting context of its parent (`ConstraintSpace.MeasuresAutomaticBlockSize`: its children resolve
  their percentages and break their lines in it, the line of a single-line flex container and its stretched items keep the
  cross size of the items), which gives its automatic block size, then its contents are laid out in the limited size (the
  lines of a flex container are not stretched to the specified size,
  [csswg-drafts#12123](https://github.com/w3c/csswg-drafts/issues/12123)).
- The intrinsic block sizes of a replaced element with an aspect ratio are its used inline size transferred through the
  ratio (`width: 100px; height: 1000px; max-height: min-content` on a square image is 100px tall,
  [csswg-drafts#3973](https://github.com/w3c/csswg-drafts/issues/3973)). When the block size is the ratio-determining one
  (the inline size is `auto` or an intrinsic size keyword), the intrinsic size keywords of its minimum and maximum block
  sizes, which limit the size that is transferred, resolve with a tentative inline size: the one the box has when its block
  size properties are ignored (as Blink, [csswg-drafts#12333](https://github.com/w3c/csswg-drafts/issues/12333): a 100x50
  canvas with `width: max-content; height: 0; min-height: max-content; aspect-ratio: 1` is 100x100).
- The absolutely positioned boxes use the same transfer, their stretch-fit sizes being the preferred sizes (see
  [Positioned layout](#positioned-layout-csslayoutpositioned)).
- The size transferred from a definite block size is also the min-content and max-content inline size of the box
  (`AxisSizing.TransferredSize`, "its automatic sizes are calculated the same as for a replaced element with a natural
  aspect ratio"): the intrinsic size keywords and the intrinsic contributions use it (see [Intrinsic sizing](#intrinsic-sizing)).
- The grid items with an aspect ratio and a `normal` alignment are sized as block-level boxes (see [Grid layout](#grid-layout-csslayoutgrid)).

### Intrinsic sizing

`IntrinsicSizing` implements [CSS Sizing 3](https://drafts.csswg.org/css-sizing-3/#intrinsic) (and the parts of
[CSS Sizing 4](https://drafts.csswg.org/css-sizing-4/) and [CSS Values 5](https://drafts.csswg.org/css-values-5/#calc-size) that
browsers ship): the intrinsic sizes of the boxes, their contributions, and the resolution of the sizing values.

- **Intrinsic sizes** (`IntrinsicSizing.GetIntrinsicInlineSizes(context, box, contentBlockSize)` → `IntrinsicSizes(MinContent,
  MaxContent)` of the content box, in the inline axis of the box): computed by the formatting context of the box
  (`FormattingContext.ComputeIntrinsicInlineSizes(context, box, in IntrinsicSizingInput)`, abstract: block, inline and
  replaced, flex, grid and table layouts). They do not
  depend on the available space; the input is the definite block size of the content box (the basis of the percentage block
  sizes of the children, the available space of the orthogonal flows), which is also the key of the cache:
  `ContainerBox.IntrinsicSizesCache` holds the sizes for one `LayoutContext.Pass` and one input, so a box is measured once
  per pass however many ancestors ask for it (no exponential cost for nested inline-blocks or floats). A box that skips its
  contents has intrinsic sizes of 0.
- **Size containment** ([CSS Contain 2](https://drafts.csswg.org/css-contain-2/#containment-size), `IntrinsicSizing.HasSizeContainment`:
  `contain: size | inline-size`, the size container types, a box that skips its contents; not the inline boxes, the internal
  table and ruby boxes, the tables): the intrinsic inline sizes and the `auto` block size are computed as if the box were empty, with the
  explicit intrinsic inner size of `contain-intrinsic-size` ([CSS Sizing 4](https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override);
  0 for `none`, or the sizes of the empty box given by its formatting context: `FormattingContext.ComputeEmptyIntrinsicInlineSizes`/
  `ComputeEmptyContentBlockSize`, the tracks of a grid without its items; the last remembered size for `auto`, see
  [Containment and content-visibility](#containment-and-content-visibility)).
- **Contributions** (`IntrinsicSizing.GetContribution(context, box, containingBlock, containingBlockBlockSize)` → outer
  sizes in the inline axis of the containing block): the box sized as an `auto`-sized float in a zero-sized or
  infinitely-sized containing block. [Cyclic percentages](https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution):
  the percentage preferred and maximum sizes behave as `auto`/`none`, the percentages of the minimum sizes, of the margins
  and of the paddings resolve against 0 (`min-width: calc(160px + 0%)` is 160px, as in Blink; Gecko and WebKit ignore such a
  minimum, [csswg-drafts#10969](https://github.com/w3c/csswg-drafts/issues/10969)); the block axis does the same (`Size.TryResolveMinimum`:
  `min-height: calc(25px + 50%)` is 25px in a containing block whose block size depends on its contents, a percentage alone is 0);
  `fit-content(<percentage>)` is the min-content size for a minimum size and the max-content size for a maximum size;
  `stretch` behaves as `auto`. `fit-content` contributes its min-content size to the min-content contribution and its
  max-content size to the max-content one. A box with a preferred aspect ratio and a definite block size contributes the
  transferred size, with its automatic minimum size (min-content); the minimum and maximum block sizes of a box with an
  aspect ratio and an automatic inline size are transferred through the ratio. The stretched item of a single-line flex
  container with a definite cross size has a definite block size (`stretchesBlockSize`), and the definite preferred size of a
  flex item does not make its contribution smaller than its contents (`isFlexItem`, see [Flex layout](#flex-layout-csslayoutflex)).
  The scrollbar gutters
  of a scroll container are added. The replaced elements and the orthogonal flows are measured by a layout
  (`MeasureContribution`): with `ConstraintSpace.IsInlineSizeIndefinite` (the percentages and `stretch` of the inline axis
  of the containing block behave as `auto`, the auto margins are not resolved) for the max-content contribution, and with a
  zero percentage basis for the min-content contribution of the [compressible replaced elements](https://drafts.csswg.org/css-sizing-3/#min-content-zero)
  (not the button-like inputs), both with an available inline size of 0 (a replaced element with only an aspect ratio
  contributes 0, as in the browsers). As in the browsers, only the percentages of the inline axis of the replaced element
  compress it (the percentage block size of an orthogonal replaced element behaves as `auto` for both contributions,
  [csswg-drafts#12133](https://github.com/w3c/csswg-drafts/issues/12133)), and a form control is only compressed by its
  preferred size (`width: 50%`, not `max-width: 50%` alone): its min-content size is then zero too (`min-width: min-content`
  does not keep its default size; in the block axis, as in Gecko and WebKit, `min-height: min-content` is zero with a
  percentage `height` or `max-height`). In quirks mode, the basis of the percentage heights
  (`IntrinsicSizingInput.QuirksPercentageBlockSize`, see below) is passed through the auto height block containers as in
  the layout: a replaced element with `height: 100%` contributes the inline size that its aspect ratio gives (the
  shrink-to-fit width of a float with a definite height around an image in an auto height block). An orthogonal flow contributes its block size, laid out with the available inline space
  of [CSS Writing Modes §7.3](https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto) (the block size of the containing
  block, else the initial containing block).
- **Sizing values** (`AxisSizing`, a struct per box and axis built by `FormattingContext.Layout`: percentage basis, box-sizing
  edges, intrinsic edges (paddings, borders, scrollbar gutters), stretch-fit size, definite content block size):
  `Resolve(size, SizingProperty)` returns the border-box size or null when the value behaves as `auto`/`0`/`none`, and
  `Clamp(size, transferredMin, transferredMax)` applies the min/max sizes (the maximum first). In the inline axis,
  `min-content`/`max-content` are the intrinsic sizes plus the edges, `fit-content` is `min(max-content, max(min-content,
  stretch))`, `fit-content(<length-percentage>)` the same formula with the argument; they are not affected by `box-sizing`.
  In the block axis, the intrinsic keywords are the automatic (content-based) block size (`AxisSizing.ContentSize`): a
  preferred size behaves as `auto`, and the minimum and maximum sizes are applied again once the contents are laid out (the
  replaced elements know it before: their natural block size, or the size transferred from the used inline size).
- **`stretch`** ([stretch-fit sizing](https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing), also `-webkit-fill-available`
  and `-moz-available`, which the sizing properties accept as legacy keywords: `CssPropertyRegistry.OverriddenTypes`): the available size minus the margins (the auto margins are 0), floored by the paddings and borders;
  in the inline axis the available inline size (the space between the floats for the boxes that avoid them, the
  inset-modified containing block of an absolutely positioned box), in the block axis the block size of the containing block
  when it is definite (else `stretch` behaves as `auto`, `0` or `none`). An in-flow child treats its block-start (block-end)
  margin as 0 when its parent participates in the block formatting context of its own parent and has no block-start
  (block-end) border or padding (`ConstraintSpace.StretchIgnoresBlockStartMargin`/`StretchIgnoresBlockEndMargin`, set by
  `BlockFormattingContext` from `ContentArea.StartMarginCollapsesWithChildren`/`EndMarginMayCollapseWithChildren`).
- **Shrink-to-fit / fit-content** (`FormattingContext.GetAutomaticInlineSize`): the `auto` inline size of the floats, atomic
  inlines, absolutely positioned boxes (without both inline insets), outside markers, buttons and orthogonal flows
  ([CSS Writing Modes](https://drafts.csswg.org/css-writing-modes-4/#auto-multicol), without the automatic multi-column
  layout) is the fit-content size in the available space.
- **`calc-size()`** (`CalcSizeValue`, parsed once from the computed value and kept on its `CalcLength`): it behaves as its
  basis for everything but the size (`Size.BehavesAs`, `Size.IsContentBasedInBlockAxis`); its calculation is evaluated by
  `CssCalc` with the `size` keyword bound to the used size of the basis (`CssCalcContext.SizeKeywordValue`), the percentages
  of the calculation resolving against the containing block or 0. A `calc-size(auto, …)` basis is the automatic size
  (inline axis: computed before the calculation; block axis: after the contents are laid out; for the minimum main size
  of a flex item: its [automatic minimum size](https://drafts.csswg.org/css-flexbox-1/#min-size-auto)).
- **`interpolate-size`** (`CssCalcSizeInterpolation`, used by `CssInterpolation` for the sizing properties): two
  `calc-size()` values (after the [preparation for interpolation](https://drafts.csswg.org/css-values-5/#calc-size-prepare-for-interpolation):
  nested bases substituted, length bases replaced by `any`, percentage bases by `100%` with the percentages de-percentified)
  interpolate or add when their bases match or one is `any`; a `<length-percentage>` is `calc-size(any, value)`, and with
  `interpolate-size: allow-keywords` a sizing keyword is `calc-size(keyword, size)`. The result is a `calc-size()` whose
  calculation combines the two calculations (the like `size` terms are not merged by the simplification); the resolved
  values of `width`/`height` are the used sizes in pixels (see [Resolved values](#resolved-values-csslayoutlayoutresolvedvaluescs)).
  Not done: the interpolation of the deeply nested `calc-size()` whose expansion is too large is not refused
  ([csswg-drafts#10369](https://github.com/w3c/csswg-drafts/issues/10369)); the computed value simplifies them.
- **Automatic minimum size**: the automatic minimum of the boxes with an aspect ratio (min-content, CSS Sizing 4) for every
  non-replaced box; the content-based minimum sizes of the flex items (see [Flex layout](#flex-layout-csslayoutflex)) and of
  the grid items (see [Grid layout](#grid-layout-csslayoutgrid)).

### Replaced elements (`Css/Layout/Replaced/`)

`ReplacedFormattingContext` lays out the replaced boxes (`ContainerBoxKind.Replaced`): its `Layout` computes their contents
(`ReplacedContent.Get`, kept in `LayoutBox.LayoutState` during the layout of the box), runs the shared sizing, then completes
the fragment. The contents of the box (the `foreignObject` boxes of an `svg`) are laid out by the SVG layout (see
[SVG layout](#svg-layout-svg)).

- **Natural dimensions** ([CSS Images](https://drafts.csswg.org/css-images-3/#natural-dimensions)) in the coordinates of the
  fragment tree (multiplied by the effective zoom), as an `ImageNaturalSize` (each value may be absent):
  - `img`: the density-corrected natural size of the current request (`srcset` `x`/`w` descriptors), the EXIF orientation
    applied unless `image-orientation: none` (the dimensions read from the header are flagged `OrientationSwapsAxes`: the
    EXIF orientation of a JPEG image, and of the `eXIf` chunk or the EXIF raw profile of a PNG image, before the image data,
    `Rendering/ImageMetadata.cs`; `none` is ignored for the images that are not CORS-same-origin). With a rendering backend,
    the natural size of an image whose header the library cannot read comes from the decoder of the backend. An image that is not available
    ([HTML rendering](https://html.spec.whatwg.org/multipage/rendering.html#images-3)): 0x0 while it is fetched or when it
    represents nothing (`alt=""`), the 16x16 broken image icon (without ratio) when it failed and has no `alt` attribute;
    an image that represents some text (a non-empty `alt`, not expected to change) is not a replaced box: the box tree
    builds a non-replaced box with a generated text run of the `alt` text (`HtmlImageElement.GetAltTextRendering`; quirks
    mode keeps a replaced box when the element has dimensions).
  - `input type=image` (`Html/Forms/HtmlInputElement.ImageButton.cs`): the image of its `src` (fetched when the element enters
    the Image Button state with a `src`, when `src` is set, and when it is adopted; destination `image`, initiator type `input`,
    credentials `include`; it delays the load event, fires `load`/`error`, and an image of the list of available images is
    available at once, as in Chrome and Safari), the EXIF orientation applied as for `img`; while it is available, the box is
    an image (not a form control: `FormControlSizing.Get` returns null). Otherwise the element is a button labeled with its
    `alt` text (only `alt` while the image is fetched; `alt`, `value`, then "Submit" otherwise, as Chrome), except that, as in
    Chrome and Firefox, an image button with a non-empty `alt` whose image failed or has no source is a non-replaced box
    whose content is the `alt` text, as an `img` (`GetImageButtonAltTextRendering`). `width`/`height` are the rendered size
    of the content box, else the concrete size of the image (default sizing with a 300x150 default object size) replaced by
    the attributes.
  - `canvas`: its `width`/`height` attributes (replaced only when scripting is enabled); `video`: the natural size of its
    poster frame while it represents it (`HtmlVideoElement.GetNaturalSize()`: until it has video data, or while it is paused at
    its start), else the natural size of the video its media player reports, else none; `audio` with controls:
    300x54; `embed`/`object` whose resource is an image (not an SVG image): the image (`ImageNaturalSize` of the element; as in
    the browsers, the image is displayed by the element itself, as the image of an `img` element, not by a content
    navigable); `embed`/`object` displaying an SVG document: the `width`/`height`/`viewBox` of its root (the root `svg`
    element of the document then fills its viewport, the box of the element: the `width` and `height` of the outermost `svg`
    of a document embedded by reference only give the natural dimensions,
    [SVG](https://www.w3.org/TR/SVG11/coords.html#ViewportSpace)); `iframe`, `frame`, the other `embed`/`object`: none
    (a `frame` that is not a child of a `frameset`: 0x0, its content navigable has an empty viewport and is not painted,
    see Framesets in [Block formatting context](#block-formatting-context-csslayoutblock)).
  - The outermost `svg` element: its `width`/`height` attributes when they are absolute lengths and the ratio of
    width/height, else of the `viewBox` (`ImageHeaders.GetSvgNaturalSize`, also used for the SVG images and documents);
    the attributes are also presentation attributes of `width`/`height`, which may override them. A natural block size of
    zero with a natural inline size and an aspect ratio (an SVG image with `height="0"` and a `viewBox`) is replaced by
    the inline size transferred through the ratio, as in the browsers (they transfer the natural inline size whenever they
    have a ratio).
  - The images of the generated content (`content: url()`, `list-style-image`): the natural size of the image fetched by
    `CssImageFetching` (recorded per URL in `Document.CssImageNaturalSizes`, 0 until it is fetched), none for a gradient; the
    image of an `image-set()` has the natural resolution of its option (an image of 100x50 image pixels at `0.5x` is
    200x100px), and a `cross-fade()` the weighted average of the natural dimensions of its images. The
    default object size of a list marker image is 1em square.
  - `object-view-box` ([CSS Images 5](https://drafts.csswg.org/css-images-5/#the-object-view-box), `ReplacedContent.ViewBox`):
    the `inset()`, `rect()` or `xywh()` rectangle, resolved against the rectangle of the natural dimensions, is the contents
    of the element for its sizing and for `object-fit`/`object-position` (the natural size and ratio are the ones of the
    view box). As in Chrome, it is ignored for the contents without natural width and height, for the nested documents, and
    when it is empty.
  - Size containment: the natural size is `contain-intrinsic-size` (0 for `none`) without ratio (e.g. `img sizes=auto`);
    `object-fit` and `object-view-box` still use the natural dimensions of the contents (`ReplacedContent.ObjectNaturalSize`).
- **Sizing**: the `auto` inline size (when not transferred through the ratio) is the natural inline size (completed through
  the ratio when only the other one is known); a box with only an aspect ratio (natural or `aspect-ratio`) fills the
  available space as a block (the SVG images with only a `viewBox`, WPT `video-intrinsic-width-height.html`); else the
  default object size (300x150). The `auto` block size without ratio is the natural block size, else 150px. Unlike the
  non-replaced boxes, the `auto` margins of a block-level replaced box take the remaining space (centering).
- **`object-fit` / `object-position`**: `BoxFragment.ReplacedContentRect` is the rectangle the replaced content is painted
  in (for the painting and `getBoxQuads`), in the coordinates of the fragment: `fill` (the content box), `contain` /
  `cover` (contain/cover constraint with the natural ratio, else the content box), `none` (the default sizing algorithm with
  the content box as default object size), `scale-down` (the smaller of `none` and `contain`), positioned by
  `object-position` (percentages of the free space). With an `object-view-box`, the view box is fitted and positioned, and
  the rectangle is the one of the whole contents scaled as the view box (the parts outside of the view box are painted
  when they are in the content box). The rectangle is also the viewport of the SVG document of an `embed` or `object`
  element (`object-fit` applies to it as to an image, as in Firefox). Null for the form controls.
- **Form controls** (`FormControlSizing`, the [widgets](https://html.spec.whatwg.org/multipage/rendering.html#widgets) of
  HTML, from the primary font of the control): text fields `(size-1)×avg + max` (`OS/2.xAvgCharWidth` and the `head` bounding
  box width, rounded as in Chrome; the advance of `0` without OS/2 table) × the line height; `textarea` `cols×avg` ×
  `rows` lines; `select` the widest option/optgroup label (list box: the max-content inline size of its rows, which are
  boxes, × display size rows of the line height, 4 for `multiple`); buttons the
  width of their label (the line feeds of the value of an `input` button break its label into lines, as in the browsers,
  whose user agent style sheets have `white-space: pre` for these controls: the block size is the one of the lines and the
  baseline the one of the last line); `checkbox`/`radio` 13x13, a switch (a checkbox with a `switch` attribute,
  whatwg/html#9546, tentative) 26x13, `range` 129x16, `color` 50x27;
  date/time fields the width of a format; `meter` 5em×1em, `progress` 10em×1em; `field-sizing: content` (the value, or the
  placeholder in the font of its `::placeholder` box, which may make the line taller, + 1px caret; the lines of a
  `textarea` are the ones laid out in its inline size, where they wrap: `ReplacedFormattingContext.GetAutomaticBlockSize`;
  a list box has as many rows as displayed options, none when it has none). A drop-down box with size containment is
  sized as an empty drop-down box (its arrow and one line) in the axes where `contain-intrinsic-size` is `none`, as in the
  browsers (it is a widget, not a replaced element). The labels are measured after `text-transform`. As in the browsers, a drop-down `select` with
  its native appearance ignores `line-height` (it is laid out with `normal`; the computed value is not changed), and `size=0` is a drop-down box whose first option
  is selected (the specification only says so for a display size of 1). The sizes are logical (swapped in vertical writing modes); the buttons have the paddings and
  borders of Chrome in the UA style sheet (`padding-block: 1px`, `padding-inline: 6px`, `2px outset`), a drop-down `select`
  adds 1em for its arrow, a list box and a `textarea` reserve the space of their vertical scrollbar (unless
  `overflow: hidden`/`clip`; the horizontal one only for `overflow: scroll`). The text controls have a baseline
  (`FormControlBaseline`: a line centered in the content box, or the first line of a `textarea`/list box) used by the inline
  layout; the other controls are aligned by their margin box. As the specification requires, the resolved value of
  `line-height` of a text field (`text`, `search`, `tel`, `url`, `email`, `password`) is its used value, no smaller than
  `normal` (`ComputedStyle.GetResolvedValue`).
- **Text controls** (`TextControlLayout`): the value of a `textarea` and of a text field (`input` of type `text`, `search`,
  `tel`, `url`, `email`, `password` or `number`) is laid out by the inline layout. The replaced box of the control (its
  sizes stay the natural sizes above) has, as in the browsers, an anonymous block container child, the **inner editor**
  (`ContainerBox.IsTextControlInnerEditor`, built by `BoxTreeBuilder.BuildTextControlContents`; no DOM node is created),
  whose text is a generated text run of the API value (bullets for a password; the offsets of the text fragments are the
  offsets in the value), followed by a line break when the value is empty or, in a `textarea`, ends with a line feed (the
  inner editor always has the line of the caret). Its style (`StyleEngine.CreateInnerEditorStyle`) inherits from the control
  (`text-transform`, `tab-size`, `letter-spacing`, `text-align`, `text-indent`, `direction`, `word-break`, `line-break`,
  `writing-mode`...), with its `unicode-bidi` (`dir=auto` is `plaintext`). In a **`textarea`** it always preserves the white
  space as in the browsers (`white-space: normal` behaves as `pre-wrap`, `nowrap` as `pre`; `break-spaces` is kept), does
  not wrap when `wrap=off`, and breaks the words that overflow (`overflow-wrap: break-word` in the user agent style sheet,
  as in the browsers); the `textarea` is the scroll container of its inner editor (`ScrollContainers.GetUsedOverflow`:
  `visible` behaves as `auto`), with its paddings in its scrolling area, `scrollTop`/`scrollLeft`/`scrollHeight`, and classic
  scrollbars that take their space from the space reserved in its natural size (the lines wrap in the whole content box
  while no scrollbar is displayed). In a **text field** the inner editor is `white-space: pre` and a scroll container that
  hides its overflow (`ContainerBox.ScrollingElement` is the `input`: `scrollLeft` scrolls the line in the content box),
  its line is at least as tall as the normal line height, and it is centered in the block axis of the content box
  (`TextControlLayout.AlignContents`, also when it is taller); as in csswg-drafts#13502, `clientLeft`/`clientWidth`/
  `scrollWidth` of a text field leave the paddings of the inline axis out, and its block axis does not scroll
  (`scrollHeight` is the padding box). When the value is empty and the control has a placeholder, a box of the
  `::placeholder` pseudo-element (a block; in a text field a clipped line of the line height of the control; the
  `writing-mode`, `direction` and `text-orientation` of the control) is laid out over the inner editor. The baseline of the
  control is the first baseline of its inner editor. `TextControlLayout.GetCaretPosition`/`GetCaretRect` give the caret
  positions from the line boxes (`caretPositionFromPoint()`, and the caret and the selection that the pointer sets in the
  value, see [browsing-context.md](browsing-context.md#selection-with-the-pointer)), and `RevealCaret` scrolls the value
  to the caret moved by the arrow keys, Home and End. The value edited by the user at the selection of the control (see
  [browsing-context.md](browsing-context.md#text-controls)) is laid out again. Not done: the other controls (the date and
  time fields, the buttons, a drop-down `select` with native appearance; see
  [the select elements with base appearance](#select-elements-with-base-appearance)) have no boxes for their contents
  (they are painted, see [Painting](#painting-csslayoutpainting)); the value is not scrolled to
  the caret after an edit of the user or a press of the pointer (the control is revealed as a whole); the wrapping
  transformation of `wrap=hard`.
- **Controls whose parts are boxes** (`FormControlContents`, built by `BoxTreeBuilder.FormControls.cs`; the specification
  leaves these parts to the user agent, and the browsers use shadow trees of the user agent, which the library does not
  have). As the inner editor of a text control, the parts are in an anonymous block container, the *contents box*
  (`ContainerBox.IsFormControlContents`), the child of the replaced box of the control, whose natural sizes stay the ones
  above; it is laid out in the content box, and its fragments are painted and hit tested as the contents of the other
  boxes (`PaintOrder` descends into these replaced boxes; `BoxGeneration`: their children are being rendered).
  - **List box with native appearance** (a `select` with `multiple` or a display size greater than 1 whose `appearance` is
    not `base-select`): the contents box holds the boxes of the children of the select element in the flat tree, so the
    author styles apply to the rows (`option`, `option:checked`, `optgroup`, `hr`, any other element). The box of an
    `option` is a block of at least one line (`min-block-size: 1lh`) whose text is the
    [label](https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-label) of the option (its children
    are not rendered, as in the browsers); an `optgroup` holds a row for its label (the text of its `legend`, which is not
    rendered, else its `label` attribute), then its children. The contents box is as wide as the widest row (at least the
    content box): the rows do not wrap and the highlight of a selected row spans the scrollable overflow. The list box is
    the scroll container of its rows, as a `textarea` (`FormControlContents.IsScrollContainer`,
    `ScrollContainers.GetUsedOverflow`: with `overflow: visible`, the block axis is `auto` and the inline axis `hidden`,
    as in the browsers), with `scrollTop`/`scrollLeft`, the classic scrollbar of the block axis taking its space from
    the natural size, in the vertical writing modes too. A selected row has a gray background
    (`option:checked { background-color: light-dark(#cecece, #545454) }`), and `SelectedItem`/`SelectedItemText`
    `!important` in a focused list box (as in Chrome, where the author cannot restyle the selected rows of a focused list
    box). Not done: the indentation of the options of a group, the paddings of the rows of the browsers, the
    drop-down box with native appearance (it is painted).
  - **Slider-like controls with primitive appearance** ([CSS Forms](https://drafts.csswg.org/css-forms-1/#slider-pseudos):
    `input type=range`, `meter` and `progress` whose `appearance` is `none`, or that are devolved widgets; with native
    appearance they are painted as widgets, and their pseudo-elements have no box): the contents box, which has the size
    of the content box (when the block size of the control is `auto`, its natural block size clamped by its minimum and
    maximum block sizes) and is the containing block of the thumb (`position: relative`), holds the box of
    `::slider-track`, which holds the box of `::slider-fill` (it inherits from the track), then the box of
    `::slider-thumb`. They are fully styleable (blocks in the user agent style sheet: the track and the fill have
    `block-size: 100%`; the thumb of a range is `position: absolute`, 16px × 16px, `border-radius: 50%`, centered in the
    block axis, and `display: none` for `meter` and `progress`). The user agent gives the fill the progress of the control
    as its inline size (a percentage of the track; 0 for an indeterminate `progress`) and moves the thumb: its
    inline-start inset is the progress and `translate` moves it back by the same fraction of its own size
    (`BuildSliderParts`: these three declarations replace the ones of the author). Without author style, a `progress` keeps
    the gray track and the green fill of the unstyled `::-webkit-progress-bar`/`::-webkit-progress-value` of Blink and
    WebKit, a `meter` is empty, and a range only shows its thumb, which, as in the browsers, has its own native
    appearance (`appearance: auto` on the pseudo-element: a disc of the accent color, until the author sets one of the
    properties that disable the native appearance on it). Not done: `slider-orientation`, the vendor-prefixed pseudo-elements (`::-webkit-slider-thumb`,
    `::-moz-range-track`...).
  - **File upload control** (`input type=file`; not a replaced box): an inline-block box whose anonymous line (it does
    not wrap) holds the box of the [`::file-selector-button`](https://drafts.csswg.org/css-forms-1/#file-selector-button-pseudo)
    pseudo-element (an inline-block with the borders, paddings and colors of the buttons in the user agent style sheet,
    `margin-inline-end: 4px`; its text is "Choose File", "Choose Files" with `multiple`), then the text ("No file chosen",
    the name of the selected file or the number of files). The control is sized by its line, so the styles of the button
    change its size. It has no `::before` nor `::after`.
  - **Controls with base appearance** ([CSS Forms](https://drafts.csswg.org/css-forms-1/#appearance): the computed
    `appearance` of an `input`, `textarea`, `button`, `meter`, `progress`, `fieldset` or `legend` element is `base`; the
    select element has its own rules, see [Select elements with base appearance](#select-elements-with-base-appearance)):
    the Basic Appearance User Agent Stylesheet applies to the control and to its pseudo-elements (see
    [computed-style.md](computed-style.md#form-controls-with-base-appearance)), and these controls are not replaced boxes
    but regular boxes that hold the boxes of their parts (`FormControlContents.HasBaseAppearanceBoxes`,
    `BuildGeneratedControlContents`): a checkbox or a radio button holds its `::before`, its
    [`::checkmark`](https://drafts.csswg.org/css-forms-1/#checkmark) and its `::after`; an `input` button its label (a
    text; as the file upload control, it is atomic whatever its display: an inline-flex box in the style sheet); a color
    well the box of its `::color-swatch` (the user agent sets its `background-image`: the color of the control over the
    pattern of the specification, `control-value()` not being supported); a slider, a meter or a progress bar the boxes
    of `::slider-track`, `::slider-fill` and `::slider-thumb` as above (the range is the containing block of its thumb).
    A `meter` and a `progress` keep the inline size of HTML (5em and 10em) as their intrinsic inline size
    (`contain: inline-size` and `contain-intrinsic-inline-size` in the rules that the library adds: the specification gives
    them no size, and the percentage of their track would give them none), so they stretch as the other boxes. The text
    fields, the `textarea`, the date and time fields and the `button` element keep their boxes (a replaced box with an
    inner editor, a painted value, a regular box): only their styles change. Not done: `::picker-icon` and the pickers of
    the controls other than the select element, the parts of the text fields, of the number fields and of the date and
    time fields (`::field-content`, `::clear-icon`, `::step-control`...), the switches, `control-value()`.
- **Invalidation**: an image load (`HtmlImageElement.InvalidateRendering` after each update of the image data and each
  `load`/`error`), a poster frame, a CSS image, an `object` image or a nested document of an `embed`/`object` calls
  `Document.InvalidateLayout()`.
- **Script-visible values**: `HTMLImageElement.width`/`height` are the rendered size of the content box when the image is
  being rendered (a forced layout; the sum of the fragments for an alternative text), `x`/`y` the position of the first
  border box relative to the initial containing block; `sizes=auto` uses the content box width of the image (its
  concrete object size).
- **Intrinsic sizes** (`ReplacedFormattingContext.ComputeIntrinsicInlineSizes`): the natural inline size, or the definite
  block size of the content box transferred through the preferred aspect ratio, else the default object size; a box with only
  an aspect ratio has intrinsic sizes of 0 (its `max-content` is the stretch fit into the available space when it is
  definite: WPT `svg-intrinsic-size-*`). The intrinsic block sizes (the keywords of `min-height`/`max-height`...) are the
  natural block size, or the block size transferred from the used inline size.
- Not done: `image-resolution` (no browser supports it), the
  `appearance: none` checkboxes/radios with children, the native borders and paddings of the other controls, the `number`
  spin button and the `search` cancel button widths, the controls of the media elements, the `svg` layout of `foreignObject`.

### Containment and content-visibility

[CSS Contain 2](https://drafts.csswg.org/css-contain-2/): `contain`, the size container types and `content-visibility`.

- **Used containment.** `LayoutStyle.HasLayoutContainment`/`HasPaintContainment`/`HasStyleContainment` combine `contain`
  (`content` and `strict` included), the size container types (style and size containment, and an independent formatting
  context but no layout containment: csswg-drafts#10544) and a `content-visibility` other than `visible` (layout, style and
  paint containment: the used value of `contain`). `UsedContainment` removes the boxes
  they do not apply to: `CanApplySizeContainment(box)` (not the inline boxes, the internal table and ruby boxes, the tables:
  this is also where `content-visibility` applies) and `HasLayoutContainment(box)`/`HasPaintContainment(box)` (the table cells
  and the tables are included). The table captions are not internal table boxes: all the containments apply to them.
  `CanApplySizeContainment(element, style)` is the same check from the computed style (for
  `BoxGeneration`, innerText...). `UsedContainment.EstablishesFixedPositionContainingBlock(box)` is the containing block
  (and stacking context) flag of the style without the containment of a box it does not apply to (a row or a row group with
  `contain: layout` is not the containing block of its absolutely positioned descendants).
- **Root and body.** The containment of the html or body element disables the propagation of the properties of the body
  element ([§2](https://drafts.csswg.org/css-contain-2/#contain-property), `ScrollContainers.HasContainment`): its `overflow`
  is not applied to the viewport, its `writing-mode` and `direction` are not the principal writing mode
  (`BoxTreeBuilder.SetPrincipalWritingMode`), and its background is not the background of the canvas (`Painter`).
- **Effects.** Size containment: see [Intrinsic sizing](#intrinsic-sizing) and `FormattingContext.Layout` (the `auto` block size
  is computed before the contents); the box keeps the baselines of its contents, as in the browsers (`contain: size` on an
  inline-block does not change its alignment), except a size query container (`container-type: size`), which gives no
  baseline (`LayoutContext.LayoutBox`, as in the browsers too: WPT `container-queries/nested-query-containers.html`).
  Layout containment: an independent formatting context
  (`BlockFormattingContext.EstablishesNewFormattingContext`), a containing block for the absolutely and fixed positioned
  descendants and a stacking context (`LayoutStyle.EstablishesFixedPositionContainingBlock`; not for the inline boxes, see
  `PositionedLayout`), no baseline (`LayoutContext.LayoutBox` clears `First/LastBaseline` of the fragment: an inline-block is
  aligned by its margin box, a parent does not take the baselines of the box; a `button` element without baseline, with
  layout containment or without in-flow line box, is aligned by the block-end edge of its content box, as in the browsers,
  `InlineLayoutAlgorithm.AlignAtomic`), and its overflow is ink overflow when it is
  not a scroll container (it contributes its border box to the scrollable overflow of its ancestors, `BoxFragment.ComputeOverflow`).
  Paint containment: the box clips its contents at its overflow clip edge like `overflow: clip` (`BoxFragment`
  `ClipsOverflowX/Y`, used by the hit testing and the scrollable overflow).
  Style containment: the counters (`CssCounters`: `counter-increment`/`counter-set` in the sub-tree of the element create
  new counters instead of changing the counters of the outside, each counter records its containment scope; the counters
  are inherited from the preceding sibling, or from the parent, as in https://drafts.csswg.org/css-lists-3/#inherit-counters,
  so that the siblings see the nested counters; the items of a reversed list that are in the sub-tree are not counted in
  its initial value, and their implicit increment is 1: their new counter is not reversed) and the quotes
  (`BoxTreeBuilder` restores the depth of nesting after the contents of the element). In a fragmentation context, a box
  with size containment in its block axis is monolithic, and the forced breaks inside a box with layout containment do not
  propagate to its parent (see [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation)).
- **Skipped contents** ([skips its contents](https://drafts.csswg.org/css-contain-2/#skips-its-contents)):
  `ContainerBox.SkipsContents` (`content-visibility: hidden`, `auto` when not relevant to the user; the `::details-content`
  box of a closed `details` has `content-visibility: hidden`): the boxes of the contents are built, but not laid out (`FormattingContext.Layout`), painted nor
  hit tested (`PaintOrder` does not descend into them), and the box has size containment. A geometry API that needs a box of
  skipped contents (`LayoutEngine.GetFragments`/`GetTextFragments`, or the scrolling area of the box itself:
  `GetScrollState`/`EnsureContentsLaidOut`) lays them out (`ContainerBox.LaysOutSkippedContents`, kept by element in the
  engine until the next rendering update, as the box tree may be built again): "the user-agent cannot avoid rendering work"
  (https://drafts.csswg.org/css-contain-2/#cv-examples). The size of the box does not change. A scroll container whose contents
  are not laid out keeps its scroll position (it is not clamped). The skipped contents do not contribute to `innerText`
  (`ContentRelevancy.IsInSkippedContents`, `SkipsContents`), the CSS animations and transitions of the skipped contents are not
  updated (no events, not finished; the computed styles still sample them, `DocumentAnimations`), and `scrollIntoView()` does
  not scroll to the skipped contents of `content-visibility: hidden` (`BoxGeneration.InSkippedContents`, also used by the
  focus). `hidden=until-found` is `content-visibility: hidden` (UA style sheet). An element of the top layer in the skipped
  contents of an ancestor is skipped too: its box stays in the contents of the ancestor instead of being a child of the
  viewport (`BoxTreeBuilder.BuildElement`), so it is not painted, and it has no `::backdrop` box (an ancestor with
  `content-visibility: auto` is relevant to the user when it contains an element of the top layer).
- **content-visibility: auto** (`ContentRelevancy`, `LayoutEngine.ContentVisibility.cs`): the principal boxes are listed by
  `BoxTree.ContentVisibilityAutoBoxes` (`ContainerBox.IsContentVisibilityAuto`); before each layout pass, a box skips its
  contents when its element is not [relevant to the user](https://drafts.csswg.org/css-contain-2/#relevant-to-the-user): not
  close to the viewport (the proximity determined by the last rendering update, `ContentVisibilityAutoState` in the
  `ElementRareData`), and neither it nor its contents are focused, selected (the range of the selection intersects the
  element; a selection change calls `LayoutEngine.OnContentRelevancyChanged`) or in the top layer. The focus, the selection
  and the top layer take effect immediately, as in the browsers. The rendering steps (`LayoutEngine.UpdateRendering`, called
  by `DomEventLoop.UpdateTheRendering` instead of `UpdateLayout`) run "update content relevancy for a document": with
  `updateNonInitial` for the first iteration, the "currently relevant to the user" flags are updated from the previous
  proximity, then the proximity is determined (the border box, extended to the overflow clip edge of the box when
  `overflow-clip-margin` expands it, intersects the viewport expanded by 50%, after the clipping by
  the overflow clips and the clip paths of its ancestors, the scrollports expanded by 50% too: the intersection of an
  intersection observer whose root is the document, `IntersectionGeometry.IntersectsViewport`) and the initial determinations update the flag
  immediately; the layout is computed again while an initial determination makes an element relevant. A change of the flag
  posts a task that fires `contentvisibilityautostatechange` (`ContentVisibilityAutoStateChangeEvent`; the first determination
  always fires one, as in the browsers; `oncontentvisibilityautostatechange` is added to GlobalEventHandlers by `tools/Meziantou.Framework.WebPlatform.SpecGen`).
  A non-initial proximity change requests another rendering opportunity (its event is fired by the next one). An element that
  loses `content-visibility: auto` or its box forgets its state. `scrollIntoView()` activates the `auto` ancestors that skip the
  contents containing the target (their proximity becomes close until the next determination), as in the browsers. A
  document whose style sheets or style attributes declare `content-visibility` is laid out by the rendering steps even when
  nothing reads its geometry (`StyleEngine.MayHaveContentVisibility`). The document of an SVG image has no rendering
  opportunity: these steps run before it is painted (`SvgImage.GetDisplayList`), so the contents of its elements with
  `content-visibility: auto` that are close to its viewport are painted.
- **Last remembered size** (`LastRememberedSizes`, [CSS Sizing 4](https://drafts.csswg.org/css-sizing-4/#last-remembered)):
  `BoxTree.ContainIntrinsicSizeAutoBoxes` lists the principal boxes whose `contain-intrinsic-size` may have `auto`
  (`content-visibility: auto` implies it: its computed value gains `auto`, csswg-drafts#8407). The rendering steps record the
  logical size of the content box (the block sizes of the fragments added) of the boxes that do not skip their contents (even
  with size containment, as in the browsers; not the non-atomic inline boxes) in the `ElementRareData`
  (`LastRememberedInlineSize`/`BlockSize`), and forget the sizes of the disconnected elements; a box tree whose box of an
  element has no `auto` in an axis forgets that axis immediately (an element without box keeps its size). The explicit
  intrinsic inner size (`LastRememberedSizes.GetContainIntrinsicSize(box, horizontal)`) is the remembered size when the
  element skips its contents and the axis has `auto`. The sizes are recorded at each iteration of the loop of the
  rendering update that gathers the resize observations (`LayoutEngine.UpdateRendering`), after the layout.

### Select elements with base appearance

A `select` element whose computed `appearance` is `base-select` (or `base`) is rendered with [base
appearance](https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2): it is not a replaced box painted
by the form control painter, its contents are boxes. The specification describes shadow trees of the user agent; the
library has none, and `BoxTreeBuilder.Select.cs` generates the boxes of their elements (the rules of the user agent that
style them are in [computed-style.md](computed-style.md#select-elements)):

- **Drop-down box** (no `multiple` attribute, display size 1): an `inline-flex` box that holds its `::before`, its button,
  its `::after` and its `::picker-icon` (the disclosure triangle of the user agent style sheet). The button is the first
  child element when it is a `button` element (`display: contents` in the user agent style sheet: its contents are the
  flex items; it is inert, so the pointer targets the select element), else the *select fallback button text*, an
  anonymous block whose text is the label of the selected option. `field-sizing: content` is forced: the box is sized by
  the shown text, with the minimum sizes of the user agent style sheet (`calc-size(auto, max(size, 24px))`: the automatic
  minimum size of a `calc-size()` minimum is zero).
- **Select popover**: the box of the `::picker(select)` pseudo-element holds the boxes of the other children of the select
  element (they inherit from it). It is only rendered when the picker has base appearance too (`::picker(select) {
  appearance: base-select }`): `display: none` while the picker is closed (unless the author gives it another display: it
  is then a child of the box of the select element, after the button), and a box of the top layer while it is open (a
  child of the viewport box, in the order of the top layer: the select element stands for its picker in the top layer of
  the document, `Document.IsTopLayerPicker`), after its `::picker(select)::backdrop`. It is a popover of the user agent
  style sheet (`position: fixed`, `overflow: auto`, `max-block-size: stretch`) anchored to the select element, its implicit
  anchor element: `position-area: self-block-end span-self-inline-end` with the `position-try-fallbacks` of the
  specification (above the select element, or aligned to its other side, when it does not fit), and
  `min-inline-size: anchor-size(self-inline)`. Its scroll state is the one of the select element
  (`ContainerBox.ScrollingElement`), whose own box does not scroll.
- **Options and groups** whose select element renders its contents with base appearance: an `option` is a flex container
  that holds its `::checkmark` (before its `::before`; hidden unless the option is `:checked`), then its children, or its
  label when it has a non-empty `label` attribute; an `optgroup` holds its `legend` (its first child element when it is a
  `legend`), else the *optgroup label element* (an anonymous block with the text of its `label` attribute), then its
  other children. Any other content of the select element is rendered as usual.
- **List box** (`multiple`, or a display size greater than 1): an `inline-block` scroll container of the boxes of its
  children (`overflow: auto`, `block-size: calc(max(24px, 1lh) * size)`, `box-sizing: border-box` as the specification
  says: the borders are taken from the rows, which Chrome does not do).
- Not done: a drop-down box with native appearance stays a painted widget (a list box with native appearance is a
  replaced box whose rows are boxes, see [Replaced elements](#replaced-elements-csslayoutreplaced)); a select element with a `multiple` attribute and a display
  size of 1 is a list box (the specification lets the platform choose: Chrome renders a multi-select drop-down box); the
  highlight pseudo-elements of the picker (`::picker(select)::selection`...); the exit transitions of the picker (it is
  removed from the top layer at once); the box of the picker of a select element in a size query container is not built
  again when the layout rebuilds the contents of the container;
  `selectedcontent` of a multiple selection, the `selectedcontentelement` attribute, the filterable select and the
  customizable combobox (`<input list>`).

### Block formatting context (`Css/Layout/Block/`)

`BlockFormattingContext` lays out the block containers whose children are block-level
([CSS 2 §9.4.1](https://drafts.csswg.org/css2/#block-formatting)): the in-flow children are stacked in the block axis from
the block-start edge of the content box, and their block-axis margins collapse
([§8.3.1](https://drafts.csswg.org/css2/#collapsing-margins)). It is also the fallback of the formatting contexts that are
not implemented yet (the grid lanes): their children are stacked without collapsing their margins.

- **New formatting contexts.** `BlockFormattingContext.EstablishesNewFormattingContext(box, containingBlock)` tells whether
  a block-level box establishes an independent formatting context: the replaced boxes, the table wrappers, the flex, grid,
  table, ruby and math containers, `display: flow-root`, the boxes that are not block boxes (inline-blocks, table cells and
  captions), the floats and the absolutely positioned boxes, the flex and grid items, the root element, the line-clamp containers (see [Line clamping](#line-clamping-csslayoutblocklineclampcs)), layout or paint
  containment (`contain`, `content-visibility` other than `visible`), the size container types, the scroll containers
  (`overflow` other than `visible`/`clip`, except the `overflow` of the root and body elements propagated to the viewport:
  `ScrollContainers.IsScrollContainer(box)`), the multi-column containers, a `block-step-size` other than `none` (see
  Rhythmic sizing below), a writing mode different from the containing block's,
  `align-content` other than `normal` ([CSS Align](https://drafts.csswg.org/css-align-3/#distribution-block), see
  [Box alignment](#box-alignment-csslayoutboxalignmentcs)), the `fieldset` and `button` elements. The anonymous block boxes never do.
- **Participation.** A box that does not establish a new formatting context participates in the block formatting context
  of its parent: the parent passes the state of the context in `ConstraintSpace.BlockFormattingContext`, and
  `FormattingContext.Layout` then collapses the margins of the box with the ones of its children: the block-start margin
  with the ones of the first children when the box has no block-start border or padding
  (`ContentArea.StartMarginCollapsesWithChildren`), the block-end margin with the ones of the last children when the box
  has no block-end border or padding and an `auto` block size. As in the browsers, when the minimum block size makes the box
  taller than its contents, the margins of the last children neither collapse with its margin nor make it taller: they are
  discarded (`BlockFormattingContextState.DiscardMargins`); a maximum block size does not change the collapsing (Blink and
  Gecko discard the margins then too, WebKit does not, CSS 2 does not either). The result is in
  `BoxFragment.CollapsedMargins` (`CollapsedBlockMargins`: the `Start` and `End` sets of adjoining margins
  (`MarginStrut`: the largest positive and the most negative margins), `CollapsedThrough`, `Clearance`); the other boxes
  have `CollapsedBlockMargins.None` (their parent uses their `Margin`). The content layouts return the margins of their
  children that collapse with the margins of the box in `ContentLayoutResult.StartMargin`/`EndMargin` (the `BlockSize`
  does not include them) and whether nothing separates them (`CollapsedThrough`: no line box and only children that
  collapse through).
- **Placement.** `BlockFormattingContext.MarginCollapsing` places the in-flow children (as Servo's `PlacementState`): the
  adjoining margins of the siblings collapse; the margins of the first children that collapse with the parent's are
  left outside of its content box (the children are at its block-start edge); a box whose margins collapse through (no
  border, padding or in-flow content and a used block size of 0, as proposed by
  [csswg-drafts#8919](https://github.com/w3c/csswg-drafts/issues/8919)) is placed where its top border edge would be with a
  bottom border, and its margins collapse with the next ones; with clearance, the margins before the box do not collapse
  with its margins, and the margins of a box with clearance that collapses through do not collapse with the parent's
  block-end margin (they are collapsed into the state of the context too). The contents of a box are never shorter than 0
  (negative margins): the state of the context then moves to the end of the content box. The margins of the root element do not collapse (it establishes a new formatting context). The
  floats are placed in the block formatting context at or below their static position (after the margins collapsed so far)
  without affecting the flow, and the boxes that establish a new formatting context are placed next to them (see
  [Floats](#floats-csslayoutblock)); the static position of an absolutely positioned box is recorded there
  (`FragmentBuilder.AddOutOfFlowCandidate`) and its containing block lays it out (the flex and grid containers record the static
  positions of their children themselves).
- **Margin trimming.** [`margin-trim`](https://drafts.csswg.org/css-box-4/#margin-trim) (`none | block | [ block-start ||
  block-end ]`, `LayoutStyle.MarginTrim`; `block-start block-end` serializes as `block`) applies to the block containers
  and to the multi-column containers (`BlockFormattingContext.GetMarginTrim`): the trimming of the margins of the flex and
  grid items and the `inline` values were removed from the property
  ([csswg-drafts#13731](https://github.com/w3c/csswg-drafts/issues/13731)), so the WPT tests of the flex and grid containers
  fail, as in Chrome. The margins of the in-flow block-level children that are adjacent to an edge that the container trims
  are "truncated to zero", with the margins that collapse with them, whether or not they would collapse with the margins
  of the container (which are kept):
  - *block-start*: the margins of the children are adjacent to the block-start edge until a child does not collapse
    through (`MarginCollapsing.IsBeforeFirstContent`): the block-start margin of that child, and both margins of the boxes
    that collapse through before it (their block-end margins are removed from the state of the context after their layout:
    `BlockFormattingContextState.RestoreMargins`);
  - *block-end*: the block-end margin of the last child that does not collapse through and both margins of the boxes that
    collapse through after it (which are then at the block-end edge of the content); when all the children collapse through,
    all their margins. These margins are only known once the children are laid out: when some of them are not zero
    (`MarginCollapsing.HasEndMargins`), `BlockFormattingContext.LayoutContents` restores the state of the context and lays
    the children out again with the index of that last child (the margins after the clamp point of a line-clamp container
    are not trimmed);
  - the child gets `ConstraintSpace.TrimsBlockStartMargin`/`TrimsBlockEndMargin`: `FormattingContext.Layout` sets these
    margins to zero (so does `BoxFragment.TrimMargins` for the layouts that create their fragment themselves, e.g. the table
    wrapper boxes) and passes the trimming to its own children when the margin collapses with theirs
    (`ContentArea.TrimsBlockStartMargins`: no block-start border or padding; `TrimsBlockEndMargins`: no block-end border
    or padding and an automatic block size), so a border, a padding, a block size or a new formatting context protects the
    nested margins. The margins looked ahead for the position of a box (`FindMarginsCollapsingWithChildren`) skip the
    trimmed ones;
  - in a multi-column container, the trimming applies to the content of the columns (the strip) and to the column spanners:
    the block-start margin of a spanner that no content precedes and the block-end margin of a spanner that no content
    follows (the margin of a spanner is part of its extent: it is trimmed by the second layout too);
  - the margins of the floats, of the absolutely positioned boxes and of the inline-level boxes are not trimmed;
  - like the margins that collapse, the trimmed margins are discarded by the layout (they are zero in `BoxFragment.Margin`)
    but they stay the resolved values of the margin properties (`BoxFragment.UntrimmedMargin`, WPT
    `css/css-box/margin-trim/margin-trim-computed-value.html`).
- **State of the context.** `BlockFormattingContextState`, created by each block formatting
  context root, is shared by the boxes that participate in the context and updated in layout order (as Servo's
  `SequentialLayoutState`): `BlockOffset` (the current position relative to the content box of the root, after the
  collapsed margins), `CurrentMargin` (the adjoining margins at that position, not collapsed yet: `AdjoinMargin`,
  `CollapseMargins` when a border, a padding, a line box, clearance or a block formatting context root separates them,
  `AdvanceBlockPosition`), `Exclusions` (`ExclusionSpace`: the floats placed in the context, `GetClearanceOffset(sides)`) and
  `ComputeClearance(sides, blockStartMargin)` (the clearance of [§9.5.2](https://drafts.csswg.org/css2/#clearance), from the
  hypothetical position). A participating box updates the state in `FormattingContext.Layout` (clearance, block-start
  margin, border and padding, then the size of the box and its block-end margin), the block formatting context root
  updates it for its independent children (`LayoutIndependentBox`), the inline layout for its lines (and the space a line
  is moved down by). At the end of the contents of a root, the position plus the pending margins equals the block size of
  its contents (asserted in debug builds). `ContentArea.BlockFormattingContextOffset` is the position of the content box of
  a participating box in the coordinates of the context (the line-left edge from `ConstraintSpace.BlockFormattingContextLineOffset`
  plus its line-left margin, border and padding; the block-start edge after the margins not collapsed yet), where the floats
  of its children are placed.
- **Margins of the first children (look-ahead).** When the block-start margin of a participating box collapses with the
  margins of its first children, `BlockFormattingContext.FindMarginsCollapsingWithChildren` adds them to its block-start
  margin before its children are laid out (as Servo): they are part of its position (its floats are placed below them) and
  of its hypothetical position for the clearance. The search follows the first in-flow children until a border, a padding,
  a line box that is not empty, a box that establishes a new formatting context or a separated box (see below); the children
  that collapse through are included with both margins (a box with an aspect ratio or a `stretch` block size does not collapse
  through). The boxes with `clear` are included: as in the browsers, their hypothetical position is the one where the floats
  before them are placed with the collapsed margins. Adding the same margin again to `MarginStrut` does not change it, so the
  children add their own margins as usual; a box whose ancestor already looked ahead through it
  (`ConstraintSpace.IsStartMarginCollapsedWithAncestor`) does not look ahead again, unless it has `clear`.
- **Separated margins and relayout.** A box whose block-start margin was adjoining to the one of its parent (and looked ahead
  with it) and that gets clearance (`clear`, a box that avoids the floats moved down below them, a column spanner) separates its
  margins from the ones of its ancestors ("clearance inhibits margin collapsing"): `BlockFormattingContextState.Separate`
  records it. The box that looked ahead (the outermost one whose margin collapsed with them) compares the margins that collapsed
  with its own (`BoxFragment.CollapsedMargins`) with the ones it looked ahead; when they differ (a separated child, or a child
  predicted to collapse through that did not), it restores the state of the context (`Save`/`Restore`, the floats placed since
  then are removed) and lays out its contents again with the margins that collapsed (at most twice, as Blink's relayout with
  the "non-adjoining" margins). In that layout, the search stops before a separated box, and the box is placed after the margins
  before it (without its own margin) and below the floats it clears, in the first layout opportunity where it fits
  (`GetSeparatedPosition`), even when its floats moved up with its parent (WPT `css/CSS2/floats/new-fc-separates-from-float-*`,
  `css/css-sizing/stretch/block-height-005.html`). The state of the context and the placement of the children then stay in
  sync; the WPT reftests run with the assertion enabled in Debug builds.
- **Sizes.** The widths of [§10.3.3](https://drafts.csswg.org/css2/#blockwidth) (auto margins, over-constrained boxes) and
  the heights of [§10.6.3](https://drafts.csswg.org/css2/#normal-block) are computed by `FormattingContext.Layout` (see
  above), with `box-sizing`, the min/max sizes, the percentages (an indefinite basis makes them `auto`; the percentage block
  sizes of the children of an anonymous block box refer to its containing block, `ContentArea.AnonymousPercentageBlockSize`:
  "anonymous block boxes are ignored when resolving percentage values that would refer to it"), and `zoom` (the
  typed values are zoomed). The `auto` margins of the block axis are 0.
- **Rhythmic sizing.** [`block-step-size`](https://drafts.csswg.org/css-rhythm/#block-step-size) (`LayoutStyle.BlockStep`)
  applies to the block-level boxes laid out by the block layout (the in-flow children and the floats of a block container,
  not the absolutely positioned boxes, the inline-level boxes nor the flex and grid items); a value other than `none` makes
  the box establish an independent formatting context. Once its block size is known, `FormattingContext.Layout` rounds its
  outer block size (its border box and its own margins, before they collapse with the adjoining ones) to a multiple of the
  step unit (`block-step-round`: `up`, `down`, `nearest`; as the WPT tests expect, the multiple is at least one step: an
  empty box is one step tall) and inserts the difference (`block-step-insert`) in its margins, which then collapse as usual
  and are the resolved values of `getComputedStyle()`, in its paddings, or in its content box; `block-step-align` splits it
  between both sides (`auto` is the `align-self` of the box when it is `start`, `end` or `center` and the space goes to the
  margins, `center` otherwise; the contents of a box that grows inside its border are moved by the space inserted before
  them). A space removed by `down`/`nearest` that is inserted inside the border shrinks the content box (down to zero). Not
  done: the rounding of each fragment of a fragmented box (the box is rounded once, before the fragmentation, and its
  fragments are sliced from it), and the boxes whose writing mode is orthogonal to the one of their containing block (not
  rounded).
- **Quirks mode.** [The percentage height calculation quirk](https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk):
  `ConstraintSpace.QuirksPercentageBlockSize` is the basis of a percentage `height` (static, relative or sticky; not a
  table row, group, cell or caption) when the containing block's height is indefinite: the auto height block containers
  pass the basis of their parent through, the other boxes stop the search (an absolutely positioned box, a flex or grid
  container, a definite or non-`auto` height; the main size of an item of a column flex container whose main size is
  indefinite is not such a height, and a table cell gives no basis to the descendants of its children); calc() expressions
  do not use it. [The html element fills the viewport
  quirk](https://quirks.spec.whatwg.org/#the-html-element-fills-the-viewport-quirk) and [the body element fills the html
  element quirk](https://quirks.spec.whatwg.org/#the-body-element-fills-the-html-element-quirk) give a minimum border-box
  height to the root html element (the initial containing block minus its margins) and to the body element (the content
  box of the html element minus its margins, the block-start one being the margin collapsed with the margins of its first
  children, as in the browsers; not floating, absolutely positioned or inline-level), and that height is a
  definite basis for the percentages and for the `stretch` block size of their children (through the anonymous block boxes,
  `ConstraintSpace.StretchUsesQuirksPercentageBlockSize`), as in the browsers. In a vertical writing mode, they apply to the
  width (the size in the block flow direction). The [margin
  collapsing quirks](https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks) are user agent rules of
  the cascade (`StyleEngine.GetMarginCollapsingQuirkDeclarations`: `margin-block-start`/`end: 0` for the elements with
  default margins at the start or end of a `body`, `td` or `th`, depending on their substantial siblings), so
  `getComputedStyle` returns them, as in Firefox.
- **Fieldsets** ([HTML](https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements),
  `FieldsetFormattingContext`, `BoxTreeBuilder.WrapFieldsetContents`). The principal box of a `fieldset` is a block
  container (`flow-root`, or `inline-block`) with two child boxes: its **rendered legend**, if any (the first child box of
  a `legend` element that is in flow and block-level: not a floating nor an absolutely positioned legend), and its
  **anonymous fieldset content box** (`ContainerBox.IsFieldsetContent`), which holds its other contents, `::before` and
  `::after` included.
  - Styles (`StyleEngine.CreateFieldsetStyles`): the content box inherits from the fieldset the properties of the list of
    the specification (the paddings, `overflow`, the columns, the flex and grid container properties, the content and item
    alignment, `border-radius`, `text-overflow`, `unicode-bidi`) and, as in Blink, the properties of the scroll containers
    and of the gap decorations that were added to CSS since; its `display` is `grid`, `flex` or `flow-root` after the one of
    the fieldset, its `block-size` is `100%`, and it is a containing block of the absolutely (or fixed) positioned
    descendants when the fieldset is one (`position: relative`, or layout containment for a fieldset with a transform, a
    filter...). The principal box has a style of its own where these properties do not apply (no `overflow`, no columns,
    `align-content: normal`); it keeps the paddings, which size it as any box, but they are the ones of the content box:
    the content box is laid out in the padding box of the fieldset.
  - The legend is shrink-wrapped (`BlockFormattingContext.UsesShrinkToFit`) in the inline size of the content box of the
    fieldset (between its inline paddings), and placed in the inline axis by its margins and its `justify-self` (which
    the `align` attribute maps to). In the block axis, as in the browsers (WPT `legend-block-margins-2.html`): a margin box
    taller than the block-start border starts at the block-start edge of the border box, and what spills out past the
    border takes the start of the content box of the fieldset (it is subtracted from a specified block size; the content
    box gets what remains, and fills the fieldset when its block size is given by `max-block-size` or its aspect ratio:
    its `block-size: 100%` of a block size that depends on the contents does not fill a `min-block-size`, as in Blink, WPT
    `css/css-flexbox/fieldset-as-container-justify-center.tentative.html`); else the border box of a legend smaller than the border is centered in it, and a taller one (with
    negative margins) is not moved before the border.
  - The border and the background are painted in the border box less the space before the block-start border, which is
    centered on the border box of the legend in the space allocated for it (`FieldsetState.BorderOffset`), and the border
    is not painted behind the legend (`FieldsetState.Gap`: its border box in the inline axis, its margin box and the
    border in the block axis; a clip in `Painter.PaintBorder`). The legend and the content box are painted atomically, in
    this order, as the items of a flex container are (`PaintOrder.PaintsChildrenAtomically`: the content box covers the
    legend and its floats where they overlap, as in the browsers).
  - The baselines of the fieldset are the ones of its content box (the first one for an inline-level flex or grid
    fieldset); its intrinsic inline sizes are the greater of the ones of the legend and of the content box. The
    percentages of the paddings that the content box inherits resolve as the ones of the fieldset.
  - The scroll container of a fieldset is its content box (`ContainerBox.ScrollingElement`: its scroll state is the one
    of the fieldset element, for `scrollTop`, `scrollHeight`, the scroll markers...; the legend does not scroll and is
    not clipped). A flex or grid item fieldset that scrolls has no automatic minimum size
    (`BlockFormattingContext.GetContentsStyle`).
  - A fieldset is fragmented as any box: its legend is monolithic and there is no break between its block-start edge and
    the legend.
  - Not done: a fieldset is never a subgrid (its grid container is the content box, a child of a block container: as in
    Chrome), the `::column::scroll-marker` of a multi-column fieldset do not scroll to their columns, a child of a
    fieldset scroll container does not find it as the source of its view timeline, and the block-end border of a fieldset
    that does not fit after its content box is split by the break (Blink breaks before it).

- **Framesets** ([HTML](https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets),
  `FramesetFormattingContext`). A `frameset` is "a box with the height and width of the viewport": as in the browsers, its
  box is a block box whatever its `display` (other than `none` and `contents`), its border box is the initial containing
  block (or the rectangle of a nested frameset) whatever its sizing properties, it is never a scroll container, and only its
  `frame` and `frameset` children generate boxes (block boxes; no pseudo-element, no text). The `cols` and `rows` attributes
  are parsed with the rules for parsing a list of dimensions (`Microsyntaxes.ParseListOfDimensions`) and converted to whole
  CSS pixels by the algorithm of the specification (absolute sizes first, then percentages, then the relative `*` sizes; the
  remainder of the rounding goes to the last relative entry, then equally to the percentage entries, then to the absolute
  ones, then to the last entry). The children are assigned to the rectangles from top to bottom, then from left to right;
  the children left once the grid is full are laid out with a zero size, as in Blink. A `frame` displays its content
  navigable in its rectangle (its viewport is the content box, as for an `iframe`).
  - Borders: the specification does not size them. As in the browsers, a frameset that has a border (the `frameborder`
    algorithm of the specification, inherited from the parent frameset) has borders between its rows and between its
    columns (none around the grid), whose thickness is its `border` attribute, else the one of its parent frameset, else
    6px; they take their space from the surface before it is split, and they are filled with the frame border color of
    the frameset (its `bordercolor` attribute, else the one of its parent frameset, else `gray`: the browsers paint a
    lighter gray with bevels). `framespacing` is ignored (as in the browsers) and the borders cannot be moved by the user
    (`noresize` has no effect).

### Floats (`Css/Layout/Block/`)

The floats of [CSS 2.1 §9.5](https://drafts.csswg.org/css2/#floats) are placed in the exclusion space of their block
formatting context (`ExclusionSpace`, owned by `BlockFormattingContextState`).

- **Coordinates.** The exclusion space is line-relative ([line-left](https://drafts.csswg.org/css-writing-modes-4/#line-left),
  `WritingDirection.LineLeft`), in the writing mode of the root (all the boxes that participate in a block formatting context
  have its writing mode, their `direction` may differ): the inline coordinate is the distance from the line-left edge of the
  content box of the root, the block coordinate the distance from its block-start edge. `float: left`/`right` are line-left/
  line-right, `inline-start`/`inline-end` and the logical values of `clear` are resolved with the direction of the containing
  block (`ExclusionSpace.GetFloatSide`/`GetClearSides`).
- **Placement** (`ExclusionSpace.PlaceFloat`, [rules 1–9](https://drafts.csswg.org/css2/#float-position)): a float is laid out
  first (its shrink-to-fit width, in its containing block), then its margin box is placed at or below its ceiling (its static
  position: the current position of the block formatting context after the pending margins, or the top of the current line
  box; `clear` on a float moves the ceiling below the floats it clears; never above the top of a previous float, rule 5): at
  each candidate position, the space between the floats that intrude into the band of its block size (`GetOpportunity`) must
  be wide enough, else it is moved down to the block-end edge of the first of them that ends; where no float intrudes, it is
  placed against the edge of the containing block even when it is wider. `BlockFormattingContext.PlaceFloat` adds the
  exclusion and the fragment (a child of the fragment of its containing block, with the relative offsets of the inline boxes
  it is in). The floats do not change the position of the flow; a float with no block size does not shorten anything.
- **Clearance** ([§9.5.2](https://drafts.csswg.org/css2/#clearance)): `BlockFormattingContextState.ComputeClearance` from the
  hypothetical position of the box (its block-start margin collapsed with the margins before it and with the ones of its first
  children, see the look-ahead in [Block formatting context](#block-formatting-context-csslayoutblock)).
- **Boxes that avoid the floats** ([§9.5](https://drafts.csswg.org/css2/#bfc-next-to-float)): a block formatting context
  root, a replaced element or a table in a block formatting context with floats is placed by
  `BlockFormattingContext.LayoutIndependentBox` in the first layout opportunity at or below its position where it fits, as in
  Blink: it is laid out in the space between the floats that intrude into the band of its block size (its `auto` inline size
  shrinks, its margins may overlap the floats: a margin only narrows the space when it is larger than the distance of the
  float from the edge of the containing block); when it is taller in that space, the larger band is checked again. Its border
  box starts at the inline-start edge of the space that its margin leaves: when it then overlaps a float or overflows the
  containing block beside a float (a margin larger than the space beside a float on the other side, a negative margin that
  makes an `auto` inline size wider than the space: the available inline size is not negative, a border box wider than the
  space), it is moved down below the first float that ends. The floats that do not narrow the containing block (their margin
  box has no inline size, `ExclusionSpace.GetFloatEdges`, or is outside of it) do not change its layout: its negative margins
  make it wider than the containing block. A box moved down has clearance.
- **Line boxes** ([§9.5](https://drafts.csswg.org/css2/#floats), `InlineLayoutAlgorithm.LayoutLine`): a line is broken in the
  space at its position; once its height is known, the floats that intrude into the band of the line shorten it too (it is
  laid out again in the narrower space). "If a shortened line box is too small to contain any content, then the line box is
  shifted downward": a line whose first unbreakable content does not fit next to the floats is moved down below the first
  float that ends. The floats in the inline content are met by the line breaking (`LineEnd.StopsAtFloat`): a float that fits
  next to the content before it on the line is placed at the top of the line (the line is then broken again in the space that
  remains), else below the line (`_deferredFloats`), with the floats after it on the line (rule 5: a float is not higher than
  the floats before it). A float deferred below a line that is then broken before it is a float of the next line (rule 6:
  it is not higher than the line box of the content before it). A floating root element is shrunk to fit and placed at
  the line-left or line-right edge of the initial containing block (`LayoutEngine.LayoutViewport`). `ExclusionSpace.PlaceFloat` compares the sizes with a tolerance of 1/64px, the layout unit of the browsers (ten floats of 0.87em fit in 8.7em, six floats of 16.6666665% fit in their containing block),
  and places a float that is wider than its containing block against its edge when the floats of the other side that are
  outside of the containing block do not overlap it (rule 3) and no float of its side is before it (rule 7).
- **Containment** ([§10.6.7](https://drafts.csswg.org/css2/#root-height)): the `auto` block size of a block formatting context
  root includes the margin boxes of its floats.
- **Shrink-to-fit** ([§10.3.5](https://drafts.csswg.org/css2/#float-width)): the `auto` inline size of the floats, atomic
  inlines, absolutely positioned boxes, outside markers and buttons ([HTML](https://html.spec.whatwg.org/multipage/rendering.html#button-layout),
  not the flex or grid items) is "min(max(preferred minimum width, available width), preferred width)" from
  `BlockFormattingContext.ComputeIntrinsicInlineSizes` (`BlockFormattingContext.UsesShrinkToFit`): the lines of the inline
  contents, or the largest contribution of the block-level children (their fixed size, or their own intrinsic sizes, plus
  their margins, borders and paddings, clamped by the min/max sizes; the percentages are 0), the max-content contributions of
  the floats placed side by side being added (and the boxes that avoid the floats next to them) (see
  [Intrinsic sizing](#intrinsic-sizing): the contributions of the children, the cache).
- **`shape-outside`** ([CSS Shapes](https://drafts.csswg.org/css-shapes/#shape-outside-property), `FloatShape`,
  `FloatShapes.Create`): the float area of a float, used for the line boxes only (the floats and the boxes that avoid the floats
  use the margin box), clipped to the margin box: `inset()` (and `rect()`/`xywh()`), `circle()`, `ellipse()` and the boxes
  (`margin-box` with the radii of the margins, `border-box`, `padding-box`, `content-box` with their `border-radius`) as a
  rectangle with elliptical corners (`RoundedRectangleShape`), `polygon()`, `path()` and `shape()` (`PolygonShape`, closed
  polygons: the curves are flattened by `BasicShapePaths.Flatten`), with `shape-margin` (an ellipse or a rounded corner grows by
  the margin, as in Blink; the margin of a polygon is exact). The basic shapes are parsed and resolved in their reference box by
  `BasicShapePaths.Resolve`, as for `clip-path` (`circle()` and `ellipse()` also take the `closest-corner` and
  `farthest-corner` radii of [CSS Shapes 2](https://drafts.csswg.org/css-shapes-2/#funcdef-basic-shape-circle)). A
  [shape from an image](https://drafts.csswg.org/css-shapes/#shapes-from-image) (`url()`, a gradient or another image function;
  `ImageShape`) needs the rendering backend of the document: the image is rasterized in a bitmap of the size of the content
  box (`PaintedImage.Draw` in a `RenderingSurface`; a raster image is sampled without interpolation, as in Blink; at most 2048px
  per side), and the float area is the pixels whose alpha is greater than `shape-image-threshold`, one rectangle per row of
  pixels across the block axis, expanded by `shape-margin`. The mask is cached per box (`FloatShapes.ImageMasks`) for a style
  and a size. Without backend, or while the image is not available (it is fetched with the `cors` mode by `CssImageFetching`,
  which delays the load event), the float area is the margin box. A line is shortened by the extent of the shape in its band:
  the parts of a shape that only touch the block-start or the block-end of the band are not in it, and a shape without area
  (`inset(50%)`) has no effect (as in Blink and WebKit). A line that does not fit next to a float area is moved down by 1px
  steps (`ExclusionSpace.ShapeStep`, as in Blink: the space changes along the shape), and a line that was broken in the band
  of a taller line is laid out again in its own band, once. The float area of a box value of a box whose corners are shaped
  by `corner-shape` is the polygon of its flattened contour (`CornerContour.GetShape` for the reference box; the corners
  stay elliptical when the corner areas of the contour overlap). Not done: the rounding of `polygon()`.
- In the columns of a multi-column container, a float that does not fit in what remains of a column is pushed to the next one
  (see [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation)):
  `LayoutContext.FloatPushes` gives the floats a position they are placed at or below, and the exclusions of these floats
  (`Exclusion.VisibleFrom`) are ignored by the line boxes and the boxes that start before that position.
- Not done: the floats of the flex/grid/table layouts (they do not float there), a float
  whose margins are not yet resolved because its containing block collapses through (it is placed at the position before the
  following margins), a float placed on a line (not below it) that is then broken before it (it stays at the top of that line).

### Inline formatting context (`Css/Layout/Inline/`)

`InlineFormattingContext` lays out the block containers whose children are inline-level
([CSS 2 §9.4.2](https://drafts.csswg.org/css2/#inline-formatting), [CSS Inline 3](https://drafts.csswg.org/css-inline-3/)) with
`InlineLayoutAlgorithm`. An instance of the algorithm holds the reusable buffers (the text, the items, the break opportunities,
the bidi levels, a `LineBreaker` and a `BidiResolver`): the `LayoutEngine` keeps a pool of them (`LayoutContext.RentInlineLayout`;
an atomic inline laid out while its parent collects its items rents another instance), so a relayout allocates no buffer.

- **Items.** The box tree of the block container is flattened into `InlineItem`s over one text (the "text content" of the
  formatting context): `Text` (a part of a text run, one style and one bidi level), `ForcedBreak` (a U+000A: a `br` or a
  preserved segment break), `Control` (bidi control codes, the U+200B of a `wbr`; not displayed), `OpenTag`/`CloseTag` (the
  inline-start and inline-end margin, border and padding of an inline box: on its first and last parts when it is split
  around blocks; an edge with a margin, a border or a padding makes its line a line box with a height even when they add
  up to zero, e.g. a border and the opposite negative margin, `InlineItemFlags.HasEdges`), `Atomic` (U+FFFC; the atomic inline is laid out at that time, in the content box of the block container)
  and `OutOfFlow`. The text keeps the offset of each character in its DOM text node, so the text fragments map back to the
  DOM (`TextFragment.Start`/`End`).
- **White space, phase I** ([CSS Text §4.1.1](https://drafts.csswg.org/css-text-4/#white-space-phase-1)), while the text runs are
  collected: with `white-space-collapse: collapse | preserve-breaks | discard`, the spaces and tabs around a segment break are
  removed, a sequence of spaces collapses to one space, including across the boundaries of the inline boxes (the atomic
  inlines and the forced breaks reset it; the bidi controls do not), and the segment breaks are transformed
  ([§4.1.2](https://drafts.csswg.org/css-text-4/#line-break-transform): a space, or nothing between East Asian wide characters
  or next to U+200B, and next to East Asian punctuation in Chinese, Japanese or Yi, see [Line breaking](#line-breaking)), preserved (forced breaks with `preserve-breaks`) or discarded. With
  `preserve`/`break-spaces` the segment breaks are forced breaks, with `preserve-spaces` spaces; the carriage returns are
  spaces. The other control characters (U+000C FORM FEED included: it is not a document white space character of CSS Text)
  are ordinary characters, rendered with a visible glyph (see [Text shaping](#text-shaping)); U+000B, U+000C and U+0085
  (the classes BK and NL of UAX #14) are forced line breaks too. A collapsible space that is collapsed with the space before
  it "retains its soft wrap opportunity": its position is recorded when its box wraps (`_wrappingCollapsedSpaces`). The white
  space is processed "ignoring bidi formatting characters (characters with the Bidi_Control property) as if they were not
  there": a collapsible space collapses with the collapsible space before a bidi control character. A preserved tab is a text item of its own (`InlineItemFlags.Tab`, shaped as a space) whose advance
  (`InlineItem.InlineSize`) is computed by the line breaking from its position: the next tab stop
  ([§4.1.3](https://drafts.csswg.org/css-text-4/#white-space-phase-2), the subsequent one when it is less than `0.5ch` away),
  at the multiples of `tab-size` (a number of advances of the space of the block container, with its spacing) from the start
  content edge of the block container (the indentation and the floats included). `white-space-trim`
  ([§4.2](https://drafts.csswg.org/css-text-4/#white-space-trim)) is applied while the text is collected: `discard-inner`
  discards the white space (and the preserved segment breaks) at the start and the end of a box, `discard-before`/`discard-after`
  the collapsible spaces before/after it (no browser implements it).
- **Text transformation**: `text-transform` (`TextTransformer`, shared with `innerText`) is applied to the collapsed text,
  before the line breaking and the shaping, character by character with the characters around it in its text node (a
  character may be transformed with the ones that follow it: a surrogate pair, a Greek letter with its combining accents):
  - the full case mappings (`CaseMapping`, over `UnicodeProperties.GetUppercaseMapping` of Meziantou.Framework.Unicode:
    the unconditional mappings of SpecialCasing.txt, e.g. sharp s to `SS`, the `fi` ligature to `FI`, else the simple
    mappings; the titlecase mappings for `capitalize`, e.g. the `fi` ligature to `Fi`, the titlecase forms of the Latin
    digraphs, the Georgian letters unchanged);
  - the conditional mappings of SpecialCasing.txt: the final sigma (after a cased letter and not before one, the
    case-ignorable characters skipped), Lithuanian (`lt`: the dot above kept on a lowercase `i`, `j` or i with ogonek under
    an accent, removed after a soft-dotted letter in uppercase and titlecase), Turkish and Azeri (`tr`, `az`: the dotted
    capital I and the dotless small i, `I` followed by U+0307 is `i`);
  - the tailorings of CLDR: Greek uppercase (`el`, as ICU's `GreekUpper`: the accents are removed, the dialytika kept, or
    added to an iota or an upsilon after a vowel that loses its accent, the ypogegrammeni is an iota, the disjunctive eta
    keeps its tonos) and the Dutch `IJ` capitalized as a whole (`nl`);
  - the language is the content language of the element of the text (`TextTransformer.GetCaseLanguage`); an explicit script
    subtag that is not the script of the tailoring disables it (`tr-Cyrl`);
  - `capitalize` at the start of the words across the inline boxes (the letters, the digits and their combining marks, the
    apostrophes, the soft hyphen, the connector punctuation such as the low line `_` (UAX #29 ExtendNumLet, as in the
    browsers) and the middle dot or hyphenation point between two letters are inside a word); `innerText` of an inline box
    starts in a word when the text before the box ends with a word character (`RenderedText.IsAfterWordCharacter`);
  - `full-width` (the characters that have a `<wide>` form or are the `<narrow>` form of another one: ASCII, the space, the
    halfwidth katakana, Hangul letters and symbols) and `full-size-kana` (also outside of the BMP).

  The context of a character does not cross the boundaries of the text nodes (e.g. a final sigma followed by an inline box).
  The SVG text (`SvgTextLayout`) and the synthesized small capitals use the mappings without context.
- **Bidi** ([CSS Writing Modes §2.4](https://drafts.csswg.org/css-writing-modes-4/#text-direction)): the control codes of
  `unicode-bidi` (`BidiControls`) are inserted around the inline boxes and around the content of a block container with an
  override (both are closed/reopened around a forced break: each bidi paragraph is overridden). The bidi algorithm runs only when the text needs it: a right-to-left or
  `plaintext` paragraph, a control code, or a strong right-to-left or Arabic number character (the characters below U+0590
  are skipped quickly). The atomic inlines are neutral, or strong for a replaced element with `embed`/`bidi-override`. The
  text items are split at the level changes, so each one is shaped in one direction; the characters of a right-to-left item
  are mirrored (rule L4) before the shaping.
- **Shaping**: each text item is shaped with its style (`GetShapingOptions`: the zoomed font size, `letter-spacing`,
  `word-spacing`, the orientation of `writing-mode`/`text-orientation`, the kerning, the features, the language, the
  direction of its level). [Shaping across element boundaries](https://drafts.csswg.org/css-text-3/#boundary-shaping): the
  consecutive text items separated only by the edges of inline boxes without margin, border, padding nor baseline shift
  (the edges between the texts on the line: the other edge of a box whose direction is not the one of its text, e.g. the
  inline-end edge of a left-to-right box at the start of its text in a right-to-left run),
  with the same bidi level, fonts and shaping options, are shaped together (`ShapeGroup`: a ligature or a kerning crosses
  the boundary; each item gets its part with `ShapedText.Slice`; on the first formatted line, the items whose `::first-line`
  styles differ are grouped again with these styles, `ShapeFirstLineGroup`); otherwise the characters around the item are given as the
  context of the shaping (`ShapingContext`), so the Arabic letters join across inline boxes even when the style changes
  (as Chrome). When a line starts or ends inside an item at a position where `ShapedText.IsUnsafeToBreak` is true (a
  ligature, a kerned pair or a joining crosses the break, or the break is inside a cluster), the edges of the line are reshaped
  up to the nearest safe positions (`ShapeLineEdges`, `ShapedText.Replace`; a cluster split by the break is shaped again on
  both sides), as HarfBuzz recommends. The line breaking measures the candidate lines with the glyphs of the whole items,
  but the width of the content before a forced break or the end of the content is the one of its line: when the collapsible
  spaces removed at the end of the line follow a letter that is kerned with the space ("A " in Times), the end of the text
  is measured as it is shaped on the line, without the space (`GetRemovedSpaceShapingCorrection`), so the max-content size
  of such a text is the width of its line. The shaped
  texts are kept on the text run (`RunShapeCache` in `LayoutBox.LayoutState`), so a relayout reuses them even when the bounded
  cache of the fonts cannot hold all the texts of a large document. The spacing that depends on the neighbors of a character is
  then applied to the glyphs of the item (`ApplyItemSpacing`, `ShapedText.WithSpacing`; only for the texts with a character from
  U+00B7, U+2018 or an ideograph next to the item): `text-spacing-trim`
  ([fullwidth punctuation collapsing](https://drafts.csswg.org/css-text-4/#fullwidth-collapsing): an opening punctuation after
  an opening/middle dot/ideographic space/closing (of a larger or equal font size)/Ps character, a closing punctuation before a
  closing/middle dot/ideographic space/larger opening/Pe character, everything with `trim-all`; the classes are in
  `TextSpacingClasses`, the colons and dots follow the conventions of the content language) trims the blank half of the
  fullwidth glyphs (glyphs of at least 0.9em of a font that has the `halt` feature, `vhal` for the upright glyphs of
  vertical text: as Chrome, the punctuation of a font without the feature is never trimmed; the trimmed half is half of the
  advance, the values of the feature are not read), and
  `text-autospace` ([inter-script spacing](https://drafts.csswg.org/css-text-4/#inter-script-spacing)) adds 1/8ic (the advance
  of U+6C34, else 1em) after a character at a boundary between an ideograph (Han script, kana, CJK strokes) and a
  non-ideographic letter or numeral (not upright in vertical text: each character is upright or not by the
  `text-orientation` and `text-combine-upright` of its own element), unless an edge of an inline box, an atomic inline or a
  `wbr` separates them (the spacing belongs to the character before the boundary, not to the innermost element that contains the boundary;
  the combining marks and the variation selectors belong to the typographic character unit of their base, also when they are
  in another inline box: the spacing is after the unit, in the box of the last mark, or in the box of the base when the
  marks are shaped with it);
  `replace` replaces a U+0020 between them; `punctuation` (French) is not implemented.
- **Break opportunities**: `LineBreaker.FindOpportunities` with the `LineBreakOptions` of the style of each item
  (`line-break`, `word-break` (`break-word` is `overflow-wrap: anywhere`), `overflow-wrap`, `hyphens`, `break-spaces`, and the
  content language for the Chinese and Japanese tailorings: the language of the block container, unless an inline ancestor has
  a `lang` attribute; the [content writing system](https://drafts.csswg.org/css-text-4/#content-writing-system) is the one
  of the script subtag of the language when it has one: `en-Hrkt` is Japanese, `ja-Hang` is not). There is an opportunity before and after each atomic inline, even next to U+00A0, but not next to
  the other characters of the classes GL, WJ and ZWJ ([§5.1](https://drafts.csswg.org/css-text-4/#line-break-details): the
  browsers break there too, the specification is followed). With `text-wrap-mode: nowrap`, the opportunities inside
  the item are removed, and the ones between two items are controlled by the style of their nearest common ancestor, or by
  the box of the space before the opportunity ("for soft wrap opportunities created by characters that disappear at the
  line break, properties on the box directly containing that character control the line breaking"; a collapsed space after
  it keeps its own opportunity).
- **Line breaking** (`BreakLine`): greedy; the content of a line is measured by segments between the opportunities (the
  offsets of the shaped texts, the edges of the inline boxes, the margin boxes of the atomic inlines); the line ends at the last
  opportunity where the content before it fits, without the spaces that are removed or hang at the end of a line (and with
  the hyphen of a soft hyphen). The content fits with a tolerance of 1/64px, the layout unit of the browsers (the used sizes
  are rounded: six inline-blocks of 16.66666667% fit in their container); the flex items are collected into flex lines
  with the same tolerance. When nothing fits, the line breaks at the first opportunity (the content overflows), or at
  the last emergency opportunity that fits with `overflow-wrap: anywhere | break-word`. The starts of the inline boxes at a
  break go to the next line (with the out-of-flow boxes after them), the other out-of-flow boxes at a break and the ends of
  the inline boxes (also after a forced break) stay on the line, as in the browsers; an empty inline box (its content collapsed
  away, no inline margin, border nor padding) after collapsible spaces stays on the line with them. At the boundary of two
  inline boxes, the `overflow-wrap` of either character allows an emergency opportunity (which properties control the
  opportunities at such a boundary "is undefined"; as in Safari, WPT `overflow-wrap-anywhere-inline-*.tentative`). The content of an inline box must fit
  on the line before the negative inline-end margin (border, padding) of the box is applied: as in the browsers, the margin
  does not bring back the content that overflows the line. The same holds for the content before an atomic inline with a
  negative margin when the line can be broken at an earlier opportunity; content that overflows without such an opportunity
  stays on the line when the negative margin of the atomic inline that follows it makes the line fit.
  The collapsible spaces (U+0020) at the start of a line are skipped (phase II); a soft wrap opportunity after preserved spaces
  at the start of a line can be taken. The spacing at the edges of the line is measured with the content
  (`GetLineStartSpacing`/`GetLineEndSpacing`): the `letter-spacing` of the last character is not applied at the end of a line
  ([§8.2](https://drafts.csswg.org/css-text-4/#letter-spacing-property): the spacing is added after each typographic character
  unit, `ShapedGlyphFlags.LetterSpaced`, and removed at the end of a line, not at a hyphenation point: the hyphen is shaped
  without spacing; "letter-spacing ignores invisible zero-width formatting characters": the last character is the one before
  the characters of the category Cf that end the line, `SkipTrailingFormatCharacters`), nor the inter-script spacing. "Each consecutive run of atomic inlines [...] is treated as a single
  typographic character unit": the `letter-spacing` of the box that contains the last atomic inline of a run is added after
  it (`ApplyAtomicLetterSpacing`: in its inline size, `InlineItemFlags.LetterSpaced`; on its line-left side in a right-to-left
  run; the boundaries of inline boxes, the out-of-flow boxes and the control characters do not end a run), and removed at
  the end of a line. As in the browsers, the spacing is the one of the element of each character, after the character: the
  drafts now add half of it on each side of the character (see the limitations), and the WPT tests of the boundaries of
  elements (`letter-spacing-203`, `-205`, `-206`, `letter-spacing-nesting-*`, `letter-spacing-bidi-004`/`-005`) still expect
  the model of the previous drafts (the spacing between two characters is the one of the innermost element that contains
  both), which no browser implements; the fullwidth opening punctuation at the start of a line follows
  `text-spacing-trim` (`trim-start`/`trim-both`/`trim-all` trim it, `space-first` except on the first line and after a forced
  break), the closing one at the end of a line is trimmed with `trim-both`, and only when the line does not fit otherwise with
  `normal` (unless white space that is not removed follows it on the line). `hanging-punctuation`
  ([§9.2](https://drafts.csswg.org/css-text-4/#hanging-punctuation-property)): `first` (an opening bracket or quote at the start
  of the first line), `last` (a closing one at the end of the last line) and `force-end` (a stop or a comma at the end of a
  line) hang unconditionally, `allow-end` when the line does not fit otherwise: they are not measured for the fit, the
  alignment and the intrinsic sizes; `first` also hangs an ideographic space, and nothing hangs after the inline-start
  margin, border or padding of an inline box. The preserved spaces at the end of a line before a forced break or at the end of the
  content conditionally hang: they count for the max-content size and for the alignment when they fit. The other space
  separators (U+3000, U+2000–U+200A...) hang at the end of the line. In a sequence that mixes them, only the preserved spaces
  that end it are followed by the forced break: the ones followed by other white space hang unconditionally, and the white
  space before them hangs only when none of them fits (hanging is only at the end of a line, WPT
  `hanging-whitespace-002`/`-003.tentative`). With `white-space-collapse: break-spaces`, the spaces
  and the other space separators (but the no-break ones of the class GL) take up space and are followed by a soft wrap
  opportunity (`LineBreaker`), and `overflow-wrap` may break before a space. The white space that hangs is not scrollable
  overflow (`LineBoxFragment.NonHangingRect`: "an overflowing hanging glyph should typically be considered ink overflow so
  as to avoid creating unnecessary scrollbars"); the punctuation that hangs is (WPT `hanging-scrollable-001`). The
  collapsible spaces that follow preserved spaces at the start of a line are not removed (a line with only a preserved space
  is not empty). Hyphenation: the soft hyphens and, with `hyphens: auto`, the
  automatic hyphenation opportunities of the dictionary of the language (see [Hyphenation](#hyphenation)); the hyphen
  (`hyphenate-character`; `auto` is U+2010 when the first available font has it, else U+002D, as in Chrome, or when a bundled
  font has it for a bundled first available font; U+1400 after the Canadian syllabics and U+0640 after an Arabic letter in
  Uyghur) is measured with the content before the opportunity, with the spelling changes of the language (see
  [Hyphenation](#hyphenation)). `hyphenate-limit-chars`
  (for the soft hyphens: the explicit numbers, the characters of the word, before and after the hyphen, without the marks and the
  punctuation; for the automatic opportunities: Chrome's limits in UTF-16 code units), `hyphenate-limit-lines` (the successive
  hyphenated lines) and `hyphenate-limit-last: always` (the line before the last line is broken again without its hyphenation
  opportunities) limit the hyphenation opportunities (`_lineAllowsHyphens`, `IsHyphenationAllowed`). The fit of a line allows a
  rounding error of 1e-6px (a width of `4ch` is rounded to 6 decimals, a little less than the advances of 4 characters).
- **`word-space-transform`** ([§2.2](https://drafts.csswg.org/css-text-4/#word-space-transform), `CollectText`): the
  expandable separators (the U+200B of the texts and the `wbr` elements, each with the value of its own element) are
  replaced by U+0020 (`space`) or U+3000 (`ideographic-space`) when the items are collected. The replacement is applied
  after the phase I of the white space processing and before `text-transform`
  ([§2.3](https://drafts.csswg.org/css-text-4/#text-transform-order)): the inserted space does not collapse with the
  collapsible spaces around it nor with another inserted space (`a<wbr> b` has two spaces; the references of the WPT tests
  `word-space-transform-007` and `-009` expect one, against the assert of the first one), `full-width` makes it a U+3000, it
  follows the `white-space` of its element (it is not a soft wrap opportunity with `pre`, and it is removed or hangs at the
  end of a line as the other spaces), and it is a soft wrap opportunity otherwise, also with `word-break: keep-all`. The
  space of a `wbr` is a text item of its own. "The user agent must not replace expandable separators immediately preceding
  or following a forced line break (ignoring any intervening inline box boundaries, and associated margin/border/padding)":
  the separators after a forced line break are kept (`IsAfterForcedBreak`), the ones before it are restored when the break
  is met (`RestoreTrailingSeparators`, `_replacedSeparators`); the start and the end of the block container are handled as
  the forced line breaks (WPT `word-space-transform-010`, `-012`), and the bidi control codes and the separators that are not
  replaced are ignored like the box boundaries. `auto-phrase` (`InlineLayoutAlgorithm.Phrases.cs`) inserts a virtual
  expandable separator, replaced as the other ones, at the phrase boundaries of the text whose content language is supported:
  Japanese (the phrases of `word-break: auto-phrase`, see [Line breaking](#line-breaking)), and Thai, Lao, Burmese and Khmer,
  whose phrases are their words (the lexical word breaking; WPT `word-space-transform-018`). When the first run of the
  formatting context with `auto-phrase` is collected, the text of the runs is concatenated as the white space processing
  does (the removed segment breaks between East Asian characters are skipped; the atomic inlines, the ruby containers and the
  forced line breaks end the phrases) and segmented; a boundary is kept when the run of the character after it has
  `auto-phrase` and the language of the script. The boundaries are only between two letters, so no word separator, other
  space separator or U+200B is ever at the boundary. Inside a run, the separator is a character of the run; before its first
  character, it is a generated text of the nearest common ancestor of the characters around it (before the inline boxes that
  start there: WPT `word-space-transform-019` expects the space in the `b` around the `u` and the `em` it separates).
- **`text-wrap-style`** (`ComputeWrapStyleLimits`, [§5.3](https://drafts.csswg.org/css-text-4/#text-wrap-style)): before the
  lines are laid out, the content is broken (without building the lines) to compute a maximum inline size used by the line
  breaking of each line (`_lineWidthLimits`; the lines are still aligned in their line box). `balance`: the narrowest inline
  size (a binary search at 0.25px) that keeps the number of lines of the greedy breaking, with at most 10 lines (as Firefox);
  the lines between two forced line breaks are balanced together, each group with its own inline size (as Safari: the groups
  before the one being balanced keep their size, the ones after it are broken greedily).
  The emergency opportunities of `overflow-wrap` are not used to balance the lines unless the greedy line breaking needs them.
  `pretty` and `avoid-short-last-line`: when the last line is a single word and the line before it ends at a soft wrap
  opportunity, its last word moves to the last line unless that adds a line. `stable` is `auto`. The lines of a block
  container that has floats or is beside floats have different inline sizes: `balance` reduces the space of each line by
  the same amount for its line breaking (`_lineWidthReductions`, `BalanceLinesNextToFloats`: the largest amount, by the same
  binary search, that keeps the number of lines of the greedy breaking, as Gecko, which applies an inset to each line).
  Each amount is tried by laying out the lines with their floats (`LayoutAllLinesNextToFloats`; the state of the block
  formatting context is restored afterwards): a float is placed on a line when it fits in the space that the line box
  really has, and the content before a float placed on a line stays on that line. Such content is broken greedily when it
  has ruby annotations or an initial letter, in a line-clamp container, and when its first or last line is trimmed by
  `text-box-trim`; with `pretty`, the content beside floats or with floats is broken greedily.
- **`text-group-align`** ([§7.6](https://drafts.csswg.org/css-text-4/#text-group-align-property), `TextGroupAlignment`,
  `LayoutStyle.TextGroupAlign`): the contents of a block container whose value is not `none` are laid out a first time to
  find the shortest remaining space of the line boxes (the space that the content of a line does not use, before the
  justification: `AlignLine` reports it), then again with that space added as padding to the line boxes: on their inline-end
  side (`start`), inline-start side (`end`), line-right side (`left`), line-left side (`right`) or half on each side
  (`center`), in the direction of the block container of each line; `text-align` and the justification then apply in the
  space that remains. The padding is not larger than the remaining space of any line, so the lines break at the same
  positions. The line boxes are the ones of the block container and of its descendants in the same block formatting context
  (the group is the current one of `BlockFormattingContextState` while the contents are laid out; a block formatting context
  root passes it in `ContentArea.TextGroupAlignment`): the independent formatting contexts are skipped. The line boxes of a
  descendant that has its own `text-group-align` are aligned by that descendant only, the hidden lines of a line-clamp
  container are not counted, and the floats are not moved.
- **Line boxes** (`BuildLine`): the collapsible spaces at the end of the line are removed and the preserved spaces of a
  wrapping `preserve` hang ([phase II](https://drafts.csswg.org/css-text-4/#white-space-phase-2)); the leaves of the line
  (text parts, atomic inlines, the `br` line breaks, the hyphen, the empty inline boxes) get the levels of rule L1 (as in
  Blink and Gecko, the spaces that `white-space: break-spaces` preserves at the end of a line, which take space, are not its
  trailing white space: they keep their level and stay next to the text they follow, `GetBreakSpacesTrailingLength`) and are
  reordered by rule L2; they are placed from the line-left edge in visual order, nested in the fragments of their inline
  boxes (`BoxFragment` children of the `LineBoxFragment`; an inline box whose content is not contiguous after the reordering
  has several fragments on the line). The spacing of the line (the tab stops, the edges of the line, the justification) is
  applied to the glyphs of the leaves (`AddLineSpacing`, then `ApplyLineSpacing`: a text fragment whose spacing changed has a
  shaped text of its own, `ShapedText.WithSpacing`, so the ranges, the caret positions and the painting use the adjusted
  advances). The inline-start edges of an inline box are on the fragment where it starts, the
  inline-end edges where it ends (`box-decoration-break: slice`), or on every line (`clone`: the line breaking reserves the
  space of the repeated edges, `GetClonedEdgesAtLineStart` and the `ClonesEdges` tags: the inline-start edges of the boxes
  that are open at the start of a line, the inline-end edges of the ones that are open where it ends; the parts of a box
  split around blocks have them too), on the line-right side for a right-to-left box. The fragment of an inline box has the height of its content area (the ascent and descent of its first
  available font) plus its block-axis paddings and borders, which do not change the line height. `text-box-trim` on an inline
  box ([CSS Inline §6.2](https://drafts.csswg.org/css-inline-3/#text-box-trim)) trims its content area to the metrics of
  `text-box-edge` (`InlineMetrics.GetOverEdge`/`GetUnderEdge`: text = ascent/descent, cap, ex, alphabetic = the alphabetic
  baseline of the font (`romn` of its `BASE` table, else the origin of the glyphs), the ideographic edges = the
  ideographic-under/over baselines (`ideo`/`idtp`) and `ideographic-ink` the ideographic character face (`icfb`/`icft`), or
  the em box centered between the ascent and the descent for a font without ideographic baseline).
- **Inline alignment**: `text-indent` (`hanging`, `each-line`, the percentages of the content box; not on an anonymous block
  of a block container that is not its first child, the outside marker excepted; the anonymous flex and grid items are
  indented; the static position of an absolutely positioned box whose original display is inline-level is after the
  indentation of the first line, also in a block container without line, where it is the position of the content of an empty
  line box: beside the floats and aligned by `text-align`; a line without text has the direction of its block container, so the
  box is at the inline-start edge of a right-to-left block; on a line, its hypothetical box is a neutral character (U+FFFC in
  Blink): between characters of different bidi levels it has the lower one, e.g. before a left-to-right word that starts a
  right-to-left line it is at the start of the line; the out-of-flow boxes of a line are recorded in content order, whatever
  the direction of the line), `text-align` and `text-align-last` (for the last line and the lines before a
  forced break), with `start`/`end` from the direction of the paragraph; a line that does not fit is start-aligned; the content
  that hangs is outside the measure (before the start edge, after the end edge). `justify`
  ([§7.4](https://drafts.csswg.org/css-text-4/#text-justify-property)) distributes the space equally to the justification
  opportunities of the line (`Justify`), the end of the line and the hanging content excluded: `inter-word` after the word
  separators, `inter-character` after each visible typographic character unit (not after the letters of the cursive scripts,
  which are not separated: Joining_Type), `auto` after the word separators and the CJK
  characters (ideographs, kana, CJK and fullwidth punctuation: the scripts without word separators), `none` does not justify.
  `text-justify` is the one of the inline box of each text (the text of a box with `none` has no opportunity).
  With `inter-character`, a run of consecutive atomic inlines is a typographic character unit: the space is added after it
  (in the inline size of its last atomic inline), unless it ends the line; with the other values,
  the tabs and the atomic inlines are not opportunities, and the content before the last preserved tab of a line is not
  expanded (the tab stops "continue to line up", as Firefox).
- **Block alignment** ([CSS 2.1 §10.8](https://drafts.csswg.org/css2/#line-height)): the root inline box has the strut (the font
  and the line height of the block container); each inline box has layout bounds of its line height around its content area
  (the half-leading); with `line-height: normal`, the layout bounds of a box also include the ones of the fallback fonts of
  the glyphs of its text on the line and of the hyphen of a hyphenated line (the content area of each font with half of its line gap on each side, as Blink's
  `AccumulateUsedFonts`: a `unicode-range` face or a fallback font taller than the first available font makes the line
  taller), an atomic inline its margin box with its baseline (the last line box of an `inline-block`, the first
  one of the other atomic inlines, `baseline-source`; the block-end margin edge of a replaced element, of a box without
  line box, and of an `inline-block` that is a scroll container in the block axis when its `baseline-source` is `auto`:
  the rule of CSS 2 for `overflow`; with `baseline-source: first | last`, it is aligned by the first or the last baseline
  of its contents, as the `-webkit-inline-box` with a line clamp). For the baseline of an `inline-block` whose `baseline-source` is `auto`
  (`ConstraintSpace.UsesInlineBlockBaseline`), a block container of its normal flow that is a scroll container in the block
  axis gives the block-end edge of its margin box instead of its last line box, and a block-level table gives none (as in the
  browsers); with `baseline-source: last`, the last baseline of a table is the one of its last row. As in Gecko and WebKit,
  a `textarea` has no last baseline (`baseline-source: last` aligns it by the block-end edge of its margin box); as in the
  browsers, with `baseline-source: auto` a `textarea` (an inline-block scroll container) is also aligned by the block-end
  edge of its margin box, whatever the position of its lines. `vertical-align` aligns a box relative to its parent
  ([CSS Inline §4](https://drafts.csswg.org/css-inline-3/#baseline-alignment), `GetBaselineShift`): the alignment baseline of
  the box (`alignment-baseline`; `baseline` is the `dominant-baseline` of the parent, `LayoutStyle.DominantBaseline`: `auto` is
  alphabetic, or central in vertical text) is matched with the corresponding baseline of its parent: `alphabetic`, `central`,
  `ideographic`, `mathematical` and `hanging` are the baselines of the first available fonts (`MatchedFont.GetBaselines`: the
  `romn`, `ideo`/`idtp`, `math` and `hang` baselines of the `BASE` table (`OpenTypeFont.GetBaselines`: the horizontal axis,
  the default script), else synthesized: the origin of the glyphs, the descent and the ascent for the ideographic-under/over
  baselines (1em apart when the font has only one), the central baseline halfway between them, 80% of the ascent for the
  hanging baseline and half the ascent for the math baseline); `middle` aligns the midpoint of the box with the x-middle
  baseline of the parent (halfway between its alphabetic baseline and its x-height; half the x-height above the central
  baseline in vertical text) and `text-top`/`text-bottom` the edges of the layout bounds of the box with the content area of
  the parent (the CSS 2 definitions, as in the browsers, not the x-middle and text-over/under baselines of the box). An
  atomic inline with a baseline gets the other baselines from the first available font of its box
  ([generate baselines](https://drafts.csswg.org/css-align-3/#generate-baselines)); without one, they are synthesized from
  its margin box ([Appendix A.3](https://drafts.csswg.org/css-inline-3/#baseline-synthesis-box)): the line-under edge
  (alphabetic, ideographic), the middle (central, mathematical) or the line-over edge (hanging). The baseline of the text of
  an inline box stays its alphabetic (or central) baseline: the glyphs of the fallback fonts are not aligned by the dominant
  baseline. `baseline-shift`: `sub`/`super` (the offsets of the browsers: 1/5 + 1px and
  1/3 + 1px of the font size of the parent), lengths and percentages (of the line height), and the aligned subtrees of
  `top`/`bottom`/`center`, which are aligned with the line box once its height is known (they may make it taller). Quirks
  and limited-quirks modes (`LayoutContext.UsesLineHeightQuirks`): [the line height calculation
  quirk](https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk) and [the blocks ignore line-height
  quirk](https://quirks.spec.whatwg.org/#the-blocks-ignore-line-height-quirk): as in the browsers, an inline box without text
  directly in it on the line and without inline-axis borders and paddings (the margins do not count) does not contribute to
  the height of the line at all (rather than with a line height of 0), and the root inline box (the strut) only contributes
  when the line has text directly in it. A forced line break is not a text, except on a line without text and without atomic
  inline (an empty paragraph with a `<br>` keeps the height of a line; a `<br>` after an image does not make its line taller).
  A preserved segment break is preserved white space: the inline box that contains the segment break that ends a line is not
  subject to the quirk (the empty lines of a `pre` element keep their height, as in the browsers). In all the modes, the
  inline box of a preserved segment break that has no other content on its line has a fragment on it (an empty line of a
  span with `white-space: pre` has the height of the strut of the span).
  As in the browsers, the lines of a list item do not use these quirks ([whatwg/quirks#38](https://github.com/whatwg/quirks/issues/38)). The emphasis marks ([CSS Text Decoration §3](https://drafts.csswg.org/css-text-decor-4/#emphasis-marks),
  `TextLayoutStyle.EmphasisMarks`) extend the layout bounds of the boxes with text directly in them by half their font size
  outside of their content area, on the side of `text-emphasis-position` (as in the browsers: the line is taller when the
  half-leading does not leave room for them).
- **Ruby** ([CSS Ruby 1](https://drafts.csswg.org/css-ruby-1/#ruby-layout), `InlineLayoutAlgorithm.Ruby.cs`, as Blink does with its
  "ruby columns"):
  - *Collection*: the children of a ruby container are split in ruby segments (a base container, or a sequence of bases with
    the white space between them, followed by its annotation levels: each annotation container, or each sequence of
    annotations). The annotations of a level are paired with the bases in order ([pairing](https://drafts.csswg.org/css-ruby-1/#pairing)),
    the excess ones with anonymous empty bases, and the single anonymous annotation of an annotation container (its text)
    spans all the bases of its segment. The intra-level white space "does not participate in standard annotation pairing" (as
    Gecko enumerates its columns, `PairRubySegment`): it is paired with the white space at the same position of the other levels,
    and with an anonymous empty box in the levels that have no white space there; the inter-base white space paired with no
    annotation stays between the columns. The white space of an annotation is collapsed as on a line of its own (an
    inter-annotation white space is an empty annotation box). [Hidden annotations](https://drafts.csswg.org/css-ruby-1/#hiding)
    (as Gecko: an annotation paired with one base whose `visibility` is `collapse`, or whose text content, before the white space
    processing, is the text content of its base) keep their place in the pairing, but are laid out as empty annotations. Each base with the annotations paired with it is a ruby column (the bases spanned by
    an annotation are one column): a `RubyColumn` item followed by the items of its bases, then by the items of its
    annotations (`InlineItemFlags.RubyAnnotation`); the white space between the columns is between them. The side of each
    level is the `ruby-position` of its annotation container (of the ruby container for an anonymous level: the one of the
    annotations does not apply), `alternate` alternating from the previous level; `inter-character` is laid out as `over`.
    The ruby container and the internal ruby boxes are bidi isolates ([bidi](https://drafts.csswg.org/css-ruby-1/#bidi)); the
    preserved segment breaks in a ruby container are collapsible segment breaks (no forced line break in the annotations; as in
    Gecko, nor in the bases).
  - *Line breaking*: a column is a unit, "ruby may be broken wherever all levels simultaneously allow a break"
    ([break-between](https://drafts.csswg.org/css-ruby-1/#break-between)): only between the columns (the line breaks within the bases,
    [break-within](https://drafts.csswg.org/css-ruby-1/#break-within), are not implemented). The break opportunities are computed
    on the text without the annotations (`FindOpportunitiesWithoutAnnotations`), so that the opportunities between the bases and
    around the ruby container are the ones of the base text with the text around it (an opportunity before the isolate controls of
    the internal ruby boxes is moved after them, to the first character of the next column). As in Blink, the collapsible spaces at
    the end of the bases of a column that ends a line are removed (`TrimTrailingSpaces`): the column is measured without them. The inline size of a column
    (`ComputeRubyColumnLayout`) is the sum of the slots of its bases: each slot is as wide as its base and as the annotations
    paired with it alone, then the annotations spanning several bases widen their slots equally. The floats in a column are
    placed when the line breaking meets the column.
  - *Inline axis* (`LayoutRubyColumnsOnLine`, `AlignRubyColumn`): the bases are laid out on the line like the other inline
    content; the space of a slot that the content of its base (or of an annotation, in the slots it spans) does not fill is
    distributed by `ruby-align`: at the end for `start`, on both sides for `center`, to the justification opportunities of the
    text for `space-between` and `space-around` (as in Gecko, after each typographic character unit except the last one, with
    half an opportunity at each edge for `space-around`; centered without opportunity). The space at the edges is added inside
    the base and annotation boxes (their fragments span their slots), the space between the characters to the glyphs (as the
    justification). When building the fragments (`PlaceLeaves`), the annotations jump back to the start of the slots of their
    bases (the position in the base row is restored after them; an annotation container spans its annotations); on a
    reordered line, the leaves of the annotations of a column are moved after the leaves of its bases. `ruby-overhang`
    ([ruby-overhang](https://drafts.csswg.org/css-ruby-1/#ruby-overhang), as Blink; the legacy `none` is an alias of `spaces`): when an annotation makes the column wider than its
    bases (not empty), the space at the edges of the bases overlaps the adjacent content on the line: with `auto`, up to half the
    font size of the annotation, over an adjacent text not larger than the ruby container (a text between two columns is shared,
    the first one overhangs first); with `spaces`, only over the adjacent spaces (the spaces, the tabs, the ideographic space,
    the blank half of the fullwidth brackets and the quarters of the middle dots, but in a text that enables a font feature
    that changes the widths of the glyphs, such as `halt`). The advance of a tab after the column depends on the position
    of the end of the column, so on the overhang over it: the line breaking measures the tab at the end of the column with
    its overhang, and reduces the overhang until the tab at that position is as large as it.
  - *Block axis* (`PlaceRubyAnnotations`): "ruby annotation containers do not contribute to measured line height"
    ([line-height](https://drafts.csswg.org/css-ruby-1/#line-height)): each annotation box is the root of an aligned subtree whose extent
    is the one of its content (the content areas of its texts and the margin boxes of its atomic inlines, not the line height
    of its boxes, as Blink) with its block-axis margins, borders and paddings, placed against the over (under) edge of the content of the bases of its column (the content areas
    of their texts, the margin boxes of their atomic inlines, the annotations of the ruby containers in them; the content area
    of the base box when the base is empty), the levels stacked outward (the emphasis marks are placed the same way, see
    [Painting](#painting-csslayoutpainting)). Then, as Blink, the line box is extended by the annotations that overflow it
    (`ApplyRubyAnnotationOverflow`): the content of the line is measured (the content areas of the texts, the margin boxes of the atomic
    inlines and the annotations); the annotations that overflow the block-start side move the line down, unless the space after
    the content of the previous line (its half-leading, when it has no emphasis marks nor annotations there) leaves room for
    them; the next line is placed after the annotations that overflow the block-end side, and moves back into the space before
    its own content; after the last line, they may overflow into the block-end padding of the block container (as Blink), not
    into its border (as Gecko). The annotations of the first line extend it even when there is space before the block container
    (its margin, the last line of the previous block): the tentative behavior of Blink (WPT
    css/css-ruby/line-spacing-on-edges.tentative.html) contradicts stable tests (ruby-annotation-pairing-001.html).
    The emphasis marks do not leave room for the annotations.
  - The intrinsic sizes measure the columns as units (the min-content contributions of their atomic inlines). The ruby
    containers and their bases and annotations have box fragments in the line (`getBoundingClientRect()`, hit testing,
    painting); the decorations of the ruby container and of its ancestors are not propagated to the annotations
    (`Painter.GetAppliedDecorations`). The default styles of CSS Ruby that the HTML style sheet does not have are in
    `StyleEngine.SupplementalStyleSheet` (`rb`, `rtc`, the font size and the line height of the annotations).
- **Empty lines**: a line without text, preserved white space, atomic inline, inline-axis edges or forced break has no height
  and no baseline ([§9.4.2](https://drafts.csswg.org/css2/#inline-formatting)), and does not separate the margins: a block
  container with only such lines collapses through.
  The fragments of the inline boxes of such a line have a block size of 0 at the top of the line (as in the browsers, for
  `getClientRects()`); an inline box whose text is only trailing white space removed at the end of a line keeps a fragment
  there.
- **`text-box-trim` on block containers** ([CSS Inline §6.2](https://drafts.csswg.org/css-inline-3/#text-box-trim)): the
  block-start side of the first formatted line and the block-end side of the last one are cut to the `text-box-edge` metric of
  the root inline box (`GetTextBoxTrimAmounts`; the block-start side is the line-under side in `vertical-lr`): the line moves
  toward the block-start edge and the block container is shorter (its content overflows); the floats placed on the first
  formatted line are at the top of its line box, before the block-start edge of the content box (the trimmed amount is
  measured on the line built without its floats, `MeasureStartTrimOfLineWithFloats`). The trimming of an ancestor applies
  through its first (last) in-flow block container children without block-start (block-end) padding or border, not through the
  flex, grid and table formatting contexts (`IsTextBoxTrimmed`). The `text-box-trim` of a multi-column container applies to
  "each column of a multi-column container": the first and the last formatted lines of each column are trimmed, in the
  columns that are adjacent to the block-start (block-end) edge of the container, i.e. not after (before) a column spanner
  ([csswg-drafts#5335](https://github.com/w3c/csswg-drafts/issues/5335),
  [csswg-drafts#11363](https://github.com/w3c/csswg-drafts/issues/11363)). The strip of the content is laid out without these
  trims; the fragmentation applies them at the breaks (`FragmentationFlow.ApplyFragmentainerTrims`): the content before a
  break ends before the trimmed part of its last line box (`BreakCandidate.EndTrim`, a column may end in that part) and
  the next column starts after the trimmed part of its first line box (`BreakCandidate.StartTrim`: the line box overflows
  the block-start edge of the column, as the first line of a trimmed block container does). As in the browsers, a box of
  the content that has its own `text-box-trim` on a side trims that side in its first (last) fragment only, so the
  trimming of the container is not applied to its lines on that side at the breaks; with `box-decoration-break: clone`,
  each of its fragments is trimmed. The amounts trimmed at a break are not negative, and the columns are balanced from
  the block size of the untrimmed strip (the balanced block size may be larger than the smallest one that fits). The
  `text-box` shorthand expands to `text-box-trim` and
  `text-box-edge` (webref has no longhands for it: `MissingLonghands` of `tools/Meziantou.Framework.WebPlatform.SpecGen`).
- **Block formatting context**: the first line box that is not empty collapses the pending margins of the block formatting
  context, then each line advances its position by its height (`BlockFormattingContextState`). A block formatting context root
  with inline content creates the state of its context. The line boxes are shortened by the floats (`LayoutLine`,
  `GetLineOpportunity`; a line whose content does not fit next to the floats moves below them, but in a block container that
  does not wrap (`text-wrap-mode: nowrap`), as in the browsers) and the floats of the inline content are placed when the line breaking meets them (see
  [Floats](#floats-csslayoutblock)); a `br` with `clear` moves the next line (and the end of the block container) below the
  floats it clears.
- **`text-fit`** ([CSS Text 5 §text-fit](https://drafts.csswg.org/css-text-5/#text-fit), `InlineLayoutAlgorithm.TextFit.cs`):
  the lines are broken with the computed font sizes ("text fitting is applied after wrapping"), then each line is built with
  the used font sizes of its boxes multiplied by its line scaling factor. The styles of a scaled line are the styles of its
  boxes with a used font scale (`ComputedStyle.WithUsedFontScale`, `GetLineStyle`: `LayoutStyle.FontSize` is the computed
  font size multiplied by the zoom and by the factor), as the styles of the first line are the styles of the boxes inheriting
  from `::first-line`; the texts are shaped again at that size (`GetScaledShaped`), and their fragments keep the style of
  the line for the painting (`TextFragment.Style`: the glyphs, the emphasis marks, the decorations, whose decorating boxes
  have the used font sizes of the line, `Painter.GetDecoratingLine`). The computed values do not change: the font-relative
  lengths (a `line-height` of `2em`, a padding or a letter-spacing in `em`) are not scaled, the values that refer to the
  used font size are (a `line-height` that is `normal` or a number, a letter-spacing or word-spacing that is a percentage).
  The factor of a line (`MeasureLineFit`) makes its scalable parts fill the line box: the content of the line is measured
  with two font sizes (`CollectLineContent`, with the spacing at the edges of the line and without the white space at its
  end), which separates the parts that follow the font size from the other ones (the atomic inlines, the edges of the
  inline boxes, the spacing that is a length, `text-indent`); a line without scalable part is not scaled. `grow` does not
  scale down and `shrink` does not scale up; the percentage limits the factor (a maximum of 100% or more for `grow`, a
  minimum from 0% to 100% for `shrink`). `per-line` and `per-line-all` scale each line by its factor (`per-line`: not the
  last line nor the lines that end with a forced break); `consistent` scales all the lines by the smallest factor of the
  lines that have scalable parts (`PrepareTextFit`: the lines are broken and measured before they are laid out). The
  lines are not scaled in a block container that has floats or is beside floats ("if a feature such as float or
  initial-letter would make the line's inline size depend on its block position, scaling is disabled for the block"), nor
  with ruby annotations (not implemented); the intrinsic sizes are the ones of the content that is not scaled. The texts
  of a scaled line are shaped item by item (not across the boundaries of the inline boxes).
- **The line of the caret of an editing host**: as in the browsers (the `HasLineIfEmpty` of Blink), the principal box of
  an [editing host](https://html.spec.whatwg.org/multipage/interaction.html#editing-host) that is a block container always
  has the line of its caret, so that an empty editing host can be seen and pressed: without line box (no content, or only
  collapsed white space), its contents are as tall as a line (`line-height`), which separates its margins; with block-level
  children, its contents are at least as tall as a line (`BlockFormattingContext.HasCaretLineIfEmpty`). The caret is
  painted there (see [rendering.md](rendering.md#caret)).
- **`::first-line`** ([CSS Pseudo §3.4](https://drafts.csswg.org/css-pseudo-4/#first-line-styling)): on the first line, the
  root inline box has the `::first-line` style of the block container (or of the ancestor whose first formatted line it is,
  through the first in-flow block children; not into the floats, the absolutely positioned boxes and the outside markers),
  and the inline boxes and texts have their style re-parented to it (the text of a `display: contents` element keeps its
  style, re-parented) (`ComputedStyle.WithParent`, cached for the pass): the texts are shaped again with these styles, and
  their fragments keep them for the painting (`TextFragment.Style`). The fictional tags of `::first-line` are "nested just
  inside the innermost enclosing block-level element": the `::first-line` style of an ancestor inherits from the style of
  the block container of the line, not from its originating element, and the styles of several ancestors are nested, the
  outermost first (`GetRootFirstLineStyle`). The background of `::first-line` (its color and its images) is painted behind
  the content of the line (`Painter.PaintFirstLineBackground`: the content area of the root inline box along the in-flow
  content of a horizontal line, which is also the positioning area of the images). The backgrounds, the shadows and the
  colors of the borders and of the outline of the inline boxes of the first line are painted with their style inheriting
  from `::first-line` (`Painter.GetDecorationStyle`: e.g. a `currentcolor` background or border; the widths and the styles
  of the borders are the ones of the box, and a `filter` applies to the box as a whole with the colors of its style). Only the
  inherited properties inherit from `::first-line`: an explicit `inherit` of another property (e.g. `background-color`)
  takes the value of the non-pseudo parent (`ComputedStyle.GetInheritanceSource`). TODO: the
  `text-transform` and the `opacity` of `::first-line`, its background
  on the vertical lines, and the borders, paddings and margins of the inline boxes of the first line, which are resolved
  with the font size the boxes have on the other lines. `::first-letter` is a box of the box tree.
- **Text of `display: contents` elements**: a text run has the style of its parent element, which is not the style of the
  box it is in when the element has `display: contents`. Its layout bounds (the content area of its font and the
  half-leading of its line height) make the line taller, as the ones of an inline box would (`IsTextOfContentsElement`), as
  in the browsers.
- **Initial letters** ([CSS Inline §5](https://drafts.csswg.org/css-inline-3/#initial-letter-styling), `InitialLetter.cs`,
  `InlineLayoutAlgorithm.InitialLetter.cs`): `initial-letter: normal | <number> <integer> | <number> && [ drop | raise ]?`
  on `::first-letter` (the only boxes it applies to: not the inline-level boxes at the start of the first line nor the
  inside markers; a floating `::first-letter` is not an initial letter). The behaviors that the specification leaves open
  follow Chrome, the only browser that passes the tests.
  - *Sizing*: the used font size is the one whose cap height spans the lines of the size: `((size - 1) × line-height +
    cap height of the block container) / cap height ratio of the font of the letter` (the computed `font-size` and
    `line-height` of the pseudo-element do not apply; the cap height of a font without that metric is its ascent). The used
    style of the box (`ComputedStyle.WithDeclarations`) has that font size, a line height that places the over alignment
    point of the letter at the block-start edge of the content box (the cap height: `2 × cap height − ascent + descent`;
    the content area for the central baseline of vertical text), `float: inline-start` (the box is out of the line, painted
    with the floats, and the text decorations of its ancestors are not drawn on it) and the computed values of its box
    properties (their font-relative lengths refer to the computed font size). It inherits from the `::first-line`
    pseudo-element of the block container.
  - *Box*: the inline size of the box is its shrink-to-fit size; its automatic block size fits the sink (from the over
    alignment point to the alphabetic baseline) and the outlines of its glyphs below the baseline
    (`InitialLetter.GetContentBlockSize`, with the bounding boxes of the glyphs: the box of a letter without descender ends
    at the baseline), the content overflowing above the cap height being ignored; with the central baseline, the box is the
    content area. The margins, borders, paddings and backgrounds apply. As in Chrome, the box inherits `text-indent`, which
    indents the letter in it.
  - *Position*: the box is an item of its line (`InlineItemFlags.Float`, placed by `BeginInitialLetter` when the line
    breaking meets it). In the block axis, the block-start edge of its content box is at the cap height of the block
    container above the baseline of the line, minus `(size − sink) × line-height`: the cap height of a dropped initial
    starts at the cap height of the first line and its baseline is the one of the line of the sink. The baseline is the
    one of the strut until the line is built, then the one of the line (`FinishInitialLetter`: e.g. a ruby annotation
    moves it). The line of a raised or sunken initial is moved down by the lines that are not sunk (the letter does not
    extend into the previous content; the content that moves the baseline of the line uses that space first), unless
    `text-box-trim` trims its block-start side. As in Chrome, the block-start margin moves the box down, and the position
    in vertical text is computed with the central baseline of the line (the letter starts 16px − 12px = 4px before the
    block-start edge of a line of 24px with a cap height of 16px). In the inline axis, the box is at the inline-start edge
    of the line, after its indentation (the lines next to it are not indented again), next to the floats that intrude into
    its block extent.
  - *Wrapping* (`ExclusionSpace.AddInitialLetter`): the margin box of the initial letter is an exclusion of the block
    formatting context (`initial-letter-wrap: none`): the lines next to it are shortened as next to a float, in the next
    blocks too (a short paragraph), and the boxes that avoid the floats avoid it. It is not a float: `clear` ignores it, it
    does not limit the position of the next floats, and a block container that does not establish a block formatting
    context does not grow to contain it (a block formatting context root does, as in Chrome). A float of the same side
    placed after it is below it (the specification requires it for the floats of the next lines; a float of the first line
    should move the initial letter inward instead), and the line of an initial letter is below the initial letter of a
    previous block (`GetInitialLetterEnd`).
  - Not done: `initial-letter-align` (parsed; the alignment is `alphabetic`: the cap height and the alphabetic baseline) and
    `initial-letter-wrap` other than `none`, the kerning of the box by the side bearing of the glyph, the white space
    collapsed after a sunken initial, the initial letters that wrap on several lines, the line box of a block container
    that only contains its initial letter (the box is placed, but there is no line), the break avoidance between the lines
    next to an initial letter, the white space preserved before the first letter (it is not part of `::first-letter`: it
    is laid out after the box).
- **List markers** ([CSS Lists §3](https://drafts.csswg.org/css-lists-3/#list-style-position-property)): an inside marker is an
  inline box. An outside marker (`PendingOutsideMarker`) is placed next to the first line box of its list item that is not
  empty, also in a descendant block container (the list item passes it to its first in-flow block container child in
  `ConstraintSpace.OutsideMarker`): the inline-end edge of its margin box is at the inline-start edge of the content box, its
  baseline on the baseline of the line. A line box shortened by a float on its start side moves the marker beside the float
  (also the marker of a list item without content, which is where its line box would be). As in the browsers, the marker of a list item with inline content is part of its
  first line: its margin box makes the line taller (a marker image, a marker with a larger font; `AlignVertically`), also
  the line of a list item without content (`PlaceWithoutLine`); the marker placed next to a line of a descendant block
  container does not change this line. The outside marker is painted before the content of its list item (it precedes it
  in the tree), which may overlap it. The bullets of the predefined counter styles (`disc`, `circle`, `square`, the
  disclosure triangles) are symbols: `letter-spacing` and `word-spacing` do not space them. The marker is not passed to a child that clips
  its overflow, which would clip it. When no line box placed the marker, it is aligned with the first baseline of the
  in-flow children of the list item, if any (the baseline of a flex or grid container, of a table, of a block that
  establishes a formatting context, of a line box of a child that did not get the marker), as in the browsers; a descendant
  block container that got the marker and has such a baseline places it next to its content box (`PlaceAtBaseline`).
  Otherwise the list item places it at the block-start edge of its content box and is at least as tall as it, as in the
  browsers. In a multi-column list item, a marker aligned with the first line of a column spanner is placed with the spanner
  (`FragmentSlicer.SpannerMarkers`).
- **Intrinsic sizes**: `InlineFormattingContext.ComputeIntrinsicInlineSizes` gives the min-content size (the lines broken at
  every opportunity; the emergency ones only with `overflow-wrap: anywhere`) and the max-content size (only the forced breaks).
  The `auto` inline size of the atomic inlines, floats, absolutely positioned boxes (unless both inline insets are set: their
  stretch-fit size), outside markers and buttons whose contents are inline is their shrink-to-fit size. The floats of the
  content add their margin box to the max-content size of their line (a float with `clear` is below the floats that it
  clears, which are no longer next to the content after it), and to the min-content size alone. The atomic inlines
  and the floats are not laid out to compute the intrinsic sizes: their min-content and max-content contributions are used
  (`IntrinsicSizing.GetContribution`, `InlineItem.MinContentInlineSize`).
- **Writing modes**: the lines use the logical coordinates of the block container (vertical lines are stacked from the right
  in `vertical-rl`). The block alignment of a line is computed from its line-over edge (`WritingDirection.LineOver`: the
  ascent faces the right in both vertical modes); in `vertical-lr`, the line-over edge is the block-end edge
  (`IsLineOverBlockEnd`), so `BuildChildren` flips the positions (the distance between the line-under edges) and the baselines
  of the fragments stay measured from their block-start edges. The metrics of a box are measured from the baseline of its
  writing mode: central in vertical text (`LayoutStyle.HasCentralDominantBaseline`: `vertical-rl`/`vertical-lr` without
  `text-orientation: sideways`), else alphabetic. The central baseline is halfway between the ideographic-under and
  ideographic-over baselines of the font (`ideo`/`idtp` of its `BASE` table); for a font without ideographic baseline, the
  ascent and the descent are both half of the content area. An
  atomic inline uses its baseline when its block axis is the one of the line (measured from its line-over side, also for an
  opposite block flow); a box without baseline is aligned by its synthesized baselines (its center for the central baseline,
  its line-under margin edge for the alphabetic baseline).
  `text-combine-upright` ([§9.1](https://drafts.csswg.org/css-writing-modes-4/#text-combine-upright), in the `vertical-*`
  writing modes only: the `sideways-*` modes use the horizontal typographic mode): the text of a run with `all`, and each
  maximal sequence of at most N ASCII digits of a run with `digits N` (`AddTextItem` splits them into items of their own; the
  digits put in full-width form by `text-transform` count), is an item with `InlineItemFlags.CombinedUpright`: it is shaped
  horizontally without `letter-spacing` and composed into a single unit 1em wide (`TextShaper.CombineUpright`:
  `ShapedText.IsCombinedUpright` and `CombinedScale` for the painting), without wrap opportunity inside. A composition wider than
  1em is [compressed](https://drafts.csswg.org/css-writing-modes-4/#text-combine-compression): its full-width characters are
  converted back to their ASCII form, the 1/n-width glyphs of the font are used for n characters (the `hwid`, `twid` and `qwid`
  features, when they make it narrower), then it is scaled horizontally; a narrower one is centered. As for the contents of an
  inline-block, the collapsible white space at its start and at its end is removed (a composition that only has white space is
  an empty square of 1em that is not removed at the end of a line, as in Blink) and its forced line breaks are spaces. Its
  baseline is the one of a horizontal line of 1em (the half-leading is floored as in the lines), so it is painted like a
  `1em` inline-block with a horizontal writing mode. The glyphs are in logical order: a right-to-left composition (an
  override) is painted from its right end.
- Not done: the repetition of a hyphen at the start of the next line (Pinyin, Polish, Portuguese...), the spelling changes
  of the other languages (German 1901 `ck`, Dutch trema...), the hyphenation dictionaries of Uyghur and Cree, the
  hyphenation dictionaries whose license is copyleft (cs, hu, hy, lv), the values of the `halt`/`vhal`/`chws` font features of
  `text-spacing-trim` (the half of a fullwidth glyph of a font that has `halt`/`vhal` is half of its advance), `text-autospace: punctuation`, the half of
  `letter-spacing` on each side of the characters (the spacing is after each character and removed at the end of the line: the
  positions are the same, except at the boundaries of elements with different spacings and for the rectangles of the
  characters), the phrase detection of Chinese and Korean (`word-break: auto-phrase` is `normal` for them), `text-wrap-style: pretty` for the lines of a block container with floats or beside floats, the
  compositions of `text-combine-upright` across the boundaries of the boxes and of
  the bidi runs (a composition is one text item; its own bidi reordering is not isolated). Ruby: the line breaks inside the bases
  ([break-within](https://drafts.csswg.org/css-ruby-1/#break-within)), `ruby-merge`, the expansion of the ruby columns by the
  justification of a line, `inter-character` (laid out as `over`), the order of the segments by
  the `direction` of their containers, the inlinification of the computed `display` (only the boxes are inlinified),
  `ruby-position` in `::first-line`, and the space after the last line of a block container used by the annotations of the
  first line of the next one.

### Flex layout (`Css/Layout/Flex/`)

`FlexFormattingContext` lays out the flex containers ([CSS Flexbox 1](https://drafts.csswg.org/css-flexbox-1/), and the
balanced lines of [CSS Flexbox 2](https://drafts.csswg.org/css-flexbox-2/)) and the legacy
`-webkit-box` ([Compat](https://compat.spec.whatwg.org/#css-display-webkit-box)): the shared `FormattingContext.Layout` sizes
the container, then `FlexLayoutAlgorithm` (one instance per layout of the contents) runs the
[flex layout algorithm](https://drafts.csswg.org/css-flexbox-1/#layout-algorithm) in its content box. The box tree already
blockifies the items and wraps their text in anonymous items (`BoxTreeBuilder.FixupFlexOrGridContainer`).

- **Properties** (`FlexContainerProperties`): the direction (row/column, reverse), `flex-wrap` (`balance` alone behaves as
  `wrap balance`, and is serialized without `wrap`), `flex-line-count` (1 for a single-line container), the alignments and
  the main and cross gaps; for `-webkit-box`: `-webkit-box-orient` (logical: `horizontal`/`inline-axis` is a row), `-webkit-box-direction`,
  single-line, `-webkit-box-pack` as `justify-content`, `-webkit-box-align` for every item (their `align-self` is ignored),
  `-webkit-box-flex` as both flex factors, `-webkit-box-ordinal-group` as the order, no automatic minimum size (as in the browsers).
- **Axes**: the algorithm works in the logical axes of the container (row: main = inline axis); the items are sized and placed
  in its writing mode, so the orthogonal items need no special case: their sizes are imposed with `FixedInlineSize`/
  `FixedBlockSize` in the axes of the container. The positions are computed from the main-start/cross-start edges, then
  converted to logical offsets (the reverse directions and `wrap-reverse` flip them). The items are added to the fragment in
  order-modified document order (the absolutely positioned children are recorded between them in document order).
- **Items** (`FlexItem`): margins in flex-relative directions (`auto` = 0, distributed later), paddings, borders and scrollbar
  gutters, flex factors, resolved `align-self` (`normal` = `stretch`; `start`/`end`/`self-*`/`flex-*` mapped to the cross-start
  or cross-end edge; `stretch` applies to an `auto` cross size without `auto` cross margins; a cross size of `stretch` fills the
  line too, [csswg-drafts#11784](https://github.com/w3c/csswg-drafts/issues/11784)). The sizes are border-box sizes.
- **Flex base size and hypothetical main size** (§9.2.3): a definite flex basis (`box-sizing` applies; a percentage of an
  indefinite main size is `content`; `calc-size()` with the main size property as its `auto` basis, the content size for
  `content`), the size transferred from a definite cross size through the preferred aspect ratio (the natural one of a
  replaced element), else the content size: the intrinsic inline sizes (`AxisSizing.GetIntrinsicSizes`, cached per pass) when
  the main axis is the inline axis of the item, else its `BoxFragment.IntrinsicBlockSize` laid out with
  `MeasuresIntrinsicBlockSize` and its definite cross size (or its fit-content size); the content sizes are clamped by the
  min/max cross sizes transferred through the aspect ratio (the minimum and maximum main sizes are ignored: the measure of
  the block size of a replaced item does not transfer them through its ratio either). The definite cross size is the cross
  size property, or the stretched size of a single-line container with a definite cross size (§9.8); an intrinsic size
  keyword in the inline axis of the item is resolved by its layout with its used main size (the percentage block sizes of
  its contents count), and the block size of a table by the table layout.
- **Automatic minimum size** (§4.5): `min-width`/`min-height: auto` in the main axis is the content-based minimum size
  (min(specified size suggestion, content size suggestion), clamped by the max main size) unless the item scrolls in the
  main axis; the content size suggestion of a non-replaced item with a preferred aspect ratio is at least the min-content
  size of its contents, measured without the ratio (`ConstraintSpace.MeasuresContentsWithoutAspectRatio` in the block axis):
  the size transferred from its cross size does not let it shrink below its contents
  ([csswg-drafts#6794](https://github.com/w3c/csswg-drafts/issues/6794)); a flex base size transferred from a definite cross
  size is definite (the percentages of the contents of the item resolve against its main size in a column container whose
  main size is indefinite); the percentages of the specified size of a compressible form control (`input`, `textarea`, `select`...) resolve
  against zero, and it is then the minimum (as in the browsers; the percentage size of an image is a specified size
  suggestion as any other size). The min/max keywords of the block axis resolve with the measured block size.
- **Tables as items**: the sizing properties of the block axis of a table apply to its grid box
  ([CSS Tables](https://drafts.csswg.org/css-tables-3/#computing-the-table-height)): the block size of its captions is added
  to its definite flex base size and to its minimum and maximum main sizes in a column container, and its cross size in a
  row container is left to the table layout. A table is never smaller than its min-content size in the main axis, whatever
  its minimum main size (`min-width: 50px`, `min-height: 0`).
- **Lines and flexible lengths** (§9.3, §9.7): the items are collected into lines with their outer hypothetical main sizes
  and the main gap (a column container with an indefinite block size breaks at its `max-height`, its main size is its largest
  line clamped by its min/max block sizes); `ResolveFlexibleLengths` is the freezing loop of §9.7 (the shrink factor scaled by
  the inner flex base size, the sum of the factors below 1; a share of the free space that is not representable, e.g. next
  to `flex-grow: calc(infinity)`, is zero).
- **Balanced lines** (`flex-wrap: balance`, [CSS Flexbox 2](https://drafts.csswg.org/css-flexbox-2/#algo-line-break),
  `FlexLayoutAlgorithm.CollectBalancedLines` and `FlexLineBalancer`): the balancing line count is the number of lines of the
  greedy breaking above with the outer hypothetical main sizes of the items floored at zero (an item with negative margins
  does not make room on its line), or the minimum flex line count when it is larger: `flex-line-count`, at most the number of
  items (it is not a maximum: the lines of the greedy breaking are always created). The items are then
  [partitioned](https://drafts.csswg.org/css-flexbox-2/#algo-balance) into that many non-empty lines that fit in the
  available main size (a single overflowing item excepted: a zero-sized item does not join it, but it joins a line that it
  exactly fills), minimizing the sum of the squares of the differences between the size of each line (its items and its gaps)
  and the available size; among the partitions with the same error, the one with the most items on the first line wins, then
  on the second line, and so on. As the number of lines is fixed, this is the partition with the smallest sum of the squares
  of the sizes of the lines, which does not depend on the available size (it may be infinite: a column container with an
  indefinite block size has `flex-line-count` lines). `FlexLineBalancer` computes it by dynamic programming over the lines,
  from the last one: for each position where a line can start (between the earliest and the latest positions that leave a
  valid partition on both sides), the smallest error of the lines from that position and the last next break that gives it
  (which gives the tie-breaking rule when the partition is read from the first line); the cost satisfies the quadrangle
  inequality, so the best next break does not move backward when the start moves forward, and each line is solved by divide
  and conquer (O(p log p) for the p possible starts of a line, instead of O(p²)). A container with more than 4 million
  possible starts in total (thousands of items on thousands of lines, far from the lines of the greedy breaking) is not
  balanced: its lines are the ones of `wrap`. The breaks only use the floored sizes: the lines are then sized and flexed with
  the sizes and the margins of their items. In the second pass of `visibility: collapse`, the collapsed items take no space
  and stay on the line of the item that precedes them. A `-webkit-box` is single-line: it is not balanced.
- **flex-line-count** ([CSS Flexbox 2](https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property)): in a multi-line
  container (balanced or not) with N = `flex-line-count` > 1, the available cross space of the items is the definite cross
  size of the content box minus N-1 cross gaps, divided by N (the specified N, whatever the number of lines; floored at
  zero): the fit-content size of an item with an `auto` cross size, its `stretch` size before its line is sized, and the
  available inline size of an orthogonal item use it (`FlexLayoutAlgorithm.GetAvailableCrossSize`, in the available sizes of
  the constraint space of the items); the percentages still resolve against the content box. Blink only adjusts the
  available space of a balanced container; the specification says that the adjustment is "the only effect `flex-line-count`
  has on non-`balance` flexboxes", which is what is done.
- **Cross sizes** (§9.4): each item is laid out with its target main size (`FixedInlineSize`/`FixedBlockSize`) and its definite
  cross size, or an `auto` cross size (fit-content in its inline axis, `AutoInlineSizeIsFitContent`); its border-box cross size
  is the hypothetical cross size. The cross size of a line is the largest outer hypothetical cross size and the largest sum of
  the baseline distances of each baseline-sharing group (the inner cross size of a single-line container with a definite
  cross size; clamped by its min/max cross sizes otherwise; the baseline of an item may be outside of its margin box, the
  distance to the other edge is then negative). The container cross size is definite, or the sum of the lines
  clamped; `align-content` (`normal` = `stretch`) distributes the free space of a multi-line container. The stretched items
  are laid out again with their line cross size (clamped by their min/max cross sizes; the content-based keywords use their
  measured block size) as a fixed, definite size. In a multi-line container, and in a single-line container whose cross size
  is indefinite, `stretch` in the cross size properties resolves against the cross size of the line once it is known (the
  content box of the container before; in an indefinite size, a minimum `stretch` behaves as zero and a maximum one as `none`,
  [csswg-drafts#11006](https://github.com/w3c/csswg-drafts/issues/11006)): the items whose min/max
  cross size is `stretch` are clamped again and laid out with that size (`StretchItems`). The last layout of each item is reused when the constraint space is the
  same (`FlexItem.Fragment`/`Space`).
- **Alignment** (§9.5, §9.6, resolved by the shared [box alignment](#box-alignment-csslayoutboxalignmentcs) in axes measured
  from the main-start/cross-start edges): the positive free space goes to the `auto` main margins, else `justify-content` (`normal` =
  `flex-start`; `left`/`right` are physical in a horizontal axis, line-left/line-right in the vertical inline axis, start
  otherwise; the distributions fall back to the flex-start edge with a negative free space); the cross `auto` margins,
  else `align-self`. An overflowing subject with a `safe` positional alignment (`justify-content`, `align-content`,
  `align-self`) is aligned "as if the alignment mode were start": the start edge in the writing mode of the container, which
  is the flex-end edge of a reversed axis (`row-reverse`, `column-reverse`, `wrap-reverse`). The distributed spaces are snapped to 1/64 px with the
  remainder on the last item or line, so the last one ends exactly at the end edge (as the layout units of the browsers).
- **Baselines** (§8.3, §8.5): the baselines are measured from a reference side of the cross axis (the block-start side of a row
  container, the inline-start side of a horizontal column container, the top of a vertical one, as in the browsers); an item
  whose block-start side is the opposite side has its first/last preference flipped; an item without a baseline in the cross
  axis gets a synthesized one (the line-under edge for a horizontal container: bottom, or left in the horizontal axis; the
  center for a vertical one, its line-under edge with `text-orientation: sideways`). The first-baseline group is aligned with the reference side of the line (the other side with
  `wrap-reverse`). The baselines of the container come from the visually first (last) line: its items that participate in
  baseline alignment, else its startmost (endmost) item in the writing mode of the container (synthesized from its border box).
  `LayoutContext.LayoutBox` clamps the baselines of a scroll container to its border box
  ([csswg-drafts#7660](https://github.com/w3c/csswg-drafts/issues/7660)); an inline flex (or grid) scroll container is
  aligned in a line by that baseline of its contents (only an `inline-block` with `overflow` uses its bottom margin edge).
- **Container sizes**: the block size of the contents (`ContentLayoutResult.BlockSize`) is content-based even when the size is
  definite (the sum of the line cross sizes given by the items, the largest line of a column container): the intrinsic size
  keywords of the block axis and `BoxFragment.IntrinsicBlockSize` use it; `ContentLayoutResult.InFlowBlockSize` is the used inner
  size for the scrollable overflow.
- **Intrinsic inline sizes** (`FlexFormattingContext.ComputeIntrinsicInlineSizes`): the
  [intrinsic main sizes](https://drafts.csswg.org/css-flexbox-1/#intrinsic-main-sizes) of a row container
  (`FlexLayoutAlgorithm.ComputeRowIntrinsicSize`, once per constraint). The
  [contribution](https://drafts.csswg.org/css-flexbox-1/#intrinsic-item-contributions) of an item is its intrinsic size
  contribution (`IntrinsicSizing.GetContribution`: the larger of its content size and of its definite preferred size; the
  cross size of a stretched item of a single-line container with a definite cross size is definite, for its aspect ratio and
  the percentage block sizes of its contents), clamped by its flex base size as a maximum when it cannot grow and as a
  minimum when it cannot shrink, then by its minimum and maximum main sizes (the automatic minimum size of a compressible
  replaced element is limited by its min-content contribution, which a cyclic percentage makes zero). The flex base sizes
  are computed with an indefinite main size, under the constraint (a content-based flex base size is the min-content size
  for the min-content size of the container). The desired flex fraction of each item is the difference between its
  contribution and its flex base size divided by its flex grow factor (multiplied when the factor is below 1); when the
  largest one is positive, each item gets its flex base size plus its flex grow factor times that fraction (clamped by its
  min and max main sizes), and the container is the sum of these sizes and of the gaps. When no item grows, the container is
  the sum of the contributions (as in Blink; the specification applies the largest negative fraction to every item, which
  gives more than the contributions need). The min-content size of a multi-line container is its largest min-content
  contribution. The max-content size of a balanced container with a `flex-line-count` above 1 is its largest line (the
  same computation on the items of each line) when its items are balanced in an unlimited main size with their outer
  hypothetical main sizes (as in Blink). The collapsed items contribute nothing. In the cross axis of a column container: the largest contributions
  of the items; the max-content size of a multi-line column container whose block size is definite, or limited by a
  maximum block size that is a length, or that is balanced with a `flex-line-count` above 1, is the sum of the cross sizes of
  its lines and of the gaps between them
  (`FlexLayoutAlgorithm.ComputeColumnLinesCrossSize`, the items measured with an indefinite inline size).
- **Definite sizes** (§9.8): the main size of an item of a column container whose block size is indefinite is indefinite for
  its contents (`IsFixedBlockSizeIndefinite`), unless its flex basis is definite (as in the browsers); the stretched cross
  sizes are definite.
- **visibility: collapse** (§4.4): the lines are laid out twice; in the second pass the collapsed items take no main space
  (and no gap, as in Firefox, the only browser that collapses the items) and their struts (the cross size of their line in
  the first pass, once `align-content: stretch` stretched it) keep the cross size of their line.
- **Absolutely positioned children** (§4.1): see [Positioned layout](#positioned-layout-csslayoutpositioned) (static position).
  A `safe` alignment (`align-self: safe center`, `justify-content: safe end`) is recorded on the candidate
  (`OutOfFlowCandidate.SetSafeAlignment`): a margin box larger than the inset-modified containing block that the static
  position gives is aligned with the start edge of that block instead (as in Blink,
  [csswg-drafts#11934](https://github.com/w3c/csswg-drafts/issues/11934)).
- **Painting and hit testing**: the flex items are painted atomically like inline blocks, in fragment (order-modified) order
  (`PaintOrder.PaintsChildrenAtomically`, also for the grid items). The absolutely positioned children of the container are
  ordered among the items as if they had `order: 0` ([§5.4](https://drafts.csswg.org/css-flexbox-1/#order-property)); the
  absolutely positioned descendants of an item whose containing block is the container are ordered with their item
  (`PaintOrder.GetItemOrder`).
- **Scrolling**: the scroll origin of a flex container is its main-start/cross-start corner
  (`ScrollContainers.GetScrollOrigin`: the overflow of `row-reverse`, `column-reverse` and `wrap-reverse` is reachable with
  negative scroll positions); the in-flow bounds of a flex scroll container are the margin boxes of its items in both axes
  (`InFlowBounds.Compute(bothAxes)`), only the negative margins of the items count in a non-scroll container.
- Not done: the line breaks of a column container at a percentage maximum block size for its intrinsic
  inline size, the line that an editable container without item reserves,
  the relayout of the items in the fragments of a fragmented container (the items are fragmented as parallel flows, see
  [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation))
  (`margin-trim` no longer applies to the flex containers, see [Block formatting context](#block-formatting-context-csslayoutblock)).

### Grid layout (`Css/Layout/Grid/`)

`GridFormattingContext` lays out the grid containers ([CSS Grid 2](https://drafts.csswg.org/css-grid-2/)): the shared
`FormattingContext.Layout` sizes the container (a block-level grid fills the available inline size, an inline-level one gets its
fit-content size from `ComputeIntrinsicInlineSizes`), then `GridLayoutAlgorithm` (one instance per layout of the contents, or per
measure of the intrinsic sizes) runs the [grid layout algorithm](https://drafts.csswg.org/css-grid-2/#layout-algorithm) in its
content box. The algorithm works in the logical axes of the container: the columns are its inline axis, the rows its block axis.
The box tree already blockifies the items and wraps their text in anonymous items. The grid lanes containers use the same
algorithm (see [Grid lanes](#grid-lanes-csslayoutgridgridlayoutalgorithmlanescs)).

- **Explicit grid** (`GridExplicitAxis`, per axis): the track sizing functions of the track list (`repeat(<integer>)` expanded,
  `repeat(auto-fill | auto-fit)` repeated as many times as fit in the content box, or its maximum size (a maximum smaller than
  the minimum is the minimum), or to fill its minimum size, with the gaps, [§7.2.3.2](https://drafts.csswg.org/css-grid-2/#auto-repeat);
  for the intrinsic inline sizes of the container, the box that asks for them gives the definite minimum and maximum inline
  sizes in `IntrinsicSizingInput.MinInlineSize`/`MaxInlineSize`: the percentages of a definite containing block and the block
  sizes transferred through the aspect ratio, `AxisSizing.GetIntrinsicSizingInput`; for a container whose rows have an automatic
  repetition, its definite minimum and maximum block sizes in `MinBlockSize`/`MaxBlockSize`, `AxisSizing.BlockLimits`),
  extended to the rows/columns of `grid-template-areas`, the names of the lines (the explicitly-assigned names, merged across
  the repetitions, and the `foo-start`/`foo-end` names of the named areas) and `grid-auto-rows`/`grid-auto-columns` for the
  implicit tracks (the pattern repeats forwards after the track list and backwards before the explicit grid). The grids are
  limited to 10,000 lines per axis ([overlarge grids](https://drafts.csswg.org/css-grid-2/#overlarge-grids): the areas are clamped).
- **Placement** (`GridPlacement`): the grid-placement properties resolved against the lines of the explicit grid (line numbers,
  negative numbers from the end, named lines, the Nth named line, the implicit lines assumed to have every name, spans to named
  lines in the search direction, the conflict handling of [§8.3.1](https://drafts.csswg.org/css-grid-2/#grid-placement-errors)),
  then the [grid item placement algorithm](https://drafts.csswg.org/css-grid-2/#auto-placement-algo) on the items in
  order-modified document order (`row`/`column`, `sparse`/`dense`, the occupied cells as intervals per row, the implicit tracks
  created before and after the explicit grid). The empty repetitions of `auto-fit` are collapsed (fixed size 0; their gutters
  collapse: a gutter precedes a track that is not collapsed when another one precedes it, so a collapsed track is at the end of
  the previous track).
- **Items** (`GridItem`): the margins in the axes of the grid (`auto` = 0, distributed by the alignment), the self-alignment
  (`justify-self`/`align-self`, `auto` = the `justify-items`/`align-items` of the container without `legacy`; `start`/`end`/
  `center`/`left`/`right`/`self-start`/`self-end`/`flex-*`/baselines in the writing mode of the container), and the
  [sizing](https://drafts.csswg.org/css-grid-2/#grid-item-sizing): `normal` and `stretch` stretch an `auto` size without `auto`
  margins (a `normal` item with a preferred aspect ratio or that is a replaced element other than a form control (as in the
  browsers, also without natural size: it has the default object size)
  is sized as a block-level box: it fills the area in its inline axis, its block size is its content size or transferred through
  the ratio; while the columns are sized, the stretch-fit block size of the grid area of a non-replaced item with such an
  aspect ratio is transferred through the ratio to its contributions, as in Blink: `GridItem.TransfersStretchedBlockSize`),
  the other values size an `auto` inline size as `fit-content`. An item is laid out by its formatting context in a
  `ConstraintSpace` whose containing block is its grid area (definite in both axes once the tracks are sized); a stretched size is
  a `FixedInlineSize`/`FixedBlockSize` clamped by the min/max sizes; the last layout of an item is reused for the same space.
- **Contributions** (`GetContributions`, cached per item and axis for the size of its area in the other axis): in the inline
  axis of the item, `IntrinsicSizing.GetContribution` (the containing block is the grid; for an orthogonal item measured in
  the rows, its own writing mode, its block size being the size of its column area); in its block axis, the item laid out with
  the size of its area in the other axis (min-content = max-content = its outer block size). The
  [minimum contribution](https://drafts.csswg.org/css-grid-2/#minimum-contribution) is the min-content contribution when the
  preferred size does not behave as `auto` or depend on the containing block (`fit-content` does not, WPT
  grid-item-min-contribution-fit-content-001), else the used minimum size: for `min-width/min-height: auto`, the
  [automatic minimum size](https://drafts.csswg.org/css-grid-2/#min-size-auto) (the content-based minimum size when the item does not
  scroll, spans an `auto` min track and no flexible track when it spans several; the transferred size suggestion of a replaced
  element, else the content size suggestion; clamped by the stretch fit into the fixed max sizing functions it spans (with its
  extra margins and baseline shim) and by its maximum size).
- **[Grid sizing algorithm](https://drafts.csswg.org/css-grid-2/#algo-grid-sizing)**: the columns, then the rows (the areas of
  the items in the columns are known, with the space distributed by `justify-content`), then the columns and the rows again when
  the min-content contribution of an orthogonal item or of an item with an aspect ratio changed. While the columns are sized,
  the area of an item in the rows is the sum of the fixed max sizing functions of its rows (with the space of `align-content`
  when the container and all its rows are definite), else indefinite. When the block size of the container is indefinite, the
  rows are sized with their percentages treated as `auto` to find it, then sized again with it (the percentages of the tracks
  and of `row-gap` resolve against it, as in the browsers); the block size of the contents is the first size.
- **[Track sizing algorithm](https://drafts.csswg.org/css-grid-2/#algo-track-sizing)** (`GridLayoutAlgorithm.TrackSizing.cs`,
  `GridTrack`: the min and max sizing functions resolved with the percentage basis (the percentages of an indefinite size behave
  as `auto`), the base size and growth limit): initialization; the items spanning one track; the items spanning several tracks
  without flexible track, by increasing span (the six steps, the [distribution of the extra space](https://drafts.csswg.org/css-grid-2/#extra-space)
  with the planned and item-incurred increases, the limits, `fit-content()` (beyond the limits, a track that reaches its argument
  is treated as fixed: the rest goes to the other matching tracks, or to all the affected tracks), the infinitely growable tracks; the distribution to the
  tracks that are not affected is not applied, as in the browsers: WPT grid-content-sized-columns-resolution); the items crossing
  flexible tracks (distributed by flex factor; as in the browsers, with their minimum contributions under a min-content
  constraint: the flexible tracks of a container sized to its min-content size do not grow to the contents of the items
  that span them); maximize (the free space is infinite for a max-content constraint and an
  indefinite available space, redone with the maximum inline size for the columns; as in the browsers, the maximum block
  size of the container does not limit its rows, whose contents overflow it); the flexible tracks ([find the size of an fr](https://drafts.csswg.org/css-grid-2/#algo-find-fr-size),
  the flex fraction from the items when the free space is indefinite, kept between the min and max sizes); the stretch of the
  `auto` tracks when the content distribution is `normal` or `stretch` (with the minimum size when the free space is indefinite).
  The flexible rows of a container whose block size is indefinite are sized again in the resulting block size when their flex
  factors sum to less than 1 (they then only take their fraction of it, as in the browsers). The columns are sized again once
  the rows are when the contribution of an item changed with them (`DependsOnRows`: an orthogonal item, an item with an aspect
  ratio, an item with a percentage or stretched block size whose intrinsic contributions differ with a definite block size,
  e.g. a replaced element with a percentage block size in it).
  The gutters are fixed tracks spanned by the items. `GridSizingItem` is an item that participates in the sizing of an axis: an
  item of the grid or of a subgrid (with its span in the tracks of the grid and its extra margins), or a hypothetical item of
  an edge of a subgrid.
- **Intrinsic sizes** (`ComputeIntrinsicInlineSizes`): the sum of the columns and gutters sized under a min-content and a
  max-content constraint (the rows are sized too when an item depends on them: an orthogonal item, an aspect ratio); the
  percentages of the gaps resolve against 0. The `auto` block size is the size of the rows. With size containment and
  `contain-intrinsic-size: none`, the grid is sized with its tracks but without its items (`ignoresItems`); the block size
  known before the contents (`ContentArea.ContainedBlockSize`) gives the automatic repetitions of the rows, and the rows are
  sized again in it when they are flexible.
- **Alignment** (resolved by the shared [box alignment](#box-alignment-csslayoutboxalignmentcs)): the tracks are aligned with `justify-content`/`align-content` ([§10.5](https://drafts.csswg.org/css-grid-2/#grid-align):
  `space-between` falls back to start, `space-around`/`space-evenly` to center; `safe` and the distributions start-align a negative
  free space; the space of a distribution is added to the gutters); the items in their areas: the `auto` margins absorb the
  positive free space, then the self-alignment (`safe` start-aligns an overflowing item).
- **Baselines** ([baseline-sharing groups](https://drafts.csswg.org/css-align-3/#baseline-sharing-group)): the items aligned by
  `baseline`/`last baseline` in a row (`align-self`) or a column (`justify-self`) share a baseline per start (first) or end (last)
  track; an item whose block flow direction is opposite to the axis has its preference flipped; an item without a baseline in the
  axis gets one synthesized from its border box (its line-under edge, or the one of an axis-compatible writing mode; central in a
  vertical grid, unless `text-orientation: sideways`). An item whose block flow is opposite aligns its own first (last)
  baseline, measured from the other side, in the last (first) baseline-sharing group. The baselines are shims of the contributions in the track sizing ([§12.5 step 1](https://drafts.csswg.org/css-grid-2/#algo-baseline-shims))
  and offsets in the alignment; an item whose size depends on an intrinsic track does not participate (its fallback alignment).
  The baselines of the container ([§10.7](https://drafts.csswg.org/css-grid-2/#grid-baselines)) come from the first (last) row:
  its baseline-aligned items, else the first item in grid order with a baseline, else synthesized from the first item.
- **Baseline content-alignment** ([CSS Align §5.3](https://drafts.csswg.org/css-align-3/#baseline-align-content),
  `GridItem.GetContentBaseline`): an item that is a block container with `align-content: baseline` (`last baseline`), whose
  self-alignment in its block axis is `stretch` or places its block-start (block-end) edge at the edge of its grid area,
  is in the baseline-sharing group of its start (end) track with the items aligned by baseline themselves: in its row, or in
  its column for an orthogonal item. Its shim is not a margin: it is a part of its contribution in the track sizing, and
  space in its box in its final layout, before (after) its contents (`ConstraintSpace.ContentShimStart`/`ContentShimEnd`:
  "Baseline content-alignment effectively increases the padding on the box"; `FormattingContext.Layout` adds it to the
  block size of the contents and moves them). The baseline of the item is the one measured without the shim. An item
  whose size depends on an intrinsic track, or with an `auto` margin in the axis, does not participate (its contents are
  aligned to the start, or to the end). Not done: the items of the subgrids and of the grid lanes containers (their
  contents use the fallback alignment), the flex items and the table cells.
- **Absolutely positioned boxes** ([§9](https://drafts.csswg.org/css-grid-2/#abspos)): the static position of a child is aligned
  with its `justify-self`/`align-self` in the content box, or in its grid area when the container is its containing block (a
  `safe` alignment is recorded on the candidate, as for the flex containers). The
  layout records the lines of the grid in `GridContainerState` (the `LayoutState` of the box, with the layout pass), which
  `PositionedLayout` uses for the boxes whose containing block is the container: their containing block is the grid area of their
  grid-placement properties in the augmented grid (`auto` lines and the lines that do not exist are the padding edges,
  `GridPlacement.ResolveAbsolute`; a grid without track has one line, at the start edge of its content box).
- **Subgrids** ([§9](https://drafts.csswg.org/css-grid-2/#subgrids), `GridLayoutAlgorithm.Subgrid.cs`): an item that is a grid
  container with `subgrid` in an axis (not with layout or paint containment, nor a size container: they establish an
  independent formatting context; `subgrid` then behaves as `none` and its line names are not used) gives its parent's tracks
  to its items. Its parent gives it a
  `SubgridInput` (in its `GridContainerState`): per subgridded axis of the subgrid (its own axes, swapped when it is orthogonal,
  reversed when its direction is opposite), the span (an automatic span is the number of tracks of its `<line-name-list>`), the
  inherited names (the explicitly-assigned names of the parent lines and the names of the parent's named areas clipped to the
  subgrid), the extra margins of the items at its edges (its margin, border, padding and scrollbar gutter plus its own extra
  margin, which accumulates through the levels) and the gap of the tracks (half the difference between the gap of the subgrid
  and the gap of the tracks is an extra margin of the other items; a `normal` gap is the used gap of the parent, which may
  itself be a subgrid with its own gap). The [edges](https://drafts.csswg.org/css-grid-2/#subgrid-edge-placeholders)
  always take the space of their margin, border and padding in their edge track: a subgrid contributes a hypothetical empty
  item in the track of each edge, with the extra margin of that edge (both edges for a subgrid of one track), whatever its
  items (an item whose extra margin beside a gutter is negative can contribute less than the edge). This is the behavior of
  the browsers; the specification extends the items of the nearest occupied track to the edge of a non-empty subgrid (in a
  subgrid with an automatic position in a grid lanes container, every track contributes its extra margins: see
  [Grid lanes](#grid-lanes-csslayoutgridgridlayoutalgorithmlanescs)). The parent creates an
  instance of the algorithm for the subgrid that places its items (the definite positions are clamped to its explicit grid
  before the automatic placement, no implicit tracks; the lines of a named area that is outside of the subgrid are its last
  line): when the parent sizes an axis, the subgrids subgridded in it are empty and their items
  (recursively) are sizing items of the parent. Once an axis is sized, the instances of the subgrids share its tracks (the areas
  of their items in that axis), a subgrid subgridded in the other axis sizes its own tracks of that axis, and the inputs get the
  positions of the tracks in the content box of each subgrid, which its own layout uses instead of sizing the axis (a subgrid whose
  rows come from its parent gives them to its nested subgrids before it sizes its columns). The standalone axis of a subgrid
  is also sized when the subgrid is created, before the parent sizes the subgridded axis: the contributions of its items to
  the tracks of the parent depend on it (the orthogonal items of a subgrid, the items of an orthogonal subgrid). It is sized
  with the block size of the subgrid when it is a length, else with the size of its grid area when the parent knows it (a
  stretched subgrid in tracks with a definite max track sizing function, the content box of a grid lanes container in its
  stacking axis; not less than the min-content size of its columns when its automatic minimum size applies), else under a
  max-content constraint ([§12.1](https://drafts.csswg.org/css-grid-2/#algo-grid-sizing): "assume the available space that it
  would have if any row with a definite max track sizing function had that size and all other rows were infinite"); an item
  measured before the columns of its subgrid are known is laid out in its max-content inline size. The parent sizes its
  columns again when the contribution of an item of a subgrid changed once its rows are known. A subgrid is
  stretched in its subgridded axes (its alignment and its sizes are ignored) and its `justify-content`/`align-content` do not apply
  there. The resolved value of a subgridded track list is `subgrid` with the names of its lines. For the absolutely positioned
  boxes of a subgrid, the lines at the edges of a subgridded axis are the edges of its content box.
- **Fragments and painting**: the items are added in order-modified document order (the absolutely positioned children between
  them in document order) and painted atomically like inline blocks (`PaintOrder.PaintsChildrenAtomically`); `z-index` applies
  to the items (`StackingContexts`). The in-flow bounds of a grid scroll container are the margin boxes of its items in both axes
  and the grid ([§5.3](https://drafts.csswg.org/css-grid-2/#overflow): `ContentLayoutResult.InFlowBlockSize` and
  `InFlowInlineSize` are the ends of the last row and of the last column, which the alignment may move; not the tracks of a
  subgridded axis), which the paddings of the container extend in its scrolling area.
- Not done: the intrinsic `repeat(auto-fill, auto)` of the grids (tentative, no browser; the grid lanes implement it), the
  baseline alignment of the items of a subgrid with the items of its parent, the relayout of the items in the fragments of a fragmented grid (the items are fragmented as parallel
  flows, see [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation)).

### Grid lanes (`Css/Layout/Grid/GridLayoutAlgorithm.Lanes.cs`)

`display: grid-lanes | inline-grid-lanes` ([CSS Grid 3](https://drafts.csswg.org/css-grid-3/), masonry) is laid out by the grid
layout algorithm in a lanes mode (`GridLayoutAlgorithm._isLanes`): the items are placed in the tracks of one axis (the **grid
axis**) and stacked in the other one (the **stacking axis**). The properties are the ones of the WPT tests (the orientation is a
"TBD property" of the spec, [csswg-drafts#12803](https://github.com/w3c/csswg-drafts/issues/12803)): `grid-lanes-direction`
(`normal | [ row | column ] [ fill-reverse || track-reverse ]?`, its computed value orders the reversals), `grid-lanes-pack`
(`normal | dense`, `grid-auto-flow: dense` also enables the dense packing), `flow-tolerance` and the `grid-lanes` shorthand
(`none | [ <string> || <track-list> || <'grid-lanes-direction'> ]`: the template of the grid axis, its areas and the direction,
the omitted direction is `column`; its serialization ignores the template of the stacking axis). `flow-start`/`flow-end` are
aliases of `flex-start`/`flex-end`.

- **Orientation**: `grid-lanes-direction: row` or `column`, else rows when `grid-template-columns` is `none` and
  `grid-template-rows` is not, else columns. The stacking axis is a grid axis with a single track, the **stacking range**, whose
  lines 1 and 2 are the lines of the absolutely positioned boxes (`GridExplicitAxis.CreateStackingAxis`); the track lists of the
  stacking axis are ignored (the resolved value of its template is the computed value, `GridTrackSizes.StackingAxis`).
- **Explicit grid and placement**: the explicit and implicit grid of the grid axis are created as for a grid (the definite
  positions, the largest span of the auto-placed items). `repeat(auto-fill | auto-fit)` accepts the intrinsic track sizes: their
  number of repetitions uses the hypothetical sizes of the tracks ([§3.1.1](https://drafts.csswg.org/css-grid-3/#repeat-intrinsic):
  the intrinsic track sizes resolved with all the items auto-placed and the track listing repeated `2 + (largest span - 2) / count`
  times, the growth limits maxed per entry, `GridIntrinsicRepeatSizes`). `auto-fit` collapses the repetitions that the heuristic
  of [§3.3.1](https://drafts.csswg.org/css-grid-3/#repeat-auto-fit) considers empty (the tracks of the explicitly placed items and
  the first N tracks, N the sum of the spans of the auto-placed items, as in the browsers).
- **Track sizing** ([§3.4](https://drafts.csswg.org/css-grid-3/#track-sizing)): the track sizing algorithm of the grid sizes the
  grid axis; the auto-placed items are sizing items at every possible start position (not in a collapsed track,
  `AddLanesSizingItems`). Their containing block in the stacking axis is the content box of the container.
- **Placement** (`PlaceLanesItems`, [§4.4](https://drafts.csswg.org/css-grid-3/#grid-lanes-layout-algorithm)): in order-modified
  document order, an item goes to its definite tracks, or to the position whose largest running position is the smallest (the
  positions within the tie threshold of `flow-tolerance`, 1em for `normal`, are tied and resolved by the auto-placement cursor),
  at the largest running position of its tracks; it is laid out there (its margin box sets the running positions of its tracks
  to its end plus the gap: the outer size and the gap are floored at zero together, as in the browsers). With dense packing, an
  item is placed in the highest space that fits it ([§4.3](https://drafts.csswg.org/css-grid-3/#grid-lanes-dense)) in tracks of the
  same used size (the openings left before the items placed after a running position, and the space after the running positions),
  when it is higher than its normal position (or as high, in an opening); the ties within the tolerance go to the start-most
  space, the cursor and the running positions are not changed. `track-reverse` places the items in the reverse order of the
  tracks; `fill-reverse` stacks them from the end of the stacking axis (the positions are computed in the direction of the
  stacking, then mirrored).
- **Sizes**: the block size of a column container is its stacking range; the inline sizes of a row container are its stacking
  range with the max-content contributions of the items (under both constraints, as in the browsers; when its block size is
  indefinite, the items are laid out as if the rows without a definite max track sizing function were infinite, the rows are
  sized after the inline size, as in a grid and in Chrome), the inline sizes of a column container the sizes of its columns.
- **Alignment**: in the grid axis, as in a grid (`track-reverse` puts `flex-start` on the end side, and the overflowing tracks of a
  scroll container on the start side). In the stacking axis ([§6.3](https://drafts.csswg.org/css-grid-3/#stacking-axis-content-distribution),
  [§6.4](https://drafts.csswg.org/css-grid-3/#stacking-axis-self-alignment)), `align-content`/`justify-content` align the
  stacking range (`normal`, `stretch` and the distributions on the side where the stacking starts); the alignment container of an
  item is its margin box extended to the next item of its tracks (minus the gap), or for the last item of a track to the end of
  the stacking range (or of the content box); `normal` aligns the items with the side where the stacking starts (as in the
  browsers: the specification does not define it, `normal` behaves as `stretch` for the items of a grid; WPT
  `subgrid/*/independent-formatting-context.html` expect the stretch, more than a hundred other reftests do not), `stretch` lays
  them out again with the size of their alignment container, the baseline values take their fallback alignment. The scroll origin
  of a reversed axis is on its end side: with `track-reverse`, the tracks that overflow the start side are in the scrollable
  overflow (`ContentLayoutResult.InFlowStart`).
- **Baselines**: in a row container, as in a grid; in a column container, the highest first baseline of the first items of the
  tracks and the lowest last baseline of their last items (in each track, the items without a baseline are skipped: the first
  and last items that have one are used, as in Chrome and Firefox).
- **Subgrids** ([§3.2](https://drafts.csswg.org/css-grid-3/#subgrids)): a grid subgrid is only subgridded in the grid axis (its
  axis that corresponds to the stacking axis is a standalone axis of a grid, as in the browsers: WPT
  `grid-subgridded-to-grid-lanes/column/grid-lanes-subgrid-001a.html`; the specification converts such a subgrid to a grid lanes
  container); an auto-placed subgrid does not inherit the line names, its items contribute to every
  possible track of the grid ([§3.4.1](https://drafts.csswg.org/css-grid-3/#subgrid-item-contribution)), and it gets the tracks of
  its position once placed. Each track of an auto-placed subgrid (and of the subgrids in it) also contributes the extra margins
  an item would have in it (the edges of the subgrid, half the difference of its gutters and of the ones of the tracks), also
  when the subgrid is empty, as in Blink (`AddEdgePlaceholders` with `everyTrack`).
- **Grid lanes subgrids**: a grid lanes container that is an item of a grid or of a grid lanes container is a subgrid when the
  track list of its grid axis is `subgrid` and the corresponding axis of its parent is a grid axis (`HasSubgridTrackList`; never
  in its stacking axis, and not with a containment that establishes an independent formatting context). Its explicit grid is
  the tracks it spans (no implicit tracks: the positions and the spans of its items are clamped), with the gutters of the
  parent; its items get the extra margins of the edges and gutters of the subgrid for the tracks they are placed in
  (`SetLanePosition`), and the placement algorithm runs on the tracks given by the parent (`ApplyParentLaneTracks`; the tracks
  are sized as the ones of a standalone container while the parent did not size them). For the parent, the items with a definite
  position are sizing items of the tracks they span, and an item with an automatic position is a sizing item in every position
  of the subgrid (`AddLanesHoistedItems`, `GridSizingItem.IsHypothetical`: it takes no part in the baseline alignment of the
  parent) with the extra margins of the edge of the subgrid where they are the largest (both edges when it spans the subgrid).
  The containing block of the items in the stacking axis, while the parent measures them, is the content box of the subgrid
  when it fills a grid area of a known size.
- **Fragmentation in the stacking axis** ([§7.1](https://drafts.csswg.org/css-grid-3/#fragmentation): "Each grid axis track is
  fragmented independently in the stacking axis [...] A grid item that is pushed, however, is placed again by the next grid
  container fragment"): a container whose stacking axis is the block axis of a multi-column container (or of the pages) and
  that crosses the end of a column (a page) is laid out again in the strip with the block-end edges of the columns where it is
  (`GridLanesFragmentation`, `GridLanesBreaks`, `LayoutContext.GridLanesBreaks`: a second pass over the strip, as for the
  floats that are pushed, see [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation)
  and [Paged media](#paged-media-csslayoutpaged-layoutenginepagedcs)). The breaks are the ones of the fragmentation
  context: in the pages, the page values of `break-before`/`break-after` force a break, `avoid-page` avoids it and the
  scroll containers are not monolithic.
  The placement (`PlaceFragmentedLanesItems`) runs in each column: an item that does not fit in what remains of the column
  is pushed when it cannot be broken (monolithic, `break-inside: avoid`), when its tracks are full or closed, or when it has a
  forced break before it, unless it is at the start of the column; it is broken otherwise. As in Gecko, an item that is pushed
  or broken closes its tracks (no next item is placed in them in the column; also a forced break after an item). The pushed
  items are placed again after the position of the strip where the next column starts, where the running positions start
  again (after the rest of the broken items, without the gaps after the items that end in the previous column) and the
  auto-placement cursor points to the first line again; the fragmentation of the strip then breaks the items that cross the
  end of a column. Gecko places the items without the auto-placement cursor (the ties go to the first track): the references of
  WPT `css/css-break/grid/grid-lanes/grid-lanes-fragmentation-001.html` and `-002.html` differ where the cursor decides.
- Not done: the fragmentation in the stacking axis of a container in a parallel flow (an item of a flex or grid container,
  a box with a specified block size) or in a nested multi-column container, with dense packing or `fill-reverse` (the items
  are placed as in a container that is not fragmented), and the rest of a broken item that is taller than the part after
  the end of the column (its contents are pushed by their own breaks) overlaps the next item of its track; the baseline
  alignment of the items of a grid lanes subgrid with the items of its parent, the extra margins of an auto-placed subgrid
  nested in a grid lanes subgrid (the ones of its first position).

### Table layout (`Css/Layout/Table/`)

The tables ([CSS 2.1 §17](https://drafts.csswg.org/css2/#tables), [CSS Tables 3](https://drafts.csswg.org/css-tables-3/)) are
laid out by two formatting contexts: `TableWrapperFormattingContext` for the table wrapper box (the principal box) and
`TableFormattingContext` for the table grid box, both overriding `FormattingContext.Layout`. `TableLayoutAlgorithm` (one instance
per use, partial: `TableLayoutAlgorithm.cs` for the columns and the width, `TableLayoutAlgorithm.Rows.cs` for the rows, the cells
and the fragments) works in the logical axes of the table grid box: the columns are its inline axis. Where CSS Tables 3 is not
defined or the browsers differ from it, the algorithms follow the browsers (the TablesNG implementation of Chromium, which the
tentative WPT tests of `css/css-tables` describe). The rows, row groups, columns and column groups are never laid out on their
own (`TableFormattingContext` gives them an empty fragment); the cells are block containers (`display: table-cell` is
`flow-root` inside) laid out by the block and inline layouts in the sizes the table gives them.

- **Wrapper and grid box** ([§2.1](https://drafts.csswg.org/css-tables-3/#table-wrapper-box),
  [overrides](https://drafts.csswg.org/css-tables-3/#global-style-overrides)): both boxes have the style of the table; the
  wrapper box takes the margins, the positioning (`position`, `float`, the insets, the containing block of the absolutely
  positioned descendants: `PositionedLayout` skips the grid box), the transforms (`LayoutTransforms` and the fragments ignore
  the ones of the grid box); the grid box takes the sizes, the borders and the paddings, its margins are 0, and it clips for
  `overflow` (`hidden`/`clip` clip the rows and the cells at its padding box, inside the borders of the table, and not the
  captions; `auto`/`scroll` are ignored: a table is not a scroll container, as in the browsers,
  `ScrollContainers.GetUsedOverflow`). The background and the borders of the grid box are painted before the captions
  (`PaintOrder.PaintBlocks`), as in the browsers: a caption moved over the table by a negative margin covers its border. The fragment of the wrapper has no border nor padding, its inline size is the border-box inline size of the
  grid box, its block size the one of the captions (with their margins, which do not collapse) and of the grid. The percentages
  of the sizes of the grid box resolve against the containing block of the wrapper. The other layouts see a table as one box
  with the style of the table: the flex and grid layouts size the wrapper with the sizes, borders and paddings of the table
  (`FixedInlineSize`/`FixedBlockSize` are the border box of the grid, minus the captions in the block axis);
  `IntrinsicSizing.GetContribution` asks `TableWrapperFormattingContext.GetContribution` (a table is never narrower than its
  min-content size). The CSSOM uses the wrapper box (the `offset*`, `client*` (no border) and `scroll*` sizes and
  `getClientRects()` include the captions, as in the browsers), `getComputedStyle()` the grid box.
- **Structure** (`TableGridModel`, built once per layout pass with the collapsed borders and the column measures in
  `TableState`, the `LayoutState` of the grid box): the [HTML table processing model](https://html.spec.whatwg.org/multipage/tables.html#forming-a-table)
  on the boxes: the columns of the column groups (`span` of an HTML `colgroup` without `col` children, else of its `col`
  children) and of the columns, the row groups in the layout order (the first `table-header-group` first, the first
  `table-footer-group` last; the consecutive rows without group form a section without box), the cells placed in the free slots
  with the `colspan`/`rowspan` of an HTML `td`/`th` (only when the box is the `table-cell` box of such an element, clamped to
  1000/65534, `rowspan=0` grows to the end of its row group; the spans are clipped to the row group). A replaced element with a
  table-internal display is inline-level, one with `table`/`inline-table` a block (`BoxTreeBuilder`).
- **Collapsed borders** (`TableCollapsedBorders`, [CSS 2.1 §17.6.2.1](https://drafts.csswg.org/css2/#border-conflict-resolution)):
  each segment of the grid (a side of a slot) keeps the winning border of the table, the column groups, the columns, the row
  groups, the rows and the cells applied in that order (`hidden` wins, `none` loses, then the width, the style, the box type,
  and the box nearer to the start). A cell uses the half of the widest segment of each side (`ConstraintSpace.TableCellBorder`
  replaces its borders), the table the half of the widest segment of each of its sides (0 without slot); its paddings and the
  border spacing are 0. The segments are painted by increasing precedence; between the borders that only differ in color, the
  segment that starts nearer to the block-start, then to the inline-start, of the table covers the corner (as in Blink and
  WebKit: the corner has the color of the border of the cell before it). The column lines are positions in the inline axis of
  the table and the row lines in its block axis: the segments of a vertical table are painted the same way (a column line is
  a horizontal rule).
- **Column measures** ([§3.8](https://drafts.csswg.org/css-tables-3/#computing-cell-measures)): the outer min-content and
  max-content widths of the cells (their intrinsic sizes with their paddings and borders; a length `width` makes the cell
  constrained, its max-content width is the width; `max-width` limits both widths and `min-width` wins, as in the browsers; a
  percentage `width` is a percentage contribution; a cell whose `height` is a length gives a definite block size to its
  contents), the widths of the columns and column groups (a length makes the column constrained with that max-content width, the
  width of a `col` wins over the one of its `colgroup`, 0% is ignored); the cells of span 1, then the spanning cells by increasing
  span distribute their widths with the width distribution algorithm (their percentage in excess to the other columns in
  proportion to their max-content widths; the max-content width of an unconstrained cell does not grow the constrained columns).
  A column where no cell originates and whose width is `auto` or 0 is merged
  ([track merging](https://drafts.csswg.org/css-tables-3/#dimensioning-the-row-column-grid--step2), automatic layout only): it
  takes no width and no border spacing. In fixed mode (`table-layout: fixed` with a width that is not `auto`/`max-content`),
  only the columns and the cells of the first row count (lengths, percentages with the borders and paddings of a
  `content-box` cell, as in the browsers).
- **Table width** ([§3.9.1](https://drafts.csswg.org/css-tables-3/#computing-the-table-width), `ComputeTableInlineSize`): GRIDMIN
  and GRIDMAX are the sums of the column widths plus the border spacing, the borders and the paddings; the used width is the
  resolved `width` (or the size transferred by `aspect-ratio` from a definite height), else the fit-content size in the available
  space, clamped by `max-width`, at least the used min-width (`min-width`, GRIDMIN, CAPMIN: the min-content contributions of the
  captions). For an `auto` width, a percentage column of P% whose max-content width is M needs a table of M / P%, and the other
  columns their max-content width in the remaining percentage (1,000,000px when no percentage remains), as in the browsers; not
  for the intrinsic size keywords, nor for the contributions of a table in the flow of a table cell or in a flex or grid
  container (the flex and grid layouts cannot use the "infinite" size, as in the browsers). A fixed table with a
  percentage width has an "infinite" max-content contribution (1,000,000px). A grid item with an aspect ratio and a `normal`
  alignment fills its area.
- **Width distribution** ([§3.9.3](https://drafts.csswg.org/css-tables-3/#distribution-algorithm), `DistributeAuto`): the
  min-content, min-content-percentage, min-content-specified and max-content guesses, interpolated between the two guesses
  that bound the width; above the max-content guess, the excess goes to the auto columns (in proportion to their max-content
  widths), else to the constrained ones, else to the percentage ones. In fixed mode (`DistributeFixed`): the constrained columns
  get their width, the percentage ones their percentage (scaled down to the remaining width), the auto ones share the rest
  equally; the excess goes to the auto columns, else in proportion to the widths, else to the percentages, else equally.
- **Rows** ([§3.10](https://drafts.csswg.org/css-tables-3/#row-layout)): the cells are laid out in the width of their columns
  with an automatic block size (`ConstraintSpace.MeasuresIntrinsicBlockSize`); the block size of a cell is the size of its
  contents (`BoxFragment.IntrinsicBlockSize`; the contents of a scroll container with a specified height do not count), at least
  its `height` (border box in quirks mode, the [table cell height box sizing quirk](https://quirks.spec.whatwg.org/#the-table-cell-height-box-sizing-quirk);
  `min-height`/`max-height` do not apply to the cells, as in the browsers); a row is as tall as its fixed `height`, its cells of
  span 1 and its baseline-aligned cells (the largest ascent plus the largest descent of their contents). The cells that span
  several rows are distributed by their last row, then their first row (the enclosed ones first), then their size (the taller
  first) with `DistributeExcessBlockSize`: the percentage rows grow to their percentage (when it resolves), then the rows where
  another spanning cell originates, else the non-empty rows that are not constrained (in proportion to their sizes), else the
  empty rows (the last one for a spanning cell), else the constrained rows (the algorithm of the browsers, csswg-drafts#4418).
- **Table height** ([§3.10.1](https://drafts.csswg.org/css-tables-3/#computing-the-table-height)): the rows, the row groups
  (their fixed `height` is a minimum, distributed to their rows) and the border spacing; the `height` (and `min-height`) of the
  table, the block size imposed by the parent and the size transferred by `aspect-ratio` are minimums: the excess goes to the
  percentage row groups (up to their percentage), then to the `auto` row groups, else the fixed ones, else the percentage ones
  (the body groups first), then from each row group to its rows (`DistributeTableBlockSize`). A table without column has no row
  border spacing unless its height is distributed. The rows with `visibility: collapse` (or in such a row group) are sized as the
  others then removed: they take no space, the table is shorter and the cells that span them shrink; the collapsed columns take
  no space (the visible columns get the table width).
- **Cells**: a cell is laid out again with the block size of its rows (`FixedBlockSize`; the percentages of its children
  resolve against it when it is "considered to have its height specified explicitly": a length `height`, or a table whose
  `height` is a length or a percentage that resolves, else `IsFixedBlockSizeIndefinite`), unless its first layout has that size; its contents
  are then aligned (`BoxFragment.MoveInFlowChildren`): `top`, `middle` (`center`), `bottom`, or `baseline` (the other values;
  the contents that overflow the cell are only moved by an `unsafe` `align-content`),
  the baseline of a cell being the one of its first line box or row, else the bottom of its content box (of its contents or
  of its specified height; the middle of its content box when the dominant baseline of a vertical table is central); a cell without content does not take part in the baseline alignment of its row (as in the
  browsers, it does not move the contents of the other cells); the row baselines come from the final layouts. The margins of the cells are 0 (`FormattingContext.Layout`); the percentages of their paddings resolve
  against the assignable width of the table plus the border spacing between its columns (as in Blink and Gecko). The
  `height` of a cell is a minimum whatever its `overflow`: its contents make it taller. In the first layout of a cell, the
  percentage block sizes of its scroll container children resolve against 0 when the cell is restricted (the `height` of
  the table is not `auto`, or the one of its row group, its row or the cell is a length: `ConstraintSpace.IsRestrictedTableCell`),
  else they behave as `auto` (as in the browsers). The percentage block sizes of the replaced children (an image, a canvas,
  a video, an embedded document; also in the anonymous block boxes of the cell) of a cell whose `height` is a length resolve
  against that height in the two layouts of the cell (`ConstraintSpace.ReplacedPercentageBlockSize`), not against the block
  size that the row gives to the cell: as in the browsers, such an element keeps its size between the two layouts and does
  not make the row taller than the height of the cell asks ([css-tables-3](https://drafts.csswg.org/css-tables-3/#row-layout)
  treats these percentages as `auto` in the first layout).
- **Baselines**: the first (last) baseline of the table is the baseline of its first (last) row (the largest ascent of its
  baseline-aligned cells that have content, else the bottom content edge of its cells in the row (the middle of their content
  box for the central baseline of a vertical table), else its block-start edge for a row without cell); a
  table without row has none. An `inline-table` is aligned by its first baseline, the flex and grid layouts use both; a
  block-level table gives no last baseline to an `inline-block` whose `baseline-source` is `auto` (the baseline of an
  `inline-block` is its last line box), as in the browsers; it gives the baseline of its last row for `baseline-source: last`
  and for the last-baseline alignment of its container.
- **Fragments**: the grid fragment contains the fragments of the columns and column groups (their position in the grid, for the
  CSSOM; not hit tested), then of the row groups (the rows without group are its children), the rows and the cells (children of
  the row they originate in). The rows, the row groups, the columns, the column groups and the grid box are not hit tested (the
  wrapper and the cells are, as in the browsers, `HitTesting`). The relatively positioned rows and row groups are offset and are
  the containing block of their absolutely positioned descendants; their percentage insets in the block axis (and the ones of
  the cells) resolve against the specified block size of their parent part when it is a length (a row group in the table, a
  row in its row group, a cell in its row), not against its used block size, and behave as `auto` otherwise (as in the
  browsers); a sticky cell, row or row group is constrained by the table (csswg-drafts#5020). The contents of a cell aligned
  by `vertical-align` move the absolutely positioned boxes of the cell that are at their static position
  (`BoxFragment.MoveInFlowChildren`).
- **Quirks**: the [table cell width calculation quirk](https://quirks.spec.whatwg.org/#the-table-cell-width-calculation-quirk)
  (no soft wrap opportunity around the `img` elements for the min-content width of a cell whose `width` is `auto`, in the inline
  layout), the [nowrap minimum width quirk](https://quirks.spec.whatwg.org/#the-table-cell-nowrap-minimum-width-calculation-quirk)
  (a cell with `nowrap` and a fixed `width` is at least that wide), the [percentage height quirk](https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk)
  of the tables and the cell height box sizing quirk. The HTML attributes are presentational hints (`PresentationalHints`,
  the `width` of `colgroup` is mapped like the one of `col`, as in the browsers).
- A rendered legend of a fieldset is blockified (`BoxTreeBuilder`, [HTML](https://html.spec.whatwg.org/multipage/rendering.html#rendered-legend));
  see *Fieldsets* in the block formatting context.
- Not done: the second layout of the percentage heights of
  the scroll containers of the cells of a restricted cell (they stay 0 when the cell is not "explicit"), the `fit-content`/`-webkit-fill-available` widths of the
  fixed tables, the baseline alignment of the orthogonal cells (their contents are aligned in the block axis of the cell, in the
  size of its columns: `top` is its block-start edge, `bottom` its block-end edge). The cells of a row of a fragmented table
  are fragmented as parallel flows, and its header and footer are repeated in its fragments, see
  [Multi-column layout and fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation).

### Multi-column layout and fragmentation (`Css/Layout/Multicol/`, `Css/Layout/Fragmentation/`)

The multi-column containers ([CSS Multicol 1](https://drafts.csswg.org/css-multicol-1/) and the parts of
[CSS Multicol 2](https://drafts.csswg.org/css-multicol-2/) that browsers ship or prototype) are laid out by
`MultiColumnFormattingContext` (`FormattingContext.For` dispatches the block containers whose `column-count`, `column-width` or
`column-height` is not `auto`, not the table wrappers). The block fragmentation of [CSS Fragmentation 3](https://drafts.csswg.org/css-break-3/)
happens in its columns (the page break values do not break the columns); the same fragmentation paginates the printed
documents (see [Paged media](#paged-media-csslayoutpaged-layoutenginepagedcs)).

- **Columns**: the used count and width come from the [pseudo-algorithm](https://drafts.csswg.org/css-multicol-1/#pseudo-algorithm)
  (`MultiColumnFormattingContext.GetColumns`, at most 1000 columns, `column-gap: normal` is 1em); the column width is floored
  to 1/64 px and the columns are placed at multiples of the exact stride rounded to 1/64 px, as in Blink (the fractional
  widths and gaps do not accumulate errors).
- **One strip, then fragmentation** (the approach of the flow threads of WebKit, not the break tokens of Blink): the content is
  laid out by the block or inline layout of the container (`FormattingContext.LayoutFlow`) in a strip of the column width
  (an independent block formatting context, `ContentArea.MultiColumnFlow`), without knowing the fragmentainers, then the
  breaks are chosen and the fragments are distributed. The behaviors that depend on the fragmentainers (the specifications
  and Blink lay out the content again in each fragmentainer) are **second passes over the strip**, not a layout per
  fragmentainer with break tokens: the breaker gives each parallel flow its own breaks and places the block-end edges of the
  boxes whose block size does not follow their contents; the nested multi-column containers are laid out again in their
  rows; the strip is laid out again when floats are pushed to the next fragmentainer (see *Floats* below) and when the items
  of a grid lanes container are placed again in the fragmentainers (see [Grid lanes](#grid-lanes-csslayoutgridgridlayoutalgorithmlanescs)); the slicer adds
  the repeated table headers and footers. This choice keeps the layout of the content that is not fragmented untouched (the
  formatting contexts do not carry break tokens nor constraint spaces of fragmentainers: nothing is slower when nothing
  breaks, and a strip without float to push is laid out once), and every formatting context is fragmented by the same code;
  a layout per fragmentainer would need each formatting context (block, inline, flex, grid, table, positioned) to resume
  from a break token. The fragmentainers of another inline size (the pages of another size) start a strip of their own
  (a targeted relayout of the whole strip from the box after the break, see [Paged media](#paged-media-csslayoutpaged-layoutenginepagedcs)).
  The few layouts that depend on the fragmentainers are **targeted relayouts** of the boxes concerned (`StripRelayout`): the
  layout of the strip records them (`LayoutContext.StripRelayout`, set while the strip of the outermost fragmentation context
  is laid out), the fragmentation finds the ones that are broken, and the strip is laid out again with explicit inputs for
  these boxes only (the other boxes are laid out the same way), then fragmented again, until the inputs are stable:
  - a box that avoids the floats gets, as in Blink, the inline size and the position of the layout opportunity beside the
    floats of each fragmentainer ([§3.3](https://drafts.csswg.org/css-break-3/#varying-size-boxes)): it is placed in the
    opportunity of its first fragment (the band of the floats ends at the first break inside it), and laid out again in the
    opportunity of each next fragment whose opportunity differs; its fragment in the strip gets the children of each layout
    that start in the corresponding fragment (`BoxFragment.ReplaceInFlowChildren`), and the slicer gives its fragments the
    inline size and the position of their layout (`InlineVariant`). The breaks are the ones of the strip: the content of a
    fragment is the content of its layout from the position of the break (a content that reflows in another inline size is
    not resumed from the break as with a break token; as in Blink, the size of a query container is the size of its first
    fragment: these layouts do not change it, `LayoutContext.LaysOutNextFragments`). A box that does not fit beside the
    floats of a fragment, or that has no space beside them, continues below them (see *Floats*);
  - the block-end margins of the last children of a box whose block size is limited, when they are in the contents that
    overflow the box after the end of the fragmentainer where the box ends (a parallel flow), do not collapse through the box
    (as in Blink);
  - the contents of a block container aligned by `align-content` that is broken are not aligned (the alignment of
    fragmented contents is not defined by [CSS Box Alignment](https://drafts.csswg.org/css-align-3/#distribution-block); the
    WPT tests accept this behavior or an alignment in the space that remains after the fragmentation). The floats are then
    pushed again from the start.
  The relayouts are only done when a recorded box is broken: nothing is recorded in a strip without float beside a box that
  avoids them, without `align-content`, and without contents that overflow a limited block size.
  Its limit is what cannot be expressed as a change of the strip: the lines and the items of a flex container aligned again
  in each fragment, see *Not done*. `FragmentationFlow.Collect` walks the fragments of
  the strip and lists the possible breaks ([§4.1](https://drafts.csswg.org/css-break-3/#possible-breaks), in the logical block
  axis of the container): between the in-flow siblings that follow each other (class A), between the line boxes (class B, with
  `orphans`/`widows`), the last-resort breaks after the block-start border and padding of a box and before its block-end ones
  (class C), and the ranges of the empty block size of a box after its in-flow children (a box with a fixed block size breaks
  anywhere in it, as in the browsers; not at the start of an in-flow box without children: no class C break without a gap; the
  gap between the content edge of a box and its first child that is not at the start of the box, aligned or placed in a track,
  breaks anywhere: class C). The anonymous boxes without block size (the empty line boxes around a block-level box in an inline
  box) are not content: there is no break opportunity before or after them. The rendered legend of a fieldset is monolithic
  and there is no break between the block-start edge of the fieldset and it. A
  break is forced by `break-before`/`break-after: column | always | all` (propagated from the first/last in-flow children through
  the block containers without borders and paddings, and, as in Blink, from the items of the first/last line or track of a
  flex or grid container and from the first/last row of a table whatever its borders and paddings; not out of a box with layout containment), violates the rules (`BreakAppeal.Avoid`)
  with `avoid`/`avoid-column`, `break-inside: avoid` on an ancestor, `orphans` and `widows` (`LineBreakInfo`: as in Blink, the
  line boxes before a break are counted in the fragmentainer, the fragment of the block container; when no break has
  `orphans` line boxes before it and `widows` after it, the first break with enough line boxes before it is taken); the
  line boxes and the monolithic boxes (replaced, scroll containers, size containment in the block axis, another writing
  mode, nested multi-column containers) are unbreakable intervals: a break inside one of them is not valid (e.g. between the lines
  beside a float), and the content before a break ends after the unbreakable intervals that start before it. The content that
  overflows a box that clips it (`overflow: clip`) has no possible break (it stays in the last fragment of the box).
- **Breaker** (`ColumnBreaker.FindBreaks`, [§4.4](https://drafts.csswg.org/css-break-3/#best-breaks)): each column ends at a forced
  break (also when the rest of the content fits in the column), else at the latest possible break whose content fits (the
  perfect ones first, then the ones that violate `orphans`/`widows` or an `avoid` value); when nothing fits, the column
  overflows and the next one starts at the next possible break. The columns are ranges of positions of the strip, not
  multiples of the column height (the margins at an unforced break are truncated because the next column starts at the border
  box after the break; the ones after a forced break are kept; a column may end in the margins between two boxes when the
  content before them fits). A break at the end of the empty block size of an in-flow box is
  the break after the box: it is not possible when the flow has a class A or C break there (which has its own rules, e.g.
  `break-before: avoid` on the next sibling); a break that violates no rule but is beside a box with `break-inside: avoid`
  (overflowing content, a float) violates that rule. As in Blink:
  - When only breaks that violate a rule fit, the ones inside fewer boxes with `break-inside: avoid` are preferred
    (`BreakCandidate.AvoidDepth`; as in the browsers, a break outside of the innermost avoided box).
  - Several breaks may be at the same position (before and after a box without block size): the candidates and such boxes are
    ordered as in the content (`BreakCandidate.Sequence`, `FragmentationFlow.GetEmptyOrdinal`), the latest break that fits is
    taken, and a box without block size at the end of a fragmentainer stays in it (`FlowMap.MapEmpty`). The break after
    the content of a flow whose parallel flows continue is after the boxes without block size at its position: the contents
    that overflow them (a box with `height: 0`) start in that fragmentainer and are broken there.
  - A forced break at the start of a fragmentainer is taken when it is at a class A break point (after a box without block
    size), not before the first content of a box.
  - The block-end margin of the last child of a box (in a block formatting context root, before a block-end padding) is
    truncated when a break is in it or after it ([§5.2](https://drafts.csswg.org/css-break-3/#break-margins)): the box ends at
    the end of the fragmentainer (`FlowMap.AddTruncation`) and the content after it starts the next one.
  - A box that follows, with a negative margin, a sibling that is broken is placed in the fragmentainer where its sibling
    ends, at a negative offset (`FragmentSlicer`: the fragmentainer of a box is not before the one where its previous in-flow
    sibling ends).
  - The first content of a box is not at a possible break, but a box that starts at or after the end of the fragmentainer
    (its block-start margin does not fit) is pushed to the next one and the margin is truncated
    (`BreakCandidate.IsPastEndOnly`: a last-resort break, taken when no other break fits; e.g. a margin taller than the
    fragmentainer; the balanced columns are stretched until they contain the box, `BreakCandidate.BoxEnd`).
- **Parallel flows** ([§4.1](https://drafts.csswg.org/css-break-3/#parallel-flows),
  [CSS Flexbox §12](https://drafts.csswg.org/css-flexbox-1/#pagination), [CSS Grid §12](https://drafts.csswg.org/css-grid-2/#pagination)):
  the items of a line of a row flex container, the items of a grid, the cells of a table row, the lines of a multi-line
  column flex container and the items of a single-line column flex container each have their own `FragmentationFlow`
  (`ParallelGroup`/`ParallelItem`, built from the state of the layout of the container: `FlexContainerState`,
  `GridContainerState.ItemRows`, the fragments of the table rows). The specifications leave the details open: the behavior
  is the one of Blink (LayoutNG block fragmentation), which the tests of css-break expect.
  - The breaker advances one cursor per flow, fragmentainer after fragmentainer (`FlowCursor.Advance`). The parallel flows
    of a track (a line, a row) start in the fragmentainer where the flow of their container reaches them, break
    independently at its end (each one at its own latest possible break), and resume at the start of the next fragmentainer:
    the result is one `FlowMap` per flow (the fragmentainer and the offset of each position of the strip), which the slicer
    uses for the fragments of the flow.
  - The block-end edge of an item follows its contents when its block size is automatic (the item grows when its contents
    are pushed by its breaks; a stretched item is at least as tall as its track), and is placed by the flow of the container
    when its block size is definite (a specified block size, an item stretched in a single-line flex container or a grid
    with a definite block size, a column flex item with a definite flex basis and block size, a stretched item of a grid
    whose rows have a fixed track sizing function, `GridItemRows.HasFixedRows`: as in Blink, a row that is neither
    intrinsic nor flexible does not grow with its items): its contents then overflow it
    in the next fragmentainers. As in Blink, an item of a single-line column flex container that is shorter than its
    contents in the container is as tall as them once the container is in a fragmentation context, when it cannot shrink and
    its flex basis is content-based, or when its block size is automatic and it cannot shrink or its minimum block size is
    automatic (`ParallelItem.EncompassesContents`; the slicer creates the fragment of a box that grows without being
    broken again). A track ends after its items (with their block-end margins): when it grows, the content of
    the flow after it (the next tracks, the end of the container, its next siblings) is shifted, unless the block size of the
    container is definite.
  - The possible breaks of the flow of the container are the breaks between the tracks (class A; the `break-before` and
    `break-after` values of the items of a track are propagated to it, and from the first and last tracks to the container)
    and a break inside a track at the end of the fragmentainer, whose appeal is the worst one of the breaks of its items (an
    item with `break-inside: avoid` that is broken, monolithic content that overflows; not the `orphans`/`widows` of an item,
    as in Blink): a line or a row is pushed to the next fragmentainer when that violates fewer rules. An item that is offset
    in its track (`align-self: end`, a margin) and does not fit is pushed alone; in a grid, only when it has no possible
    break (as in Blink, a break before the first content of a row is not a possible break: the item is rather broken
    between line boxes that violate `orphans` and `widows`, `ParallelItem.BreaksBeforeAsLastResort`). An item that starts
    before its track (a negative margin) starts with the track, at a negative offset when the track starts a fragmentainer.
    A monolithic item that overflows the fragmentainer ends its track after it (the flow does not break beside it; the items
    whose block size follows the track end with it), unless another item of the track continues in the next fragmentainer
    (its contents, or its definite block size, which is broken at the end of the fragmentainer): the track is then broken
    at the end of the fragmentainer, beside the item that overflows. The fragments of a parallel flow that are broken end
    where that flow is broken (`FlowMap.GetBreakEnd`), not where the unbreakable content of another flow that overflows the
    fragmentainer ends. A forced break between two tracks is taken beside (and after) the monolithic item of the first
    one that overflows the fragmentainer.
  - The items of a column flex container keep their offsets after a break (their margins are breakable anywhere and are not
    truncated), but the gaps between them (`gap`, the space of `justify-content`) disappear at the breaks, also at the
    forced ones. The lines of a
    multi-line column container are parallel flows; each item of a single-line one is a track. As in Blink: the margin
    after a forced break is preserved and breaks anywhere (an item that starts at the end of the fragmentainer after it is
    pushed to the next one); the first item of a line is separated from the start of the line by its block-start margin
    (which breaks anywhere; a forced break before the item is taken there, and the margin is preserved after it); the
    `break-before` values of the first item of each line and the `break-after` values of the last one are propagated to the
    container; a forced break before an item that overlaps the previous one (a negative margin) is taken after the
    previous item, and the item starts the next fragmentainer at the offset its margin gives it, before the start of the
    fragmentainer (`FragmentationFlow.GetForcedStart`: its position in the strip is before the break).
  - **Boxes whose block size does not follow their contents** (a specified block size, a box as tall as its `max-height`,
    an item of a grid lanes container whose lanes are rows that is shorter than its contents, as a grid item in a fixed row;
    also the flex and grid containers, and the anonymous content box of a fieldset that is one; not the ones that contain a
    column spanner): "content overflowing the content edge of a fixed-size box is considered parallel to the content after the
    fixed-size box". The box is a track of one item in the flow of its container (`FragmentationFlow.TryAddDefiniteBox`,
    `ParallelItem.IsDefiniteBox`): its block-end edge is placed by that flow (a box that is broken uses the block size of the
    fragmentainers, whatever the breaks of its contents: the block size it has left is what its fragments did not consume),
    and its contents have their own flow, which is broken at the end of each fragmentainer and continues after the end of the
    box; the flow of the container breaks beside the contents that overflow the box. The box extends over monolithic contents
    that overflow the fragmentainer, up to its block size. As in Blink, its block-end border and padding (when it does not
    repeat them) are not broken: the box breaks before them (its fragment ends there) when its content progresses, and its
    first fragment contains its block-start border and padding. When the box fits in the fragmentainer, its first content is placed
    with it even when it is monolithic and overflows the fragmentainer
    ([csswg-drafts#6056](https://github.com/w3c/csswg-drafts/issues/6056), `ParallelItem.LeadingMonolithicEnd`: the box is not
    pushed because of its first line or monolithic child; also for the items with a definite block size). A box with
    `box-decoration-break: clone` repeats its edges at the breaks of its border box only (`ParallelGroup.OwnCloneStart`,
    as in Blink): the repeated edges are added to a block size of the content box (the flow of the container breaks in
    the content box, before the room of the block-end edges, and continues after the block-start edges in the next
    fragmentainer) and taken from a block size of the border box (`ParallelGroup.ShrinksAtBreaks`: the flow skips their
    block size after each break, `BreakCandidate.SkipAfter`); the contents that overflow the box are placed over its
    block-end edges in its last fragment (they are not repeated edges) and without any edge in the next fragmentainers
    (`ParallelGroup.HasEnded`, `BreakCandidate.CloneOwner`: the possible breaks of the contents, also the ones of the
    floats that are in the flow of the block formatting context, lose the edges of the boxes that have ended). The floats
    of the contents that overflow such a box are not content of the strip (the strip is as tall as its floats, but they
    are in the parallel flow). The `orphans` and
    `widows` of these contents lower the appeal of the breaks of the container. The parallel flows of an item start again
    (a new generation of the breaker) when the item is pushed by a break before it.
  - **Absolutely positioned boxes** whose containing block is in the strip (`FragmentationFlow.OutOfFlowItems`,
    `ColumnBreaker.PlaceOutOfFlow`; the boxes whose containing block is the container or an ancestor only have their static
    position in the columns): they do not take part in the fragmentation of the in-flow content. Each one is a flow of its
    own, placed once the in-flow content is, in the sequence of the fragmentainers (across the column spanners; a row before a
    spanner has all its columns for them): from the offset of the box from the block-start edge of its containing block in
    that sequence (the fragmentainer where the containing block starts, a next one when the offset is larger than what
    remains of it, a previous one when it is negative: the box is then a child of an empty fragment of its containing block,
    which does not clip), or from the block-end edge of the fragments of the containing block stitched together (`bottom`
    without `top`, an aligned static position in a flex or grid container: `OutOfFlowPlacement.EndRatio`), or from a
    position of the content (the static position, a grid area: it follows the breaks of the content). The breaks are the
    ones of the flow that contains the contents of the containing block (`OutOfFlowPlacement.OwnerFlow`: the strip, or the
    parallel flow of the contents of a box with a specified block size, of an item, of a line of a column flex container
    or of an absolutely positioned box: the owner of a flow is known while its contents are visited). A box stretched
    between its two insets (`top` and `bottom`, automatic block size) is as tall as the fragments of its containing block
    stitched together (`OutOfFlowPlacement.IsStretched`: the containing block of the strip includes the column spanners
    that interrupt it). The contents of the
    box are then broken in the fragmentainers that follow; a specified block size does not follow them (the box uses what
    remains of each fragmentainer), a box whose block size is automatic grows when its contents are pushed. The contents of a box that
    does not start at the start of a fragmentainer are pushed to the next one when they cannot be broken without violating a
    rule (a first child with `break-inside: avoid`, a first line that does not fit) while the box starts at its position,
    unless the box or its containing block has a specified block size and fits. The boxes that end after the in-flow content
    add columns to the last row. The block-start margin of a box at its static position in a block container is applied in
    the fragmentainer of the static position (a negative margin does not move the box to the previous one). The static
    position of a box whose containing block is outside of the strip is mapped with
    the flow that contains it (`OutOfFlowCandidate.StaticPositionContainer`). As in Blink, the boxes are not placed
    between the edges that the boxes around them repeat (`box-decoration-break: clone`: a box starts at the padding edge
    of its containing block and its fragments start at the start of the next fragmentainers). A box whose containing
    block is a relatively positioned inline box is placed from its position before the relative positioning (the offset
    of its containing block is the relative offset of its fragment, `PositionedLayout.GetInlineContainingBlockRect`), as
    the other boxes are. The stacking contexts split by the
    fragmentation paint the layers they adopt with their last fragment (`PaintOrder`). The boxes of a nested multi-column
    container are placed the same way in the sequence of its inner columns (see *Nested multi-column containers*).
- **Floats** ([§4.1](https://drafts.csswg.org/css-break-3/#parallel-flows): a float is a parallel flow), as in Blink
  (`FloatPusher`, `FloatPush`, `FragmentationFlow.AddFloat`). The floats are fragmented with the flow of their containing
  block, but a float is pushed alone to the next fragmentainer when it does not fit in what remains of the current one:
  - A float is pushed when it is not at the start of the fragmentainer (the block-start edges repeated by
    `box-decoration-break: clone` do not count) and it is monolithic or has `break-inside: avoid`, or its first content (a
    line box, a monolithic box) does not fit, or its border box fits but not its block-end margin (the margin of a float is
    not truncated, [csswg-drafts#9191](https://github.com/w3c/csswg-drafts/issues/9191)). As in Gecko, a float with a forced
    break before it (`break-before`, [CSS 2 §13.3.1](https://drafts.csswg.org/css2/#page-break-props)) is also pushed when it
    is not at the start of the fragmentainer. The floats inside a float that is pushed are not pushed in the same pass (they
    move with it).
  - The strip is then laid out again with the float placed at or below the position of the strip where the next
    fragmentainer starts (`LayoutContext.FloatPushes`; the positions of the strip are converted to the block formatting
    context of the float with `LayoutContext.FloatPlacements`): the line boxes and the boxes after the float that are still
    in the previous fragmentainer are laid out without it (`Exclusion.VisibleFrom`), the next floats are not above it and
    the boxes that clear it follow it. The flow may break before a pushed float, and a break in the clearance after a
    float is after the float. The fragmentation is done again, and the floats of the next fragmentainers are pushed in the
    next passes (the floats of one fragmentainer per pass, 12 passes at most; a pushed float follows the start of its
    fragmentainer when the breaks before it change). A strip whose floats all fit is laid out once.
  - A box that avoids the floats (it establishes a formatting context) and starts before a pushed float is laid out
    without it in its first fragment; its part in the fragmentainer of the float is laid out beside the float when it fits
    there (see *One strip, then fragmentation*), else it starts below the float: the breaker shifts the content of the
    flow at the start of that fragmentainer (`FlowMap.AddStartShift`, also in the next fragmentainers while the float
    continues; the content is pushed to the next fragmentainer when nothing fits below the float), and the float is
    fragmented apart, by the block size of the fragmentainers (`FragmentationFlow.GetFloatAvoider`).
  - A float in the contents that overflow a box (a parallel flow) that is reached in a later fragmentainer than the box
    after it is not cleared by that box (`FloatPush.IsDeferred`, found from the spaces of the strip that are not margins,
    `FragmentationFlow.ClearanceGaps`).
  - The columns are balanced as if the flow could break before the floats (`FragmentationFlow.BreaksBeforeFloats`); the
    floats that do not fit in the balanced columns are then pushed.
  - A break in the empty block size of a float at a position where the in-flow content has a possible break is that break
    (between two line boxes: with their `orphans` and `widows`).
  - As in Blink, a float that continues in a fragmentainer after a break is placed again at its start: its fragment there is
    moved toward its side, beside the floats of the same side that also continue there before it (the floats that ended no
    longer push it; the content beside it is not laid out again).
- **Repeated table headers and footers** ([CSS Tables 3](https://drafts.csswg.org/css-tables-3/#repeated-headers), as in
  Blink in the columns, the only browser that implements them there; `RepeatedSections`): the `table-header-group` and the
  `table-footer-group` of a table that have `break-inside: avoid` and are not taller than a quarter of the fragmentainer
  where the table starts (decided by the breaker when it reaches the table) are repeated in each fragment of the table grid
  box. In the pages, the header and the footer groups are repeated whatever their `break-inside` ("When rendering the
  document into a paged media, user agents must repeat header rows and footer rows on each page spanned by a table", as in
  Gecko) when they are not taller than a quarter of the page area of the first page of the strip (Blink repeats them in
  the pages with `break-inside: avoid` only). The breaker
  reserves their block size (with the border spacing between them and the other sections) at the start and at the end of
  the fragmentainers where the table continues, as it does for the edges of `box-decoration-break: clone`; the columns are
  balanced with them. There is no break between the header and the section after it. The slicer adds a copy of the
  fragments of the header (`Fragment.CloneSubtree`) at the start of the fragments after the first one that have other
  sections, and a copy of the footer in the fragments before the one of the footer: at the end of the fragmentainer when
  a row is broken there, after the last row when the break is between two rows. The copies are fragments of the same
  boxes (they are painted and hit tested; `getClientRects()` returns the rectangles of the original ones).
- **Collapsed borders of a fragmented table**: each fragment of the table grid box paints the collapsed borders of the
  fragments of its cells; the borders along a cell that is broken run to the edge of its fragment, no border is painted
  across a broken cell, and a border on a grid line that is at a break is half in each fragment.
- **Balancing** (`ColumnBreaker.Balance`, [column-fill](https://drafts.csswg.org/css-multicol-1/#cf)), as in Blink: the initial column
  height distributes the content runs between the forced breaks into the columns (at least the tallest unbreakable interval),
  then the columns are stretched by the minimal space shortage until the content fits in the column count without violating a
  rule (or the available block size is reached). A break past the end of a column whose content before it fits (a break inside a
  box before its block-end padding and border) does not give the shortage: the next breaks, or the end of the content, do. The column heights are rounded up to 1/64 px. `column-fill: auto` in a container
  whose block size is definite or limited (`max-height`: the container is then as tall as its content) fills the columns
  sequentially (the first column gets all the content of a container without block size, see below); the rows before a
  spanner are always balanced. A column has a block size of 1px at least for the fragmentation
  of its content (a container without block size makes columns of 1px, as in Blink). The content that does not fit creates
  overflow columns in the inline direction. `column-fill: auto` in a container without block size puts the content in the
  first column (and in the ones after the forced breaks), as tall as its content (a container in the strip of another
  fragmentation context is laid out there with balanced columns: the outer context fragments it). As in Blink, the
  absolutely positioned boxes are content of the balanced columns: the ones that start in the columns (and whose containing
  block is not interrupted by the spanner after them) count in the initial column height, with their boxes with
  `break-inside: avoid` as unbreakable content, and the last columns are stretched until the boxes fit in them without
  violating a rule (`OutOfFlowFragmentainers`).
- **Rows** ([`column-height`, `column-wrap`](https://drafts.csswg.org/css-multicol-2/#ch), as in Blink): with `column-wrap: wrap`
  (the used `column-wrap: auto` is `wrap` when `column-height` is set) the columns that do not fit in the column count are in
  new rows separated by `row-gap`. When the block size of the rows is known (`column-height`, or the definite block size of a
  container whose columns wrap), the rows are at fixed positions (`FragmentainerSizes`: one block size per row): the content
  fills what remains of the current row, then the next rows; the content of each row is balanced in the row (the last row, the
  rows shortened by forced breaks) unless `column-fill` is `auto`, and the last row of the container keeps its whole block size.
  A spanner is placed in the row where the content before it ends when it fits in what remains of the row, else at the start
  of the next row; the content after it continues in the row where the spanner ends. Without a block size for the rows, the
  columns wrap at the forced breaks only (every `column-count` forced breaks), and each row is as tall as its balanced content.
- **Column spanners** (`column-span: all`, [§6](https://drafts.csswg.org/css-multicol-1/#column-span)): the in-flow block-level boxes
  that participate in the block formatting context of the container (`BlockFormattingContextState.MultiColumnFlow`) are laid out
  in its content box where they are met (`BlockFormattingContext.LayoutColumnSpanner`: a new block formatting context); they
  divide the strip into rows of columns (multicol lines), each balanced separately, and are placed between them. As in Blink, a
  box is not a spanner when an ancestor inside the container is a containing block for the fixed positioned boxes (a
  transform, a filter...: `MultiColumnFlow.IsValidColumnSpanner`).
  - A spanner ends the row of the content before it: it is placed below the floats and below the content that overflows the
    boxes before it (`MultiColumnFlow.GetOverflowEnd`: the boxes shorter than their contents record where their contents end).
  - Margins: the margins of a spanner do not collapse with the margins of the content of the columns (its block-end margin is
    part of its extent in the strip, so the margin of the content after it is kept, like after a forced break; a negative
    margin moves that content above its row). The margins of two adjacent sibling spanners collapse
    (`MultiColumnFormattingContext.GetSpannerExtent`), and the margin of a spanner collapses with the margin of its parent when
    it is its first content (not with the margin of a spanner before the parent, as in the browsers).
  - A box whose block size does not follow its contents (a specified or limited block size) is interrupted by the spanners it
    contains: it continues after them with the block size that remains (`MultiColumnFlow.ExtendBlockSize`: its extent in the
    strip includes the spanners; the slicer leaves the extent of the spanners out of its fragments). When it ends before a
    spanner of its contents, the content after the box is still placed after the spanner.
- **Nested multi-column containers** ([§3.2](https://drafts.csswg.org/css-multicol-1/#nested-multicol), `NestedColumns`,
  `ColumnBreaker.NestedColumnsCursor`), as in Blink: an in-flow multi-column container laid out in the strip of another one
  (also as a table cell, a flex item or a grid item) is fragmented by the outer columns. It is first laid out in the strip as
  if it was not fragmented (its size there gives the positions of the content after it); `MultiColumnState` (in
  `LayoutBox.LayoutState`) keeps what is needed to lay out its strip again, without fragmenting it, when the outer container
  collects its possible breaks. The inner container is a parallel flow of the outer flow with its own cursor:
  - In each outer fragmentainer, a row of columns is as tall as the block size that remains (less what remains of the block
    size of the inner container when it is definite): its content is balanced in it when it ends there (or the columns are
    filled, with `column-fill: auto`), else the row has `column-count` columns of that block size and the content continues in
    a row of the next outer fragmentainer. The last row of a container whose definite block size ends in the outer
    fragmentainer has overflow columns. A row of a fixed block size (`column-height`) is broken by the end of the outer
    fragmentainer; when it follows other content of the container in the outer fragmentainer, it starts there only if it fits.
    These rows overflow a container with a definite block size (its block-end edge is placed before the end of its content).
  - A column that would break where a rule is violated (`orphans`/`widows`, `avoid`, content that overflows) ends the row
    before it and is placed in the next outer fragmentainer, unless a break that is as bad was already taken in the columns
    of the row before it; the first column of the first row is compared with the break before the container (an `avoid`
    there accepts the violations of `orphans`/`widows` and `avoid`). When no content fits after the start of the container,
    the outer flow breaks before the container if it can.
  - The spanners of the inner container are fragmented by the outer fragmentainers (each one has its own
    `FragmentationFlow`, `FragmentationFlow.CollectBox`): a spanner whose content does not fit starts in the next outer
    fragmentainer, its forced breaks (`break-before`, `break-after`) break the outer columns (they are propagated to the
    container when the spanner is its first or last content), and the content that overflows a spanner with a definite block
    size continues in the next outer fragmentainers while the rows after the spanner are placed.
  - The block-end edge of the inner container follows its content (or the end of its definite block size, which may be in a
    later outer fragmentainer than its content; `box-decoration-break: clone` repeats its edges in every fragment); the content
    of the outer flow after it is shifted accordingly. The outer columns are balanced with the same cursor (the block size the
    inner rows need is the shortage of the outer columns).
  - The slicer creates one fragment of the inner container per outer fragmentainer (`FragmentSlicer.SliceNestedColumns`), with
    the columns of its rows (the inner strip is sliced by a slicer of its own), the parts of its spanners and its gap
    decorations (the row gaps before a fragment, also the ones that disappear at the outer breaks, consume the values of the row
    rules). When the container is in one outer fragmentainer and as tall as in the strip, its layout in the strip is kept.
  - The absolutely positioned boxes whose containing block is in the inner strip are flows of their own, as in a container
    that is not nested (`ColumnBreaker.PlaceNestedOutOfFlow`, called by `PlaceOutOfFlow` once the in-flow content of the
    outer flow is placed): they are placed in the sequence of the inner columns, across the rows, the spanners and the outer
    fragmentainers (`NestedColumns.Fragmentainers`). The columns of a row without in-flow content have the block size that
    remains in the outer fragmentainer, a row before a spanner has all its columns, and the last row is balanced with the
    boxes. The boxes that end after the in-flow content create inner columns: in the last row while it has room
    (`column-count` columns; overflow columns when the definite block size of the container ends in the outer
    fragmentainer), then in new rows in the next outer fragmentainers (the columns that do not fit in the outer
    fragmentainer continue in the next one, [§8.2](https://drafts.csswg.org/css-multicol-1/#pagination-and-overflow-outside-multicol);
    Blink adds them all to the last row, where they overflow in the inline direction: the tests cannot tell the two
    apart). These rows are as tall as the outer fragmentainer (in empty fragments of the container once it has ended); they
    do not move the block-end edge of the container nor the content after it, and are not counted when the outer columns are
    balanced (the outermost container gets overflow columns for them). The columns created for the boxes count for the gap
    decorations. The absolutely positioned children of the nested container (it is their containing block) are flows of the
    outer context, fragmented by the outer fragmentainers. A box whose containing block is outside of the nested container
    and whose static position is in its strip is moved to that static position in the inner columns that the breaker placed
    (`ColumnBreaker.TryGetNestedStaticPosition`, `OutOfFlowPlacement.InlineShift`), also when its containing block is an
    inline box of the outer strip or is outside of the outer container.
  - The floats, the absolutely positioned boxes and the inline-blocks that are multi-column containers are monolithic.
- **Fragments** (`FragmentSlicer`, [§5](https://drafts.csswg.org/css-break-3/#box-splitting)): a fragment whose subtree is in one column
  (and contains no column spanner, which the multi-column container places itself) is moved to it; a box that spans several columns gets one `BoxFragment` per column (`BoxFragment.CreateSlice`:
  `IsFirstFragment`/`IsLastFragment`, `UnfragmentedFragment` is the fragment before the split), whose children are distributed
  recursively (their ranges are computed before any fragment is moved). A fragment broken at the end of a column extends to the
  end of the column (as in Blink); the content that overflows a box continues in empty fragments of the box in the next
  columns. `box-decoration-break: slice` keeps the block-start border and padding on the first fragment and the block-end ones on
  the fragment where the border box ends ("the box is rendered as if it were not fragmented, and then sliced by the breaks":
  the fragments know the box they make when stitched together, `BoxFragment.StitchedBox`/`StitchedOffset`, which positions
  their background images and slices their shadows; the corners at the breaks are square); `clone` repeats them on every fragment of its border box (not at the breaks after its end, in the contents
  that overflow it, as in Blink: `VisitContext.CloneBoxes`; the breaker reserves the block-end edges before a
  break and displays the content after the repeated block-start edges, `FragmentainerBreak.DisplayOffset`; the fragments of
  the contents that are broken end before the repeated block-end edges; as in Blink, a break after the repeated
  block-start edges of a box that starts its fragmentainer is not possible, `BreakCandidate.OnlyEdgesFrom`: the first
  content would not fit better in the next one, and the block-end edges of a box are placed with its last content when it
  overflows the fragmentainer, `BreakCandidate.IsBeforeEndEdges`). The column boxes have no
  fragment: the fragments of the content are children of the fragment of the container (the hit testing, the painting order and
  the scrollable overflow, including the overflow columns, see them as usual). The absolutely positioned boxes of the strip whose
  containing block is the container or an ancestor get their static position in the columns (in the inner columns of a
  nested multi-column container when the static position is in its strip); the ones whose containing block is
  in the strip are fragmented apart, from their position in it.
- **Geometry**: `getClientRects()` returns one rectangle per fragment (`LayoutEngine.GetFragments` indexes all the fragments of a
  box), `offsetTop`/`offsetLeft` the first fragment, `offsetWidth`/`offsetHeight` the bounding box of the fragments, the resolved
  `width`/`height` the size of the box before the fragmentation; the hit testing applies the rounded corners of the edges a fragment
  has (`BorderRadii.Get(BoxFragment)`, also for the inline boxes split over lines).
- **Column rules**: the gaps of the container (`BoxFragment.GapGeometry`, see [Gap decorations](#gap-decorations-csslayoutgaps)):
  the column gaps run from the first to the last row of columns (as tall as the columns of the rows: the columns of the last row
  of a container with a definite block size are as tall as what remains of its content box, also when their content is
  balanced) and are interrupted by the column spanners; the row gaps separate the rows of `column-wrap: wrap`. By default a
  column rule is painted between two columns that both have content.
- **Baselines** of the fragments: a fragment of a broken box that does not contain the baselines of the box has the baselines of
  its own content (its first and last line boxes).
- **Baselines**: as in Blink ([csswg-drafts#7639](https://github.com/w3c/csswg-drafts/issues/7639)), the first baseline of the
  container is the highest first baseline of its columns and spanners, the last baseline the lowest last baseline.
- **Intrinsic sizes** (as in the browsers, after the former [CSS Sizing 3 definition](https://www.w3.org/TR/2016/WD-css-sizing-3-20160510/#multicol-intrinsic),
  [csswg-drafts#1742](https://github.com/w3c/csswg-drafts/issues/1742)): the intrinsic sizes of the content (one column) times the column
  count (`auto` is 1) plus the gaps; a specified `column-width` limits the min-content size (and ignores the count for it) and is
  the max-content size of a column, or, with a `column-count`, the min-content size of the content when it is larger
  ([CSS Sizing 3](https://drafts.csswg.org/css-sizing-3/#multicol-intrinsic): "the larger of its column-width and the largest
  min-content inline-size contribution of its contents", as in Chrome). The column spanners are measured apart (`LayoutContext.ColumnSpannerSizes`): they are not in
  a column, the container is at least as wide as their contributions. With size containment, the content has no size (the gaps
  remain).
- Not done: the spanners in the rows of a fixed block size of a nested multi-column container (they are placed after the
  content, not in the rows), the margins after the last box of the content (they must fit in the last column), the
  painting of a spanner with the effects of its ancestors inside the container (`opacity`: the spanner is a child of the
  fragment of the container), the containing block of the absolutely positioned boxes of a spanner (a positioned ancestor of
  the spanner inside the container is used), the relayout of the content in
  the space that remains after a break (the percentages of a broken box; the items of the flex and grid containers and the
  cells are not laid out again: their lines and items are not aligned again in a container that is broken or grows), the
  content of a box that avoids the floats resumed from the break in a fragment of another inline size (the content of the
  fragment is the one of its layout in that inline size from the position of the break in the strip: a text that reflows may
  repeat or skip words at the break), the inline sizes of the fragments of a box that avoids the floats in a nested
  multi-column container, in a parallel flow of another flow than the one of its container, or whose writing mode differs
  from the one of its containing block (it keeps the inline size of its first fragment), the floats that are
  broken apart from the flow of their containing block when no box avoids them (a float that is broken is broken where
  the content beside it is), the floats of the nested multi-column containers (they are not pushed when the container is
  fragmented by the outer columns), the rows that the absolutely positioned boxes of a nested multi-column container add in the next outer
  fragmentainers (one row per outer fragmentainer, also for the rows of a fixed block size that wrap; they are not counted
  when the outer columns are balanced; the last row of a nested container whose strip has parallel flows is balanced
  without the boxes), the repeated edges of a box with `box-decoration-break: clone` and a specified block size that a
  column spanner interrupts, the block size of a table row when the monolithic contents of a cell overflow a
  fragmentainer (the row is as tall as in the strip, where the overflow counts: the percentages of the other cells resolve
  against it), the gap of a row flex container after a forced break (it disappears, where Blink keeps a part of it), the
  forced breaks between the tracks without block size at the start of a fragmentainer (the lines of items with negative
  margins), an item that is offset in its line and starts after the end of a fragmentainer without block size (it is
  placed when the flow reaches its offset, column after column), the margin of a table caption that follows a caption
  without block size after a break (it is not truncated), the items that follow, before the position of the break, an
  item placed after a forced break although it overlaps the previous one, a column flex item that is as tall as its
  contents once fragmented in a multi-line container, the stitched scrollable
  overflow of a fragmented box (`scrollWidth`/`scrollHeight` use its first fragment), `column-span: <integer>`.

### Paged media (`Css/Layout/Paged/`, `LayoutEngine.Paged.cs`)

The printing of a document ([CSS Paged Media 3](https://drafts.csswg.org/css-page-3/), [CSS Fragmentation 3](https://drafts.csswg.org/css-break-3/),
[CSS 2 §13](https://drafts.csswg.org/css2/#page-box)) paginates it: `Document.BeginPrint(PrintOptions)` starts a
`PrintSession`, whose `GetPages()` returns the pages (`LayoutPage`: the page name, `:left`/`:blank`, the geometry of the
page box and the fragment of its page area) and `Paint()` their display lists (see [Printing](rendering.md#printing)).

- **Engines**: while the session is active, the document has a style engine whose environment is the `print` media type
  (an environment override: it starts no transition and does not update the animations, whose current values apply) and a
  layout engine that paginates (`LayoutEngine.IsPrintLayout`): the screen engines are kept as they are and used again when
  the session ends (the screen is not styled nor laid out again). As in the browsers, the media queries see the page area of
  the default page of the options (the page without the default margins: the `width` and `height` media features do not
  depend on the `@page` rules, [csswg-drafts#5437](https://github.com/w3c/csswg-drafts/issues/5437)), and the viewport-relative
  lengths and the initial containing block are the page area of the first page (`StyleEngine.PrintViewportSize`; when the
  first page is a named page whose page area differs, the document is styled and laid out again).
- **Page context** (`StyleEngine.GetPageStyle`, `StyleEngine.Pages.cs`): the declarations of the `@page` rules (collected with
  the other at-rules by `CascadeRuleCollector`, under the conditional rules and in their cascade layers) whose selector
  matches the page (the page name, `:first`, `:left`, `:right`, `:blank`), cascaded by importance, layer, specificity
  (name, then `:first`/`:blank`, then `:left`/`:right`) and order; the page context inherits from the root element. As in
  Blink and WebKit, the `@page` rules and the margin rules keep the declarations of all the properties (the CSSOM shows
  them; Gecko drops the ones that do not apply), and their cascade ignores the properties that do not apply to the page
  context or to the page-margin boxes (`CssStyleDeclaration.AppliesToPageContext`, `AppliesToMarginBox`: the margins,
  borders, paddings, backgrounds, outlines, sizes, counters, fonts and text properties...). The viewport-relative lengths
  of the page contexts and of the page-margin boxes refer to the page box of the options
  ([csswg-drafts#5437](https://github.com/w3c/csswg-drafts/issues/5437), `StyleEngine.PrintPageSize`). The margins of the
  options apply when no rule sets them.
- **Page box** (`PageGeometry`, [§3](https://drafts.csswg.org/css-page-3/#page-model)): the page sheet has the size of `size`
  (lengths, one length for a square page, the page size keywords `A5`... `ledger`, `portrait`/`landscape`, `auto` for the
  size of the options; as in the browsers, an empty page size, `size: 0`, is the size of the options). The page box is
  placed in it as a block box in its containing block: the margins, borders and paddings of the page context (their
  percentages refer to the width of the page sheet horizontally and to its height vertically, as in the browsers) and the
  page area inside them (1px at least), whose size is the `width` and `height` of the page context (with `box-sizing`,
  `min-*` and `max-*`) when they are not `auto`: the automatic margins share what remains (they may be negative,
  [csswg-drafts#8508](https://github.com/w3c/csswg-drafts/issues/8508)), and when no margin is automatic the page sheet is
  resized to the margin box of the page box ("the containing block is resized to coincide with the margin edges of the page
  box"). A document whose root element generates no box prints blank pages (no page box is painted, as in Gecko).
  `page-orientation` rotates the printed page (see [Printing](rendering.md#printing)), not its layout.
- **Printable area** ([`page-margin-safety`](https://drafts.csswg.org/css-page-3/#page-margin-safety)): `PrintOptions.SafePrintableInset`
  is the unprintable region along the edges of the sheet (0 by default; the WPT runner reads it from
  `<meta name=safe-printable-inset>`, in centimeters, as the runner of Chromium). With `clamp`, the page margins that are not
  automatic are at least the inset; with `add`, the inset is added to them. In the margin rules (where the descriptor is
  also accepted), only the margins of a page-margin box that face an edge of the sheet are adjusted: an automatic margin
  is the inset when the size of the box is automatic, and a centered box moves away from the edge until its margin
  reaches the inset (as far as the opposite margin allows). An inset whose two edges cover the sheet is ignored.
- **Pagination** (the fragmentation of the multi-column containers, see above, with the page areas as fragmentainers): the
  root element is laid out in the initial containing block (the page area of the first page) as a strip, which is as tall
  as the margin box of the root element. Its possible breaks are collected with the rules of the pages (`PageBreakRules`,
  given to `FragmentationFlow.Collect`): `break-before`/`break-after: page | left | right | recto | verso | always | all`
  force a page break, `avoid` and `avoid-page` avoid it, `column` does not apply; a class A break between two siblings
  whose page names differ is forced ([§4.4](https://drafts.csswg.org/css-page-3/#using-named-pages): the end page value of
  the box before the break and the start page value of the box after it, which propagate through the first and last in-flow
  block-level children; `auto` is the used value of the parent). `orphans`, `widows`, `break-inside: avoid`,
  `box-decoration-break`, the floats, the parallel flows (flex, grid, tables), the repeated table headers and the nested
  multi-column containers (laid out as nested containers: the pages fragment their columns) are fragmented as in the
  columns. `ColumnBreaker.FindBreaks` fills the pages: the block size of a page depends on its page style, which depends on
  the breaks (the page name of the content that starts the page, its side), so the breaks are found again with the block
  sizes of the pages until they are stable (5 times at most). The floats that do not fit are pushed to the next page by new
  layouts of the strip, as in the columns, and the items of the grid lanes containers fragmented in their stacking axis are
  placed again in each page (see [Grid lanes](#grid-lanes-csslayoutgridgridlayoutalgorithmlanescs)).
- **Pages of another inline size** (`LayoutEngine.LayoutPageSegment`): the content of a page whose page area has another
  inline size than the strip (a named page of another `size`, other `:left`/`:right` margins) is laid out again in a strip
  of its inline size (its initial containing block is that page area): the pages are segments of strips, one per change of
  inline size. A segment ends before the first page whose page area has another inline size when that page starts before an
  in-flow block-level box of the block flow of the root element (`FindBreakAnchor`: a break between two block boxes, also
  inside the root element and the body); the next segment lays out the strip again and paginates it from the start of that
  box (with the margin preserved after a forced break), the content before it being in a virtual first fragmentainer whose
  fragments are dropped. The pages of a segment followed by another one are copies of the fragments (the next layout may
  return and move the fragments of the boxes laid out the same way). The boxes fixed to the viewport are laid out once, in
  the initial containing block of the first page, as in Blink. A page of another inline size that starts inside a line, a
  flex container... is laid out in the inline size of the strip. Nothing changes (nothing is laid out again) when all the
  pages have the inline size of the first one.
- **Monolithic content**: as in the browsers, the scroll containers are not monolithic in the pages (their contents are
  printed). The monolithic content (a replaced element, a box with size containment, a line box) that does not fit in an
  empty page is sliced: the next page starts at the end of the page
  (`FragmentationFlow.SlicesMonolithicContent`) and shows the rest of it (`FragmentSlicer` places a copy of its fragment in
  each next page, at a negative offset; the page area clips it), and the content after it follows it. As in Blink, whose
  page grows over the monolithic content, the boxes with `box-decoration-break: clone` do not repeat their edges at these
  slices (`FragmentationFlow.StartsInMonolithicSlice`; a box with a definite block size consumes the whole page). The content that
  overflows a monolithic box (that does not clip it) adds pages, as in the browsers: the flow continues until its end
  (`FragmentationFlow.GetOverflowEnd`, also for an absolutely positioned monolithic box), and the copies of the box are
  placed in these pages.
- **Pages** (`LayoutEngine.GetPageSequence`): the name of a page is the start page value of the box that starts it (or the
  used page value of the box broken by its break); the first page is a right page when the pages progress from left to
  right (the writing mode and the direction of the root element, [§3.2](https://drafts.csswg.org/css-page-3/#progression)),
  unless the content starts with a forced break to a side; a blank page (`:blank`) is inserted before a page that starts
  after a forced break to a left or right page (`left`, `right`, `recto`, `verso`) when it would be on the other side.
  The fragments of the strip are distributed to the page areas by `FragmentSlicer`: each page has a fragment of the
  viewport box whose children are the fragments of its content. The fixed positioned boxes are repeated on every page
  ([CSS Position 3](https://drafts.csswg.org/css-position-3/#fixed-pos): positioned in the page area of each page, as in the
  first one; the pages added by the absolutely positioned boxes alternate between left and right pages, with the block
  sizes of their page areas); the absolutely positioned boxes whose containing block is the initial containing block are on the page of
  their position (in the content for a box at its static position in the block axis or anchored, else in the initial
  containing block, whose pages follow each other by the block sizes of their page areas), unbroken, when they fit in the rest of it with their descendants and their contents
  have no forced page break, else they are fragmented apart from their position in the initial containing block
  (`PageBreakRules.InitialContainingBlock`; the pages they reach are added; as in Blink, the page names of their contents
  break the pages only when the box is at its static position in the block axis); the absolutely positioned boxes
  whose containing block is in the strip are fragmented as in the columns. The layers adopted by a stacking context split by the pages are painted with its fragment of the same page
  (`PaintOrder`).
- **Page-margin boxes** (`PageMarginBoxes`, [§4](https://drafts.csswg.org/css-page-3/#margin-boxes)): the sixteen boxes of
  the margin rules (`@top-left-corner`... `@left-top`) whose `content` is not `none` nor `normal`, styled by the margin rules
  of the matching `@page` rules (`StyleEngine.GetPageMarginStyle`: they inherit from the page context, with the default
  `text-align` and `vertical-align` of each box). Their content is the strings, the quotes, the counters (`counter()`,
  `counters()`, with a counter style) and the images (replaced boxes, as in `::before`) of `content`. The corner boxes fill the corners of the page margins; the three boxes
  of an edge share its length ([§4.2.2](https://drafts.csswg.org/css-page-3/#variable-auto-sizing)), as in Blink: when the
  middle box B is generated, it is centered and the boxes at the ends A and C have the same size: an imaginary box twice as
  large as A, then as C, shares the edge with B, and the larger of the two keeps B centered (a specified size is a
  min-content and max-content size; when all the max-content sizes are 0, the boxes share the edge equally); else A and C
  share the edge; a box with a specified size keeps it, two automatic sizes grow from their max-content sizes in proportion
  to them (or shrink in proportion to the difference of their max-content and min-content sizes). The sizes along an edge
  are the intrinsic inline sizes of a box whose inline axis is along it, else its size laid out in the depth of the margin;
  the percentages of a box refer to the width of its edge or corner horizontally and to its height vertically. In the depth
  of the margin, a box fills the page margin (or has its specified or intrinsic size: `max-content`, `fit-content`...); the
  automatic margins center a box with a specified size (they are zero when the box does not fit with them), and an
  over-constrained box is placed next to the page area. The
  contents are aligned by `vertical-align` (`top`, `middle`, `center`, `bottom`, line-relative in a vertical writing mode).
  Each box is painted atomically, in the order of its `z-index` (the negative ones before the document), then from the
  top-left corner, clockwise (see [Printing](rendering.md#printing)).
- **Page counters** (`PageCounters`, [§6.1](https://drafts.csswg.org/css-page-3/#page-based-counters)), as in Blink: the page
  context inherits the counters of the root element and the `page` counter (incremented by 1 on every page unless
  `counter-increment` names it). Each page applies `counter-reset`, `counter-increment` and `counter-set` of its page context:
  the changes of the counters that the page does not reset are kept for the next pages, a counter reset by a page is a
  counter of that page only; a page-margin box applies its own counter properties for itself only. The counters are not
  nested (`counters()` shows one value).
- Not done: the layout of the content of a page whose page area has another inline size than the strip when the page does
  not start before a block box of the block flow of the root element (it is laid out in the inline size of the strip), the
  nested documents of the `iframe` elements (they are painted as on the screen), `window.print()`,
  `beforeprint` and `afterprint`.

### Gap decorations (`Css/Layout/Gaps/`)

The [gap decorations](https://drafts.csswg.org/css-gaps-1/#gap-decorations) (`column-rule`, `row-rule` and their longhands) of
the grid, grid lanes, flex and multi-column containers. The layout computes the geometry of the gaps, the painting resolves
the properties: the decorations take no space.

- **Geometry** (`GapGeometry`, in `BoxFragment.GapGeometry`, only when `LayoutStyle.HasGapRules`): the column gaps (which separate
  boxes in the inline axis) and the row gaps of the container, in the logical coordinates of its border box. A `GapLine` has the
  position of its centerline, its size, the index of its values, and its `GapIntersection`s in order
  ([segment endpoints](https://drafts.csswg.org/css-gaps-1/#segment-endpoints)): its two bounds and the center of each gap
  junction, with the size of the crossing gap (the start and end segment endpoints of the junction are at half that size on each
  side; it is also the percentage basis of the insets, the [crossing gap width](https://drafts.csswg.org/css-gaps-1/#crossing-gap-width))
  and the state of the portion of the gap that follows (`GapSegmentState`: an item spans across it, the area before or after
  it is empty). The space added by the content distribution is part of the gaps.
  - **Grid** (`GridLayoutAlgorithm.Gaps.cs`): the gutters between the tracks that are not collapsed (the gutters of the collapsed
    tracks are one gap; the gutters of a subgrid are its own ones, centered in the gutters of the tracks of its parent), from
    the start to the end of the grid; each gap intersects every gap of the other axis. The states come
    from the areas of the items (the cells an item covers, the cells without item).
  - **Grid lanes**: the gutters between the tracks run across the stacking axis; the gutters of the stacking axis are confined to
    their track, just before their item (after it with `fill-reverse`), so the gutter before a spanning item has one segment per
    track. A portion of a gutter between two tracks is blocked when the same item is on its two sides. The gutters of the two
    tracks that overlap one another are one junction (`GapOverlapWindow`).
  - **Flex** (`FlexLayoutAlgorithm.Gaps.cs`): the gaps between the items run across their line (from the gap before the line to
    the gap after it); the gaps between the lines run across the container and are abutted by the gaps of the two lines (the
    gaps of the two lines that overlap are one junction).
  - **Multi-column**: see [Multi-column layout](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation). The
    column gaps exist up to the specified `column-count`, also beside the columns without content.
- **Values** ([assigning](https://drafts.csswg.org/css-gaps-1/#assigning), `GapValueList<T>.Get`): each gap has its index in a
  sequence of gaps: without auto repeater the values repeat; with one, the leading values go to the first gaps, the first
  trailing values to the last gaps, the values of the auto repeater to the others. The sequences follow the placement: the gaps
  between the flex items are one sequence across the lines (reversed with `row-reverse`/`column-reverse`, the lines with
  `wrap-reverse`), the gutters of the stacking axis of a grid lanes container restart in each track, `track-reverse` and
  `fill-reverse` reverse them.
- **Decorations** (`GapDecorations.GetDecorations`, the [segments](https://drafts.csswg.org/css-gaps-1/#determine-gap-decoration-segments)):
  a segment starts at the first intersection whose following portion is painted, and continues through the next intersections
  according to `*-rule-break`: `none` paints one decoration behind the items; `normal` (grid) breaks where an item spans across the
  gap; `intersection` also breaks at the junctions that are not flanked by spanning items. `normal` is `none` in a flex container
  and for the row gaps of a multi-column container, `intersection` for its column gaps. `*-rule-visibility-items` hides the
  portions next to empty areas (`normal` is `all` in the grids; in the flex and multi-column containers it is `between`, as in
  Blink: the specification says `all` for the row gaps of a multi-column container). The ends of a segment are moved by the
  insets: the cap insets at the bounds of the gap (and, when the rules are painted `between` the items, at the junctions where
  the crossing gap has no decoration, as in Blink), the junction insets elsewhere; `overlap-join` extends the segment by half
  the junction plus half the width of the crossing rule (only half the junction for the gaps between the flex lines and the rows
  of columns, which the crossing gaps abut). The rectangle of a decoration is centered on the gap, as wide as the rule.
- **Painting**: `Painter.PaintGapDecorations` paints them just above the border of the container (`BorderPainter.PaintRule`:
  `inset`/`outset` as `ridge`/`groove`), the column decorations first unless `rule-overlap: column-over-row`; in a scroll container
  they are clipped and scrolled with its contents.
- **Fragmentation** ([§](https://drafts.csswg.org/css-gaps-1/#fragmentation)): "the gap disappears when it coincides with a
  fragmentation break". The row gaps of a grid that no item spans across, the gaps between the lines of a row flex container
  and the gaps between the items of a column flex container (`GridContainerState.GetRowGaps`, `FlexContainerState`) are known
  to the fragmentation (`FragmentationFlow.SkipGap`): a fragmentainer that ends in such a gap, or right before it, is followed
  by the content after the gap (`ColumnBreaker`), and the gap is not content of the fragmentainer (the rows after it are
  shifted). A row gap that an item spans across disappears too, but the content of that item cannot be removed: as in
  Blink, when a fragmentainer starts in such a gap (at its start or inside it), the row after the gap starts at the break
  (`ColumnBreaker.GetSpannedGapSpillover`: its items move up by the part of the gap after the break, the stretched ones
  grow) and the next rows keep their positions. The rows of a subgrid are the contents of its parallel flow (also its
  empty rows: they break anywhere), and its block-end edge is not after them (`ParallelItem.IsRowSubgrid`): as in Blink, a
  subgrid is shorter than its area by the gaps of its own rows that disappeared at its breaks. The rows of a subgrid whose
  gutters differ from the ones of its parent are its own ones (`GridContainerState.SubgridRowGutterShift`), and the empty
  rows of a grid with a definite block size after its items are content of its flow (their gaps disappear at the breaks).
  The gap after a line or a row that is broken (its items continue in the next fragmentainer) remains. `GapGeometry.Slice` gives each fragment of the container its gaps (`FragmentSlicer`):
  the row gaps that are entirely in the fragmentainer and not adjacent to a break, and the parts of the column gaps (a column
  gap that continues runs to the end of the fragment, or of the monolithic items that overflow the fragmentainer); a gap
  keeps its values ("a gap that is suppressed due to fragmentation
  still consumes a gap decoration value"). The gaps are placed from the fragmented positions of the content: each block
  offset of the geometry is mapped by the breaks of its flow (`GapFlowSlice`: the flow of the container, whose map has
  the shifts of the content after the tracks that grow; or the parallel flow of a line of a multi-line column flex
  container, `GapLine.FlowKey`, `GapIntersection.FlowKey`: each line is broken at its own positions), and a gap is placed
  before the content that follows it (the content before it may end earlier than in the container). As in Blink, a gap
  between two items of a line of a row flex container is in a fragment when the item on its inline-end side has a
  fragment there (`GapLine.EndItem`: no rule beside the rest of an item whose neighbor, monolithic or shorter, stayed in
  the previous fragmentainer).
- **Interpolation** ([§](https://drafts.csswg.org/css-gaps-1/#interpolation-behavior), `CssGenericInterpolation.CombineRepeatableLists`):
  the integer repeaters of the width and color lists are expanded, then the lists are repeatable lists; two lists with an auto
  repeater are combined when they have the same numbers of leading and trailing values, else the interpolation is discrete.
- Not done: the suppression of the gutters of a grid lanes container at the breaks; Blink moves up the first row of the
  next set of tracks after a suppressed gap that an item spans across (a set groups the adjacent tracks of one track
  definition, e.g. `repeat(5, 1fr)`, between the lines where items start or end), which may be several rows after the
  break, where the library moves up the row right after the gap; the overlap windows of the gaps between the lines of a column flex container are the ones of the
  container before its fragmentation; the thirds of `double` and the halves of
  `groove`/`ridge` are not snapped to whole pixels (as the borders).

### MathML layout (`Css/Layout/MathML/`)

[MathML Core](https://w3c.github.io/mathml-core/#presentation-markup) lays out the MathML elements whose display is
`block math` or `inline math` (`MathFormattingContext`, the `Math` kind of `FormattingContext`; the other displays of a
MathML element are laid out by CSS). `MathLayoutBoxes.GetKind(box)` gives the algorithm of a box (`MathBoxKind`): the
grouping elements (`math`, `mrow`, `mstyle`, `merror`, `mphantom`, `maction`, `semantics`, `a`, `mprescripts`), the unknown
MathML elements and the anonymous mrow boxes are rows; the token elements (`mi`, `mn`, `mtext`, `ms`, `annotation`,
`annotation-xml`), `mo`, `mspace`, `mfrac`, `msqrt`, `mroot`, `mpadded` and the scripts (`msub`, `msup`, `msubsup`, `munder`,
`mover`, `munderover`, `mmultiscripts`) have their own algorithm; an element with the wrong number of in-flow children is
laid out as a row. `mtable`/`mtr`/`mtd` are CSS tables (the UA style sheet: `inline-table`, `table-row`, `table-cell`); the
`columnspan` and `rowspan` attributes of an `mtd` are the `colspan` and `rowspan` of an HTML cell (`TableGridModel`). An `mo`
with an element child has the default operator properties (its content is not "only text"). The contents of a block-level
`math` root are centered with a safe alignment (start-aligned when they are wider than the box).

**Box tree.** The box tree builder (`FixupMathContainer`) keeps the children of the MathML boxes as they are (no anonymous
block): the text of the non-token boxes is not rendered (dropped), their inline boxes are blockified (box display only), the
children of `mspace` are not rendered; the token elements are block containers (their contents are laid out by the inline
or the block layout). `msqrt`, `mpadded` (with a math display) and `mtd` (as a table cell) wrap their children in an
anonymous mrow box ([anonymous mrow boxes](https://w3c.github.io/mathml-core/#anonymous-mrow-boxes)). The cascade does not
blockify a float child of a math box (`float` does not apply there). The table fixup treats consecutive white space runs (e.g.
around a comment) as one run.

**Math content box.** Each box computes a `MathContent` (the "math content box"): its inline size, its line-ascent and
line-descent from the alphabetic baseline, its ink line-ascent/descent, its italic correction and top accent attachment, the
placements of its in-flow children (inline offset of the margin box, baseline offset) and its graphical items. The children
are measured (`Measure`) by laying them out (`AutoInlineSizeIsFitContent`, the available inline size of the parent) and
reading their margin box metrics: the `BoxFragment.MathData` (`MathFragmentData`: ink extent, italic correction, top accent
attachment of the border box, and the paint items) of a MathML child, the first baseline of a CSS child (the block-end edge
without baseline; the center of an `mtable` is on the math axis). The ink extent of a margin box includes the margins (as in
the browsers), its italic correction (when nonzero) the inline-end margin. The same algorithms compute the min-content and
max-content sizes (`MathMode.MinContent`/`MaxContent`: the children are measured by `IntrinsicSizing.GetContribution`).

**Sizing.** The automatic inline size of a MathML box is the inline size of its math content (`GetAutomaticInlineSize`,
which computes the content: the state of a layout is in `LayoutBox.LayoutState`, `MathLayoutState`); a block-level `math`
root outside of a math layout fills its containing block and centers its content; a box with inline-axis size containment
uses its `contain-intrinsic-size`. The content box gets the math content (start-aligned, centered for `mfrac` and the
under/over scripts), the block size is the line-ascent + line-descent, the first and last baselines are the line-ascent:
an inline `math` is aligned on the alphabetic baseline of the line. Padding, borders and margins apply around the math
content (no margin collapsing). Out-of-flow children are placed at the start of the content box.

**Tokens and operators.** A token whose contents are a single line of text has the ink extent of its glyphs as its
line-ascent/descent (the line box is moved so that the ink-over edge is at the top of the content box); otherwise it is a
block aligned on its first baseline. Its inline size is the fit-content size of its contents in the available inline size
of the box (not its specified width: the content overflows). A single glyph (after `text-transform: math-auto`, applied by
the inline layout and `innerText` to the text nodes of a single character, `MathItalicMapping`) gives the italic correction
and the top accent attachment of the MATH table. `mo` uses the operator dictionary (`MathOperatorDictionary`, generated by
`tools/Meziantou.Framework.WebPlatform.SpecGen` from `tests/Meziantou.Framework.WebPlatform.SpecData/mathml-core/tables`: the categories of the (content, form) keys, the values of the
categories, `Operators_2_ascii_chars`, the inline stretch axis) and its attributes (`MathLayoutBoxes.GetOperatorProperties`:
form from the position of the outermost embellished operator, `lspace`/`rspace` with the font of the core operator,
`minsize`/`maxsize`, the stretch axis); the MathML length attributes are resolved with the style of their element (math
functions are rejected, as in the browsers). A stretchy operator with a stretch size constraint
(`ConstraintSpace.MathStretch`, `MathStretchConstraint`) and a large operator in `math-style: normal` are shaped by
`MathStretchyGlyphs` (the glyph, a size variant or the glyph assembly of the MathVariants table: `MathShapedGlyph`, a paint
item) instead of the inline layout (their text gets no fragment). In rtl, the glyph is the `rtlm` substitution, else the
Bidi_Mirroring_Glyph (a radical without mirrored form keeps its glyph).

**Stretching.** The rows stretch their stretchy operators along the block axis to the ink extent of the other children
(first laid out with the constraint (0, 0) when all the children stretch), the under/over scripts stretch theirs along the
inline axis to the widest other child; the embellished operators (`MathLayoutBoxes.GetCoreOperator`: scripts and fractions
through their first child, rows and mpadded with one embellished operator and space-like children) pass the constraint to
their core operator. The glyph assemblies are valid with connectors shorter than `MinConnectorOverlap` (they limit the
overlap; the outer connectors of the first and last parts are ignored), as in the browsers.

**Parameters.** `MathFontParameters.Get(style)` gives the MATH constants of the first available font in CSS px, with the
fallbacks of the specification without MATH table. The `font-size: math` scale factor of `math-depth` uses the
`scriptPercentScaleDown`/`scriptScriptPercentScaleDown` of the inherited first available font.

**Deviations from the specification (browser behaviors).** The anonymous mrow of an `mtd` adds the operator spacing; the
ink extent of the margin boxes includes the margins; the under/over gaps are measured between the ink edges of the base
and the margin box of the script (`gap + ascent/descent of the script`), the accent overscripts are at least
AccentBaseHeight above the baseline; the OverbarExtraAscender/UnderbarExtraDescender extend the box beyond the scripts.

**Painting.** `MathFragmentData.PaintItems` are the fraction bars, the radical overbars (rules) and the shaped glyphs
(stretched operators, radical symbols: `MathShapedGlyph.Glyphs` are origins relative to the inline-start/baseline of the
item, y down), in the coordinates of the border box; the painter (`Painter.PaintMath`) paints them with the background and
the borders of the box, in its `color` (a math box is a stacking context, https://w3c.github.io/mathml-core/#stacking-contexts,
not done). A radical without `rtlm` glyph is not mirrored in right-to-left math (the painter does not flip the glyph).

**Not done.** The Unicode-based glyph assemblies (fonts without MathVariants), the `ssty`/`dtls`/`flac` features, the
italic correction of the large operators in the intrinsic sizes, `mtable` alignment attributes, `menclose` (an unknown
element: a row), the linebreaking of math, the `math` stacking context, the mirroring of the radical in right-to-left math.

### Layout API containers (`Css/Layout/LayoutApi/`, `Css/LayoutApi/`)

The [CSS Layout API](https://drafts.css-houdini.org/css-layout-api-1/) lets a script lay out a box: `display: layout(name)`
(and `inline layout(name)`) makes a box a layout API container, laid out by the class that a module of the layout worklet
registers (`CSS.layoutWorklet.addModule()`, `Document.LayoutWorklet`; see [Worklets](scripting.md#worklets)):

```js
// layout.js
registerLayout('centered', class {
  static inputProperties = ['--gap'];
  static childInputProperties = ['--weight'];
  static layoutOptions = { childDisplay: 'block', sizing: 'block-like' };
  async intrinsicSizes(children, edges, styleMap) {
    const sizes = await Promise.all(children.map(child => child.intrinsicSizes()));
    return { minContentSize: Math.max(...sizes.map(s => s.minContentSize)) + edges.inline, maxContentSize: Math.max(...sizes.map(s => s.maxContentSize)) + edges.inline };
  }
  async layout(children, edges, constraints, styleMap) {
    const fragments = await Promise.all(children.map(child => child.layoutNextFragment({ availableInlineSize: constraints.fixedInlineSize - edges.inline })));
    let blockOffset = edges.blockStart;
    for (const fragment of fragments) {
      fragment.inlineOffset = (constraints.fixedInlineSize - fragment.inlineSize) / 2;
      fragment.blockOffset = blockOffset;
      blockOffset += fragment.blockSize;
    }
    return { autoBlockSize: blockOffset + edges.blockEnd, childFragments: fragments };
  }
});
```

- **Display and boxes**: `layout(<ident>)` is an inner display type of the grammar of `display` (`@supports (display:
  layout(foo))` matches; the computed value keeps the name: `layout(foo)`, `inline layout(foo)`). For the box tree, the box
  is a flow-root block container (`Display.IsLayoutFunction`): "This is the same as establishing a block formatting context,
  except that the layout provided by the author is used instead of the block layout". While its layout is not registered
  (or once its registration is invalid: the global scopes registered classes with different properties or options), the box
  *is* laid out as a flow-root block container. Once it is registered, `ContainerBox.LayoutApi` is set, the box is laid out
  by `LayoutApiFormattingContext`, and its in-flow children are the children of the layout (`LayoutChild`): as for a flex
  container, each child box and each run of text (wrapped in an anonymous block box) is a child, and establishes an
  independent formatting context; the absolutely positioned children are not children of the layout, the floats are. The
  display of the children is blockified by the cascade, unless the `childDisplay` of the layout options is `normal` (the
  children are blockified while the layout is not registered, as in Chromium: `block` is the default). The multi-column
  properties do not apply to a layout API container, registered or not (they apply to the block containers). The
  registration of a layout computes the styles and builds the boxes again (`LayoutWorklet.HasLayoutContainers`).
- **Registration** (`LayoutWorkletGlobalScope.RegisterLayout`): the algorithm of the specification (the name, the
  `inputProperties` and `childInputProperties` filtered to the supported and custom properties, the `layoutOptions`
  dictionary, the `intrinsicSizes` and `layout` functions of the prototype; the [document layout
  definition](https://drafts.css-houdini.org/css-layout-api-1/#document-layout-definition) is registered by a task). The
  layout worklet has two global scopes, used in turn (`LayoutWorklet.SelectGlobalScope`: 4 callbacks each), and a box has
  one instance of the class per global scope, kept for its element while the box tree is rebuilt.
- **Sizing** (`block-like`, the default): the box is sized by the shared `FormattingContext.Layout`, like a block container:
  its `auto` inline size fills the available space, or is its fit-content size computed from the result of the
  `intrinsicSizes(children, edges, styleMap)` callback (`ComputeIntrinsicInlineSizes`; the returned sizes are border-box
  sizes, a missing or negative size is 0 and the min-content size is at most the max-content size). The `layout(children,
  edges, constraints, styleMap, breakToken)` callback then runs in the content box (`LayoutContents`): `constraints.fixedInlineSize`
  is the border-box inline size, `fixedBlockSize` the border-box block size when it is definite before the layout (else
  null: an `auto` block size, but also the size that a parent imposes when it is the one the box got from its own layout, e.g.
  a stretched flex item that is alone in its line, and in quirks mode the size of the body that fills the html element is
  fixed), the available sizes are the fixed sizes, the percentage sizes the percentage resolution sizes of the containing
  block; `edges` (`LayoutEdges`) are the sums of the borders, scrollbar gutters and paddings. The `autoBlockSize` of the
  result is the border-box block size of a box whose block size is `auto` (the min/max sizes apply).
- **`sizing: 'manual'`**: the fixed sizes of the constraints are only set when the formatting context forces them (the
  sizes that a flex, grid or layout API parent imposes, the stretch-fit inline size of an in-flow block-level box whose
  inline size is `auto`, the sizes given by the insets of an absolutely positioned box), the available sizes are the ones
  of the containing block, and the `inlineSize` and `blockSize` of the result are the border-box sizes of the box, whatever
  its sizing properties: the box is laid out twice, the second time with the sizes of the result imposed.
- **Children**: `child.styleMap` has the computed values of the `childInputProperties` (as the `styleMap` argument for the
  `inputProperties`: see [CSS Typed OM](css-typed-om.md)). `child.intrinsicSizes()` resolves to the border-box
  min-content and max-content contributions of the child (`IntrinsicSizing.GetContribution` without the margins).
  `child.layoutNextFragment(constraints)` lays out the child in a `ConstraintSpace` in the writing mode of the container
  ([translate a LayoutConstraintsOptions to internal constraints](https://drafts.css-houdini.org/css-layout-api-1/#translate-a-layoutconstraintsoptions-to-internal-constraints):
  an available size or a percentage size that is missing or not positive is zero, the fixed sizes are imposed border-box
  sizes, an `auto` inline size is the fit-content size in the available inline size) and resolves to a `LayoutFragment`:
  its `inlineSize` and `blockSize` in the writing mode of the container, the `inlineOffset` and `blockOffset` the layout
  sets (from the inline-start and block-start edges of the border box of the container, in its writing mode and
  direction), and `data`. The `data` of the constraints and of the result of a layout are structured clones
  (`StructuredSerializeForStorage`: a function or a `SharedArrayBuffer` fails) passed between a layout API container and
  its layout API children.
- **Result**: the fragments of `childFragments` are the children of the fragment of the box, in that order (their painting
  order; a child without fragment is not rendered; `z-index` makes a child a stacking context as for a flex item). The
  static position of an absolutely positioned child is the inline-start and block-start padding edge. The first and last
  baselines are the ones of the first and last fragments that have one, as for a block container. The result is a
  `FragmentResultOptions` dictionary, or a `FragmentResult` constructed from it by the callback.
- **How the callbacks run** ([run a work queue](https://drafts.css-houdini.org/css-layout-api-1/#run-a-work-queue)): the
  layout is synchronous, and the callbacks are async functions. `LayoutApiFormattingContext` invokes the callback in a
  global scope of the worklet, then alternates between the work queue (the `layoutNextFragment()` and `intrinsicSizes()`
  calls queued so far: the children are laid out, then their promises are resolved) and a microtask checkpoint, until the
  promise of the callback is settled or the queue is empty. The microtasks of the worklet are in a queue of their own
  (`ScriptEngine.EnterNestedMicrotaskQueue`, see [Worklets](scripting.md#worklets)): a layout forced by a script of the
  document (`element.offsetHeight`) does not run the microtasks of that script. A `LayoutChild`, a `LayoutFragment` or a
  `FragmentResult` is only valid in the invocation it was created for (its [layout API
  context](https://drafts.css-houdini.org/css-layout-api-1/#layout-api-context)): `layoutNextFragment()` rejects with an
  `InvalidStateError` for a child kept from another invocation, and with a `NotSupportedError` in `intrinsicSizes()`.
- **Fallback**: when a callback throws, when its promise is rejected or still pending once the work queue is empty, when
  its result is invalid (a value that is not a dictionary, a fragment of another invocation, a child with two fragments)
  or when the constructor of the class throws, "the box falls back to the flow layout": it is laid out by
  `BlockFormattingContext` for the rest of the layout pass (its intrinsic sizes too, `LayoutApiContainer.FallbackPass`); its
  children still establish independent formatting contexts, and its floats float. The error is reported to the global
  scope of the worklet (`BrowsingContextOptions.UnhandledScriptError`), or logged to the console of the document.
- **Invalidation**: there is no cache: each layout pass (see [When the layout runs](#when-the-layout-runs)) invokes the
  callbacks again, with new style maps, so a change of an input property, of a child or of the box tree is seen by the next
  layout.
- **Feature toggle**: `WebPlatformFeatures.LayoutWorkletEnabled` hides `CSS.layoutWorklet`; the `display: layout()` boxes
  are then flow-root block containers.

**Deviations and limits.** The draft is followed where Chromium, the only other implementation (behind a flag), differs:
a callback may return a value that is not a promise (Chromium falls back to the flow layout); a percentage of the block
axis of a child resolves against zero when the layout gives no positive `percentageBlockSize` or `availableBlockSize`
(Chromium treats the size as indefinite); a child without fragment is not rendered (Chromium falls back to the flow layout
unless every child has exactly one fragment, in the order of the box tree); the `baseline` member of the result and of
`LayoutFragment` of Chromium is not in the draft and not implemented. The arguments of `layoutNextFragment()` are optional
and its break token nullable, as the examples of the draft and Chromium need. Not done: the fragmentation by the layout
(`blockFragmentationType` is always `none`, `breakToken` is null and no `ChildBreakToken` is created: a layout API
container in a multi-column container is laid out in one piece and sliced with its content by the
[fragmentation](#multi-column-layout-and-fragmentation-csslayoutmulticol-csslayoutfragmentation)); with `childDisplay:
'normal'`, a run of inline-level children is one child laid out as an anonymous block (one fragment with all its lines,
not one fragment per line, and its block-level and floating descendants are not inlinified); the results are not cached
between the layouts of a box in a pass.

### SVG layout (`Svg/`)

The SVG elements other than the outermost `svg` (a replaced box) and the `foreignObject` elements have no CSS box: their
geometry is computed on demand from their computed styles, in user units (not zoomed: the zoom of the outermost `svg` maps
its user space to CSS px), by the classes of `Svg/`:

- **Coordinate systems** (`SvgUserSpace`, `SvgAffine`): the lengths of the geometry properties and attributes
  (`ResolveLength`; the percentages against the [SVG viewport](https://svgwg.org/svg2-draft/coords.html#Units): the
  `viewBox` or the size of the nearest `svg`, the content box of the outermost one from the layout, `SvgOutermostViewport`,
  else its computed size in the viewport of the browsing context); the transform of an element (`GetTransform`: the
  `translate`, `rotate`, `scale` and `transform` properties of `LayoutTransforms.GetTransformFunctions` around
  `transform-origin` in the reference box of `transform-box`; the `transform` attribute is the presentation attribute of
  `transform`, converted by `SvgTransform.ToCss`, and the SVG user agent style sheet sets `transform-origin: 0 0`); the
  nested viewports (`x`, `y`, `viewBox` and `preserveAspectRatio`); `GetTransformToOutermostContentBox` (an element to the
  content box of its outermost `svg`), `GetCtm`/`GetScreenCtm` (`getCTM()` stops at the nearest `svg`, whose viewport is
  included; `getScreenCTM()` maps the content box of the outermost `svg` with `LayoutEngine.MapToRoot`, so the CSS transforms
  and the scroll positions apply). The matrices exposed to scripts snap the rounding errors of the trigonometry.
- **Shapes** (`SvgShapeGeometry`): the path equivalent to each shape (SVG 2), flattened to polylines within 0.002 user
  units; `IsPointInFill` (nonzero/evenodd winding, the boundary included), `IsPointInStroke` (`SvgStrokeStyle`: the
  segments widened by half the width, the joins (miter with its limit, bevel, round), the caps, the zero-length subpaths,
  the dashes restarted per subpath and scaled by `pathLength`, a transform for `vector-effect: non-scaling-stroke`) and
  `Intersects` (for `getIntersectionList()`, `SvgIntersection`). `SvgPathGeometry` gives the lengths, the points and the
  tangents along a path. `SvgClipping` tests the `clipPath` of an element (the fill of its shapes and texts, with
  `clipPathUnits`).
- **Bounding boxes** (`SvgBoundingBox`): see [Geometry](computed-style.md#geometry); `GetBoundingQuad` for
  `getClientRects()`. The `auto` size of an `image` is the default sizing of its natural size
  (`SvgImageElement.GetUsedSize`, once loaded).
- **Text layout** (`SvgTextLayout`, [SVG 2 text layout algorithm](https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm)):
  computed per `text` element on demand and cached on it (`SvgTextElement.CachedTextLayout`) until the style version or
  the viewport of its outermost `svg` change. The content (the texts of the `text`, `tspan`, `a` and one level of `textPath`
  elements that are not `display: none`) is processed for white space on a single line (`white-space-collapse`; the SVG
  user agent style sheet maps `xml:space="preserve"` to `preserve-spaces`); the addressable characters are the UTF-16 code
  units left (`SvgTextCharacter`: the element, the text node and its offset, the position on the baseline, the progression
  and the across vectors, the advance, the glyph cell, the rotation). The embedding levels of the characters are resolved
  by the Unicode Bidirectional Algorithm (`BidiResolver`: the `text` element is the block container of the paragraph, whose
  direction is its `direction`; the `unicode-bidi` of the text content elements inserts the bidi control codes of CSS Writing
  Modes; nothing is resolved for a left-to-right text without right-to-left characters). The runs of the same style and
  level are shaped in the direction of their level (`TextShaper.Shape`: the fonts, the kerning, the spacing, the vertical
  orientation), the advance of a typographic character (a cluster) is on its first code unit, and the characters are laid
  out on a line in visual order (`LayoutLine`, rules L1 and L2): the position of a character of a right-to-left run is its
  line-right edge, and its progression is against the direction of the line (`SvgTextCharacter.IsRightToLeft`).
  Then, in logical order: the `x`, `y`, `dx`, `dy` and `rotate` lists of the positioning
  elements (the descendants override their ancestors), the `textLength`/`lengthAdjust` of the elements from the innermost
  (spacing distributes the difference between the typographic characters; `spacingAndGlyphs` stretches the positions, the
  advances and the glyphs, `SvgTextCharacter.GlyphScale`), the absolute positions per anchored chunk (a new chunk at an absolute position in the inline axis and at
  the start and the end of a text path), `text-anchor` per chunk, and the text paths (the `path` attribute when it has a
  segment, else the shape referenced by `href` with its transform; `startOffset` with the `path-length` of the shape; the middle of each
  character at its distance along the path, hidden beyond the ends of an open path, the `dy` shift along the normal; the
  inline axis of the text on a path is the distance along the path, in visual order). The first character (in logical
  order) starts an anchored chunk at the origin when it has no `x` or `y`; a chunk is anchored by its visual extent (its
  line-left edge for `text-anchor: start` in a left-to-right paragraph, its line-right edge in a right-to-left one); the
  spacing of `textLength` is distributed between the typographic characters in visual order.
  A `text` element with an [`inline-size`](https://svgwg.org/svg2-draft/text.html#InlineSize) (the logical name of `width`,
  of `height` in vertical text; a percentage of the size of the SVG viewport; `auto` or zero: no wrapping) is
  **auto-wrapped** (`BreakLines`, `PositionLines`): its content is broken at the soft wrap opportunities of CSS Text
  (`LineBreaker` with `line-break`, `word-break`, `overflow-wrap`, `text-wrap-mode`; the forced breaks of the preserved line
  feeds; the spaces at the ends of the lines hang) in the lines of a block of that inline size, each line is shaped and
  reordered on its own, the line boxes are stacked in the block direction (a line box is as tall as the strut of the `text`
  element and the layout bounds of its characters, the half-leading of `line-height` included; to the left in `vertical-rl`,
  to the right in `vertical-lr` and `sideways-lr`), the first baseline is at the `x` and `y` of the first character, and
  each line is anchored there by `text-anchor`. The other positioning attributes (`x`, `y`, `dx`, `dy`, `rotate`),
  `textLength` and the text paths are ignored in an auto-wrapped text, as SVG 2 says. No browser wraps the SVG texts (the
  WPT references hard-code the lines of another font). The
  glyph cells extend from the ascent to the descent of the first available font (centered in vertical text), moved by
  `dominant-baseline`/`alignment-baseline` (the SVG 1.1 values `text-before-edge` and `text-after-edge` are legacy aliases of
  `text-top` and `text-bottom`) and `baseline-shift` (lengths, percentages of the line height, `sub`/`super`;
  in horizontal text, `top`/`center`/`bottom` align the aligned subtree of the element with the line box of the text, which
  is as tall as the strut of the `text` element, the other characters, then the aligned subtrees, `ApplyLineRelativeShifts`;
  as in Firefox, a `y` attribute then gives the position of the shifted baseline of its character); the positions stay on the
  alignment point (as in the browsers). `getRotationOfChar()` adds 90 degrees for the sideways glyphs of vertical text (-90 in `sideways-lr`).
- **Hit testing** (`HitTesting.HitTestSvgChildren`): the elements of an SVG document fragment are tested in reverse document
  order, the point mapped to their user space: the shapes with their fill and stroke areas for their `pointer-events`
  (`visiblePainted`...; `visibility` and the `fill`/`stroke` paints), the glyph cells of a text (the hit element is the
  innermost element of the character, with its own `pointer-events` and `visibility`), the bounding box of an `image` and of
  the elements with `pointer-events: bounding-box`, the content referenced by a `use` (the `use` element is hit), the
  `clipPath` of the element and of its containers, and the fragments of a `foreignObject` (`PaintOrder.CollectStackingContext`).
- **foreignObject** (`Css/Layout/Replaced/SvgForeignObjectLayout.cs`): after the sizing of the replaced box of an outermost
  `svg`, its `foreignObject` boxes are laid out as block formatting context roots with the fixed size of their `width` and
  `height` (zoomed); their fragments are children of the `svg` fragment at its content box, with
  `BoxFragment.SvgTransform` (`LayoutTransforms.GetMatrix` returns it): the position `x`, `y` and the transforms of the
  element and of its SVG ancestors to the content box. A `foreignObject` establishes a stacking context (it is painted and
  hit with the SVG content: `PaintOrder` does not descend into the replaced boxes) and the containing block of its
  positioned descendants, and it is the `offsetParent` of their fixed positioned descendants. An `svg` element whose parent is
  a `foreignObject` is an outermost `svg` (a replaced box).
- `getIntersectionList()`, `getEnclosureList()`, `checkIntersection()`, `checkEnclosure()` (`SvgIntersection`): the
  rendered graphics elements that can be hit (`pointer-events` is not `none`), tested with their painted geometry (shapes)
  or their bounding box, clipped by the bounding box of their `clipPath`, in the user space of the `svg`; a container
  intersects when all its graphics elements do (as in the browsers). `deselectAll()` clears the selection;
  `selectSubString()` selects the DOM range of the characters.
- The root `svg` of a document is 100% of the viewport (`svg:root` in the SVG user agent style sheet).
- `text-transform` applies to the characters of the SVG texts (the transformations into one character; one into several
  characters keeps the original character, the addressable characters being the ones of the DOM).
- Not done: the content areas of `shape-inside` and `shape-subtract` (with `shape-padding` and `shape-margin`; the text is
  not wrapped), `text-align`, `text-indent` and the hyphenation in the auto-wrapped texts, the preserved line breaks of the
  texts without `inline-size` (the pre-formatted multi-line text of SVG 2, which no browser implements: one line),
  `side`/`method`/`spacing` of `textPath`, nested `textPath`, the
  markers in the bounding boxes, the `clipped` option of `getBBox()`, the masks and the `clip-path` basic shapes in the hit
  testing. The geometry uses the animated values of the SMIL animations (see [animations.md](animations.md#svg-animations-smil)).
  The SVG content is painted by the SVG painting (see [SVG painting](#svg-painting)).

### Box alignment (`Css/Layout/BoxAlignment.cs`)

[CSS Box Alignment 3](https://drafts.csswg.org/css-align-3/) is shared by the layout modes: `BoxAlignment` resolves the
computed values (`Alignment`, see [Typed style](#typed-style-csslayoutstyle)) in an axis of an alignment container
(`AlignmentAxis`) to an `AxisAlignment` (`Start`/`End`/`Center` relative to the side the positions are measured from,
`Stretch`, `Baseline`/`LastBaseline`, the distributions); the layout modes decide what `normal` means, which boxes share a
baseline and the default overflow alignment.

- **Axes** (`AlignmentAxis`): `Start` is the side the positions are measured from (the main-start or cross-start side of a
  flex container, the start side of the writing mode of the container otherwise), `WritingModeStart` the side of `start`,
  `FlexStart` the side of `flex-start` (the main/cross-start side in a flex container, `start` elsewhere), `self-start`/
  `self-end` the start side of the writing mode of the alignment subject in the physical orientation of the axis, `left`/
  `right` the physical sides of a horizontal axis, the line-left/line-right sides of a vertical inline axis, else `start`.
- **Self-alignment** (`ResolveSelfAlignment`, `GetOffset`): the offset of the subject for its free space; `safe` start-aligns an
  overflowing subject. `GetJustifySelf`/`GetAlignSelf` resolve `auto` with the `justify-items` (without `legacy`)/`align-items`
  of the parent box. The baseline values that do not take part in a baseline alignment use their fallback alignment
  (`GetBaselineFallback`: the start, or the end for `last baseline`, of the writing mode of the container, as in the browsers).
- **Content distribution** (`ResolveContentAlignment`, `Distribute`): the offset of the first subject and the space added
  between the subjects; the fallback alignments of a free space that cannot be distributed (`safe flex-start` for
  `space-between`, `safe center` for `space-around` and `space-evenly`, `flex-start` for `stretch`); the baseline values use
  their fallback alignment (the baseline content-alignment is done by the grid layout for its items, see *Baseline
  content-alignment* in [Grid layout](#grid-layout-csslayoutgrid)).
- **Flex and grid** use it for `justify-content`/`align-content` and the self-alignment of their items (see
  [Flex layout](#flex-layout-csslayoutflex) and [Grid layout](#grid-layout-csslayoutgrid)); they keep their own baseline
  sharing groups.
- **Block containers** ([§5.1](https://drafts.csswg.org/css-align-3/#distribution-block)): `align-content` other than `normal`
  makes the box an independent formatting context (`BlockFormattingContext.EstablishesNewFormattingContext`) and
  `FormattingContext.Layout` moves its contents (`FragmentBuilder.MoveChildren`: the children, the line boxes, the floats and the
  static positions of the absolutely positioned boxes, and the baselines) by the alignment of its automatic block size (which
  includes the floats of a formatting context root) in its content box. The distributions use their fallback alignment;
  without `safe`/`unsafe`, the alignment is `safe` (the overflowing contents of a scroll container are start-aligned too: the
  extension of the scrollable overflow area of [§5.3](https://drafts.csswg.org/css-align-3/#overflow-scroll-position) is not
  implemented). The table cells map `align-content` to the alignment of the table layout (`TableLayoutAlgorithm.GetAlignment`:
  `normal` uses `vertical-align`, `baseline` the row baseline, the others top, middle or bottom).
- **Block-level boxes** ([§6.1](https://drafts.csswg.org/css-align-3/#justify-block), `FormattingContext.GetBlockJustification`):
  the `justify-self` of an in-flow block-level box of a block container (not of an anonymous box, an internal table box or an
  item of another layout) other than `normal` makes its automatic inline size `fit-content` (except `stretch`) and, without
  `auto` inline margin, aligns its margin box in the containing block (reduced by the floats for a box placed next to them):
  `ResolveAutoInlineMargins` adds the offset to its inline-start margin (the default overflow alignment is unsafe). An
  orthogonal box is aligned once its block size is known (`stretch` stretches its block size). The block-level tables too
  (`TableWrapperFormattingContext`).
- **Absolutely positioned boxes** ([CSS Position 3 §4](https://drafts.csswg.org/css-position-3/#abspos-alignment),
  `PositionedLayout.AbsoluteAlignment`): `justify-self` in the inline axis of the containing block, `align-self` in its block
  axis (`auto` is `normal`). `stretch` (and `normal` for a non-replaced box that is not a table) gives an `auto` size the
  stretch-fit size of the inset-modified containing block when both insets are set, the other values the fit-content size
  (an explicit `stretch` keeps the stretch-fit size in the ratio-dependent axis of an `aspect-ratio`, which is the axis whose
  size is `auto` when the other one is not; the table wrapper honors it, never smaller than the table; as in Blink and WebKit,
  the fit-content size of a table is computed in at most the inline size of its containing block, even when its
  inset-modified containing block is larger). With both insets set and no `auto` margin, the margin box is aligned in the inset-modified
  containing block: `safe` start-aligns an overflowing box; the default overflow alignment
  ([§4.4.2](https://drafts.csswg.org/css-align-3/#auto-safety-position)) keeps an overflowing box covering the inset-modified
  containing block within the overflow limit rect (the bounding box of the inset-modified containing block and the containing
  block, extended to infinity in the reachable directions of a scrollable axis of a scroll container), else start-aligns it in
  that rectangle. One `auto` inset aligns the box with the other edge; two use the static position (see above).
- **Multi-column containers** ([CSS Align](https://drafts.csswg.org/css-align-3/#distribution-multicol)): `align-content`
  aligns the rows of column boxes and the column spanners as a unit in the content box (with the column rules), like the
  contents of a block container.
- **Baselines of the inline-blocks**: only a block-axis scroll container (and an inline flex container that is one) gets the
  synthesized baseline (its block-end margin edge), as in the browsers; a scroll container in the inline axis only keeps the
  baseline of its contents.
- Not done: the baseline content-alignment of the flex items, of the items of the subgrids and of the grid lanes
  containers, and of the table cells (`align-content: baseline`; the cells are aligned by `vertical-align`), the legacy `text-align:
  -webkit-left | -webkit-right | -webkit-center` values (not parsed), the "smart" default overflow alignment of the other layout
  modes (unsafe).

### Positioned layout (`Css/Layout/Positioned/`)

`PositionedLayout` implements [CSS Position 3](https://drafts.csswg.org/css-position-3/) (the relative and absolute
positioning), `StickyPosition` the sticky positioning and `StackingContexts` the stacking information.

- **Containing blocks** (`PositionedLayout.FindContainingBlock`, [§2.1](https://drafts.csswg.org/css-position-3/#def-cb)): the
  ancestors are walked with `GetPositioningParent` (`SplitInlineParent ?? Parent`, so a box between the parts of a split
  inline box is in it). For `position: absolute`: the nearest positioned ancestor, or the nearest one that establishes a
  containing block for the fixed positioned boxes (`LayoutStyle.EstablishesFixedPositionContainingBlock`: transforms,
  perspective, filters except on the root element, an offset path, layout/paint containment, the `will-change` of these
  properties, `LayoutStyle.WillChangeProperties`, where the legacy name aliases such as `-webkit-perspective` are
  resolved...) or has `will-change: position`; the root element only when
  it is positioned; else the viewport box (the initial containing block). For `position: fixed`: the nearest ancestor that
  establishes a containing block for the fixed positioned boxes, else the viewport box (the viewport). The non-atomic inline
  boxes only establish one when they are positioned, or have a filter (`filter`, `backdrop-filter`, or their `will-change`;
  the transforms do not apply to them); an inline box is represented by its first part. The boxes of the top layer are children of the viewport box.
- **Out-of-flow candidates**: an absolutely positioned box is not laid out by its parent. Its parent's formatting context
  records its static position in its `FragmentBuilder` (`AddOutOfFlowCandidate`: an `OutOfFlowCandidate` with its containing
  block, its *host* and the edges of its margin box at the static position, from the writing mode and the direction of the
  static-position containing block); `FragmentBuilder.Build` converts the static position to a physical point. At the end of
  `FormattingContext.Layout` (and of the viewport layout), `LayoutOutOfFlowDescendants` collects, in tree order, the candidates
  of the builder and the ones of the children (`BoxFragment.PendingOutOfFlow`, and the atomic inlines in the line boxes:
  `Fragment.HasPendingOutOfFlowDescendants`), translating their static positions; the ones whose host is the box are laid
  out and their fragments appended to its children (`BoxFragment.AppendOutOfFlowChildren`, which updates the overflow), the
  others go up in `PendingOutOfFlow`. The host is the containing block, or for an inline box its block container (the parent
  of the anonymous blocks of its parts when it is split around blocks), whose fragment contains all its fragments. The
  viewport lays out all the remaining candidates. The candidates whose containing block is an inline box that is a direct
  content of a multi-column container (its host is the container, whose fragments are the columns) are laid out in the strip
  of the columns before it is fragmented (`LayoutInlineContainingBlockCandidatesOfStrip`): the containing block is formed
  by the fragments of the inline box stitched together, and the fragment of the box is fragmented with the strip. So the fragment of an absolutely positioned box is a child of the fragment
  of its containing block (of the root fragment for the viewport), after the in-flow children, and contributes to its
  scrollable overflow (the fixed positioned boxes of the viewport do not).
- **Static position** ([§4.2](https://drafts.csswg.org/css-position-3/#staticpos-rect)): in a flex container, the box is
  aligned as the sole flex item (`justify-content` and its `align-self`: a start, end or center edge of its margin box at a
  point of the content box, `FragmentBuilder.AddOutOfFlowCandidate(box, position, inlineEdge, blockEdge)`); in a block formatting context, the
  content box in the inline axis at the current block position (after the margins collapsed so far); in an inline
  formatting context, the inline position of the box on its line and the line box in the block axis when its original display (the
  cascaded `display` before the blockification) is inline-level, else the edge of the content box in the inline axis (it does
  not avoid the floats, as in the browsers) at the block-start of the line, or of the next line when content precedes it on
  the line: a text, an atomic inline, or the start edge of an inline box with a margin, a border or a padding (the line is then
  not a zero-height line box); it moves with the relatively positioned inline boxes it is in. A box whose original display is
  inline-level has its inline-start edge at that position in the direction of its bidirectional embedding level (in a
  left-to-right run of a right-to-left block container, the box extends to the right, as in Blink); at a line break it stays
  at the end of the line, with the starts of the inline boxes before it that have no margin, border or padding (the line may
  break after the box, so an inline box that starts with it has a fragment on that line, which is the start of the
  containing block that it forms). Among block-level boxes (no line box), a box whose original display is inline-level is
  placed as on an empty line box at the current position: in the space that the floats leave, aligned by `text-align`
  (`BlockFormattingContext.GetEmptyLineStaticOffset`). In the block and inline layouts, the box is aligned in this static-position rectangle by its `justify-self` and
  `align-self` (`PositionedLayout.GetStaticPositionAlignment`: `auto` is the `justify-items` of its parent box when it is a
  block container; as in the browsers, the `align-items` of a block container and the `*-items` of an inline box, which do
  not apply to their in-flow children, are not used). The boxes of the top layer and
  an absolutely positioned root element have the static position 0 in the initial containing block.
- **Sizes and position** (`LayoutAbsolutelyPositionedBox`, [§4](https://drafts.csswg.org/css-position-3/#abspos-layout), the
  results of CSS 2.1 §10.3.7, §10.3.8, §10.6.4 and §10.6.5 in every writing mode): in each axis of the containing block (its
  padding box; for an inline box, the rectangle from the start padding edges of its fragments on its first line to the end
  padding edges of its fragments on its last line, in its writing mode, ignoring the fragments on empty lines: the
  bidirectional reordering may split it in several fragments on a line, whose union is used, as in Blink; the tentative WPT
  tests of an inline box whose `direction` is not the one of its block container expect the direction of the block container,
  as Blink does), the insets give the inset-modified
  containing block: both `auto` use the static position (the start or end edge, from the static-position containing block, or
  the center: `OutOfFlowCandidate.CentersHorizontally`/`CentersVertically`, an inset-modified containing block centered on it,
  of zero size when the static position is outside of the containing block: the absolute values of the distances of the
  specification would mirror it, the browsers keep the box centered on it),
  one `auto` is 0 and the box is aligned with the other edge, a negative size reduces the weaker inset. The box is laid out by
  its formatting context in a `ConstraintSpace` whose available sizes are the inset-modified containing block and whose
  percentage bases are the containing block; an `auto` size is the stretch-fit size (`StretchInlineSize`/`StretchBlockSize`)
  for a non-replaced box that is not a table with both insets set in the axis (the form controls stretch too, as in the
  browsers: they are not replaced elements), else the fit-content size (the shrink-to-fit
  size, see [Floats](#floats-csslayoutblock)); with an `aspect-ratio` and both sizes
  `auto`, the stretch-fit size only applies to the ratio-determining axis (the inline axis, unless only it has an `auto`
  inset). Then the `auto` margins share the remaining space when both insets are set (in the inline axis of the containing
  block, a negative space goes to the end margin), else they are 0; an over-constrained box is aligned with the start edge.
  The fragment gets its `Offset`, its used `Margin` and its `ResolvedInsets`. The box alignment properties (`justify-self`,
  `align-self`, `place-self`) apply in the inset-modified containing block, see [Box alignment](#box-alignment-csslayoutboxalignmentcs);
  for the anchor positioning see [Anchor positioning](#anchor-positioning-csslayoutpositionedanchorpositioningcs). The boxes whose
  containing block is an inline box of the host are laid out first (the deepest inline boxes first), the fragments are appended in
  tree order; the candidates of a box laid out by its host (a fixed positioned box in an absolutely positioned box) follow it.
- **Relative positioning** (`SetRelativeOffset`, `GetRelativeOffset`, [§3.3](https://drafts.csswg.org/css-position-3/#relpos-insets)):
  the offset is computed from the insets when the box is laid out (percentages of the containing block, a percentage of an
  indefinite height behaves as `auto`; when both insets of an axis are set, the end one, in the writing mode of the
  containing block, is ignored) and stored in `BoxFragment.RelativeOffset`; `FragmentBuilder.Build` adds it to the position of
  the fragment, so the overflow of the ancestors includes it. The inline boxes get it when the inline layout creates their
  fragments (their content moves with them), the table wrapper box (not the table grid box) of a table. A block (or float)
  split out of relatively positioned inline boxes (`SplitInlineParent`) is moved by their offsets too
  ([CSS 2 §9.2.1.1](https://drafts.csswg.org/css2/#anonymous-block-level)).
- **Sticky positioning** (`StickyPosition.Apply`, [§3.4](https://drafts.csswg.org/css-position-3/#stickypos-insets)): after the
  layout pass (the fragments of the sticky boxes are collected in tree order by `LayoutEngine.IndexFragments`), the
  constraints of each fragment are computed (`StickyPositionConstraints`): the nearest scroll container that can scroll
  horizontally and the one that can scroll vertically (the viewport otherwise), the sticky view rectangle (their content
  boxes, as in the browsers, inset by the insets, whose percentages refer to their sizes), the border box of the box at its
  position in the flow and the content box of its containing block minus its computed margins (an `auto` margin and the
  margin of an over-constrained box are not margins: the box moves in that space, as in the browsers), in the coordinates of
  the root at the scroll positions 0 (the containing block of a box whose containing block is a scroll container is its
  scrollable contents, as in Blink; the anonymous block boxes around the inline content of a block container are skipped: the
  containing block of a sticky inline box is the block container). A box in a fixed positioned box of the viewport is
  constrained by the viewport without its scroll position (it does not move when the viewport scrolls). `ComputeOffset(scrollPositions)` gives the offset (the end edges first, then the start edges computed on
  the box shifted by them, so that the start edges win, limited by the containing block, as in Blink) for the current scroll positions of the scroll containers; it is added to the `Offset` of
  the fragment (`StickyOffset`). `Apply` runs again after each scroll (the previous offsets are removed first). Nested sticky
  boxes are constrained at the shifted position of their ancestors. The absolutely positioned boxes whose containing block is
  a sticky inline box move with its first fragment (their fragments are children of its block container). The sticky offset
  does not change the overflow of the ancestors. A block-level box split out of a sticky inline box does not follow the
  offset of the inline box (not implemented).
- **Resolved values** ([CSSOM](https://drafts.csswg.org/cssom/#resolved-values)): `ComputedStyle.GetResolvedValue` returns
  `BoxFragment.ResolvedInsets` (a forced layout, divided by the effective zoom) for the insets of a positioned box: the used
  values, the specified values in px for the inset of an over-constrained box, `auto` for an `auto` inset of a sticky box.
- **Stacking contexts** (`StackingContexts`): `GetZIndex` (positioned boxes, flex and grid items, the top layer) and
  `EstablishesStackingContext` (the root, the top layer, fixed and sticky, a `z-index` that applies, `opacity` < 1, the
  properties that establish a containing block for the fixed positioned boxes, `isolation`, `mix-blend-mode`, `clip-path`,
  masks, `view-transition-name`, and the `will-change` of these properties, of `position`, and of `z-index` on the boxes it
  applies to; the properties targeted by the animations of the element that are current or in effect are handled as if
  `will-change` named them, `LayoutStyle.WillChangeProperties`, https://drafts.csswg.org/web-animations-1/#side-effects-section),
  exposed by `BoxFragment.ZIndex`/`EstablishesStackingContext`; the painting
  order and the hit testing build the stacking context tree from them (`PaintOrder`, see
  [CSSOM View geometry and hit testing](#cssom-view-geometry-and-hit-testing-csslayouthittesting)).
- **Grid containers**: the containing block of a box whose containing block is a grid container is the grid area of its
  grid-placement properties (`GridContainerState.GetAbsoluteArea`, see [Grid layout](#grid-layout-csslayoutgrid)); the static
  position of a child of a grid container is aligned in its content box, or in that grid area when the container is the
  containing block (`OutOfFlowCandidate.StaticPositionIsContainingBlock`).
### Anchor positioning (`Css/Layout/Positioned/AnchorPositioning.cs`)

[CSS Anchor Positioning 1](https://drafts.csswg.org/css-anchor-position-1/): an absolutely positioned box is positioned and sized
relative to anchor boxes. `PositionedLayout.LayoutAbsolutelyPositionedBox` hands the boxes that use it (an anchor function, `position-area`,
`position-anchor` other than `normal`/`none`, `position-try-fallbacks` or `anchor-center`) to `AnchorPositioning.Layout`, which lays
them out with `PositionedLayout.LayoutInContainingBlock` and `AnchorLayoutParameters` (per physical axis: the `auto` insets and margins
that are 0, the default alignment of `position-area`, the center of the default anchor box, the overflow limit rect).

- **Values** (`Css/Values/CssAnchorFunction.cs`, `CssCalc`): `anchor()` and `anchor-size()` are parsed (the canonical form puts the
  name first and serializes the fallback as a specified value), allowed in the math functions of the properties whose grammar has
  them (`CssCalcContext.AnchorFunctions`, kept as keyword nodes of type `<length>`), and kept in the computed values with their fallback
  and their side computed. The anchor functions of a box that is not absolutely positioned compute to their fallback, else the
  declaration is invalid at computed-value time (`CssValueComputer.Compute`). `position-visibility` accepts the legacy aliases
  `anchors-valid`/`anchors-visible`; `position-area` computes to its short logical keywords (`CssPropertyRules.ComputePositionArea`).
  `@position-try` rules (`CssPositionTryRule`, `CSSPositionTryDescriptors`: the accepted @position-try properties, no `!important`)
  cascade like `@keyframes` (`StyleEngine.FindPositionTry`, tree-scoped).
- **Target anchor element** (`AnchorQuery`): the anchor names are indexed once per pass (`LayoutContext.AnchorNameIndex`, the
  principal boxes with `anchor-name` in box tree order); a name looks up the nearest ancestor (flat tree) with the name, else the last
  one, among the anchors whose name loosely matches (the tree of the `anchor-name` declaration is the tree of the reference or an
  ancestor tree), in scope (`anchor-scope` of an inclusive ancestor of the anchor: the box must be a descendant; and the nearest
  ancestor of the box whose `anchor-scope` scopes the name, declared in the tree of the reference, must contain the anchor: "limits
  descendants to only match these anchor names to anchor elements within this subtree", e.g. an `anchor-scope` of a `::part()` rule
  for the slotted elements) and acceptable: laid
  out strictly before the box, i.e. its chain of containing blocks reaches the containing block of the box through in-flow
  descendants, or an absolutely positioned box of the same containing block already laid out (as the boxes of a containing block are
  laid out in tree order, top layer last, this is the spec's top layer and tree order rule). The containing block itself is not
  acceptable. `position-anchor: auto` (and `normal` with a `position-area`) uses the implicit anchor element: the source of a showing
  popover (`HTMLElement.ImplicitAnchorElement`), the originating element of a pseudo-element; `match-parent` the default anchor of
  the parent.
- **Anchor box** (`AnchorBox`): the fragment of the last layout of the anchor box (`ContainerBox.LastLayoutFragment`, set by
  `LayoutContext.LayoutBox`; the fragments of an inline box are searched in the fragment of its block container) is mapped to the
  fragment of the host through the transforms and the scroll offsets (the bounding box), or searched in the fragment tree of the host
  (the slices of a fragmented box). The rectangle is kept at the scroll positions 0 with the scroll containers between the anchor and
  the containing block (and the viewport scroll for a fixed positioned box of the viewport whose anchor scrolls with the document).
- **Style and layout interleaving**: a position option and the resolved anchor functions are applied as declarations of the position
  fallback origin (`ComputedStyle.WithPositionOption`: they win over the cascaded values but the important ones, the resolved values
  replace their declaration; the logical properties are mapped first). The box is laid out with that style (`box.Style`, whose
  `PositionBaseStyle` is its computed style, restored before each layout). `getComputedStyle()` and the computed style map read the
  accepted @position-try properties from it (`ComputedStyle.TryGetAnchorPositionedStyle`): the insets, sizes and margins in px. The
  children still inherit from the computed style (`inherit` of an anchor function is not resolved). `revert` in a position option
  is `unset` (the position fallback origin is not a separate origin of the cascade), `revert-layer` and `revert-rule` are ignored.
- **Anchor functions** (`AnchorFunctionEvaluator`): `anchor()` gives the inset that aligns the edge of the inset-modified containing
  block with the edge of the anchor box (`inside`/`outside` from the property, `start`/`end` in the writing mode of the containing
  block, `self-*` in the one of the box, a percentage from the start side; a physical side of the other axis is unresolvable),
  `anchor-size()` the width or height (logical keywords from the writing modes, the axis of the property when omitted); the result
  is divided by the effective zoom.
- **position-area**: the area of the position-area grid (the lines of the containing block and of the default anchor box, the outer
  ones extended to the anchor box) replaces the containing block; `auto` insets and margins are 0; `normal` aligns toward the anchor,
  `center` for the center track, `anchor-center` for the three tracks, toward the non-`auto` inset (unsafe) when only one inset is
  `auto`; the default overflow alignment keeps the box in the original containing block (the overflow limit rect extends to
  infinity in the reachable scrollable directions of a scroll container, the viewport included for the initial containing block,
  and to the scrolling area of the viewport for a fixed positioned box whose default anchor scrolls with the document). The scrollable containing block (the padding
  box extended to the in-flow contents, without their relative offsets and transforms, csswg-drafts#12552) replaces the local
  containing block of a scroll container when the box has a default anchor.
- **anchor-center**: the margin box is centered on the default anchor box, clamped to the inset-modified containing block (whose
  `auto` insets are 0), and kept in the containing block by the default overflow alignment when it is larger (as in the browsers).
- **Position options**: the base styles then the entries of `position-try-fallbacks` (a `@position-try` rule with try tactics, or a
  `<position-area>`), sorted by `position-try-order` (the size of the inset-modified containing block, `auto` insets as 0; the base styles are
  sorted with the other options, so that a larger option is used first even when the base styles do not overflow). The try
  tactics (`TryTactics`) map the physical sides (`flip-block`, `flip-inline`, `flip-x`, `flip-y`, `flip-start`, composed in order) and
  move the accepted @position-try properties (computed values) with them: the sides and percentages of `anchor()`, the axes of
  `anchor-size()`, the self-alignment keywords and the tracks of `position-area`. The last successful position option (recorded with
  the last remembered sizes, `LayoutEngine.RecordLastSuccessfulPositionOptions`; cleared by a fallback-sensitive change, i.e. a change
  of the position, the accepted properties, the `position-try-*` longhands or the referenced `@position-try` rules) is tried first; when
  it overflows its inset-modified containing block (after the default scroll shift), it is tried with the current scroll offsets, then
  the other options; when none fits, the current option is kept (with the current scroll offsets, as in the browsers). The documents
  whose styles declare `position-try-fallbacks` are laid out by every rendering update (`StyleEngine.MayHavePositionTryFallbacks`),
  as the browsers render from the start.
- **Scroll compensation** (`LayoutEngine.AnchorPositioning.cs`, `AnchorPositionState` per element): the anchor references remember the
  scroll offsets of their scroll containers at the anchor recalculation points (the box starts generating boxes, or the position
  fallback is determined again). A box with a default anchor that it references (`position-area`, `anchor-center`, or an `anchor()` of
  the axis in the scrolling context of the default anchor box: same nearest scroll container scrollable in the axis) is moved after
  the layout and after each scroll by its default scroll shift (`BoxFragment.AnchorScrollShift`, part of its offset like the sticky
  offset, so `offsetTop`/`getBoundingClientRect()`/the hit testing see it): the movement of the default anchor box since the layout
  (mapped to the root through the scroll offsets, sticky offsets, default scroll shifts and transforms; the remembered scroll offsets in
  a fragmentation context). After the scroll steps of a rendering update (before the animation frame callbacks), a box with position
  options whose shifted margin box overflows its inset-modified containing block (whose anchor-relative edges follow the shift,
  `InsetModifiedContainingBlockFollower`) is laid out again.
- **position-visibility** (`BoxFragment.IsForceHidden`: not painted nor hit tested): `anchor-valid` (references its default anchor box
  and has none), `anchor-visible` (the default anchor box has `visibility` other than `visible`, or is fully clipped by the overflow
  clip of a box between it and the containing block, or is in a box hidden by `position-visibility`: the chained anchors),
  `no-overflow` (the margin box moved by the default scroll shift overflows the inset-modified containing block after the
  fallback; it also applies to the absolutely positioned boxes without anchor), updated after the layout and the scrolls.
- **Relevance** (https://drafts.csswg.org/css-anchor-position-1/#anchor-relevance): an element with a descendant that is a target
  anchor element of a positioned box (all the options tried) outside of it is relevant to the user (`LayoutEngine.IsAnchorRelevant`).
- **Animations of the anchor functions**: the computed values keep the anchor functions, so an anchor function is interpolated with
  another length (or anchor function) in a `calc()` (`CssGenericInterpolation`: an anchor function is a `<length>`), e.g.
  `calc((0.5 * anchor(--a1 top)) + (0.5 * anchor(--a2 bottom)))` halfway, which the layout resolves (the resolved values of a
  style whose anchor functions are resolved replace its animation and transition values, `ComputedStyle.ResolvedAnchorProperties`).
  The functions of both values are resolved against the current layout (the specification resolves each value when it is
  computed): the result is the same while the anchors do not move.
- **Transitions of the resolved values** (https://github.com/w3c/csswg-drafts/issues/9598: the anchor functions and the declarations
  of the position option are resolved at computed-value time): `AnchorPositioning.Layout` keeps the values of the after-change style
  resolved by the last layout (`AnchorPositionState.TransitionValues`: the properties that accept the anchor functions and the
  declarations of the option), and `CssTransitionUpdater.UpdateFromLayout` starts the transitions of the values that changed while
  the declared values did not (the anchor moved, `position-anchor`, `anchor-name` or the tree scope of a name changed, another
  position option applies); the box is then laid out again with the new transitions (`ComputedStyle.WithCurrentAnimations`). A box
  that becomes anchor-positioned transitions from its before-change style (`StyleEngine.GetBeforeChangeStyle`), one that stops
  being anchor-positioned from its last resolved values. These transitions are `CssTransition.IsStartedByLayout`: the style changes
  do not replace them (the layout does, when the resolved after-change value changes). The style engine includes the declarations
  of the option applied by the last layout in the before-change and after-change styles of the transitions
  (`LayoutEngine.GetAppliedPositionOption`), while `position-try-fallbacks` is the same.
- **Anchored container queries** ([CSS Anchor Positioning 2](https://drafts.csswg.org/css-anchor-position-2/#anchored-container-queries),
  `LayoutEngine.AnchoredQueries.cs`): `container-type: anchored` (which applies style containment) makes an anchor-positioned box a
  query container of `@container anchored(fallback: ...)` (`ContainerQueryParser.ParseAnchoredQuery`: the boolean syntax, the
  `fallback` feature with `none`, a `@position-try` name and try tactics, or a `<position-area-query>` with `any`). The option of
  `position-try-fallbacks` applied by the last layout of the container is kept by element (`RecordAnchoredFallback`, removed when a
  pass does not lay the container out) and the queries are evaluated against it (`ContainerQueryContext.EvaluateAnchoredFallback`:
  a name and try tactics match the same name, not tree-scoped, and the same tactics in the same order; a `<position-area-query>`
  matches the same physical tracks, resolved with the writing modes of the containing block and of the box, `any` matching any
  track, a single unambiguous keyword meaning `any` in the other axis). As for the size containers, the style engine records the
  option each style used (`StyleEngine.RecordQueriedAnchoredFallback`); when `AnchorPositioning.Layout` applies another option,
  the styles of the descendants of the container are computed again (`InvalidateQueryContainerDescendants`, with their
  before-change styles: the transitions compare with the styles before the change), its contents are built again
  (`BoxTree.RebuildContents`) and it is laid out again with the same option (no loop: the option is not chosen again).
  `getComputedStyle()` lays out the document when an ancestor is an anchored container.
- Not done: the anchor functions and the position options are applied at layout time, not at computed-value time with the style
  and layout interleaving: the tree scopes of the anchor names of an interpolated value (the names resolve in the tree of the
  declaration of the after-change value), `inherit` of an anchor function or of a declaration of a position option (the children
  inherit the unresolved function, or the value without the option).

### Overflow and scrolling (`Css/Layout/Scrolling/`)

[CSS Overflow 3](https://drafts.csswg.org/css-overflow-3/) and the scrolling of [CSSOM View](https://drafts.csswg.org/cssom-view/#scrolling).

- **Used overflow** (`ScrollContainers`): `GetUsedOverflow(box)` is the `overflow` of the typed style, `visible` for the inline
  boxes, the internal table boxes (but the cells and captions) and the boxes whose `overflow` is
  [propagated to the viewport](https://drafts.csswg.org/css-overflow-3/#overflow-propagation) (`PropagatesOverflowToViewport`:
  the root element, and the first body child of an html root whose `overflow` is `visible`, unless one of them has
  containment), and `clip` instead of a scrollable value for the replaced elements (they clip, they do not scroll, as in the
  browsers). `IsScrollContainer(box)`. The viewport uses the `overflow` of the root (or of the body, `GetViewportStyles`),
  `visible` being `auto` and `clip` being `hidden` (`GetViewportOverflow`); its `scrollbar-gutter` and `scrollbar-width` come
  from the root element.
- **Classic scrollbars** ([§3](https://drafts.csswg.org/css-overflow-3/#scrollbar-layout)): `ViewportOptions.ScrollbarThickness`
  (15px by default, as Chrome on Windows and Linux, which the web-platform-tests assume; 0 gives overlay scrollbars), 11px for
  `scrollbar-width: thin`, 0 for `none`; they do not scale with `zoom`. The scrollbar gutters are between the border and the
  padding (`BoxFragment.Scrollbars`; `PaddingBoxRect`, the scrollport, excludes them): the vertical scrollbar is on the right,
  on the left in a horizontal writing mode with `direction: rtl` (as in the browsers), the horizontal one at the bottom;
  `scrollbar-gutter: stable` reserves the gutter on the inline edges (the vertical scrollbar in a horizontal writing mode, the
  horizontal one in a vertical writing mode) when `overflow` is `hidden`, `scroll` or `auto`, `both-edges` on the opposite edge
  too (`GetScrollbarSides`). `FormattingContext.Layout` lays out a scroll container with its forced scrollbars (`scroll`) and the
  `auto` scrollbars of its last layout (`ScrollState.Scrollbars`, the first guess), then again when its scrolling area needs
  other `auto` scrollbars (at most twice; a scrollbar added by the second pass is kept, so the result cannot oscillate). The
  boxes that are sized from the intrinsic sizes of a scroll container before its layout (a shrink-to-fit ancestor, the flex
  layout of its parent: `IntrinsicSizing.GetScrollbarGutters`) assume the scrollbars of its last layout too: when a layout
  pass displays scrollbars that were not assumed (`ContainerBox.ScrollbarGuttersPass`/`AssumedScrollbars`, checked when the
  fragments are indexed), `LayoutEngine.UpdateLayout` lays out the document once more, with these scrollbars as the first
  guess ("UAs must start with the assumption that no scrollbars are needed, and recalculate sizes if it turns out they
  are"). The
  gutters are subtracted from the content box (a definite size keeps its `box-sizing` meaning), added to the automatic sizes
  (shrink-to-fit, `auto` block size), and the border box is at least the borders and paddings (a box too small for its gutters is overflowed by them, as in the browsers). The viewport does the
  same (`LayoutEngine.LayoutViewport`): its scrollbars are outside of the initial containing block (the `clientWidth` of the
  root), `innerWidth` includes them. On the first layout of a document, the vertical scrollbar of an `auto` viewport is
  assumed when the text of the in-flow boxes cannot fit in twice the height of the viewport (`EstimateTextBlockSize`: half
  an em wide and 1.2em tall per character), which saves the second layout of a long document at a narrower width (the
  layout result cache keeps nothing once the initial containing block changes); the guess is kept only when the document
  overflows the viewport by at least half its height (the scrollbar cannot make it fit), else the viewport is laid out
  again without scrollbars.
- **Scrollable overflow** ([§2.2](https://drafts.csswg.org/css-overflow-3/#scrollable-overflow-calculation),
  `BoxFragment.ScrollableOverflow`, lazy): the border box and the contributions of the children: the scrollable overflow of a
  child (its border box and its contents) clipped to its overflow clip edge in the axes it clips (`ClipsOverflowX`/`Y`: the used
  `overflow` is not `visible`, or paint containment; `OverflowClipRect`: the padding box of a scroll container, which
  `overflow-clip-margin` can only shrink, or the `<visual-box>` of `overflow-clip-margin` extended by its length for
  `overflow: clip`), transformed by its transform (`LayoutTransforms`: `translate`, `rotate`, `scale`, the offset transform
  of the motion path and `transform` around `transform-origin`, the bounding box of the projected corners), the line boxes
  contributing their contents, and the column boxes of a multi-column container (`BoxFragment.ColumnRects`, as Blink: the
  overflow columns extend it to the end of their column box, not only of their content). The offset transform ([Motion Path](https://drafts.fxtf.org/motion-1/), `MotionPath`) moves
  the anchor point of the box (`offset-anchor`, the transform origin by default) to the point at `offset-distance` along its
  `offset-path` and rotates the box by `offset-rotate` (the direction of the path for `auto`): the basic shapes, `ray()`
  (with its size, `contain` and `at` position; the distance is not clamped), a `<coord-box>` alone (the edge of the box with
  its rounded corners) and the shapes of the SVG geometry elements referenced by `url()` are in the reference box of the
  containing block (the `<coord-box>`, its border box by default), while `path()` is relative to the position of the box
  itself, as in the browsers; `offset-position` is the starting position of the rays and the center of the circles and
  ellipses without position. The distance wraps around a closed path and is clamped to an open one
  (`SvgPathGeometry`, with exact directions on the lines and the arcs). An offset path makes the box a stacking context and
  a containing block, as a transform; the SVG elements without box have the same transform in their user space
  (`SvgUserSpace.GetTransform`). The path of a `<coord-box>` follows the contour of the box when its corners are shaped by
  `corner-shape` (the flattened curves of `CornerContour`, from the end of the top left corner). As in Blink, a box
  with a zero size in one axis contributes its extent in the other one (only the boxes without size are ignored). In a scroll
  container (and the viewport), the children wholly in the unreachable region (before the block-start or inline-start padding
  edge) do not contribute. The fixed positioned boxes of the viewport do not contribute to its overflow. `InkOverflow` is
  computed the same way (without the decorations that do not affect the layout: shadows, outlines, glyph bounds).
- **Scrolling area** (`BoxFragment.GetScrollingArea()`, [CSSOM View](https://drafts.csswg.org/cssom-view/#scrolling-area)): the
  padding box, the contents, and the in-flow contents extended by the paddings (`InFlowBounds`, "to enable scroll positions
  that satisfy [...] place-content: start and place-content: end", as Blink: in the block axis, the content box with the block
  size of the contents including the collapsed margins of the last children, `ContentsBlockSize`; in the inline axis, the
  margin boxes of the in-flow children, floats and line contents at their position before the relative positioning, with their
  computed margins), clamped at the scroll origin (the block-start and inline-start padding edges: `ClampToScrollOrigin`). The
  viewport uses the margin box of the root element as its in-flow contents. `scrollWidth`/`scrollHeight` are its size.
- **Scroll positions** (`ScrollState`, kept by the `LayoutEngine` per element (its principal box; `ViewportScrollState` for the
  viewport) across the layout passes; `BoxFragment.ScrollState` links the fragment of the last layout): `Offset` is the
  physical translation of the contents from the scroll origin, i.e. (`scrollLeft`, `scrollTop`) in the coordinates of the
  fragment tree (negative when the overflow extends leftwards or upwards: `direction: rtl`, `vertical-rl`). After each layout
  (`IndexFragments`), the states get the new scrolling area and scrollport and their positions are clamped (a clamped position
  fires `scroll` and `scrollend`, as in the browsers); the states of the elements that are not scroll containers anymore are
  discarded: as in the browsers, the position of an element that still generates a box (e.g. its `overflow` became
  `visible`) is kept (`_savedScrollPositions`) and restored when it becomes a scroll container again, the one of an element
  that generates no box is reset. An axis whose `overflow` is `clip` or `visible` does not scroll.
  `Fragment.GetOffsetInRoot()` subtracts the scroll positions of the scroll containers (`GetOffsetInRootWithoutScrolling()`
  does not, for the sticky positioning) and adds the one of the viewport to the fixed positioned boxes of the viewport;
  `ToViewport` subtracts the scroll position of the viewport. Scrolling does not lay out again: only the sticky offsets are
  computed again (`StickyPosition.Apply`).
- **Scroll operations** (`LayoutEngine.PerformScroll`, [perform a scroll](https://drafts.csswg.org/cssom-view/#perform-a-scroll)):
  abort the smooth scroll in progress and resolve its promises, then an instant scroll, or a smooth scroll (`behavior: smooth`,
  or `auto` with `scroll-behavior: smooth` on the associated element: an ease-in-out animation of 300 ms, advanced by the
  rendering steps with the time of the frame; a smooth scroll to the target of the smooth scroll in progress does not restart
  it, as in the browsers). The task completes when the scroll completes or is aborted (with `undefined`; the result object
  with `interrupted` of csswg-drafts#12495 is not implemented). The CSSOM View members (`CssomView.Scrolling.cs`) clamp the
  positions, divide/multiply them by the effective zoom, and redirect the root element (the body element in quirks mode) to
  the viewport.
- **Scroll into view** (`ScrollIntoView.Run`, [scroll a target into view](https://drafts.csswg.org/cssom-view/#scroll-a-target-into-view)):
  the bounding border box of the fragments of the target (a sticky box at its unshifted position, as in the browsers) extended
  by its `scroll-margin` is aligned (`block`/`inline`: `start`, `end`, `center`, `nearest`, in the writing mode of each scrolling
  box) with the scrollport of each scroll container of its containing block chain (the fragment ancestors), reduced by its
  `scroll-padding`, then with the viewport (not for a fixed positioned box, except in the axes where an anchor-positioned one
  compensates for the scroll of a default anchor that scrolls with the document), then with the scrolling boxes of the same-origin
  parent documents; `container: "nearest"` stops after the first one. With `nearest`, a target as large as the scrollport is
  aligned like a larger one (as in the browsers). `focus()` (`center`, `center`, unless `preventScroll`), the fragment
  navigations (`start`, `nearest`) and the edition of a text control by the user (`nearest`, `nearest`: the control is
  revealed as a whole, not its caret; a sticky control at its unshifted position, `Editing.RevealTextControl`) use
  `ScrollIntoViewWhenLaidOut`: in a document whose layout was never requested, the scroll
  waits for its first layout (the scroll positions are only observable through the geometry), unless the `scroll`/`scrollend`
  events of the viewport are listened to. Adding a listener of the `scroll`, `scrollend`, `scrollsnapchange` or
  `scrollsnapchanging` events (to any target) registers the document for the rendering updates, which lay it out
  (`LayoutEngine.OnScrollEventListenerAdded`). The positions are rounded to 1e-6 px (the floating-point errors of the
  rectangles mapped through nested scroll containers).
- **Events** ([scrolling events](https://drafts.csswg.org/cssom-view/#scrolling-events)): a changed scroll position appends
  (target, `scroll`) to the pending scroll events of the document (without duplicates), a completed scroll that changed the
  position (target, `scrollend`), and requests the scroll steps of the rendering (`DomEventLoop.RequestScrollSteps`);
  `RunScrollSteps` (after the resize steps) advances the smooth scrolls, then fires the events in order (bubbling at the
  `Document`, not at the elements).
- **History**: the scroll position of the viewport is saved in the session history entry that stops being active
  (`SessionHistoryEntry.ScrollPositionData`) and restored by the traversals when `history.scrollRestoration` is `auto`, and by
  the navigation API (`NavigateEvent.scroll()`, `scroll: "after-transition"`); the `top` fragment scrolls to the beginning of
  the document.
- The boxes of the pseudo-elements that are scroll containers (e.g. a `::before` with `overflow: scroll`, a
  `::scroll-marker-group`) have their own scrolling box (`ScrollState.PseudoElement`, kept by element and pseudo-element): its
  scroll events target the originating element, its `scroll-behavior` and `scroll-padding` are the ones of the box. The wheel
  scrolls the ones at its position first (the target of the event is their originating element).
- The scrolls of the user (the wheel, a panning touch, the keys, the scrollbars; see [User input](browsing-context.md#user-input))
  scroll the nearest scrolling box that can scroll in their direction (`LayoutEngine.ScrollByUser`, `LayoutEngine.UserScrolling.cs`,
  by kind of `UserScrollKind`): the keys and the presses on the track of a scrollbar are relative scrolls from the destination
  of the smooth scroll in progress (`ForDisplacement`: the keys pressed during an animation add up); the wheel is a gesture
  (`ScrollState.UserGestureStart`) of instant scrolls by the deltas of its events (precise deltas, as the wheel actions of
  WebDriver in Blink), without snapping; a panning touch and a drag of the thumb of a scrollbar are direct manipulations
  (`UserGestureHeld`) that follow the pointer, without snapping, until the pointer is released. The wheel and the direct
  manipulations stop the smooth scroll in progress. A gesture ends with a snap (`ForEndAndDirection`), then
  `scrollsnapchange` and a single `scrollend` (no `scrollend` is queued while it lasts); during the gesture, the snap targets
  of the position it would snap to are updated (`scrollsnapchanging`). With `ViewportOptions.SmoothUserScrolling` (the WPT
  runner sets it), the scrolls of the keys and of the track presses and the snap at the end of a gesture are smooth scrolls
  (300 ms, retargeted by the next key), and a gesture of the wheel ends when no wheel event follows for 100 ms
  (`WheelGestureEndDelay`, a timer of the event loop); without it (the default), they are instant and each wheel event is a
  complete gesture. A scroll operation of the page ends the gesture of the wheel without snapping it.
- The classic scrollbars are painted in their gutters and react to the pointer (`ScrollbarGeometry`: see
  [rendering.md](rendering.md#scrollbars) and [browsing-context.md](browsing-context.md#user-input)); their box is hit
  tested as its scroll container. As in Blink, a scrollport without size in one axis gets no automatic scrollbar for the
  other one (`ScrollContainers.GetNeededScrollbars`: the scrollbar would give its thickness to a box that displays
  nothing). The viewport of the document of an `iframe` or `frame` whose `scrolling` attribute is `no`, `off` or
  `noscroll` displays no scrollbar (`ScrollContainers.HidesViewportScrollbars`; it still scrolls), and the `overflow` of a
  `body` element with `display: contents` is not propagated to the viewport.
- Not done: the pinch zoom (the visual
  viewport, see [Viewports](#viewports-layoutengineviewportcs)), the restoration of the scroll positions of the
  elements and across documents (reload, back/forward cache), the hanging spaces (they contribute to the scrollable overflow),
  the `resize` property.

### Text overflow (`Css/Layout/Inline/LineTruncator.cs`, `LineEllipsis.cs`)

[CSS Overflow 4 §text-overflow](https://drafts.csswg.org/css-overflow-4/#text-overflow) and its
[ellipsing details](https://drafts.csswg.org/css-overflow-4/#ellipsing-details). The elision is a rendering effect: it is
computed when the lines are painted, from the fragments of the line in visual order, and does not change the layout (the
geometry APIs, the scrollable overflow and the hit testing see the content as laid out: "Ellipsing only affects rendering and
must not affect layout nor dispatching of pointer events"; `elementFromPoint` on an ellipsis finds the elided inline, as the
browsers).

- **Value** (`TextOverflow`, `TextOverflowSide` of `OverflowStyle`): one value applies to the end edge of the line (the
  line-right edge, or the line-left edge when the bidi paragraph of the line is right-to-left, `LineBoxFragment.IsRightToLeft`:
  `direction`, or the content with `unicode-bidi: plaintext`), two values to the line-left and the line-right edges
  (`GetLineSides`). `ellipsis` and `<string>` elide (`Elides`), `clip` and `fade`/`fade()` only clip (`fade` is not
  implemented).
- **When** (`LineTruncator.GetTextOverflow(line)`, called by `PaintOrder` for each line box when the painting order is collected
  for the painting): the block container of the line (the parent of an anonymous block container in a block formatting context,
  as Blink; not the anonymous flex and grid items) has an eliding value and clips its overflow in its inline axis
  (`overflow: hidden | scroll | auto | clip`, `overflow-x` alone in horizontal text), and the content of the line overflows an
  eliding edge of the line box translated by the scroll position of the block container (the edges of the scrollport along
  the line: scrolling reveals more content and moves the ellipsis, "implementations should [...] display additional content
  that fits"). As the browsers, the lines of an editable block container that contains the focus of the selection (the caret)
  are only clipped ("During user interaction [...] user agents may treat ellipsis [...] as `text-overflow: clip`").
- **Units** (`CollectUnits`): the content of the line in line-relative coordinates (from the line-left edge of the line box,
  `ToLineRange`; after the bidi reordering, the relative positioning and the justification): the extended grapheme clusters of
  the text fragments (the clusters of the shaping merged at the non-boundaries of `GraphemeClusters`, e.g. a spacing mark), the
  atomic inlines (their border boxes), through the inline boxes (the out-of-flow boxes are skipped), sorted in visual order.
- **Truncation** (`Truncate`): the end edge is truncated first (`TruncateRight`, mirrored by `TruncateLeft`): when a visible
  unit overflows the edge, the units that overflow the edge minus the advance of the ellipsis are hidden, but the first visible
  unit from the opposite edge ("The first character or atomic inline-level element on a line must be clipped rather than
  ellipsed": the ellipsis follows it and is clipped by the overflow clip), and the ellipsis is placed "immediately adjacent to
  the end edge of the remaining inline content". Then the start edge with the remaining units; when the two ellipses overlap,
  only the end one is kept ("If there is insufficient space for both [...] only the end ellipsis/string renders"). The result
  (`LineTruncation`) is the visible range of the line (`ClipLeft`/`ClipRight`, the inner edges of the ellipses), the ellipses,
  and the hidden fragments (`IsHidden`: the atomic inlines that overlap an ellipsis or are beyond it, "hidden entirely", and the
  texts without a visible cluster).
- **Painting**: `PaintOrder.PaintLine` skips the hidden fragments (an atomic inline with its descendants), gives the texts and
  the inline boxes of the line the truncation (`PaintItem.Truncation`), and adds the fragments of the ellipses after the content
  of the line; the painter clips the texts and the inline boxes (their backgrounds, borders and decorations) to the visible range
  of the line (`ApplyTruncationClip`, `LineTruncation.GetContentClip`: along the line only). The mask of `background-clip: text`
  truncates the lines the same way (with the glyphs of the ellipses).
- **Ellipsis** (`LineEllipsis`, the reusable part with `LineTruncator.Truncate(line, leftEdge, rightEdge, leftEllipsis,
  rightEllipsis)`): `Create(line, text)` shapes the string (`null`: U+2026, or `...` when the first available font has no glyph
  for it, as Blink; a `<string>` has its segment breaks (line feeds, carriage returns, U+2028, U+2029) collapsed and transformed as
  collapsible segment breaks, `GetText`) in the style of the root inline box of the line (`LineBoxFragment.Style`: the style of the
  block container, or its `::first-line` style on its first line): its fonts, its color, its decorations (the run of the
  ellipsis is a generated `TextRun` whose parent is the block container, so it gets the decorations of the block container and of
  its decorating ancestors), its visibility; no emphasis marks (`TextFragment.IsEllipsis`). It is "treated as if it were wrapped in
  an anonymous inline with `unicode-bidi: isolate`": a text fragment per bidi level run of the string resolved with the direction
  of the paragraph (`BidiResolver.GetVisualRuns`, mirrored glyphs in the right-to-left runs), in visual order. `Place(line,
  lineLeft)` attaches the fragments to the line box (`Fragment.AttachTo`: they are not in its children, so the geometry, the
  hit testing and the fragment index do not see them) with the baseline of the root inline box (`LineBoxFragment.Baseline`; the
  content area of the first available font, `InlineLayoutAlgorithm.GetContentArea`), in any writing mode.
- **`block-ellipsis`** (see [Line clamping](#line-clamping-csslayoutblocklineclampcs)): the layout reserves the space of the
  ellipsis at the end of the last line before the clamp point (`LineEllipsis.Create` gives its advance before the line is broken),
  then places it after the remaining content (`LineEllipsis.Place`) and stores it in `LineBoxFragment.BlockEllipsis`, which
  `PaintOrder` paints after the content of the line like the ellipses of `text-overflow`. `LineTruncator.Truncate` truncates a
  line at an edge when the content is not displaced by the layout (the legacy ellipsis of `-webkit-line-clamp`,
  `LineTruncator.GetTruncation`).
- **Text controls**: the lines of the inner editor of a text field (which has the `text-overflow` of the control and clips its
  line) and of a `textarea` (whose `text-overflow` applies to the lines of its inner editor, clipped and scrolled by the
  `textarea`: `GetTextOverflowOwner`) are truncated as the other lines, at the scroll position of the value, unless the
  control has the focus. The text of the date and time fields, painted without boxes (see
  [Painting](#painting-csslayoutpainting)), is truncated the same way by `Painter.TruncateControlText` (the clusters of the
  shaped text).
- **Fonts**: the ellipses are shaped when the lines are painted, too late to start the loads of their web fonts: the font face
  set loads the faces used by the ellipses of the elements whose `text-overflow` elides with the faces used by the texts
  (`FontFaceSet.LoadUsedFontFaces`; the first family that has font faces: the next families, e.g. for the characters that
  the first one does not have, are requested by the layout that the set updates before it is loaded, see
  [Fonts](computed-style.md#fonts)), and the faces used by the generated content: the strings of the `content` of `::before`,
  `::after` and `::marker`, and the list markers (the representation of the counter is not computed there: the digits and
  the basic Latin letters stand for it).
- Not done: `fade`/`fade()` (clipped), the selection of the ellipsis
  ("selecting the ellipsis should select the ellipsed text"), the ruby annotations (the ruby layout is not implemented).

### Line clamping (`Css/Layout/Block/LineClamp.cs`)

[CSS Overflow 4 §line-clamp](https://drafts.csswg.org/css-overflow-4/#line-clamp): the `line-clamp` shorthand (`max-lines`,
`block-ellipsis`, `continue`) and the legacy `-webkit-line-clamp`. The content after the clamp point of a line-clamp container is
laid out but hidden (`continue: collapse`, as Blink), and the container is sized as if it had no content after the clamp point.

- **Line-clamp containers** (`LineClamp.IsLineClampContainer`): the block containers laid out by the block or inline layout (not
  the inline boxes, the anonymous boxes, the multi-column containers: `continue: collapse` "acts as `auto`" there) whose
  `continue` is `collapse` or `discard` (`LayoutStyle.Continue`), or `-webkit-legacy` when the specified `display` is
  `-webkit-box`/`-webkit-inline-box` with `-webkit-box-orient: vertical` (that display computes to `flow-root`/`inline-block`;
  `ComputedStyle.IsLegacyLineClampBox` records it when the display is computed: a `flow-root` with `-webkit-line-clamp` is not
  clamped). A line-clamp container establishes an independent formatting context; the state of the clamping (`LineClamp`) is in
  the state of its block formatting context (`BlockFormattingContextState.LineClamp`), shared by the boxes that participate in it.
- **Clamp points**: counted in layout order, after each line box that is not empty (`LineClamp.OnLine`: the phantom line boxes
  do not count) and after each in-flow block-level box (`BeginBox`/`EndBox` in `BlockFormattingContext.LayoutContents`); the
  contents of the independent formatting contexts (inline-blocks, flow roots, scroll containers, floats, absolutely positioned
  boxes...) are skipped. With `max-lines: N`, the clamp point is after the N-th line box when content follows it (a line box, or a
  block-level box that does not collapse through: WPT `line-clamp-036`). With `max-lines: auto` (its initial value: the
  grammar `[ <integer [1,∞]> || auto ]` of the current draft and of the WPT tests, the `none` of webref is invalid), the clamp point is the last one where the container, clamped there, fits in its block size (its definite `height`,
  else its `max-height` floored by its `min-height`), before the content when nothing fits; with both, the earliest one.
- **Layouts** (`LineClamp.Layout`, called by `BlockFormattingContext`/`InlineFormattingContext.LayoutContents` for the container):
  a first layout measures the clamp points (and, for each one, the line that gets the ellipsis: the last line before it when no
  block-level box starts between them, WPT `line-clamp-auto-036`, `039`, `040`); the clamp point by the block size is found by a
  binary search over the layouts clamped at the candidate points (without ellipsis: displacing content for the ellipsis "must not
  cause a reevaluation"). When the container is clamped, its contents are laid out again with the clamp point.
- **Clamped layout**: once the clamp point is passed (`LineClamp.IsClamped`), the line boxes, the block-level children and the
  floats laid out are hidden (`Fragment.IsHiddenForPaint`), and each block container whose content is clamped restores the state
  of the placement of its children and of the block formatting context saved at the clamp point once its children are laid out
  (`ClampedState`, `ClampedLines`): it ends at the clamp point with its block-end padding and border and its collapsed margins
  (WPT `line-clamp-012`, `022`–`025`), its baselines are the ones of the content before the clamp point (the baselines of the
  clamped flex and grid items), and the ruby annotations under the last line and `text-box-trim` (the last line before the clamp
  point is the last formatted line, through the block-end edges of its ancestors) apply to it. The floats do not extend the block
  size of a clamped container (`LineClamp.FloatsEnd` only extends a container that is not clamped). The absolutely positioned boxes
  are hidden when their containing block is (a box after the clamp point, an inline box that starts after it); the boxes whose
  containing block contains or precedes the clamp point are shown, even when their static position is after it.
- **`block-ellipsis`** (`ellipsis` is U+2026, or a `<string>`; `no-ellipsis` and the empty string reserve nothing): the last line
  before the clamp point is broken again with the advance of the ellipsis reserved (`InlineLayoutAlgorithm._blockEllipsis`); it is
  not moved down below the floats to make room for its content ("must not shift down the line box [...] to clear a float"), only
  when the ellipsis itself does not fit next to them (as Blink). "Displacing content from the end of
  the line as if wrapping, until the last soft wrap opportunity": `BreakLine` ignores the emergency opportunities of
  `overflow-wrap` (and the opportunities inhibited by `nowrap`), and returns an empty line when nothing fits, which "is considered
  to contain a strut" (the root inline box contributes to its height, also in quirks mode); the spaces that would hang at its end
  are removed. The ellipsis is shaped in the style of the block container (not its `::first-line` style), isolated in the direction
  of the bidi paragraph of the line, placed after the content in that direction (on the line-left side of a right-to-left line) and
  aligned with it (`text-align`, after the justification); as the anonymous inline of `line-height: 0`, its fonts do not extend
  the line. With `-webkit-line-clamp`, as in the browsers (WPT `webkit-line-clamp-035`, the `*-align-baseline-line-clamp-*`
  tests), a line whose content does not fit with the ellipsis keeps its content, which is truncated by the ellipsis when it is
  painted (`LineBoxFragment.IsTruncatedByBlockEllipsis`, `LineTruncator.GetTruncation`, as `text-overflow`). `text-overflow` still
  truncates the lines of a clamped container.
- **`text-wrap-style: balance`**: "the clamping effect is applied first, then the remaining lines are balanced": the layouts that
  look for the clamp point do not balance (`LineClamp.Balances`); the lines before the clamp point are balanced in the clamped
  layout (`BalanceClampedLines`: the narrowest inline size whose lines, the last one with the ellipsis, contain the clamped
  content), and when the balanced lines do not fit in the block size, the container is clamped again at the previous clamp point
  (WPT `line-clamp-balance-*`).
- **Painting, hit testing, overflow**: `PaintOrder` skips the hidden fragments (not painted, not hit tested); the scrollable
  overflow and the in-flow bounds of the scrolling area do not include them ("treated as ink overflow", WPT `line-clamp-021`); the
  geometry APIs see them as laid out. The floats of a line-clamp container are clipped to its block-end content edge when they are
  painted (`Painter.AddLineClampClip`; the other sides are not clipped).
- **`continue: discard` in a multi-column container** ([CSS Overflow 4](https://drafts.csswg.org/css-overflow-4/#valdef-continue-discard):
  "Content after the first region break is not rendered"; the multi-column container is not a line-clamp container, so
  `max-lines` has no effect, WPT `discard-multicol-004`): the region break is the end of the last column of the first row
  whose content overflows into overflow columns (`MultiColumnFormattingContext.DiscardPoint`). The columns of that row are
  balanced without the content after it; the fragmentainers from the first overflow column, the column spanners and the rows
  after it, and the absolutely positioned boxes whose static position is there are not placed (no fragment), and the content
  box ends with the row. With a `block-ellipsis`, the strip is laid out once with a `LineClamp` that records the block-end edge
  of each line box (`LineClamp.CreateLineRecorder`), then again with the ellipsis on the last line that ends before the region
  break (`CreateEllipsisClamp`), and fragmented again. Not done: a container nested in another fragmentation context (its
  columns are fragmented by the outer one) and the rows of `column-wrap: wrap` (not region breaks).
- Not done: the hanging of the block overflow ellipsis (`hanging-punctuation`), the clipped parts of the floats in the scrollable
  overflow, the intrinsic sizes of a clamped container (all its content counts), and a box whose block size is clamped by
  `max-height` still collapses its block-end margin with its last children (WPT `line-clamp-auto-063`).

### Viewports (`LayoutEngine.Viewport.cs`)

The [viewports](https://drafts.csswg.org/cssom-view/#viewport) of a document from the layout.

- **Nested viewports**: the viewport of a nested browsing context (`StyleEngine.CreateEnvironment`) is the content box of
  the principal box of its container (`iframe`, `frame`, `object`, `embed`; the rectangle of `object-fit` for the SVG
  document of an `object` or `embed`) laid out in the parent document
  (`LayoutEngine.GetNestedViewportSize(container)`: the layout of the parent document is updated when it is out of date,
  without registering it for the rendering steps, as Blink updates the layout of the parent frame before the styles of a
  child frame), divided by the effective zoom of the container: the zoom multiplies the `devicePixelRatio` of the nested
  document instead. During a layout pass of the parent document (e.g. the natural size of the SVG document of an `object`),
  the size of its last layout is used; without box (e.g. in skipped contents), the computed `width`/`height` (or the
  attributes, 300x150 by default). The environment of the nested document is pulled at each style update of the nested
  document: a change of the layout of the parent document restyles and relayouts the nested one, and its resize steps fire
  the `resize` events.
- **Viewport-percentage lengths** ([css-values-4](https://drafts.csswg.org/css-values-4/#viewport-relative-lengths)): they are
  computed values (`CssValueComputer`), relative to `StyleEngine.GetViewportUnitSize(environment)`: the viewport minus the
  scrollbar gutters that do not depend on the contents (`ScrollContainers.GetViewportUnitGutters`: the forced scrollbars of
  `overflow: scroll` and the gutters of `scrollbar-gutter: stable` applied to the viewport, from the `overflow` propagated by
  the root or the body element and the `scrollbar-gutter`, `scrollbar-width` and `writing-mode` of the root element, read one
  property at a time so that the other properties of the root element can use the units). The gutters are computed once per
  generation of styles; a viewport unit used while they are computed (a cycle through `if()`, csswg-drafts#13189) uses the
  whole viewport. The small, large and dynamic viewports are the same (no dynamic browser interface). The container query
  units fall back to the same size, and the SVG lengths use it. The media queries use the whole viewport.
- **Visual viewport** (`VisualViewport`, [CSSOM View](https://drafts.csswg.org/cssom-view/#the-visualviewport-interface)):
  there is no pinch zoom (the `scale` is 1 and the offsets from the layout viewport are 0): its size is the initial
  containing block of the last layout (`LayoutEngine.InitialContainingBlockSize`, the viewport without its classic
  scrollbars; reading it updates the layout). Once it has a `resize` event listener (its size is read then), each layout pass
  reports its size (`VisualViewport.OnLayout`): a change queues a `resize` event, fired by the next resize steps
  (`Window.RunResizeSteps`, after the `resize` event of the window), even when the size changed back before them (as in
  Blink); the resize steps also update the layout of the documents whose visual viewport is observed (a change of the
  viewport size). It is never scrolled relative to the layout viewport: no `scroll`/`scrollend` events.
- **Viewport segments** (`window.viewport.segments`, [css-viewport](https://drafts.csswg.org/css-viewport/#viewport-segments)):
  a single segment, the rectangle of the viewport; the `env(viewport-segment-*)` variables are not supported (no display
  features). `<meta name=viewport>` is ignored, as in the desktop browsers.

### Scroll snapping (`Css/Layout/Scrolling/SnapContainer.cs`, `SnapSelectionStrategy.cs`, `LayoutEngine.ScrollSnap.cs`)

[CSS Scroll Snap 1](https://drafts.csswg.org/css-scroll-snap-1/) and the snap events of
[CSS Scroll Snap 2](https://drafts.csswg.org/css-scroll-snap-2/#snap-events).

- **Typed style** (`OverflowStyle`): `ScrollSnapType` (`ScrollSnapAxis` x/y/block/inline/both/pair, `ScrollSnapStrictness`),
  `ScrollSnapAlign` (block, inline), `ScrollSnapStopAlways`, `IsScrollInitialTarget`, with `ScrollPadding` and `ScrollMargin`.
  `ScrollState.ContainerStyle` is the style of the scroll container, of the root element for the viewport (scroll-snap-type and
  scroll-padding are not propagated from the body).
- **Snap containers** (`EnsureSnapContainers`, once per layout pass, only when a scrolling box has a `scroll-snap-type` or was
  a snap container): a `SnapContainer` per scrolling box whose `scroll-snap-type` is not `none` (`ScrollState.Snap.Container`):
  its physical axes (block/inline resolved with the writing mode of the box), the snapport (the scrollport reduced by
  `scroll-padding`, whose percentages refer to the scrollport), the scroll range (`MinOffset`/`MaxOffset`: negative positions in
  `rtl` and `vertical-rl`), and the proximity range (a third of the snapport, as Blink; unlimited for `mandatory`).
- **Snap areas** (`CollectSnapAreas`, the boxes in tree order with a `scroll-snap-align`; the principal box of an element and
  its continuations, or the box of a pseudo-element): each axis is captured by the nearest scroll container of the containing
  block chain (the fragment ancestors) that is scrollable in that axis, even when it does not snap; a box captured by two
  containers gives one `SnapArea` per container. The rectangle is the bounding box of the transformed border boxes of its
  fragments in the coordinates of the snap container at the scroll position 0 (the scroll positions of the scroll containers
  in between apply), extended by `scroll-margin`. The alignments are resolved to physical ones (`AlignX`/`AlignY`, start =
  left/top) with the block and inline axes of the nearest scroll container, start and end flipped with the writing mode of the
  snap container (of the box itself when the area is larger than the snap container). `ParentOffset` links the nearest snap
  area of an ancestor. `HasFocusWithin`/`IsTargeted` (`:target`) are updated before each selection (`UpdateAreaStates`).
- **Selection of a snap position** (`SnapContainer.FindSnapPosition`, a port of Blink's `cc::SnapContainerData`): in each axis,
  the closest valid snap position (clamped to the scroll range: unreachable positions) to the base position of the strategy
  whose snap area is visible in the snapport in the other axis; a snap area larger than the snapport gives the positions where
  it covers the snapport (avoiding the snap areas intersecting it: `FindCoveringCandidate`); then the two axes are made
  mutually visible (the axis to follow: the closest, or after a layout change the focused, the targeted, then the block axis);
  `pair` chooses a single area aligned in both axes. Between aligned snap areas, the focused one wins, then the targeted one,
  then a descendant over its ancestor, then the first in tree order (an area aligned in both axes is preferred when it is also
  aligned in the other axis). The strategies (`SnapSelectionStrategy`): `ForEndPosition` (an absolute scroll: `scrollTo()`,
  `scroll()`, `scrollLeft`/`scrollTop` (only the given axes), the scroll into view of the focus (the snap position closest to
  the scroll-into-view position, as the browsers)); `ForElementAreas` (`scrollIntoView()` and the fragment navigations, in the
  nearest scroll container of the target: "the user agent must snap to one of that element's snap positions", the one closest
  to the scroll-into-view position, also when the target is already visible, as the browsers, WPT
  `targeted-column-scroll-marker-selection-001`; the axes in which the target has no snap position snap as usual,
  `LayoutEngine.GetSnapPosition(state, strategy, element)`); `ForDisplacement` (a relative scroll:
  `scrollBy()`: the positions ahead in the direction, the closest to the intended position, without passing over a
  `scroll-snap-stop: always` area; a mandatory container that finds none snaps to the closest one); `ForEndAndDirection` (the
  end of a gesture of the user: the snap position closest to its end among its start position and the ones beyond it in its
  direction, as Blink's `EndAndDirectionStrategy`: a short gesture snaps back); `ForTargetElement` (a re-snap: the current
  snap targets are followed). A `proximity` container snaps only within the proximity range.
  `LayoutEngine.GetSnapPosition(state, strategy)` returns the clamped position and records the snap targets.
- **Re-snap** (`ResnapAfterLayout`, after each layout pass, so a forced layout re-snaps at once): each snap container whose
  smooth scroll and scroll gesture of the user are not in progress snaps with `ForTargetElement` from its position (the first layout snaps a `mandatory`
  container); the scroll to the same targets is instant, to other targets it uses `scroll-behavior`. A box that stops snapping
  loses its targets.
- **Snap events** (`ScrollState.Snap`, a `ScrollSnapState`): the snap targets (`TargetX`/`TargetY`, a `SnapTarget` = element and
  pseudo-element), the scrollsnapchanging targets and the scrollsnapchange targets (block/inline, from the writing mode of the
  box). Setting the targets (a scroll operation starts, a re-snap) queues `scrollsnapchanging` when they changed; a completed
  scroll (before `scrollend`), a scroll that does not move or a re-snap queues `scrollsnapchange` when the changing targets
  differ from the change targets. The pending scroll events (`PendingScrollEvent`) carry the targets: `SnapEvent`
  (`snapTargetBlock`/`snapTargetInline`: the element of a pseudo-element target) bubbles only at the `Document`. The first snap
  of a box fires no event (the layout is lazy: it may run long after the load). The event handlers are `onscrollsnapchange` and
  `onscrollsnapchanging` (the names of the browsers and of the prose; the IDL of the specification says `onsnapchanged`, renamed
  by SpecGen).
- **Columns** ([CSS Multicol 2](https://drafts.csswg.org/css-multicol-2/#column-pseudo)): when the `::column` pseudo-elements
  of a multi-column container have a `scroll-snap-align` (`StyleEngine.GetStyle(element, "column")`), its column boxes
  (`BoxFragment.ColumnRects`) are snap areas (`AddColumnSnapAreas`, target `column(index)`), captured by the container itself
  when it is a scroll container, extended by the `scroll-margin` of `::column`.
- **Initial scroll position** (`LayoutEngine.ScrollInitialTarget.cs`, `ScrollToInitialScrollTargets`, after each layout pass
  and the scroll anchoring, before the re-snap): `IndexFragments` collects the boxes whose `scroll-initial-target` is `nearest`
  in tree order; the first one of each scrolling box (its nearest scroll container in the fragment ancestors, the viewport
  included) is its initial scroll target, and the box is scrolled to it (a scroll event, then `scrollend`), snapped with
  `ForEndPosition`. The target is aligned with its `scroll-snap-align` in each axis, `start` when it is `none`, as Chrome and
  the tentative tests (the specification says `block: "start"`, `inline: "nearest"`). As in Chrome, the position follows the
  target after each layout (a target that appears, moves, changes its alignment or whose scroll container changes: "post-first
  layout arrivals") until a scroll operation scrolls the box (`ScrollState.IsScrolledByOperation`, set by the scrolls of
  the scripts, of the user, of the navigations and of `scrollIntoView()`/`focus()`, but not by the clamping, the scroll
  anchoring or the re-snap), so a fragment navigation or a restored scroll position wins.
- Not done: the fling of the gestures (their inertia), the
  `:snapped` pseudo-classes (the `snapped` scroll-state queries read
  `ScrollState.Snap`, see [Scroll-state container queries](#scroll-state-container-queries-layoutenginescrollstatequeriescs-scrollingscrollstatesnapshotcs)).

### Scroll markers and scroll buttons (`Css/Layout/LayoutEngine.ScrollMarkers.cs`)

The scroll navigation controls of [CSS Overflow 5](https://drafts.csswg.org/css-overflow-5/#scroll-navigation), as Chrome (the
only implementation) where the specification is silent or differs. The selectors and the computed styles are described in
[selectors.md](selectors.md#scroll-markers-and-scroll-buttons) and [computed-style.md](computed-style.md); the user interactions
(`Html/ScrollControlActions.cs`) in [browsing-context.md](browsing-context.md#focus).

- **Names** (`Scrolling/ScrollControls.cs`): the pseudo-elements are `scroll-marker-group`, `scroll-marker`, the four
  `scroll-button(block-start | inline-start | inline-end | block-end)` (a physical direction of a selector is the button of
  the flow-relative direction of the writing mode of the originating element) and `column::scroll-marker(index)`, the scroll
  marker of a column of a multi-column container (the `::column::scroll-marker` rules style all of them; the `::column`
  rules give the `scroll-snap-align` of the columns).
- **Boxes** (`BoxTreeBuilder.ScrollControls.cs`, `BoxTree.ScrollControls`):
  - `scroll-marker-group: before | after` on a scroll container (`ScrollContainers.IsScrollContainer`: not the body whose
    overflow is propagated to the viewport) generates the `::scroll-marker-group` box immediately before or after the box
    of the element, as its sibling (a flex item of a flex parent, etc.: its display is blockified against that parent). The
    user agent style is `display: block`; the computed `contain` always has `layout` (the group is the containing block of
    its positioned markers) and `size` unless the group is absolutely positioned.
  - The `::scroll-button()` pseudo-elements of any element (not only the scroll containers) whose `content` is not `none`
    generate boxes after the box of the element, in the order block-start, inline-start, inline-end, block-end, before the
    group when it is after. The specification says "immediately preceding siblings"; Chrome and the web-platform-tests
    (`css-pseudo/relative-box-order-of-pseudo-elements.html`) put them after. They are laid out as buttons (inline-block or
    flow-root unless flex or grid). The buttons of a `display: contents` element follow its contents.
  - The boxes of the root element are children of its box: the group before is its first child, the buttons and the group
    after its last children (the viewport is the scroll container).
  - Once the tree is built, the `::scroll-marker` of each element whose `content` is not `none` is appended, in tree order,
    to the group of the nearest scroll container of the containing block chain of the box of the element
    (`PositionedLayout.FindContainingBlock`: an absolutely positioned box whose containing block is outside of the scroll
    container has no marker there); nothing is generated when that scroll container has no group. The display of a marker
    is blockified against the group (a flex or grid group), not against its originating element.
  - **Columns** ([CSS Multicol 2](https://drafts.csswg.org/css-multicol-2/#column-pseudo)): a multi-column container records
    its column boxes (`BoxFragment.ColumnRects`: the column boxes have no fragment; they also contribute to its scrollable
    overflow and are the `::column` snap areas); when a `::column::scroll-marker` rule may style it, it generates one marker per column, after its own `::scroll-marker`, in the group of the nearest scroll
    container of the columns (the container itself when it is a scroll container). The box tree is built before the layout:
    the markers are generated for the number of columns of the previous layout (`LayoutEngine.GetColumnCount`), and when a
    layout pass gives another number, the markers are collected again and the document is laid out again
    (`UpdateColumnScrollMarkers`, at most three times: the groups have size containment, so their markers do not change the
    columns). The scroll target of such a marker is its column (aligned by the `scroll-snap-align` of `::column`).
  - The counters (`CssCounters`): a `::scroll-marker` is in the tree of its originating element after its `::marker`, then
    the markers of its columns; the buttons after its `::after`.
  - When the layout builds again the contents of a size container (`RebuildContents`), the markers are collected again and
    the document is laid out once more (the groups may already be laid out).
- **State** (a snapshot, like the [scroll-state container queries](#scroll-state-container-queries-layoutenginescrollstatequeriescs-scrollingscrollstatesnapshotcs)):
  `UpdateScrollMarkerSnapshots` runs in the "snapshot post-layout state steps" of the rendering steps, and at the end of a
  layout pass for the groups and the buttons that were not in the previous snapshot (a forced style update sees their initial
  state). A change increments `ScrollMarkerSnapshotVersion`, which is part of the version of the styles (a new generation of
  styles, then a new box tree and layout), only once a selector matched the state. The rendering steps lay out the documents
  whose style sheets declare `scroll-marker-group` or `scroll-target-group` or style a `::scroll-button()`
  (`StyleEngine.MayHaveScrollNavigationControls`), and a scroll requests a rendering update when the document has controls.
  - **Groups** (`ScrollMarkerGroup`): the markers of each `::scroll-marker-group`, and, for each element with
    `scroll-target-group: auto`, the HTML and SVG `a` elements among its descendants (nearest container) whose URL is the
    URL of the document with a fragment that indicates an element (the scroll target).
  - **Active marker** ([calculating the active scroll marker](https://drafts.csswg.org/css-overflow-5/#active-scroll-marker),
    the algorithm of Chrome): from the nearest common ancestor scroll container of the scroll targets, down the scroll
    containers that contain targets. In each one, when its [current scroll target](https://drafts.csswg.org/css-overflow-5/#current-scroll-target)
    is set (`ScrollState.CurrentScrollTarget`: the element of the last `scrollIntoView()`, fragment navigation, `focus()` or
    marker activation; reset by the scrolls without target of CSSOM View and of the scroll buttons), the target at or before
    it in tree order (the column that was activated, or the column in which the target starts, for the markers of the
    columns, unless an element of that column before the target has a marker: the column box contains its content, as Chrome,
    WPT `targeted-column-scroll-marker-selection-004`); else, in the block axis then in the inline axis, the target whose scroll-into-view position (aligned
    by its `scroll-snap-align`, `start` by default) is the last one at or before the eventual scroll position (the target
    of a smooth scroll in progress), or the nearest one after it when it is closer and less than half a scrollport away;
    the positions that the scroll range cannot reach are distributed over min(1/8 of the scrollport, half the range) at each
    end of the range. The last target wins a tie. When the focused marker stops being active, the focus moves to the new
    active marker. A group that is a scroll container scrolls to its new active marker (as Chrome: at the nearest edge, or
    aligned with its `scroll-snap-align` when the group is a scroll snap container, with the `scroll-behavior` of the group).
  - **Buttons**: a button is enabled when a scroll by one page in its direction (87.5% of the scrollport, or the scrollport
    less 40px when that is larger, snapped as a relative scroll) changes the position rounded to the pixel; the buttons of
    an element that is not a scroll container are disabled.
- **Hit testing**: the boxes are hit like the other pseudo-element boxes (`elementFromPoint()` and the target of the events
  are the originating element). Their inertness is the one of their scroll container, or `interactivity: inert` on the
  group (its markers are then inert whatever the inertness of their originating element) or on the pseudo-element itself;
  `interactivity` has no other effect yet.
- Not done: `::scroll-button(prev | next)`; the accessibility roles of the modes
  (the `tabs` mode does not hide the inactive panels); the `::column` pseudo-element only gives the `scroll-snap-align` of
  the scroll targets of the column markers and of the column snap areas (no box, no `getComputedStyle(element, "::column")` of a column,
  no `::column::scroll-marker` in `getComputedStyle()`), and the columns of a multi-column container that is fragmented by
  another one have no marker.

### Scroll anchoring (`Css/Layout/Scrolling/ScrollAnchor.cs`, `LayoutEngine.ScrollAnchoring.cs`)

[CSS Scroll Anchoring 1](https://drafts.csswg.org/css-scroll-anchoring/): the scroll positions are adjusted to compensate for
the layout changes above the visible contents. The algorithms follow Blink's `ScrollAnchor` (the reference implementation of
the WPT tests); the spec leaves most details open.

- **When**: each layout pass (`UpdateLayout`: a forced layout or the layout of the rendering steps, so the end of the
  "suppression window" is the end of the pass) first queues the adjustments (`QueueScrollAnchoringAdjustments`), with the
  fragments of the previous pass and the styles computed for this one (the box tree of the previous pass is kept until the next
  pass when `SetNeedsLayout(rebuildBoxTree: true)` drops it: `_discardedBoxTree`), then lays out, then performs them
  (`PerformScrollAnchoringAdjustments`, the nested scrolling boxes first), before the re-snap of the snap containers.
- **Scrolling boxes**: the viewport and the scroll containers that can scroll in their block flow direction, not generated by
  an element with `overflow-anchor: none`; a box at the scroll position 0 in its block axis has no anchor (a suppression
  trigger).
- **Anchor node selection** (`ScrollAnchorSearch.FindAnchor`, kept in `ScrollState.Anchor` until invalidated): the priority
  candidate first (the focused element when it is an editing host, editable, a mutable `textarea` or a mutable text-entry
  `input`; its nearest box that is not a non-atomic inline box), when it is viable (in the scroll container, no ancestor up to
  it excluded), then its subtree; else the walk of the box tree from the scroll container: a box with `overflow-anchor: none`,
  fixed or sticky positioned (with the previous or the current style), absolutely positioned with a containing block outside the
  scroll container (its fragment is not in it), not rendered anymore, or that occupies no space or is fully clipped, is skipped
  with its subtree; the inline boxes, the anonymous boxes and the generated texts are descended into; a fully visible box is
  selected, a partially visible box is selected unless one of its descendants (then its absolutely positioned descendants whose
  box is not its child) is viable; a multi-column container looks at its absolutely positioned descendants after a partially
  visible child. The boxes are the ones of the elements, of the pseudo-elements and the text runs of the text nodes (all the
  runs of a text node). Visibility is tested against the scrollport reduced by `scroll-padding`. The scroll anchoring bounding
  rect is the union of the fragments of the box (in the visible contents of the scroll container: transforms and nested scroll
  positions applied), each extended to the bottom of its scrollable overflow unless it clips its overflow (as Blink). The
  anchor is identified by its node (`ScrollAnchor.Node`/`PseudoElement`): the box tree is rebuilt by the style changes.
- **Adjustment**: the position of the block-start edge of the rect (top, left, or right for `vertical-rl`/`sideways-rl`) is
  saved when the anchor is selected (`SavedBlockStart`, relative to the visible contents, from the block-start edge of the
  scrollport, so that a resize of the scrollport does not move the contents of a `vertical-rl` box: it stays valid after an adjustment)
  and compared after the pass; the scroll position moves by the difference in the physical axis of the block flow direction of
  the scrolling box (clamped, not rounded: sub-pixel adjustments accumulate; less than 1/1024px is ignored). The anchor stays
  selected. An anchor whose node lost its box is dropped without adjustment; an anchor selected while removed nodes were
  skipped (the contents were replaced since the previous pass) is selected again by the next pass (as Gecko).
- **Suppression triggers** (`HasScrollAnchorDisablingStyleChange`, only when the styles changed): a change of the computed
  `top`/`right`/`bottom`/`left`, margins, paddings, sizes and min/max sizes, `position` or `transform` on the anchor or an
  ancestor up to the scroll container; an element of the scroll container that became or stopped being absolutely positioned
  (as Blink, its nearest scroll container or the root element is marked, and the adjustment is suppressed when the mark is on
  the anchor path; an element whose `display` became `none` is not a position change: its box is removed, as Blink destroys its
  layout object). A suppressed non-zero adjustment drops the anchor.
- **Invalidation**: an explicit change of the scroll position (`SetScrollPosition`: the scroll operations, the smooth scroll
  steps, the re-snap, the history restoration) drops the anchors of the box and of the scrolling boxes that contain it; a change
  of `overflow-anchor` in a scrolling box drops its anchor; a change of the priority candidate selects the anchor again. The
  clamping of the scroll position after a layout does not.
- **Events**: an adjustment is not an explicit scroll (`ApplyScrollPosition` + `DeferScrollEvent`): its `scroll` event is
  deferred to the end of the next rendering update (`QueueDeferredScrollEvents`), queued only when the position differs from the
  one of the last scroll event (adjustments that cancel each other out fire nothing), followed by `scrollend` unless a smooth
  scroll is in progress (as Blink); the events fire at the scroll steps of the following frame. A smooth scroll in progress is
  moved with the contents.
- **Interactions**: the scroll-driven timelines and the sticky offsets are updated by `ApplyScrollPosition`; the layout shift
  tracker does not count a node that moved in the viewport by the anchoring adjustments of its scroll containers
  (`ScrollState.AnchoringAdjustments`, as Chrome's `scroll_anchor_adjustment`); a snapped scroll container re-snaps after the
  adjustment (the re-snap follows its snap targets: "scroll anchoring is limited to adjustments that would be allowed by
  re-snapping").
- Not done: the find-in-page priority candidate (no find-in-page), the anchor serialization of the history restoration (Blink
  restores the scroll position of a reload by the anchor: the reload does not restore the scroll position).

### Fragments

- `Fragment`: `Box` (the `LayoutBox` that generated it, null for a line box), `Size`, `Offset` (physical, relative to the
  top-left corner of the border box of the parent fragment; for the inline content, relative to the line box), `Parent`,
  `Children` (in content order), `GetOffsetInRoot()`/`GetRectInRoot()` (relative to the root fragment, the initial containing
  block: document coordinates, the scroll positions of the scroll containers subtracted, see
  [Overflow and scrolling](#overflow-and-scrolling-csslayoutscrolling)).
- `BoxFragment` (`ContainerBox`): the used `Border`, `Padding` and `Margin` (physical `Sides<double>`), `BorderBoxRect`,
  `PaddingBoxRect`, `ContentBoxRect`, `MarginBoxRect` (in the coordinates of the fragment), `WritingDirection` of the box,
  `FirstBaseline`/`LastBaseline` (alphabetic, from the block-start border edge in the writing mode of the box),
  `IsFirstFragment`/`IsLastFragment` (for the boxes split into several fragments: inline boxes over lines, fragmentation;
  `UnfragmentedFragment` is the fragment of a box before the fragmentation split it), `GapGeometry` (the gaps of the containers that paint gap decorations),
  `ClipsOverflowX`/`ClipsOverflowY`, `IsScrollContainer`, `Scrollbars`, `ScrollState`, `ScrollableOverflow`, `InkOverflow`,
  `OverflowClipRect` and `GetScrollingArea()` (see [Overflow and scrolling](#overflow-and-scrolling-csslayoutscrolling)). The
  root fragment is the fragment of the viewport box, with the size of the initial containing block (the viewport without its
  scrollbars).
- `LineBoxFragment`: a line box, child of the fragment of its block container; `Baseline` from its block-start edge, `Style`
  (the style of its root inline box: the `::first-line` style on the first line), `IsRightToLeft` (the direction of its bidi
  paragraph) and `BlockEllipsis` (see [Text overflow](#text-overflow-csslayoutinlinelinetruncatorcs-lineellipsiscs)).
- `TextFragment` (`TextRun`): `Start`/`End` (offsets in the text node, for the ranges, the selection and the caret; the
  collapsed white space between two fragments of a line belongs to the first one), `ShapedText` + `GlyphStart`/`GlyphCount`
  (the glyphs, `Glyphs`; a shaped text of the fragment when the spacing of the line changed its advances), `Baseline` from its block-start edge, `IsHyphen` (the
  hyphen of a line broken after a soft hyphen), `Style` (the style of the run, or its `::first-line` style on the first line, for
  the painting) and `IsEllipsis` (a fragment of an ellipsis attached to a line box). The line break of a `br` is a text fragment
  without glyph. `DomOffsets`
  maps the characters of the run to the offsets in the text node when they differ (collapsed white space, `text-transform`);
  `GetInlineOffset(domOffset)` / `GetDomOffsetAt(distance)` / `GetRangeRect(start, end)` convert between the DOM offsets and
  the positions along the line (snapped to the clusters, with the justification), for the ranges and the caret.
- The fragments of the inline content are nested: a line box contains the text fragments, the fragments of the atomic inlines
  and of the inline boxes (a `BoxFragment` per line, or more on a reordered line, `IsFirstFragment`/`IsLastFragment` tell
  which edges it has; its `Border`/`Padding`/`Margin` are the ones applied to the fragment), and the fragment of an inline box
  contains the fragments of its content. The outside markers and the floats are children of the fragment of the block
  container; the absolutely positioned boxes are children of the fragment of their containing block (see
  [Positioned layout](#positioned-layout-csslayoutpositioned)).
- `BoxFragment` positioning data: `RelativeOffset` and `StickyOffset` (included in `Offset`), `StickyConstraints`,
  `ResolvedInsets` (the resolved values of the insets of a positioned box), `ZIndex` and `EstablishesStackingContext`
  (`StackingContexts`: the stacking information for the hit testing and the painting).
- The fragments are immutable once the pass is complete (the offsets are set when the parent places them). The
  formatting contexts collect the children at logical offsets in a `FragmentBuilder`, which converts them to physical
  offsets once the size of the container is known (e.g. in `vertical-rl`, the x of a child depends on the final width of its
  container).
- `FragmentTree.Dump(fragment)` / `LayoutEngine.Dump()`: one fragment per line (`box <div#id> x,y wxh`, `line`, `text "..."
  [start, end)`), for the tests.

### Geometry

- Physical: `PhysicalOffset`, `PhysicalSize`, `PhysicalRect` (union, translate, expand/contract by `Sides<double>`), `PhysicalSide`.
- Logical: `LogicalOffset` (from the inline-start and block-start edges), `LogicalSize`, `LogicalRect`, `LogicalSides<T>`.
- `WritingDirection` (writing mode + direction, `FromStyle(LayoutStyle)`): the
  [abstract-to-physical mappings](https://drafts.csswg.org/css-writing-modes-4/#logical-to-physical) (`BlockStart`,
  `InlineStart`... as physical sides, `sideways-lr` lines go from bottom to top), sizes and sides both ways, and offsets of a
  rectangle in a container both ways (`ToPhysical(offset, outerSize, innerSize)`), `IsOrthogonalTo`, the line-relative
  sides (`LineLeft`/`LineRight`, `LineOver`/`LineUnder`).

### Writing modes

[CSS Writing Modes 4](https://drafts.csswg.org/css-writing-modes-4/): every formatting context works in the logical axes of its
box (`WritingDirection`) and converts to the physical fragments with the `FragmentBuilder`, so `vertical-rl`, `vertical-lr`,
`sideways-rl` and `sideways-lr` (whose lines go from bottom to top) and `direction` apply to the block, inline, flex, grid and
table layouts, the positioned boxes, the floats and the scroll origins (`ScrollContainers.GetScrollOrigin`: the scroll
positions are negative in `vertical-rl` and `direction: rtl`).

- **Principal writing mode** ([§8](https://drafts.csswg.org/css-writing-modes-4/#principal-flow)): the box tree sets the used
  `writing-mode` and `direction` of the root element (`LayoutStyle.SetPrincipalWritingMode`) to the computed ones of its first
  `body` child element, for each build; the computed values do not change. The initial containing block, the viewport (its
  scrollbars and its scroll origin) and the root box use them.
- **Orthogonal flows** ([§7.3](https://drafts.csswg.org/css-writing-modes-4/#orthogonal-flows)): a box whose writing mode is
  perpendicular to the one of its containing block establishes an independent formatting context; its available inline space is
  the block size of the containing block, or the fallback of [§7.3](https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto)
  (see [Formatting contexts](#formatting-contexts)); its `auto` inline size is its fit-content size and its block size is the
  size of its contents (the `auto` margins of a block-level box in normal flow, in the inline axis of its containing block,
  are resolved with that block size); its contributions to the intrinsic sizes of its parent are measured by laying it out
  (`IntrinsicSizing.GetContribution`). An `inline` box whose writing mode differs from its parent's computes to `inline-block`
  (the cascade).
- **Text**: the shaping orientation comes from `writing-mode` and `text-orientation` (`mixed`, `upright`, `sideways`; the
  `sideways-*` modes are sideways: `text-orientation` and `text-combine-upright` do not apply to them,
  `LayoutStyle.IsVerticalTypographicMode`), the line boxes align on the central baseline of vertical text,
  `text-combine-upright` (`all`, `digits`) makes 1em compositions (see [Inline formatting context](#inline-formatting-context-csslayoutinline)).
  `text-orientation: upright` [makes the used direction ltr](https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-upright)
  (`LayoutStyle.Direction` is the used value: the layout, the tables, the bidi paragraph; all the characters of the upright
  text are strong left-to-right characters for the reordering; the flow-relative properties and values are mapped with it,
  `LogicalProperties.IsRightToLeft`; the computed `direction` does not change). `text-orientation` applies to `::marker` (as in
  the browsers).
- **Tables**: the first value of `border-spacing` is the spacing between the columns and the second one the spacing between
  the rows ([§7.2](https://drafts.csswg.org/css-writing-modes-4/#dimension-mapping)), whatever the writing mode.
- **Baselines**: the first/last baselines of the fragments (`BoxFragment.FirstBaseline`) are the baselines of their
  lines (central in vertical text, else alphabetic, whatever `dominant-baseline`), measured from the block-start edge of the box; the flex, grid and table layouts flip the ones
  of the items whose block flow is opposite, and synthesize the missing ones from the border box: the center for the central
  baseline, the line-under edge (`WritingDirection.LineUnder`) for the alphabetic baseline.
- **Bidi** ([§2](https://drafts.csswg.org/css-writing-modes-4/#text-direction)): `direction` and `unicode-bidi` in the inline
  layout (see [Bidirectional text](#bidirectional-text)); the `unicode-bidi` of a block container (an override, `plaintext`)
  also applies to the inline content of its anonymous block boxes, which do not inherit it (`GetBlockUnicodeBidi`); the HTML `dir` attribute (including `dir=auto`) sets `direction` through
  the `:dir()` rules of the user agent style sheet.
- Not done: the upright text of the vertical form controls other than the text controls, whose value is laid out by the
  inline layout (their contents are painted sideways, see
  [Painting](#painting-csslayoutpainting)), the vertical alternates (`vert`),
  the baselines of the vertical axis of the `BASE` table (the ones of the horizontal axis are used in vertical text, as
  Firefox), the multi-column fallback of the orthogonal flows whose inline size is automatic
  ([§7.3.2](https://drafts.csswg.org/css-writing-modes-4/#auto-multicol)).

### CSSOM View geometry and hit testing (`Css/Layout/HitTesting/`)

The CSSOM View APIs (`Css/Om/CssomView.cs`, `CssomView.Scrolling.cs`, `CssomView.GeometryUtils.cs`, `CaretPosition.cs`)
read the fragment tree (a forced layout):

- `LayoutEngine.GetFragments(element, pseudoElement?)`: the box fragments of the principal box and of its continuations, in
  fragment tree order (indexed after each pass: `LayoutBox.FirstFragment` + `Fragment.NextFragmentOfBox`);
  `GetFragmentsWithSplitBlocks(element)` adds the blocks split out of an inline box between its parts (`getClientRects()`,
  `offsetWidth`); `GetTextFragments(textNode)`: the text fragments of all the runs of a text node.
- Mappings (`LayoutEngine.Geometry.cs`): `MapToRoot(fragment, rect)` returns a `PhysicalQuad` in the coordinates of the root
  fragment, applying for each ancestor its transform (`LayoutTransforms.GetMatrix`, the plane z=0 of the 3D matrix) and its
  scrolled offset (`GetScrolledOffset`: the offset minus the scroll position of a scroll container parent, the fixed boxes of
  the viewport kept in place); `TryMapFromRoot`/`TryMapFromParent` are the inverse (false for a non-invertible transform);
  `ToViewport`/`FromViewport` subtract/add the scroll position of the viewport. Without transform, `MapToRoot(...).Bounds` is
  `GetRectInRoot()`.
- `getClientRects()` is the bounds of the quads of the border boxes; the elements of an SVG document fragment use
  `SvgBoundingBox.GetBoundingQuad` (the bounding box transformed to the content box of the outermost `svg`); `Range` adds the
  `TextFragment.GetRangeRect` of the selected characters (the glyph cells of the SVG text layout for a text of an SVG text). `offsetTop`/`offsetLeft` use the positions in the initial containing
  block without transforms and scroll positions (`CssomViewGeometry.GetPositionInInitialContainingBlock`).
- **Painting order** (`PaintOrder.Collect(root, items)`, cached per pass by `LayoutEngine.GetPaintOrder()`): the fragments in
  the painting order of [CSS 2 Appendix E](https://drafts.csswg.org/css2/#elaborate-stacking-contexts) from
  `BoxFragment.ZIndex`/`EstablishesStackingContext`: the negative z-index layers, the in-flow blocks, the floats, the inline
  content, the positioned boxes and z-index 0 layers, the positive layers; the positioned descendants and the floats that do
  not establish a stacking context are painted atomically as pseudo stacking contexts; the top layer is painted last (with
  the fixed boxes it contains, which are children of the root fragment). A `PaintItem` is a fragment, or the band of an
  inline box split around blocks (`IsSplitInlineBand`: the anonymous block area belongs to the inline box). A block split out
  of an inline box that is a layer (a stacking context or a positioned box) is painted in the layer of the inline box, with
  its floats, its inline content and its layers (`GetBlocksPaintedInInlineLayer`): the `z-index`, the opacity and the filters
  of the inline box apply to it. A block-level replaced box has two items (`PaintItem.Part`): its background and borders with
  the in-flow blocks, its replaced content with the inline content (a block after it covers its background, not its content).
  The background and the borders of a table grid box are painted before its captions. The `z-index` values outside of the
  range of the 32-bit integers are clamped. The painting
  collects it with the bounds of the stacking contexts (`PaintItem.Marker`, see [Painting](#painting-csslayoutpainting)).
- **Hit testing** (`HitTesting.HitTest(engine, point, results, all)`): walks the paint order backwards; for each fragment,
  maps the point (memoized per fragment for the query), skips it when an ancestor clips the point (`OverflowClipRect`, the
  root excepted, with the rounded corners of the overflow clip edge of a box that has a `border-radius` and clips both axes: the
  same shape as the clip of the painting, `RoundedRect.FromBorderBox(...).Outset(...)`; the result is kept for the last box
  that clips both axes, so the border radii of a scroll container are read once for its children, which follow each other in
  the painting order), tests its border box with the rounded corners (`BorderRadii`, also usable by the painting; the outer
  contour of the box when its corners are shaped by `corner-shape`, `CornerContour.Contains`) or the text
  fragment. A box with `visibility` other than `visible`, `pointer-events: none` or an inert element (`FocusModel.IsInert`)
  is not a target (its descendants may be). An image with a `usemap` hits its `area` instead (`ImageMaps`: shapes, parsing
  of the coordinates, the map lookup); the elements of an SVG document fragment are hit with their geometry (see
  [SVG layout](#svg-layout-svg), in reverse document order). `HitTestResult.Element` is the target
  element (`Target` for an `area` or an SVG element, else the element of the fragment). As in the browsers, an anonymous table
  cell is hit for the element of its nearest ancestor box that has one (`GetAnonymousCellOwner`: a row or a row group, e.g.
  the area of a `table-header-group` beside its block content), the other anonymous boxes are not hit. The ::backdrop of a dialog hits the
  dialog. Used by `elementFromPoint()`, `elementsFromPoint()` (of the documents and, as in the browsers, of the shadow
  roots: the result is retargeted against the shadow root, so it can be an element of its shadow tree, or an element
  outside of it; CSSOM View only defines the members of the documents), `caretPositionFromPoint()`, the light dismiss of the popovers
  and dialogs (via `getBoundingClientRect()`) and the WPT test driver (the target of the pointer actions). The pseudo-element
  of the box hit (`LayoutBox.PseudoElement`) is the `pseudoTarget` of the pointer events (`UserInput.HitTestDocument`, see
  [computed-style.md](computed-style.md)).
- `GeometryUtils` (`getBoxQuads()`, `convert*FromNode()`) maps the quads through `MapToRoot` and the `iframe` chain (the
  viewport of a nested document is at the content box of its `iframe`).
- `MouseEvent`: `offsetX`/`offsetY` and `layerX`/`layerY` are computed from the layout when first read during the dispatch
  and cached (they keep their value after it); `pageX`/`pageY` use the scroll position of the window of the realm that
  created the event; the events fired by the user agent without pointer (`IsPositionless`) report 0.
- 3D (#376): the transforms include the `perspective` of the parent element (`LayoutTransforms.GetMatrixWithPerspective`;
  a box of a 3D rendering context that has no transform has the perspective of its parent too, as the accumulated 3D
  transformation matrix of CSS Transforms 2 specifies: its descendants in the context are seen in the perspective, in the
  painting, the geometry and the hit testing);
  the points of a 3D rendering context are mapped with the accumulated 3D transform of their box
  (`LayoutEngine.Get3DRenderingContextMatrix`: homogeneous coordinates up to the root of the context, flattened in the plane of
  its parent; the nearly integral transformed coordinates are rounded, as the single-precision transforms of the browsers);
  among the hits of a 3D rendering context the nearest one at the point wins (`Get3DDepth`: the depth at the hit point of the
  plane of the nearest box that participates in the context, without the splitting of the intersecting planes of the
  painting, which a single point does not need; the root of the context and its contents that are not transformed are a plane
  of the context, so a child translated behind the plane of its parent is hit through the parent only where the parent is
  not); a box showing its back face with
  `backface-visibility: hidden` is not hit (its descendants can be). `clip` and `clip-path` (basic shapes, geometry boxes,
  `clipPath` elements) clip the hit area of a box and of its containing block chain (`Painter.IsClippedOut`).
- A box whose border is shaped by `border-shape` is hit in the outer edge of its border (`BorderShapeGeometry.OuterPath`:
  the shape and its stroke, or the first shape; also outside of its border box), and its contents are only hit in its
  overflow clip (the inner edge of the border) when it clips its overflow in both axes.
- Not done: the masks in the hit testing, the `::marker` of a `::before` or `::after` (hit as its pseudo-element),
  `caretRangeFromPoint()` (not standard, not in the IDL).

### Caret navigation (`Css/Layout/Caret/`)

`Selection.modify()` (`Dom/Ranges/Selection.Modify.cs`), the caret movement keys (`Html/CaretMovementKeys.cs`) and the caret
placed in a focused editing host (`Editing.PlaceCaretAtStart`) move the caret over the line boxes, as the browsers do:

- `CaretLineCollector.Collect(engine, root, editingHost)` builds the `CaretLine`s of a navigation root: the editing host of the
  focus (the outermost one), else the atomic inline that contains it (an inline-block has its own lines), else the document.
  The lines of an inline root are the lines of its block container, restricted to its content. The fragment tree is walked
  (the replaced boxes and the force-hidden anchored boxes excepted), then the lines are sorted by the boundary point of their
  first unit (e.g. the `order` of the flex items changes the order of the fragments, not of the content).
- A line has `CaretUnit`s in logical order (the leaves of the inline layout are in visual order: they are sorted by the
  pre-order of the inline-level boxes of their block container, then by offset): the typographic character units of the
  text fragments (the boundaries of the clusters of the glyphs, of the grapheme clusters and of increasing DOM offsets: a
  collapsed white space is one unit, the collapsed characters are a gap between the units), the atomic inlines, the forced
  line breaks (a `br` element, or the preserved segment break recorded by the inline layout on the line box,
  `LineBoxFragment.ForcedBreakRun`/`ForcedBreakRange`, as it has no fragment) and, in an editing host, the non-editable
  elements (one atomic unit per line). The generated content and the hyphens have no unit. Each unit has its DOM range, the
  coordinates of its logical start and end edges along the inline axis (root coordinates) and its direction.
- `CaretNavigator`: a caret position (`CaretStop`) is a line and the index of a boundary between its units (the position
  after a forced line break is the first position of the next line). Its upstream point is the end of the unit before it, its
  downstream point the start of the unit after it. `Resolve(node, offset, attachedUpstream)` maps a boundary point to a
  position (the end of a line and the start of the next one are the same point when a word is broken: the caret affinity
  chooses); a point between units (e.g. in an element) maps to the closest unit in tree order. The result of a movement is
  the edge of the crossed unit in its text node (`<b>ab|c</b><i>def</i>` moves right to the end of `abc`, then after `d`),
  else the text position in the direction of the movement.
- Character: logical (`forward`/`backward`) or visual (`left`/`right`: the units of the line sorted by coordinate; at a bidi
  boundary the caret is attached to the unit of its last movement, `Selection` keeps this affinity while the focus does not
  change, else to the unit of the direction of the line). Word: `WordBoundaries.Move` over the text of the units (a line feed
  between the blocks). Sentence: a simplification of UAX #29 (terminal punctuation followed by spaces, paragraph
  separators). Line and paragraph (the paragraphs end at the forced line breaks and the block boundaries): the next or
  previous line (of another paragraph), at the position closest to the inline coordinate of the caret, kept by consecutive
  movements. Boundaries: the start or end of the line, paragraph or content.
- `Selection.modify()`: "move" collapses a range by a character at its end or start, the other granularities move from it
  (the line movements from the focus); nothing moves while the focused editing host does not contain the focus.
- Not done: the empty editable blocks have no caret position (no line box), the caret does not enter an inline-block from
  outside, the sentence boundaries are approximate, the movements by words are the ones of macOS/Linux (forward to the end of
  the word), the selection with the pointer (a press, a drag, a double or triple click).

### Resize observers (`Css/Layout/ResizeObservations.cs`)

[Resize Observer](https://drafts.csswg.org/resize-observer-1/) (`Dom/ResizeObserver.cs`, `Dom/Document.ResizeObservers.cs`):

- The `[[resizeObservers]]` of a document are the observers created by its window (the current global object; without one,
  e.g. from .NET, the document of the first observed element) that observe at least one element, in their order of creation
  (a sequence number): an observer without targets is not referenced by the document. `observe()` registers the document for
  the rendering steps without computing its layout (`LayoutEngine.RequestRenderingUpdates()`) and schedules a rendering
  update; the documents with observers are then laid out by every rendering update. The rendering updates are scheduled by
  the changes of the DOM and of the styles, and, while an observer of the event loop has targets
  (`DomEventLoop.ResizeObserverCount`), by `LayoutEngine.SetNeedsLayout` (e.g. an image is loaded).
- `LayoutEngine.UpdateRendering()` runs the loop of [update the rendering](https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering)
  (step 16): update the layout, the initial determinations of `content-visibility: auto` (which restart the loop), the last
  remembered sizes, then "gather active resize observations at depth" (`Document.GatherActiveResizeObservations`: the
  observations whose size differs from the last reported sizes, deeper than the depth of the last broadcast in the flat tree,
  the other active ones are skipped) and "broadcast" (the callbacks, the new depth is the shallowest broadcast target) until
  no observation is active. When observations were skipped, an error event whose message is "ResizeObserver loop completed
  with undelivered notifications." is reported at the window (error null) and another rendering update is scheduled to deliver them.
- The sizes (`ResizeObservations.CalculateBoxSize`) come from the box fragments of the principal box
  (`LayoutEngine.GetFragments`): one `ResizeObserverSize` per fragment (one per column in a multi-column container,
  csswg-drafts#3673, as Gecko), logical in the writing mode of the box, divided by the effective zoom; the content box
  excludes the scrollbars; `contentRect` is the content box of the first fragment at (padding-left, padding-top).
  `isActive()` compares all the fragments. The device pixel content box is the content box in device pixels
  (`ViewportOptions.DevicePixelRatio`, not divided by the zoom) whose edges are snapped from their position in the document
  (before the transforms, rounded half up). The elements without box, the disconnected ones and the non-replaced inline
  boxes have one 0×0 size (they are notified once); an `SVGGraphicsElement` without CSS box uses its bounding box without the stroke (`SvgBoundingBox.Compute`) at (0, 0), zoomed for the
  device pixels.
- The observations of the elements in the skipped contents of an element (`ContentRelevancy.IsInSkippedContents`, checked only
  when the styles may use `content-visibility`) are not active: they are reported when the element is rendered again.
- As in the browsers, `observe()` of an observed element with the same box does nothing (the spec text unobserves it first,
  which resets its last reported size: WPT `resize-observer/notify.html` expects no error).
- `WebPlatformFeatures.ResizeObserverEnabled` hides the interfaces from the scripts.

### Intersection observers (`Css/Layout/IntersectionGeometry.cs`)

[Intersection Observer](https://w3c.github.io/IntersectionObserver/) (`Dom/IntersectionObserver.cs`,
`Dom/Document.IntersectionObservers.cs`), with the V2 visibility (`trackVisibility`, `delay`):

- An observer belongs to the document of its window (the current global object; without one, the document of its root or of
  its first target): the document keeps the observers that have targets, in their order of creation, and registers itself
  with the event loop (`DomEventLoop.AddDocumentWithIntersectionObservers`). `observe()` registers the documents from the
  one of the target to the top-level document for the rendering steps (`LayoutEngine.RequestRenderingUpdates()`) and
  schedules a rendering update; while an observer of the event loop has targets (`DomEventLoop.IntersectionObserverCount`),
  `LayoutEngine.SetNeedsLayout` schedules a rendering update too. The DOM, style and scroll changes schedule one anyway.
- `DomEventLoop.UpdateTheRendering` runs "run the update intersection observations steps" for each document with observers
  after the layout, the resize observers and the focus fixup (`Document.RunUpdateIntersectionObservationsSteps`), with the
  time of the frame relative to the time origin of the window; the delays are measured with the precise (not coarsened) frame
  time, and a skipped observation schedules another rendering update. The entries are queued, and one task per document
  ("queue an intersection observer task") notifies the observers that queued entries, in their order of creation.
  `disconnect()` drops the queued entries, as in the browsers.
- `IntersectionGeometry.Compute` (steps 4 to 14): the root intersection rectangle is the viewport of a document root or of the
  implicit root (the padding box of the root fragment: without the scrollbars), the padding box of an element root that clips
  its overflow, else the bounding box of the element root; expanded by the scroll margin (a scroll container, the viewport
  included) and by the root margin (the percentages of the vertical offsets are relative to the height, as in the browsers),
  only for the same-origin-domain targets (else `rootBounds` is null). The target rectangle is the bounding box of
  `getBoundingClientRect()`; each box fragment (the SVG elements, as in the browsers: their stroke bounding box, clipped by
  the bounds of the `clipPath` elements of the element and its SVG ancestors, `SvgClipping.GetBounds`, then by the outermost
  `svg`) is clipped by its own clip path and by the overflow clip edges (`BoxFragment.OverflowClipRect`, in the axes that clip) and
  the clip paths (`ClipPaths.GetBounds`: the bounding box of the path of the basic shape in its reference box,
  `BasicShapePaths`; `url()` does not clip) of the ancestor fragments, up to the root (the containing block chain: the out-of-flow fragments are
  children of their containing block), the scroll containers expanded by the scroll margin. A document of a nested browsing
  context clips the rectangle to its viewport (a scrollport), then the rectangle is mapped to the content box of its
  container (`LayoutEngine.MapToRoot`, with the transforms) and clipped by the container and its ancestors; the scroll margin
  stops applying after the first document that is not same origin-domain with the one of the target. The intersection with the
  root rectangle is mapped back to the viewport of the document of the target (`TryMapFromRoot`). All the intersections are
  edge-inclusive. A box whose transform is not invertible on the plane z = 0 (`LayoutTransforms.IsInvertibleOnPlane`, e.g.
  `rotateX(90deg)`) hides its contents.
- As in the browsers: a target that is not rendered (no box, in the skipped contents of `content-visibility`), not in the
  containing block chain of an element root, or not in the document of an explicit root has zero rectangles (`rootBounds`
  included) and does not intersect; the threshold index of a target that does not intersect is 0, and a target is
  intersecting only when its ratio reaches the first threshold (w3c/IntersectionObserver#432); the target of an implicit root
  observer in a document that is not fully active (e.g. `createHTMLDocument()`) is not observed until it is adopted.
- The visibility (`trackVisibility`): false when the target or an ancestor up to the root (the containers of the nested
  browsing contexts included) has a transform other than a 2D translation or a proportional upscaling, an opacity below 1 or
  a filter, or when a box painted after it (`LayoutEngine.GetPaintOrder()`, in its document or in the document of a container)
  that paints something (text, a replaced element, a background, a border or an outer shadow, not transparent nor hidden)
  covers its intersection: the area of an occluder is its border box, or the bounds of its border shaped by `border-shape`,
  and the bounds of its outer shadows (`Painter.GetDecorationBounds`). The rest of the ink overflow of the occluders
  (filters, outlines), the SVG content and the 3D rendering contexts are not taken into account.
- The lazy loading (`Html/Elements/LazyLoading.cs`) uses an observer of the user agent per document (the lazy load
  intersection observer) whose scroll margin is 1250px, created by the internal constructor (not exposed to the scripts):
  see [browsing-context.md](browsing-context.md#lazy-loading). `content-visibility: auto` shares the geometry
  (`IntersectionGeometry.IntersectsViewport`) without an observer object.
- `WebPlatformFeatures.IntersectionObserverEnabled` hides the interfaces from the scripts and disables the lazy loading.
- The rectangle of an SVG target is its stroke bounding box, clipped by the bounds of the clip paths of the target and of its
  SVG ancestors (as in the browsers).
- Not done: the ink overflow in the occlusion, the `scrolling` attribute of the iframes.

### Layout shifts (`Css/Layout/LayoutShiftTracker.cs`)

[Layout Instability](https://wicg.github.io/layout-instability/) (`Dom/Performance/LayoutShift.cs`,
`Dom/Document.LayoutShifts.cs`):

- The layout shifts of a document are computed once a `PerformanceObserver` of its window observes the `layout-shift`
  entries (`Performance.OnObserving` → `Document.StartLayoutShiftTracking`): the layout is computed at once (the previous
  frame of the next rendering update: the changes made before `observe()` in the same task are not shifts), the document
  is laid out by every rendering update from then on, and `LayoutEngine.SetNeedsLayout` schedules a rendering update. A
  document whose layout shifts are not observed pays nothing (as a deviation, the `layout-shift` buffer of the performance
  timeline only holds the entries since the first observer).
- `DomEventLoop.UpdateTheRendering` runs "report the layout shift" (`Document.ReportTheLayoutShift`) for each tracked
  document at the end of the rendering update (after the intersection observers and "mark paint timing", see
  [Paint timing](rendering.md#paint-timing)). The
  tracker compares the layout with the previous frame only when it changed (`LayoutEngine.LayoutCount`, or
  `LayoutEngine.ScrollVersion` for a scroll). A shift whose value is not 0 queues a `LayoutShift` entry (name `""`,
  duration 0, `startTime` = now, `lastInputTime` = the time of the most recent excluding input of the document or 0,
  `hadRecentInput` when it is less than 500 ms before) to the observers and to the `layout-shift` buffer of the performance
  timeline (150 entries, used by the `buffered` observers; the entries are not available from `getEntries()`).
- The excluding inputs are the trusted `keydown`, `mousedown` and `pointerdown` events and the `mousemove`/`pointermove`
  events with a pressed button (a drag), recorded by `UserAgentDefaultActions.BeforeDispatch` for the document of the target.
- The tracked objects (`LayoutShiftTracker.IsTracked`, as Chrome's `LayoutShiftTracker::NeedsToTrack`): the principal boxes of
  the elements and pseudo-elements (the table wrapper box of a table), except the anonymous boxes, the non-atomic inline
  boxes, the sticky positioned boxes, the SVG elements in an svg element, and the empty block containers that paint nothing
  (no background, border, shadow, outline, not a scroll container); and the text nodes (`br` excluded). They are keyed by
  node (a box tree rebuilt by a `display` change keeps the previous positions).
- A walk of the fragment tree from the root carries, per fragment, its position without the transforms (the fragments under
  a transform are mapped with `LayoutEngine.MapToRoot`), without the transforms and the sticky offsets (the
  "transform-indifferent" position), the clip of its ancestors in viewport coordinates (overflow clips in the clipped axes and
  clip path bounds, as `IntersectionGeometry`, intersected with the viewport without scrollbars), the nearest scroll
  container (not the viewport) and the position in its scrolling contents, the sum of the scroll offsets, and whether it is
  in a fixed positioned box of the viewport. The starting point is the flow-relative starting corner (the writing mode of the
  box) of the first fragment of the box, or of the first text fragment of a text node.
- The visual representation of a box is its visual overflow as in Chrome: its border box united with the visual overflow
  of its descendants that are not positioned, transformed or stacking contexts (those are tracked by themselves) and with the
  text fragments, clipped by its own overflow clip; the one of a text node is the union of its text fragments, each extended
  to the inline-end edge of the content box of its block container. The ink overflow (shadows, outlines) is not included.
- A node is unstable when it is visible in both frames (`visibility: visible`, no zero opacity on its box or an ancestor box),
  in the same coordinate space (the same nearest scroll container, both fixed to the viewport or not: Chrome's "layout shift
  root"), and its starting point and its transform-indifferent starting point both moved by 3px or more (Chrome's threshold)
  in the viewport coordinates and in the document coordinates, and in the scrolling contents of its scroll container. As in
  Chrome, the previous rectangle and starting point are moved by the change of the scroll offsets of its scroll containers
  (the viewport included) and both rectangles are clipped by the current clip: a node whose clipped rectangles are both
  empty, or an inline clip crosser (one of them is empty and it only moved in the inline axis), is not unstable.
- The value is the area of the union of the rectangles (`RectangleUnion`: a sweep with a segment tree) divided by the area
  of the viewport (without the scrollbars), times the greatest move distance divided by the greater dimension of the viewport
  (at most 1). The sources (`LayoutShiftAttribution`, at most 5, sorted by impact area): a node enclosed by a source (both
  rectangles) is redundant, a node that encloses sources replaces them, else the smallest one is replaced when 5 sources are
  kept; the node of a pseudo-element, of a disconnected node or of a node in a shadow tree is null.
- `WebPlatformFeatures.LayoutInstabilityEnabled` hides the interfaces and the `layout-shift` entry type.
- A node that moved in the viewport by the scroll anchoring adjustments of its scroll containers since the previous frame
  (`ScrollState.AnchoringAdjustments`, its previous starting point moved by the scroll and anchoring deltas is within the
  threshold) is not unstable, as Chrome's `scroll_anchor_adjustment`.
- Not done: the delayed report of a touch `pointerdown` until it is known to be a tap or a scroll (the runner has no touch input), the
  `change` events and the viewport resizes as excluding inputs, the ink overflow (shadows, outlines, filters).

### Scroll-driven timelines (`Css/Layout/ScrollTimelineGeometry.cs`)

[Scroll-driven Animations](https://drafts.csswg.org/scroll-animations-1/) (`Animations/ScrollTimeline.cs`,
`Animations/ViewTimeline.cs`, `Animations/ScrollTimelineSnapshot.cs`, `Animations/TimelineRange.cs`,
`Css/Animations/CssScrollTimelines.cs`; see [animations.md](animations.md#scroll-driven-animations)):

- A `ScrollTimeline` or `ViewTimeline` holds a `ScrollTimelineSnapshot` (a record: activity, source, scroll offset, largest
  scroll offset and, for a view timeline, the start and size of the subject and the edges of the view progress visibility
  range), in CSS px of the scroll container along the axis of the timeline, measured from the scroll origin (a reversed
  axis, `direction: rtl` or `vertical-rl`, is flipped: the distance is `max - offset` when `ScrollState.MinOffset` is
  negative). The current time is a percentage: `offset / max` for a scroll timeline, the position in the cover range for a
  view timeline; `GetRange(name)` gives the named timeline ranges (cover, contain, entry, exit, entry-crossing,
  exit-crossing, scroll; the ranges of a scroll timeline are the whole timeline) and `GetFraction(boundary)` the position of a
  boundary of an attachment range (`TimelineRangeBoundary`: name + `LengthPercentage` offset, null for `normal`).
- `ScrollTimelineGeometry.ComputeScrollTimeline` / `ComputeViewTimeline` compute the snapshot from the current layout
  (`EnsureLayout`): the scrolling box is `LayoutEngine.GetScrollState`, the viewport (`ViewportScrollState`) for the root
  element and the scrolling element; the axis must be scrollable (`ScrollState.Overflow`), a scroll timeline needs a
  scrollable overflow, a view timeline only a non-empty cover range (as the browsers). The subject position is the union
  of its fragments in the root (`GetRectInRoot`: transforms ignored, relative positioning and intermediate scroll offsets
  included) translated to the scroll container at the scroll position 0, relative to the scrollport; `auto` insets are the
  `scroll-padding` of the scroll container. The sticky offsets are not handled specially (the browsers compute the range
  of a sticky subject from its sticky constraints).
- `FindNearestScrollContainer` (the `nearest` of `scroll()` and the source of a view timeline) walks the containing block
  chain (`PositionedLayout.FindContainingBlock` for the out-of-flow boxes) from the principal (or pseudo-element) box; the
  logical axis is resolved once with the writing mode of the containing block of the element (as the browsers, WPT
  `*-single-axis-*`), the fallback is the viewport (`document.scrollingElement`, the body in quirks mode), whose axis is
  resolved with the writing mode of the viewport.
- Sampling (https://drafts.csswg.org/scroll-animations-1/#event-loop): the timelines register weakly in
  `DocumentAnimations` (and the document in `DomEventLoop.AddDocumentWithScrollDrivenTimelines`). They are updated at
  the start of "update animations and send events" (`UpdateScrollDrivenTimelines`), then after the layout update of the
  rendering steps (after the resize observers) with `staleOnly`: only the timelines never sampled or whose ranges,
  activity or source changed (not a scroll alone, which waits for the next frame); a change runs `FlushAnimationStyles` and
  `UpdateLayout` once more. A timeline created by script is sampled at once with the current layout; a timeline created
  by the styles is stale until the next rendering update. The current time does not change during a task.
  `LayoutEngine.SetScrollPosition` and `SetNeedsLayout` request an animation frame while timelines exist
  (`DocumentAnimations.OnScrollOrLayoutChanged`).
- An element of an SVG document fragment (an SVG graphics element without CSS box, or a `foreignObject`) is a subject
  through its outermost svg element: the lookup of the scroll container starts at the box of the outermost svg element,
  and the subject box is the bounding box of the element with the transforms of the SVG document fragment
  (`SvgBoundingBox.GetBoundingQuad`, as `getBoundingClientRect()`), in the content box of the outermost svg element.
- A subject that is sticky positioned in the scrollport of the timeline, or in a sticky positioned box of its containing
  block chain, moves with the scroll position: as in the browsers, its ranges are the scroll offsets at which its edges,
  at its sticky position for each scroll offset, reach the edges of the visibility range (`ComputeStickyEdgeOffsets`,
  `ViewTimelineEdgeOffsets`, from the `StickyPositionConstraints` of the fragments; the position relative to the
  scrollport does not increase with the scroll offset, so each edge is found by bisection; an edge that sticks at the start
  edge of the range has not left it yet).
- Not done: the scroll containers of pseudo-elements, a sticky positioned inline box that contains the block-level box of
  the subject (it is not on the fragment chain of the subject), the additional style and layout pass of the timelines
  created by the resize observers.

### Painting (`Css/Layout/Painting/`)

The painting turns the fragment tree into a backend-agnostic display list (`Rendering/DisplayList.cs`, see
[docs/rendering.md](rendering.md)): `LayoutEngine.GetDisplayList()` (`Document.GetDisplayList()`) forces the layout and paints
the viewport with `Painter.Paint(engine, root, viewportScroll, baseColor)`. The list is cached and painted again only when
the layout pass changed (`_fragmentIndexVersion`: the styles, including the paint-only properties, the DOM, the images...),
the DOM changed (`Document.DomVersion`: e.g. an attribute of a filter primitive or of the content of a mask, which have no
box) or a scroll position changed (`LayoutEngine.InvalidatePaint()`, called by `SetScrollPosition`). Nothing is painted unless
something asks for the display list (`LayoutEngine.PaintCount` for the tests).

- **Order.** `PaintOrder.Collect(root, items, markers: true)`: the painting order of the hit testing with the bounds of the
  stacking contexts (`PaintItem.Marker` = `BeginStackingContext` / `EndStackingContext`, the root item follows the begin
  marker; without `markers` the list is unchanged for the hit testing). The top layer is painted after the viewport group.
  The outlines are collected per stacking context and painted at the `Outlines` markers, which follow the in-flow content of
  each stacking context and of each positioned box (as in Blink and WebKit, rather than at the end of the stacking context,
  CSS 2 Appendix E step 10: an outline is below the positioned boxes that follow its box). The layers with the
  same `z-index` are sorted in tree order of their boxes (`TreeOrder`: a preorder index of the box tree; the flex and grid
  items keep their order-modified document order, the absolutely positioned children of the container being ordered as
  `order: 0`), and a layer whose containing block chain skips its nearest ancestor
  stacking context in the DOM (an absolutely positioned box whose containing block is outside of an element with `opacity`,
  or the initial containing block: the root element is the root stacking context) is collected by that stacking context
  (`ComputeAdoptions`: `Moved` / `Adopted`). The non-replaced inline boxes are stacking contexts only for their filters
  (not for the transforms and the containment, which do not apply to them). The stacking contexts of the fragments of an
  inline box split over several lines are nested in the one of its first fragment when the effects of the box only
  composite it (`PaintOrder.IsCompositedAsOneGroup`: opacity, blend mode, isolation; `Painter.ApplyEffects` then applies
  them once, to the group of the first fragment), and the floats of an inline box that establishes a stacking context,
  whose fragments are children of the fragment of the block container, are painted in that stacking context
  (`PaintFloatsOfInlineStackingContext`), not with the floats of the block container.
- **Groups.** A stacking context saves the state, applies its `clip` rectangle and its transform
  (`LayoutTransforms.GetMatrixWithPerspective`: with the `perspective` of its parent, translated to its position: the
  coordinates of its contents become its border box; a 3D transform outside of a 3D rendering context is flattened with a
  `z = 0` projection; a transform that only translates the plane of the box, e.g. `translate()` or `translateZ(0)`, is not a
  transform of the display list: the stacking context is painted in the coordinates of its parent at its translated position,
  so that its boxes are snapped to the device pixels as the boxes that are not transformed, except for the root of a 3D
  rendering context), then its effects (`Painter.Effects.cs`, see below). The list starts with the base color of the
  canvas (the canvas color of the color scheme for a top-level document), then `translate(-viewportScroll)` (snapped).
- **Effects** (#376, `Painter.Effects.cs`, https://drafts.fxtf.org/filter-effects/#placement: the filters, then the clipping,
  the masking and the opacity). `ApplyEffects` emits, in the coordinates of the stacking context: the clip of a basic shape
  of `clip-path` (`BasicShapePaths`: `inset()`, `rect()`, `xywh()` with `round`, `circle()`, `ellipse()` (rounded rectangle
  clips), `polygon()`, `path()`, `shape()`, in the reference box: the geometry boxes, the computed margins for `margin-box`
  (its radii are the outset-adjusted border radii of CSS Borders 4), the box of the first fragment of an inline box split over several
  lines for all its fragments (as in the browsers: the specifications do not define it), the nearest SVG viewport for the
  `view-box` of a `foreignObject`; a `<geometry-box>` alone clips to the box with its rounded
  or shaped corners; a `clipPath` element with a single shape is a path clip); a layer (`SaveLayerItem`) for the opacity, the blend
  mode, the isolation (not when a descendant stacking context has a `backdrop-filter`: `isolation: isolate` is not a backdrop
  root), a clip path (a backdrop root), `will-change` of a backdrop root property (or an animation of one that is current
  or in effect) when a descendant stacking context has a `backdrop-filter` (a layer whose backdrop nothing reads would
  only round the colors of its translucent contents twice) and the backdrop filters
  (`BackdropFilters` + `BackdropClip`, the rounded border box; not on the root element); the filters
  (`FilterEffects.Get`: the CSS functions, and the `url()` references to SVG filter elements as `FilterGraph`s built by
  `SvgFilterBuilder`, a missing reference ignores the whole chain, the SVG filters are dropped over cross-origin frames) in
  the same layer (they also filter the filtered backdrop of the element), or in a nested one when a mask is painted between
  them. The masks are painted at the
  end of the stacking context (`Group.Mask`, after the filter layer is composited, `PaintMask`), each one in a
  `DestinationIn` layer: a `clipPath` element (its shapes in black, clipped by their clip paths and by the one of the
  `clipPath`; the clip path of a shape is applied in the user space of the shape; a reference to a `clipPath` being painted,
  i.e. a reference cycle, is ignored; the `transform` of a `clipPath` with `clipPathUnits="objectBoundingBox"` is in user
  units, after the bounding box units of its contents), the mask layers (`GetMaskLayers`: `mask-image`/`-position`/`-size`/`-repeat`/`-origin`/`-clip`/`-composite`
  /`-mode` painted as background layers, from the bottom one, each one in a layer composited with the ones below by its
  `mask-composite` operator (`source-out`, `source-in`, `xor`), a `LuminanceToAlphaFilter` for the luminance masks; a mask
  element (`mask-type`, `maskUnits`, `maskContentUnits`, the mask region) is painted with the SVG painting; an SVG image is
  painted by its document) and `mask-border` (the border image painting with the `mask-border-*` properties,
  `mask-border-mode`). The resources are applied in a `SvgResourceSpace` (the user space and the object bounding box of the
  element: the border box for a CSS box, the bounding box of the box and its descendants for the filters, as the browsers,
  the percentages of the user space lengths referring to the border box; the user space of a `foreignObject` is the one of
  its SVG parent: its box is at its `x` and `y`, `SvgForeignObjectLayout.GetViewportRect`).
  The SVG elements apply the same effects in their user space (`ApplySvgEffects`: the SVG boxes for the geometry boxes, the
  mask layers that reference nothing are ignored; the images of the mask layers are painted in zoomed CSS px). The stacking contexts whose child stacking contexts have a
  `mix-blend-mode` are isolated layers (`CollectIsolatedGroups`); the effects of the root element (and its isolation) apply
  to the background of the canvas, which is then painted in its stacking context. The `filter()` image function is a
  `FilteredShader` (the filters in the coordinates of the drawn image, clipped to it).
- **3D rendering contexts** (#376, `Paint3DRenderingContext`, `PlaneSorting`). A stacking context with the used value
  `preserve-3d` of `transform-style` (`LayoutTransforms.UsesPreserve3D`: no grouping property) is the root of a context: the
  stacking contexts whose parent elements up to it all preserve 3D are planes with their accumulated matrices (not flattened),
  the other items are in the plane of their nearest plane (a segment of a plane after one of its child planes is another
  polygon, so the planes at the same depth keep the painting order). The planes are sorted from the farthest with a BSP tree
  of the polygons of their ink overflow rectangles (split along the other planes, the parts clipped by their polygon), or by
  the depth of their center when a vertex is behind the viewer. `backface-visibility: hidden` hides a plane showing its back
  face (the root's own items only for the root); a stacking context with a non-invertible transform is not painted.
- **Coordinates.** A fragment is painted at `GetOffsetInRoot() - origin` where the origin is the position of the nearest
  transformed stacking context (or of the root): the scroll positions of the scroll containers are applied by
  `GetOffsetInRoot`, the viewport-fixed boxes cancel the viewport translation.
- **Clips.** The overflow clips do not nest with the painting order: each item has its chain of clips (its fragment ancestors
  that clip their overflow, which is its containing block chain since an absolutely positioned fragment is a child of its
  containing block fragment, up to its stacking context, `GetClipChain`); the painter keeps the clips of the previous item
  (one save each) and restores/applies only the difference (`SyncClips`), as the property trees of Blink. The clip is the
  `OverflowClipRect` in the clipping axes, rounded by the inner border radii when both axes clip (the corners of an edge
  expanded beyond the border edge by `overflow-clip-margin` are shaped as the spread of a box shadow, from the border edge:
  the outset-adjusted radii; the contour of the box when its corners are shaped by `corner-shape`); the nested documents
  (`iframe`...) are clipped to the content edge curve too. The contents of a replaced
  box are clipped by its own clip (`includeSelf`): the user agent style sheet has `img, video, canvas { overflow: clip }` and
  `iframe, embed, object { overflow: clip !important; overflow-clip-margin: content-box !important }`
  ([CSS Overflow 4](https://drafts.csswg.org/css-overflow-4/#overflow-control), the rules of the browsers), and the box of
  `overflow-clip-margin` defaults to the content box of the replaced elements (not of an `img` rendered as its alternative text,
  nor of the form controls), so an image overflowing its content box (`object-fit: cover`) is clipped to it unless the author
  sets `overflow: visible`; the contents of the other replaced boxes (the generated images) are always clipped to the content
  box, and an outermost `svg` element to its overflow clip edge.
- **Pixel snapping.** The geometry of the layout (and of the CSSOM View) keeps its fractional positions; the painter snaps
  what it paints to the device pixels of the viewport (`ViewportOptions.DevicePixelRatio`), as Blink does: the border boxes
  of the backgrounds, the borders, the outlines and the shadows, the overflow clips, the replaced content rectangles, the
  column rules, the decorations and the baselines of the texts (the glyphs keep their subpixel positions along the line),
  and a background image that is not repeated (its rectangle is computed in the positioning area of the layout, then
  snapped; an SVG image is laid out in its unsnapped size and scaled to the snapped rectangle).
  Each edge is rounded in the coordinates of the group (`SnapToDevicePixels`), a non-empty side keeps at least one device
  pixel, and the viewport scroll translation is snapped too. The fonts take part in it: the ascent, the descent and the line
  gap are rounded to whole pixels (`MatchedFont.GetMetrics`, except the tiny fonts, as Blink's `FontMetrics`), so
  `line-height: normal` is an integer (18px for the 16px default font instead of 18.4px), the half-leading above the text is
  floored (`InlineLayoutAlgorithm.GetHalfLeadings`, Blink's `CalculateLeadingSpace`) and the central baseline gives the odd
  pixel to the ascent: the line boxes and the baselines of the integer font sizes are on whole pixels. The border widths
  are computed as single-precision floats before being snapped (25.4mm is 96px).
- **Backgrounds** (`Painter.Backgrounds.cs`, `PaintedImage`, `CssGradient`): the background color in the `background-clip`
  area of the bottom layer, then the layers from the bottom one. A layer resolves its `<image>` (`PaintedImage.Resolve`: a
  `url()` fetched by `CssImageFetching`, whose encoded bytes are kept in `Document.CssPaintImages` when the document has a
  rendering backend; the gradients; `image(color)`; the option of `image-set()` selected for the device pixel ratio, see
  below; `cross-fade()`), sizes its tiles (`background-size`
  with the natural size and ratio (an image with one natural dimension and a ratio, e.g. an SVG image with a `width` and a
  `viewBox`, gets the other dimension from the ratio), `cover`/`contain`, the default sizing of the images without natural dimensions, the
  `round` adjustment), positions them in the positioning area (`background-origin`, the viewport for `fixed` when no
  transformed stacking context contains the box, the scrolled scrolling area for `local`, the area of all the fragments of a
  sliced inline box, the fragments stitched together of a box split by the block fragmentation, `BoxFragment.StitchedBox`), spaces them (`space`) and draws them in the painting area (`background-clip` rounded by the radii,
  `border-area` (the border box without the padding box: the border styles are not taken into account), `text` through a
  layer masked with `DestinationIn` by the glyphs, the decoration lines and the emphasis marks of the box and of its
  descendants that are not absolutely positioned, `border-area text` by the union of the two): a single tile is an image or a rectangle
  filled with the gradient, the repeated tiles an `ImageShader` (repeat) or a `TiledShader` (a picture shader of the tile and
  its spacing, for the gradients and `space`; in the axis where the tiles are not repeated, their edges are snapped to the
  device pixels as the ones of a single tile). The painting area of a `local` layer of a scroll container is what its
  padding box shows of its scrolled contents (`GetBackgroundPaintingArea`): the padding box for `border-box` and
  `padding-box`, the padding box clipped to the content box of the scrollable overflow area moved by the scroll position
  for `content-box`. `background-blend-mode` blends the layers in an isolated layer; the color and the layers above it
  that are clipped to the text are painted and blended as one group that is masked once, and a text-clipped layer above a
  layer with another painting area is a group composited with its blend mode. Under rounded borders that hide it (four
  borders with a width, an opaque color and a style other than `dotted`/`dashed`, no border image), a `border-box`
  background stops in the middle of the borders (at a sixth for `double`), as the background bleed avoidance of Blink
  (`GetBackgroundBleedInset`): the antialiased edges of the background and of the border do not coincide. The canvas
  background (the root's, or the body's when the root has none and the body generates a box: not with `display: none`,
  nor with `display: contents` as in the browsers; also in XHTML) covers the canvas with the positioning area
  of the root element; the base color below it is the `Canvas` system color of the used color scheme of the root element
  (white, or dark for `color-scheme: dark`). The used color scheme of `color-scheme: normal` is the one of the page's
  supported color schemes (the first valid `<meta name="color-scheme">`, `Document.PageColorSchemes`), light without them.
  The preferred color scheme (`prefers-color-scheme`) of a nested document is the used color scheme of the element that
  embeds it (an element that supports no color scheme passes on the preferred color scheme of its document), and the canvas
  of a nested document is transparent unless its color scheme differs from the used color scheme of its container: it is then
  painted over its `Canvas` color ([CSS Color Adjustment](https://drafts.csswg.org/css-color-adjust-1/#color-scheme-effect);
  the initial `about:blank` document stays transparent, as in the browsers).
- **Gradients** (`CssGradient`): the computed functions (`linear-`, `radial-`, `conic-`, `repeating-` and the legacy
  `-webkit-` forms) become shaders for a tile size: the gradient line of the angle or of the corner (the magic corners), the
  ending shapes of the radial gradients (sizes, positions, degenerate shapes), the conic start angle; the color stop fixup,
  the transition hints and the interpolation color spaces other than sRGB (Oklab for the non-legacy colors, `in <space>
  <hue>`) are expanded into sRGB stops (`CssColorMix.Interpolate`: 24 per segment, and each interval whose middle color
  differs from the sRGB interpolation of its ends by more than a quarter of an 8-bit level is split again, down to
  intervals of half a pixel: the curves are steep near black and have angles at the primary hues of `hsl`); a stop with a
  missing component (`none`) has the component of the other stop of the segment on each of its sides; the colors that
  depend on `currentcolor` (`color-mix()`, the relative colors) are computed with the color of the element; the stops
  outside of the line extend it, the repeating gradients use the repeat spread (the conic ones are replicated over the
  turn). The positions and the sizes are clamped to ±2^25px (the largest length of Blink), and a radial gradient whose center
  or stops are further than 10^6px from its box (e.g. `calc(infinity * 1px)`) is painted as a solid color when its box has a
  single color (the single-precision numbers of the shaders cannot place its stops).
- **`image-set()` and `cross-fade()`** ([CSS Images 4](https://drafts.csswg.org/css-images-4/#image-set-notation),
  `CssImageSet`, `PaintedImage.CrossFadeImage`): the options of an image set whose `type()` is not a supported image type
  are removed, then the ones that repeat a resolution; the option with the smallest resolution that is not smaller than the
  device pixel ratio is selected (else the largest one), fetched (only this one) and painted with its resolution as natural
  resolution; an option of resolution 0 is an invalid image. `-webkit-image-set()` is a parse-time alias, and the specified
  value is serialized in its canonical form (`url()` for the strings, `1x` for an omitted resolution). A cross-fade is the
  sum of its images multiplied by their percentages (the omitted ones share what is left of 100%, a total over 100% is
  scaled down, a total under 100% leaves the result translucent), each drawn in the rectangle of the result: layers added
  with plus-lighter in a picture shader, or the exact mix when all the images are colors.
- **Borders** (`Painter.Boxes.cs`, `BorderPainter`, `Painter.BorderImage.cs`): one ring when uniform, else each side
  clipped to its trapezoid (the diagonal of a rounded corner extends to the chord of the curve of the padding edge, so a side
  next to a missing side keeps its part of the curve; the corners of a fragment at the breaks are square and do not reduce the
  other radii, `BorderRadii.Get`). The corners shaped by `corner-shape`
  ([CSS Borders 4](https://drafts.csswg.org/css-borders-4/#corner-shaping); `round`, `scoop`, `bevel`, `notch`, `square`,
  `squircle`, `superellipse()`) make the edges of the box contours (`RoundedRect.Contour`, `CornerContour`: the border contour
  path of the specification, see [the display list](rendering.md#display-list)): the backgrounds and their clips, the
  borders (the area between the outer and the inner contour), the shadows, the outlines, the overflow clips, the box values
  of `clip-path`, the backdrop of `backdrop-filter` and the hit testing follow them. The radii of the concave corners are
  also reduced so that the hulls of two opposite corners do not intersect (the opposite corner scale factor, `BorderRadii.Get`),
  and `border-shape` makes the radii ignored. The styles: solid, double, dotted/dashed as strokes along the side with the dash lengths of Blink adjusted to
  the length of the side (`BorderPainter.GetDashes`: dashes of 3 widths and gaps of 2, 2 and 1 from 3px, dots of one width,
  whole dashes at both ends), groove/ridge/inset/outset darkened by 2/3. `border-image` replaces them: the nine slices of the
  image (`border-image-slice`, `fill`) in the regions of the border image area (`border-image-outset`, `border-image-width`
  reduced when they overlap; the lines between the regions are snapped to the device pixels), the sides stretched, repeated
  (centered), rounded or spaced (`TiledShader` of the slice: the pixels of the slice of a raster image, the display list of
  an SVG image painted again for the tile, scaled and clipped to the slice); a
  fetched image that cannot be painted still hides the borders. `empty-cells: hide` (separated borders).
  The outlines around the border box at `outline-offset` (`inset` is the opposite of the width of the outline: the outline
  is inside of the border box; a negative offset does not shrink the outline of a fragment below twice its width in each
  axis; `auto` is the focus ring of Blink: 3px whatever `outline-width` is, two rings of a 2:1 ratio with corners rounded to
  at least 3px, the inner one in the color of the outline (the accent color when `outline-color` is `auto`), the outer one
  white, or #101010 in a dark color scheme; it does not enclose the overflowing descendants), not for the columns; the gap decorations
  (the column and row rules of the grid, flex and multi-column containers, see [Gap decorations](#gap-decorations-csslayoutgaps);
  `inset`/`outset` as `ridge`/`groove`); the MathML rules and glyphs (`MathFragmentData.PaintItems`).
- **Shaped borders** (`border-shape`, [CSS Borders 4](https://drafts.csswg.org/css-borders-4/#border-shape);
  `BorderShapeGeometry`, `Painter.BorderShape.cs`): with one shape, the border is the stroke of the shape (in the
  `half-border-box` by default), of the width and the color of the relevant side (the first of block-start, inline-start,
  block-end and inline-end whose style is not `none`); with two shapes, it is the area between the first one (in the border
  box by default) and the second one (in the padding box), filled with the border color of the relevant side whatever the
  border widths. The shapes are the basic shapes of `clip-path` (`BasicShapePaths`), positioned at the border box of the
  layout (not snapped to the device pixels). The outer path is the outer edge of the border (the shape and its stroke, or the
  first shape) and the inner path its inner edge (the shape without its stroke, or the second shape): the outer shadows are
  cast by the outer path expanded by their spread and clipped out of it (one pixel inside of it when an opaque
  `background-color` covers it: a shape without stroke inside of the border box, as for the other boxes), the inset shadows are in the inner path, the
  overflow is clipped to the inner path when the box clips both axes (with the overflow clip edge; `overflow-clip-margin`
  moves the edge of its box outwards: the outer path for `border-box`), the contents of the replaced elements are always
  clipped to it, and the box is hit in the outer path. `border-radius`, `corner-shape` and `border-image` are ignored. What
  the specification leaves undefined follows Blink (the only implementation) and the tests: the border styles are not
  applied (the stroke is solid, and the border is painted when its style is `none` with two shapes); the joins of the
  stroke are miter joins, without miter limit when the shape only has straight segments (`inset()` without radius,
  `polygon()`, `path()` and `shape()` made of lines) and with the limit of 4 otherwise; the backgrounds are clipped to
  the stroked shape (the middle of the border) or to the second shape, in the box of `background-clip` (so they do not show
  through a translucent border between two shapes), `background-clip: border-area` is the area of the shaped border (also
  outside of the border box; an image layer is masked by the border painted in a `destination-in` layer), the backgrounds
  of the rows and of the columns painted in a cell are clipped by the shape of the cell; the outline follows the outer
  path at the distance of `outline-offset` (a rounded rectangle is outlined as a box with border radii, with all the
  outline styles; for the other shapes `double` is two bands, `dotted` and `dashed` are a dashed stroke and the other
  styles are solid). Unlike Blink, the contents are also hit tested in the inner path (Blink tests them in the outer one),
  and `overflow-clip-margin` offsets the path by its length instead of resolving the shapes in an expanded reference box.
  The stroke of a rectangle with circular corners (or of a circle) is painted as the ring of a border and the edges of a
  rounded rectangle stay rounded rectangles (`PathOperations.Offset`: the radii grow or shrink, the edges of an ellipse
  are ellipses, an approximation of its stroke); the edges of the other shapes are computed by `PathOperations`: the
  stroke is the union of the quadrilaterals of its flattened segments and of its joins, and the unions and the differences
  of the areas are computed in horizontal slabs between the vertices and the intersections of the edges (the result is
  a path of trapezoids, so the backends only fill and clip paths). Not done: the shaped borders of the fragments of a box
  split by a fragmentation break or over several lines (each fragment has the whole shape), `polygon()` with `round`,
  and the shapes in the backdrop of `backdrop-filter` and in the box values of `clip-path`.
- **Shadows** (`Painter.Shadows.cs`): the outer `box-shadow`s below the background (the border box offset and spread, the
  radii grown by the spread as the [outset-adjusted border radius](https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius):
  a radius smaller than the spread grows by less, unless the corner covers the box (`coverage`: a circle stays a circle), a
  blur of standard deviation `blur / 2`, clipped out of the border box; one pixel inside of the border box when the box
  paints an opaque `background-color` in its border box, as Blink does, so that nothing shows through between the
  antialiased edges of the shadow and of the background), the inset ones
  above it (the padding box minus the shrunk and offset hole, clipped to the padding box); the first shadow on top. The shadows
  of a box split by the block fragmentation (`box-decoration-break: slice`) are the ones of its fragments stitched together,
  clipped at the breaks (no shadow is painted there); the ones of an inline box split over several lines are the ones of a
  box that continues past the broken edges of the fragment, clipped to the fragment in the inline axis. The rows and the
  row groups of a table paint their shadows with their backgrounds (the outer ones only in the separated borders model).
- **Tables** (`Painter.Tables.cs`): the table wrapper paints nothing; the rows, the row groups, the columns and the column
  groups have no border and paint their backgrounds in each of their cells (positioned in the area of the part, clipped to
  the cell), when the table grid is painted (also an anonymous one). In the collapsing border model, the table paints the
  resolved borders of `TableCollapsedBorders` (each segment keeps its box style and side for its color) after its last cell:
  each segment centered on its grid line (the edges of the border boxes of the cells), extended by half of the widest
  crossing border, by increasing precedence so the winner covers the corners; the segments inside of a spanning cell are
  not painted (horizontal tables only).
- **Text** (`Painter.Text.cs`, `Painter.TextDecorations.cs`): one `GlyphRun` per font run of the fragment
  (`DisplayFont.From(MatchedFont, emSize)`: the font file, the face index, the variation settings
  `MatchedFont.GetVariationSettings(emSize)`, the synthetic bold/oblique), the glyph origins from the final advances and
  offsets of the shaped text (the spacing is already in them); invisible glyphs skipped; right-to-left runs from the right
  edge; in vertical text the sideways glyphs are drawn in a frame rotated by 90 degrees (-90 for `sideways-lr`) on their
  alphabetic baseline (shifted from the central baseline of the line by the central baseline of their font,
  `InlineLayoutAlgorithm.GetCentralBaseline`: halfway between its ideographic baselines, else `(ascent - descent) / 2`), the upright
  glyphs centered on the central baseline (their em box centered on the central baseline of their font along the line), the compositions combined upright horizontally with `GlyphRun.ScaleX`. The text
  of a run is the text node data of its clusters (dumps, #380). The painting order of css-text-decor-4: the `text-shadow`s
  (glyphs and decorations, blurred), the backgrounds of the highlights, the underlines and overlines, the glyphs (filled with
  `-webkit-text-fill-color`, stroked with `-webkit-text-stroke`, in the order of `paint-order`), the emphasis marks (half
  size marks over or under each cluster, their content area next to the content area of the text and outside of its ruby
  annotations, as the annotations; upright, to the right or to the left of the clusters of vertical text, or rotated with the
  sideways text; [no mark](https://drafts.csswg.org/css-text-decor-4/#text-emphasis-style-property) for the separators
  (`Z*`, unless a combining mark follows), the control, format and unassigned characters (`Cc`, `Cf`, `Cn`) and the
  punctuation (`P*`, except the symbols listed by the specification such as `#`, `%`, `&` and `@`), `HasEmphasisMark`),
  the line-throughs, then the highlighted glyphs again in the color
  of their highlight (clipped to the highlighted characters) with the decorations of the highlight, and the decorations of
  the text again in this color ("drawn in the pseudo-element's own color when that is not currentColor"). The texts are painted with
  their style on the line (`TextFragment.Style`: `::first-line`), and the lines truncated by `text-overflow` are clipped to their
  visible content, their ellipses painted after them (see [Text overflow](#text-overflow-csslayoutinlinelinetruncatorcs-lineellipsiscs)). The decorations are
  propagated from the decorating boxes (the ancestors up to an atomic inline, a float or an out-of-flow box, through the
  inline boxes a block was split out of; not into the tables in quirks mode) and drawn at the baseline and with the font of
  the decorating box (for a block container: the baseline of the line box and the font of the block container of the line,
  as the decoration is propagated to the anonymous inline box of each block): `text-underline-position`/`-offset`
  (the offset is added to the alphabetic baseline, to the underline position of the font for `from-font`, or to the edge of
  the text for `under`, `left` and `right`), `text-decoration-thickness`
  (floored to device pixels as in Blink; the overline is outside of the ascent: a thick overline grows away from the text),
  the styles (dashes anchored to the coordinates so the fragments are continuous,
  wavy as quadratic curves), `text-decoration-skip-ink` (the glyph bounding boxes that cross the line, extended by its
  thickness, at most 13px as in Chrome, are clipped out, their union snapped to the device pixels; also for the sideways
  glyphs of vertical text, not for the upright ones, as in the browsers; the value of the element of the text applies, not
  the one of the decorating box; the glyphs of the text fragments before and after the fragment on the line are skipped
  too, `GetLineInkSkipIntervals`: the gap around a glyph at the edge of an inline box continues along the text next to
  it), `text-decoration-skip-spaces` (`GetSkippedSpaces`: the lines stop at the first and last
  characters of the line box that are not spacers, the characters of the category `Zs` except U+202F, for `start` and
  `end`, the initial value, so the preserved or non-breaking spaces at the ends of a line are not decorated; `all` also
  skips the other spacers and word separators with the letter-spacing before them) and `text-decoration-inset`
  ([CSS Text Decoration §2.9.1](https://drafts.csswg.org/css-text-decor-4/#text-decoration-inset-property),
  `ApplyDecorationInset`): the insets move the ends of the lines that a decorating box draws inward, or outward when they
  are negative, from the ends of the text that the box decorates on each line (`GetDecoratedRun`: the extent of the text
  fragments that its decorations propagate to, without the skipped spaces at the ends of the line and without the edges of
  the inline boxes; the lines of each block container are the fragments of the anonymous inline box that the decorations
  of a block container are propagated to). With `box-decoration-break: slice`, the decorated text of the lines is one run:
  the percentages refer to its size, the start inset applies from the start of the first line and the end inset from the
  end of the last line (an inset longer than a line continues on the next line, a negative inset only extends the first
  or the last line); with `clone`, the insets apply to each line and the percentages refer to the decorated text of the
  line. The text fragment at an end of the decorated text draws the part of a line that a negative inset adds. `auto` is
  half the thickness of the line, 1px at least, on each side (the amount is chosen by the user agent; when `auto` is
  interpolated with lengths it stands for that length, computed from `text-decoration-thickness` and a tenth of the font
  size for `auto`, [csswg-drafts#13036](https://github.com/w3c/csswg-drafts/issues/13036)). The WPT references of the
  insets of the lines that end with no-break spaces expect these spaces to be underlined, which the initial
  `text-decoration-skip-spaces: start end` does not do (they pass with `none`). In the vertical typographic mode
  (not for the sideways text), the underline is at the left or at the right edge of the text: `left`, `right`, and for
  `auto`, as in Chrome, the right edge for the languages of the
  [default style sheet](https://drafts.csswg.org/css-text-decor-4/#default-stylesheet) of the specification (`ja`, `ko`,
  `mn`), else the left one; an underline on the line-over side moves the overline to the other side. The
  highlights ([CSS Pseudo §3.6](https://drafts.csswg.org/css-pseudo-4/#highlight-painting), `GetHighlights`) are, in painting
  order, the custom highlights by priority (`::highlight(name)`), the target text of the text directives of the URL
  (`::target-text`, `Document.TargetTextRanges`; the `Mark` and `MarkText` system colors when it is not styled) and the
  selection of the document (`::selection`; the `Highlight` system color when it is not styled). The
  [paired defaults](https://drafts.csswg.org/css-pseudo-4/#paired-defaults) are used only when neither `color` nor
  `background-color` is specified for the pseudo-element (also by the highlight pseudo-element of an ancestor); a
  `revert` to the user agent origin is not a specified value. The ranges are split where the highlights below start and
  end: a `color` that is `currentcolor` (the default) is the color of the text in the highlight overlay below (or of the
  originating text), and `currentcolor` in `background-color` and `text-decoration-color` is the color of the text in the
  overlay. The overlapping ranges of a custom highlight are highlighted once (their union), the
  [static ranges that are not valid](https://drafts.csswg.org/css-highlight-api-1/#range-invalidation) are ignored, and the
  selection does not highlight the text whose used `user-select` is `none`. The decoration lines of a highlight span its
  characters as the lines of an inline box would (a wavy line starts at the first highlighted character), and its underlines
  and overlines are under its glyphs: a highlight with these lines paints the glyphs it covers. The text of a highlight has
  no stroke (`-webkit-text-stroke` does not apply to the highlight pseudo-elements and is not inherited from the text),
  except, as in Chrome, when no declaration applies to the pseudo-element (the default selection keeps the stroke of the
  text). The `text-shadow` of a highlight is painted over the background of its overlay, under the backgrounds and the
  shadows of the overlays above it ([CSS Pseudo §3.6.2](https://drafts.csswg.org/css-pseudo-4/#highlight-shadows): the
  shadows of the highlighted glyphs, `currentcolor` being the color of the text of the highlight; the shadows of the
  originating text are painted for all the text, under the overlays). The shadows of the highlights are under the text of
  the whole fragment, also the text that is not highlighted, as Blink when several highlights apply ("UAs should avoid
  painting text shadows over text in adjacent elements"; for a selection alone, Blink paints the shadows of the selected
  text over the text that is not selected, which WPT `css/css-pseudo/highlight-painting-002.html` expects); the decorations
  of a highlight have no shadow. Not done:
  `::spelling-error` and `::grammar-error` (no spelling nor grammar checking), the highlights of the
  replaced elements and of the text of the form controls, but the selection of the value of a text control
  ([text field selection](https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection):
  `select()`, `setSelectionRange()`...), which is highlighted in the lines of its inner editor with the `::selection` of the
  control when it is not collapsed, also when the control is not focused (as Gecko; Blink focuses the control in
  `select()`); its overlay covers the line boxes
  ([highlight bounds](https://drafts.csswg.org/css-pseudo-4/#highlight-bounds): "may extend [...] to the line box edges"),
  as in the browsers (`TextControlLayout.GetSelectedRange`; a change of the selection invalidates the painting). The boundary points of the ranges are compared in
  shadow-including tree order (`AbstractRange.CompareComposedPoints`): the text of the shadow trees between them is
  highlighted, and the selection set by `setBaseAndExtent()` with an anchor and a focus in different trees is highlighted
  between them (`Selection.GetRenderedBoundaryPoints`; the live range of the selection is still collapsed at the focus).
  The selected text of the inert nodes is not highlighted.
- **Replaced content** (`Painter.Replaced.cs`, `Painter.FormControls.cs`): nothing when the contents are skipped
  (`content-visibility`); the image of an `img` (`HtmlImageElement.GetPaintImage()`) and the poster or the frame of a `video`
  (`HtmlVideoElement.GetPaintImage()`) in `ReplacedContentRect`, sampled per `image-rendering`; the images of the generated content and of the
  list markers (`ContainerBox.GeneratedImage`); the outermost `svg` paints its descendants in document order (`g`, nested
  `svg`, `use`, the shapes with solid `fill`/`stroke` colors, `paint-order`, `opacity` layers; the fallback color of a paint
  server); the `foreignObject` fragments are painted as stacking contexts at their place in that order
  (`PaintOrder.CollectStackingContext(fragment, items, markers: true)`); the document of the navigable of an `iframe`,
  `frame`, `embed` or `object` (its display list appended in the content box, or in the rectangle of `object-fit` for an SVG
  document, `DisplayListBuilder.AddList`, scaled by the effective zoom of the container), or the image of an `embed` or `object` element; the form
  controls: the value of the date and time fields (`text-indent`, `text-align` (a line that overflows is start-aligned),
  `text-transform`; a line with right-to-left characters, or of a right-to-left or `unicode-bidi: plaintext` control, is
  reordered by the bidi algorithm, and `text-overflow` does not elide it), the labels of
  the buttons (after `text-transform`; one line per line feed for the `input` buttons), the selected option and an arrow of a drop-down box with native appearance (a `select` with base appearance is painted as its boxes, see [Select elements with base appearance](#select-elements-with-base-appearance); the rows of a list box with native appearance, the parts of a slider-like control with primitive appearance and the button of a file upload control are boxes too, see [Replaced elements](#replaced-elements-csslayoutreplaced)), the format of an empty date and time field (`mm/dd/yyyy`), and, with the native appearance (`appearance`
  other than `none`, and no author background or border that
  differs from the user agent style sheet: the widget fallback; any author declaration of the
  [properties that disable the native appearance](https://drafts.csswg.org/css-ui-4/#appearance-disabling-properties) for
  `progress`, `meter`, the text controls and the drop-down boxes, after the rollback of `revert`, as in the browsers: WPT
  `css/css-ui/compute-kind-widget-generated`), a simple
  deterministic look for the checkboxes, the switches (a rounded track, filled with the accent color when checked, and a
  disc at its inline-start side, at its inline-end side when checked; `indeterminate` does not apply), the radio buttons,
  the sliders, the color wells, the progress bars and the meters, and a thin rounded frame (1px, `#767676`) along the
  border box of the text fields, the `textarea` elements and the drop-down boxes (`Painter.PaintNativeFrame`: the user agent
  style sheet of the specification gives them no border, so the frame takes no space; a drop-down box with
  `appearance: menulist-button` has no frame, its borders are the ones of CSS, and keeps its arrow, as a devolved drop-down
  box does: "including a drop-down button")
  (`accent-color` replaces the accent color of the checkboxes, the radio buttons, the sliders and the progress bars; a
  transparent one is composed over `Canvas`). The text of a date and time field is clipped at the content edge in the
  inline axis and at the padding edge in the block axis. The value and the placeholder of the text fields and of the
  `textarea` elements are not painted by the painter of the form controls: they are the lines of the inner editor and of
  the `::placeholder` box of the control (see [Replaced elements](#replaced-elements-csslayoutreplaced)), whose fragments
  are children of the fragment of the control and are painted in the painting order as the contents of the other boxes, in
  the clips of the control (the scrollport of a `textarea`, which scrolls them, so they overflow over its paddings as in
  the browsers; the overflow clip edge of a text field, the padding box, and its inner editor, which clips and scrolls its
  line in the content box). The hit testing of these fragments returns the control.
  Without the native appearance, the parts of a slider, of a progress bar and of a meter are the boxes of their
  `::slider-track`, `::slider-fill` and `::slider-thumb` pseudo-elements; the thumb of a slider keeps its own native
  appearance (`Painter.PaintNativePart`). The
  values of the sliders, the progress bars and the meters grow from the inline-start side (the right with `direction: rtl`).
  In a vertical writing mode (the sizes of the controls are logical), the contents of a control are painted as in a
  horizontal one, in its content box rotated a quarter turn (`PaintFormControl`: clockwise, counter-clockwise in
  `sideways-lr`): the tracks and the lines of text run along the inline axis from the inline-start side; the text is set sideways (the upright
  characters are not upright). The text controls and the controls whose parts are boxes are not rotated: their boxes are laid out in their writing mode.
- **Images.** `PaintImage` (`Rendering/PaintImage.cs`) is created by the image loading (`ImageRequest.Image`,
  `Document.CreatePaintImage`, the CSS images, the poster frames) with the natural size (with the EXIF orientation); the
  encoded bytes are kept only when the browsing context has a `RenderingBackend`, whose decoder opens them
  (`PaintImage.GetDecoder(backend)`, `RenderingBackend.CreateImageDecoder`) and decodes a frame on first use
  (`PaintImage.GetDecodedImage(backend)`, cached per variant). The variants of an image share its bytes and its decoder:
  `WithOrientation(fromImage)` (`image-orientation: none` paints the encoded pixels, except for the images that are not
  CORS-same-origin, `PaintImage.IsCorsSameOrigin`) and `GetFrame(index)`. `image-orientation` applies to the `img`
  elements, the SVG `image` elements, the generated content, the list markers and all the CSS images (backgrounds, border
  images, masks), as in the browsers. The painter draws the frame of an animated image (GIF, APNG, WebP) displayed now
  (`Painter.GetDisplayedFrame` → `PaintImage.GetDisplayedFrame(backend, out nextChange)`: the elapsed time since the image
  became available on the clock of the event loop, `PaintImage.AnimationClock`; the frames of 10 ms or less last 100 ms;
  the repetition count of the image), and `Painter.Paint(..., out nextFrameDelay)` gives the time of the next frame:
  `LayoutEngine.GetDisplayList` paints again after it (no rendering update is scheduled for the animations: a screenshot
  paints the current frame). `Painter.PaintCanvas` draws the bitmap of a canvas: the snapshot of the surface of its 2D
  context (`CanvasBitmap.GetSnapshot()`, cached until the bitmap changes: each drawing calls `Document.LayoutEngine.InvalidatePaint()`),
  the output bitmap of its `ImageBitmapRenderingContext` (`BitmapPixels.GetPaintImage(backend)`, over black without alpha
  channel), or the last frame committed by the `OffscreenCanvas` of a placeholder canvas (see [rendering.md](rendering.md#canvas)).
- Not done: the split inline band (`IsSplitInlineBand`) paints nothing (the browsers do not paint the inline backgrounds
  behind the block); the
  caret; the scrollbars; `element()`; the `BackgroundImage`
  inputs, the masks of the inline boxes split over several lines (applied per fragment), the hit testing of the masks,
  `SaveLayerItem.Bounds` (unset: the ink overflow does not include the shadows and the outlines); the
  `image-animation` property (the animations always play).

#### SVG painting

The SVG content is painted by `Painter.Svg.cs` (the entry `PaintSvgContent` paints the SVG document fragment of an outermost
`svg` in the coordinates of its content box), `Painter.SvgPaintServers.cs`, `Painter.SvgMarkers.cs` and `Painter.SvgText.cs`,
in document order (the [painter's model](https://svgwg.org/svg2-draft/render.html#PaintersModel)):

- **Elements** (`PaintSvgElement`, with a `SvgPaintContext`): the transform of the element (`SvgUserSpace.GetTransform`), the
  [conditional processing](https://svgwg.org/svg2-draft/struct.html#ConditionalProcessing) (`SvgConditionalProcessing`:
  `requiredExtensions` supports the HTML and MathML namespaces, `systemLanguage` matches the primary subtag `en`; `switch`
  paints its first matching child), the group opacity and the effects (`ApplySvgEffects`, the builder is transformed to the
  user space of the element: `_svgBuilderTransform` keeps the transform from the builder to the coordinates of the group,
  used by the non-scaling strokes, the context paints and the `foreignObject` stacking contexts), `display: contents` unboxes
  `g`/`svg`/`a`/`switch`/`use` (https://drafts.csswg.org/css-display/#unbox-svg). The shapes without geometry and their
  effects are not painted. The `outline` of an element is a rectangle around its object bounding box, in its user space
  (`PaintSvgOutline`, as in the browsers).
- **Viewports**: the nested `svg` elements and the `symbol` referenced by a `use` establish a viewport (`x`, `y`, the `width`
  and `height` attributes, which the attributes of the `use` element override; the CSS properties do not apply,
  w3c/svgwg#1059), their `viewBox` and `preserveAspectRatio`; a `viewBox` of zero size disables the rendering. The SVG user
  agent style sheet sets `overflow: hidden` on `svg:not(:root)`, `symbol`, `image`, `marker`, `pattern` and `foreignObject`:
  the viewport is clipped (per axis) when its overflow is not visible (`auto` clips the `svg` elements, w3c/svgwg#1072, not
  the markers); the outermost `svg` is clipped to its content box at paint time (a replaced box is not a scroll container;
  the root `svg` of a stand-alone SVG document is always clipped to its viewport, as in the browsers; a hidden outermost
  `svg` still paints its visible descendants),
  a `foreignObject` with `overflow: hidden` clips without becoming a scroll container (`ScrollContainers.GetUsedOverflow`).
  The root element of an SVG document is always clipped to its viewport, as in the browsers (the style sheet does not set
  its overflow). The `x`, `y`, `width` and `height` percentages of an `svg` or `symbol` element referenced by a `use`
  resolve against the viewport of the `use` element (`SvgPaintContext.UseViewportSize`), and the percentages of its content
  against the viewport of the instance (`SvgOutermostViewport.OverriddenViewportElement`). The `width` and `height`
  attributes of a nested `svg` are not presentation attributes (w3c/svgwg#1057).
- **Use elements** (`PaintSvgUse`): the referenced element is painted as the root of a [use-element shadow
  tree](https://svgwg.org/svg2-draft/struct.html#UseShadowTree) without cloning: the styles of the element instances are
  computed by `StyleEngine.GetUseInstanceStyle(element, parentStyle, instanceRoot)` (the rules of the document matched with the
  instance root as the root of the tree: `SelectorContext.UseInstanceRoot` stops the combinators, `:root` and the structural
  pseudo-classes there; the instance inherits from the `use` element; cached per style version). The referenced content
  is painted whatever the display of its ancestors (e.g. `defs`); the depth of the references is limited (`MaxSvgDepth`).
  The URL references of the `href` attributes (`SvgLinking`: `use`, the paint server templates, `textPath`, `mpath`,
  `filter`) are [processed as URLs](https://svgwg.org/svg2-draft/linking.html#processingURL): the leading and trailing white
  space is stripped, a URL that only differs from the URL of the document by its fragment is a same-document reference, and
  the fragment is percent-decoded. A `use` element that references another document (`SvgUseElement.UpdateExternalResource`)
  fetches it when it is connected (a same-origin request that delays the load event of the document; `load` and `error`
  events; a `data:` URL is not loaded, as in the browsers; `SvgResourceDocumentCache`: one fetch per resource and one
  document per URL for the `use` elements of a document) and parses an XML response into a resource document without
  browsing context (no script runs; `Document.SvgResourceHost` fetches the resources of its own `use` elements, to a limited
  depth): the referenced element (the root element for a URL without fragment) is painted from that document, with the
  style sheets of that document, where it matches `:target` (the URL of the resource document has the fragment).
- **Paints** (`GetSvgPaint`): colors, `context-fill`/`context-stroke` (the paints of the context element: the `use` element,
  or the shape of a marker, transformed from its user space; a context paint that is `currentcolor` is the color of the
  element that uses it, as in Chromium and WebKit), and the paint servers (`TryCreatePaintServer`) with their
  fallback (an invalid reference, a non-invertible transform, a pattern without tile): the linear and radial gradients
  (`gradientUnits`, `gradientTransform` = the `transform` property of the gradient, `spreadMethod`, the focal circle
  `fx`/`fy`/`fr`, the stops clamped and increasing, interpolated unpremultiplied, `color-interpolation: linearRGB` expanded
  into stops, a single stop or a vector of zero length paint a color) and the patterns (`patternUnits`, `patternContentUnits`,
  `viewBox`/`preserveAspectRatio`, `patternTransform`: the tile is a `DisplayList` painted with the content of the template
  that has children, repeated by a `TiledShader` of a `DisplayListShader`), the attributes inherited through `href`
  (`GetTemplateChain`). The object bounding box comes from `SvgBoundingBox.ComputeContent` (also for the content of the
  resources and of the `use` elements, which are not rendered).
- **Shapes** (`PaintSvgShape`): the fill (`fill-rule`, `fill-opacity`), the stroke (`PaintSvgStroke`: width, caps, joins,
  `stroke-miterlimit`, the dashes (lengths, percentages and math functions) scaled by the `path-length` property, whose
  presentation attribute is `pathLength` (`SvgStrokeStyle.GetPathLength`), `vector-effect: non-scaling-stroke` stroked in the
  coordinates of the group with the width multiplied by the zoom, and in an SVG image by the zoom of the element that draws
  the image, `SvgImage.Zoom`) and the markers in the order of `paint-order` (its computed value keeps the
  specified order, serialized in its shortest form); `shape-rendering: crispEdges`/`optimizeSpeed` turns off the antialiasing.
- **Markers** (`PaintSvgMarkers`): the vertices of the path from its commands (`GetMarkerVertices`: one per segment, the
  arcs kept; a closepath of zero length after a segment closes the subpath without a vertex, after a moveto it is an implicit
  subpath), the angle of the bisector (the incoming direction turned by -90 degrees for opposite directions), `orient`
  (`auto`, `auto-start-reverse`, angles), `markerUnits` (with a non-scaling stroke, the stroke width is divided by the scale
  of the transform to the host coordinates, as in the browsers), `markerWidth`/`markerHeight`, `viewBox`, `refX`/`refY` (also the
  keywords), the clip of the viewport when the content overflows it; the content is painted with the marker as context
  element. The `marker` shorthand sets the three properties.
- **Texts** (`PaintSvgText`): the glyphs of the characters of `SvgTextLayout` (`SvgTextCharacter.Shaped`/`GlyphStart`/
  `GlyphCount`/`IsUpright`/`BaselineOffset`), in runs of the characters of an element with the same axes and font
  (`BuildSvgGlyphRuns`: a frame whose x axis is the direction of the line for the sideways glyphs, the physical x axis of the upright
  glyphs; the glyphs of a character of a right-to-left run are drawn from its end position, against its progression; the axis of the progression is scaled for the glyphs stretched by
  `lengthAdjust="spacingAndGlyphs"`), the backgrounds of the highlights, the `text-shadow`s (of the fill and of the stroke), then for the fill and the stroke in the order of
  `paint-order`: the underlines and overlines, the glyphs, the line-throughs (`PaintSvgTextDecorations`, from the font metrics
  of the decorating element, its paints); the opacity of the `tspan`, `textPath` and `a` elements is a group per run of
  glyphs, or multiplies the opacity of the group of the text when it is the same for all the characters
  (`GetUniformSvgTextOpacity`). The texts of the `clipPath` elements clip to their glyphs (`PaintSvgTextGlyphs`).
  The characters in the selection, in the target text or in a custom highlight (`GetSvgTextHighlights`: the layers in the
  painting order of the [highlight pseudo-elements](https://drafts.csswg.org/css-pseudo-4/#highlight-pseudos); a run of
  glyphs has one state of highlights) are painted with the paints of the highlights: the background colors on their glyph
  cells (the consecutive cells of a line are one quadrilateral; the default `Highlight` and `Mark` colors when neither
  `color` nor `background-color` is specified), under the shadows; `fill`, `stroke` and `stroke-width` of the topmost
  highlight pseudo-element that specifies them (they apply to the highlight pseudo-elements and inherit through them),
  else the ones of the element, as in Chromium; the `color` of a highlight is the fill of its text when it does not specify
  `fill` (as in Gecko; WPT `css/css-pseudo/svg-text-selection-002.html`); the `text-shadow`s of the highlights are painted
  under the ones of the text. The text decorations of the highlights of the SVG texts are not painted.
- **Images** (`PaintSvgImage`): the `image` element (`SvgImageElement.Image`, a `PaintImage` loaded with the `load` event;
  `x`, `y`, `width`, `height`, auto from the natural size), a raster image fitted by its `preserveAspectRatio`, an SVG image
  laid out in the viewport with the `preserveAspectRatio` of the element replacing the one of its root, clipped.
- **SVG images** (`SvgImage`): an `img`, a CSS image (backgrounds, border images, masks, list markers, generated content)
  or an SVG `image` whose resource is `image/svg+xml` is parsed once (cached on the `PaintImage`) into an isolated `Document`
  without browsing context (`Document.SvgImage`: no script, no fetch except the `data:` URLs of its images, loaded
  synchronously; the rendering backend of the host document), rendered (`ComputedStyleDeclarations.IsRendered`) with an
  environment of the size it is drawn at (the concrete object size; `StyleEngine.CreateEnvironment`), the
  `prefers-color-scheme` of the used color scheme of the element that uses it, and a user agent rule that makes its root `svg`
  fill the viewport. An image with a natural width and height but no `viewBox` gets a synthesized `viewBox` (it is scaled, as
  in the browsers); the near-integral natural sizes are rounded (crbug.com/1392140). The fragment of the URL of the image
  selects a [view](https://svgwg.org/svg2-draft/linking.html#SVGFragmentIdentifiers) (`SvgViewSpec`, also for the SVG
  documents of the navigables): `svgView(viewBox(...);preserveAspectRatio(...);transform(...))`, optionally followed by a
  time segment, or the id of a `view` element, which override the `viewBox` and the `preserveAspectRatio` of the root element
  and transform its content; or a [spatial media fragment](https://www.w3.org/TR/media-frags/#naming-space)
  (`xywh=x,y,w,h`, `xywh=pixel:...`, `xywh=percent:...`, among `&`-separated dimensions: the last valid one wins and the
  temporal dimension `t=` is ignored), whose region is the `viewBox` ("the viewBox is overridden by the x, y, width and
  height values provided by the fragment identifier"): integers, in user units or in percentages of the resolved size of
  the image (its `width` and `height`; a missing dimension comes from the ratio of the `viewBox`, else it is 300 or 150
  pixels), clipped to the image; a region without area, or a percentage past 100, is ignored (only Gecko implements them). `GetDisplayList(size, colorScheme, preserveAspectRatio, zoom)` caches the last display lists.
  The image of a replaced element is laid out at the size of its rectangle before the snapping to the device pixels and
  scaled to the snapped rectangle (an image whose size has the ratio of its `viewBox` is not letterboxed). `PaintedImage.SvgPaintedImage` draws it (clipped, `AddList`) or makes
  a `DisplayListShader` (a picture shader of the backend).
- **Filters**: `feImage` (`ImagePrimitive`): an element of the document painted as by a `use` element in the user space of
  the filtered element, at the origin of the primitive subregion (`RenderSvgElementForFilter`, a limited nesting), or an
  image drawn in the subregion with its `preserveAspectRatio`: a `data:` image, or the image fetched by the element
  (`SvgFEImageElement.Image`, fetched as the image of an SVG `image` element: it delays the load event and the filter is
  transparent until it is loaded).
- **foreignObject**: its stacking context is an isolated group when a descendant has a blend mode (`HasBlendingDescendant`).
  A `mask` element that has `display: none`, or an ancestor that has, is not applied (w3c/fxtf-drafts#245, `IsDisabledMaskElement`).
- **References to other documents** (`SvgResources.GetReferencedElement`): a `url()` of `fill`, `stroke`, `marker-start`,
  `marker-mid`, `marker-end`, `filter`, `backdrop-filter`, `clip-path`, `mask-image` or `offset-path` (a property or a
  presentation attribute, of an SVG element or of a CSS box) whose URL is not a URL of the document references an element of
  another document ([processing of URL references](https://svgwg.org/svg2-draft/linking.html#processingURL)). The document
  is fetched "at the time the property is required for rendering an element": at the rendering opportunity that follows a
  change of the tree or of the styles, for the rendered elements (`CssImageFetching.FetchResourceDocuments`, as the CSS
  images: the elements of a `display: none` subtree fetch nothing), once per URL without fragment and per request URL
  modifiers (`SvgResourceDocumentCache.LoadForProperty`). The request is a CORS request ("for a reference from a style
  property or presentation attribute, the anonymous state"; `RequestDestination.Image`, initiator type `css`), with the
  `cross-origin()`, `integrity()` and `referrer-policy()` modifiers of the URL; a `data:` URL is loaded. The fetches started
  before the load event delay it. An XML response is parsed into a resource document without browsing context (no script
  runs, and its own property references to other documents are not fetched: the secure static mode), and the element is
  painted from it, with the styles of that document; while the document is loading, and when it cannot be fetched (a network
  error, the CORS check, the integrity metadata) or has no element with this id, the reference is invalid (the fallback of
  a paint, no marker, no filter, no clipping, no mask). A `mask-image` with a fragment is fetched once, as an image: it is
  the mask element that its fragment identifies in the document, else the image.
- Not done: an HTML document as a resource document, the images of a resource document, the references to other documents of
  the elements that are not rendered (e.g. in `defs`) and of the `href` attributes other than the one of `use` (the paint
  server templates, `textPath`, `mpath`, `feImage` to an element), the content areas of `shape-inside` of
  the SVG texts (also the preserved line breaks of the texts that are not auto-wrapped), the cloned
  `foreignObject` elements of the `use` shadow trees, the user action pseudo-classes and the event listeners of the element
  instances of the `use` shadow trees (the instances are not elements), `::first-letter` in the SVG texts, the CSS linked
  parameters. The attributes are painted with the animated values of the SMIL animations (see
  [animations.md](animations.md#svg-animations-smil)).

### Resolved values (`Css/Layout/LayoutResolvedValues.cs`)

`ComputedStyle.GetResolvedValue` returns the [resolved values](https://drafts.csswg.org/cssom/#resolved-values) of
`getComputedStyle()`; the ones that are used values come from the fragment tree (`LayoutResolvedValues`, see
[computed-style.md](computed-style.md#layout-dependent-values) for the behavior):

- Each accessor (`GetSize`, `GetMargin`, `GetPadding`, `GetInset`, `GetTransformReferenceBox`, `GetGridTrackList`) returns a
  `LayoutResolution`: `UsedValue` (from the first fragment of the principal box of the element or pseudo-element, a
  forced layout), `ComputedValue` (no box, or the property does not apply: e.g. `width` of a non-replaced inline box, the
  margins of an internal table box), or `NoLayout` (no browsing context or not connected: `ComputedStyle` falls back to the
  sizes known without layout, `Css/Cascade/BoxSizes.cs`). Only these properties compute the layout; the margins and paddings
  in px do not (their used value is their computed value). The values are divided by the effective zoom of the element.
- `width`/`height`: the border box, minus the borders and the paddings for `box-sizing: content-box` (the scrollbar
  gutters stay in the content box, so the value round-trips); a table uses the fragment of its table grid box (and its used
  borders and paddings: the half of the collapsed borders in the collapsing border model).
- The margins: the margins of an in-flow block-level box in the inline axis of its block container are computed from its
  position in the content box of the parent fragment (`auto`, including the floats beside it) or from the computed value
  resolved against the inline size of that content box (the layout stores the inline-end margin of an over-constrained box
  as the space left, which the browsers do not return). The other boxes use `BoxFragment.Margin` (`UntrimmedMargin` for
  the margins trimmed by `margin-trim`, whose resolved values are not changed); the inline boxes resolve
  their computed margins and paddings against the inline size of the nearest block container fragment (they only store the
  inline-axis edges on their first/last fragments).
- The reference box of the transforms is `LayoutTransforms.GetReferenceBox(fragment)` (`transform-box`: the content box for
  `content-box`/`fill-box`, else the border box; also used by the transformation matrices); an SVG element without CSS
  box uses `SvgBoundingBox.GetTransformReferenceBox` (bounding boxes, or the nearest SVG viewport, the outermost one being
  the content box of the `svg` fragment).
- Grid track lists: the grid layout calls `LayoutContext.OnGridLaidOut(box, columns, rows)` with the used track sizes
  (`Grid/GridTrackSizes`: the sizes in zoomed px of all the tracks of the columns and of the rows, plus the explicitly-assigned
  names of each line; `GridTrackSizes.ForSubgrid` for a subgridded axis, serialized as `subgrid` and the names of its lines);
  `LayoutEngine.TryGetGridTrackSizes` (cleared at each pass, it does not compute the layout: the box comes from the fragments
  of the current layout) feeds the resolved value. A grid container that is not laid out returns the computed value.
- The SVG elements `rect`, `image` and `foreignObject` (which have no box, or whose box is laid out by the SVG layout) return
  their used `width`/`height` in user units (`auto` is 0, or the default sizing of the natural size of an image); a nested
  `svg` returns its computed value, as in the browsers.
- A box split by the
  fragmentation uses the fragment of the box before the fragmentation (`BoxFragment.UnfragmentedFragment`: its size and edges).

## Fonts

Text is measured with fonts read by the managed font reader of `Meziantou.Framework.Fonts`, which
`Meziantou.Framework.Imaging` also uses (`Css/Fonts`, namespace `Meziantou.Framework.WebPlatform.Css.Fonts`, adapts it to
CSS): there is no native dependency (no FreeType, no HarfBuzz), so the library stays AOT-compatible and the results are
the same on every machine. The fonts installed on the machine are not used unless the host sets
`BrowsingContextOptions.UseSystemFonts` (see [System fonts](#system-fonts)).

### Font files

`FontFile.Load` decodes a font file with the font reader and returns null when it is invalid (the font is then not
loaded, like in the browsers that sanitize the web fonts):

- OpenType and TrueType fonts (`0x00010000`, `OTTO`, `true`) and font collections (`ttcf`, one `OpenTypeFont` per face);
- [WOFF 1.0](https://www.w3.org/TR/WOFF/) (the tables are decompressed with zlib) and
  [WOFF 2.0](https://www.w3.org/TR/WOFF2/) (Brotli, with the reconstruction of the transformed `glyf`, `loca` and `hmtx`
  tables, and the collections). The files that do not meet the requirements of the formats are rejected (lengths,
  offsets, overlapping tables, transforms, decompressed sizes; for WOFF 2.0, the data between or after the compressed
  data, the metadata and the private data blocks, which follow each other at 4-byte boundaries, and the `glyf` and `loca`
  tables of a font of a collection that are not next to each other in the table directory; the reserved field of the
  header is ignored, as in the browsers). The decoded data of a WOFF or WOFF2 file is limited to
  128 MB, and the buffers grow with the decompressed data, so a small file cannot allocate the size it declares.

`FontFile.Data` is the sfnt data after decoding (an OpenType font or a collection) and `OpenTypeFont.FaceIndex` the
index of a face in it: a rendering backend creates its typefaces from them and draws the glyph ids positioned by the
layout. The outlines are decoded only for the bounding boxes of the glyphs that do not declare them (`CFF`, `CFF2`).

A face is created only when the font reader validates it (the table directory, `head`, `maxp`, `hhea`, `hmtx`, the
structure of `cmap`, and the headers of the other tables it understands; a malformed `COLR` table rejects the font) and
it has outlines (`glyf` and `loca`, `CFF`, `CFF2`, `CBDT`, `sbix` or `SVG`). A malformed glyph, or a malformed table
read for a glyph, gives no value: `OpenTypeFont` catches the exceptions of the font reader. The layout tables (`GSUB`,
`GPOS`, `GDEF`, `kern`) are read by the shaping engine with bounds-checked reads (`FontBinary`): a malformed table reads
zeros, it never throws. The `font` fuzzing target (see
[fuzzing.md](fuzzing.md)) loads any input and reads every kind of data.

### Tables

`OpenTypeFont` gives, in font units (divide by `UnitsPerEm` and multiply by the font size):

| Table | Data |
|---|---|
| `head`, `maxp` | units per em, glyph count, `macStyle`, format of `loca` |
| `hhea`, `hmtx` | ascender, descender, line gap; advance widths and left side bearings |
| `vhea`, `vmtx` | vertical ascender, descender, line gap; advance heights and top side bearings (without them, the advance height is the ascent plus the descent) |
| `OS/2` | weight and width classes, `fsSelection` (italic, bold, oblique, `USE_TYPO_METRICS`), typographic and Windows metrics, x-height, cap height, strikeout, subscript and superscript offsets |
| `post` | italic angle, underline position and thickness, fixed pitch |
| `name` | family, subfamily, full and PostScript names (the typographic names first; the English Windows names, then the Unicode and the Macintosh Roman names) |
| `cmap` | formats 0, 4, 6, 10, 12 and 13 (the full repertoire subtables first; the symbol fonts also map U+0020-00FF to U+F020-F0FF), and format 14 (variation sequences: `TryGetGlyphId(codePoint, variationSelector)`) |
| `kern` | the horizontal format 0 subtables of the Microsoft and Apple versions (applied by the shaper when `GPOS` has no `kern` feature) |
| `GSUB`, `GPOS` | read by the shaper of `Meziantou.Framework.Fonts` (see [Text shaping](#text-shaping)): the script and language system lists, the features, the feature variations and the lookups, GSUB lookup types 1-8, GPOS lookup types 1-9 |
| `GDEF` | read by the shaper: the glyph classes, the mark attachment classes, the mark glyph sets, the ligature caret values (formats 1 and 3, `FontFace.GetLigatureCarets`) and the item variation store (the deltas of the device tables) |
| `glyf`, `loca` | the bounding boxes the glyph headers declare |
| `CFF `, `CFF2` | the bounding boxes of the decoded outlines (cached per glyph) |
| `MATH` | `OpenTypeFont.MathTable` (`OpenTypeMath`): the MathConstants (`GetConstant(MathConstant)`, the percent scale downs, `DisplayOperatorMinHeight`, `RadicalDegreeBottomRaisePercent`), the italics corrections and top accent attachments of the glyphs, and the MathVariants (`GetConstruction(glyph, vertical)`: the size variants and the glyph assembly, `MinConnectorOverlap`); the variation index device tables vary the values of an instance, the other device tables are ignored |
| `fvar`, `avar`, `HVAR`, `MVAR`, `gvar` | variable fonts: see below |

`FontMetrics` (`OpenTypeFont.Metrics`, `GetMetrics(instance)`) selects the ascent, the descent and the line gap like the
browsers ([CSS Inline](https://drafts.csswg.org/css-inline-3/#ascent-descent)): the typographic metrics of `OS/2` when
`USE_TYPO_METRICS` is set, else the metrics of `hhea` (when they are 0: the typographic metrics, then the Windows
metrics). The x-height and the cap height come from `OS/2` (version 2), else from the top of the bounding box of `x`
and `H`; they are null when they are unknown (the CSS fallback, 0.5em for `ex`, is applied by the caller).

`GetKerning(first, second)` and `GetPairAdjustment(first, second, feature)` apply the lookups of a `GPOS` feature of the
default language system of a script to a pair of glyphs (else the `kern` table for `kern`), and
`GetSingleSubstitution(glyph, feature)` the lookups of a `GSUB` feature to a glyph (the `rtlm` mirrored forms of the math
layout): they run the lookups of the shaping engine on a buffer of one or two glyphs.

### Variable fonts

`OpenTypeFont.Variations` exposes the axes and the named instances of `fvar`. `CreateInstance(settings)` creates an
instance from the values of its axes in user coordinates (e.g. `wght` 650; the missing axes have their default value): it
is the face of the font reader at these values, which clamps, normalizes and maps them with `avar` (versions 1 and 2).
The instance is passed to `GetAdvanceWidth` (deltas of `HVAR`, else the phantom points of `gvar`; an advance is a whole
number of font units, as in HarfBuzz and FreeType), `GetMetrics` (deltas of `MVAR`: ascender, descender and line gap,
x-height, cap height, underline, strikeout, subscript and superscript offsets) and `GetKerning` (deltas of the `GDEF`
item variation store, rounded to font units).

### Color fonts

`OpenTypeFont.ColorGlyphs` (`OpenTypeColorGlyphs`) reads the color glyphs of the
[`COLR`](https://learn.microsoft.com/typography/opentype/spec/colr) table, version 0 (the base glyph records, also when
they are in a table of version 1), and the palettes of the
[`CPAL`](https://learn.microsoft.com/typography/opentype/spec/cpal) table (version 0 and 1, with the types of the palettes
that mark them as usable with a light or a dark background). A color glyph is a list of layers, from the bottom one to the
top one: each layer is the outline of another glyph of the font and an entry of the palette, or the color of the text
(the entry `0xFFFF`); a layer whose entry is not in the palette is ignored.

`FontPalette.Resolve(style, font)` gives the colors of the palette of a font for an element
([`font-palette`](https://drafts.csswg.org/css-fonts-4/#font-palette-prop)):

- `normal` is the palette at index 0; when `color-scheme` is not `normal`, it is the first palette of the font marked
  for the used color scheme of the element (light or dark), if any, as the specification recommends.
- `light` and `dark` are the first palette marked as usable with a light or a dark background, and `normal` when the font
  has none.
- A `<palette-identifier>` is the palette of the
  [`@font-palette-values`](https://drafts.csswg.org/css-fonts-4/#font-palette-values) rule of that name (case-sensitive)
  whose `font-family` descriptor has the family of the font (compared as the family names are): the family of the
  `@font-face` rule of a web font, or a family name of an installed font. The rules are the ones of the style sheets of the
  document that apply (the names are not scoped to the shadow trees); the last rule of the strongest cascade layer wins
  as a whole (its descriptors are not merged with the ones of the other rules). The palette is the one of
  `base-palette` (`light`, `dark` or an index; the palette at index 0 without descriptor or when the palette does not
  exist), with the colors of `override-colors` (an entry that is not in the palette is ignored, the last color of an entry
  wins; the relative units of the math functions are the ones of the element). Without such a rule, the value is `normal`.
- `palette-mix()` mixes the colors of each entry of its palettes as
  [`color-mix()`](https://drafts.csswg.org/css-color-5/#color-mix) does, with its color interpolation method (Oklab by
  default) and its percentages.

The palette is applied by the painting ([rendering.md](rendering.md#color-glyphs)): the layout does not depend on it (a color
glyph has the advance and the metrics of its base glyph), so a change of `font-palette` or of the `@font-palette-values`
rules only repaints. `font-palette` is animated by computed value: the value between two palettes is their
`palette-mix()` in Oklab (see [computed-style.md](computed-style.md)).

Not implemented: the paint graphs of `COLR` version 1 (gradients, transforms, compositing: a glyph that only has a paint
graph is not a color glyph), the bitmap and SVG color glyphs (`sbix`, `CBDT`, `SVG `), and `FontFace.palettes`.

### Bundled fonts

`BundledFonts` embeds the fonts of `src/Meziantou.Framework.WebPlatform/Css/Fonts/Bundled` (see the README of that
folder for their sources and licenses), loaded on first use and shared by all the documents (they are immutable):

- **Ahem**, the test font of the web-platform-tests (public domain): 1em square glyphs, ascent 0.8em and descent 0.2em,
  which make the text metrics of the layout tests exact;
- **Tinos** (`serif`, metric-compatible with Times New Roman), **Arimo** (`sans-serif`, Arial; a variable font of
  weight 400 to 700) and **Cousine** (`monospace`, Courier New), the Chrome OS core fonts (SIL Open Font License 1.1),
  in regular, bold, italic and bold italic, subsetted to Latin, Greek and Cyrillic in WOFF2 (about 370 KB);
- **STIX Two Math** (`math`, SIL Open Font License 1.1), the font of the MathML elements (the UA style sheet sets
  `font-family: math` on `math`): its OpenType MATH table gives the layout constants, the italic corrections and the
  stretchy glyphs; subsetted to the mathematical characters (operators, arrows, the mathematical alphanumeric symbols...)
  with its `MATH` table and the `ssty`, `rtlm`, `dtls` and `flac` features (about 260 KB). It is also the last font of the
  installed font fallback (for the mathematical symbols and the combining marks that the other bundled fonts do not have).

`BundledFonts.GetGenericFamily` maps the generic families to the bundled families (`ui-serif`, `system-ui`,
`ui-sans-serif`, `ui-rounded` and `ui-monospace` included) and `math` to STIX Two Math; `cursive`, `fantasy`, `emoji` and
`fangsong` have no bundled font. `eng/webplatform/update-fonts.sh` downloads the fonts from their pinned commits and produces the subsets with
fontTools.

A host can install more fonts for the documents of a browsing context (`InstalledFontCollection`,
`BrowsingContextOptions.InstalledFonts`; internal for now: it is used by the runner of the web-platform-tests, see
[Fonts](web-platform-tests.md#fonts)): installed families, which `font-family` and `local()` can name by any of their
family names (the localized names of the `name` table included; a bundled family hides an installed family of the same
name, and the first face installed with a style is the one of its family), and fallback fonts, each one limited to a list
of Unicode ranges, which render the characters of their ranges that no other font supports. The bundled fonts are always
searched first: the installed fonts change neither the default font nor the fonts of the characters that the bundled
fonts support.

### System fonts

`BrowsingContextOptions.UseSystemFonts` (false by default) lets `font-family` and `local()` name the fonts installed on the
machine. The index of these fonts is `SystemFontCollection.Default` of `Meziantou.Framework.Fonts`: the font directories of
the operating system (Windows: the fonts directory, the fonts of the user and the files the registry lists; macOS: the
`Library/Fonts` directories and the downloaded fonts; Linux: the directories of the configuration of fontconfig and the XDG
font directories), scanned once per process by reading only the table directories and the `head`, `OS/2`, `post`, `name`
and `fvar` tables. `InstalledFontCollection.System` wraps it, and is used when the options have no `InstalledFonts`:

- a face belongs to the families of its typographic family names and of its family names, in all their languages (the
  legacy names such as `Arial Black` name a face, as in the browsers);
- the font matching uses capabilities computed from the index (`FontSelectionCapabilities.FromSystemFont`, the same values
  as `FromFont` once the font is loaded); only the selected face is loaded, from its file, and the faces of a file share
  the loaded file. A face whose file can no longer be loaded (removed or changed since the scan) makes its family missing;
- the bundled fonts keep their names and the generic families (`serif`, `sans-serif`, `monospace`, `math`): only the
  families the bundled fonts do not have come from the machine, and the default font does not change. The fonts of the
  machine add no installed font fallback.

### CSS Font Loading

A `FontFace` is loaded when one of its sources is a valid font file: its `FontFile` (internal) is the decoded file, for the
font matching. The font matching of the layout starts the loads of the font faces it needs (`FontSelector.RequestLoad`), and
the font face set of a rendered document updates the layout before it is switched to loaded
(`FontFaceSet.UpdateLayoutForFonts`), so `document.fonts.ready` waits for them. A source that starts with a font signature but whose structure is invalid is skipped, like a
network error. A `local()` source is a bundled font, else a font installed by the host, whose full name or PostScript name
matches (case-insensitive), else a font installed on the machine when `UseSystemFonts` is set. A `local()` source that names
a face of a font collection uses that face.

### Font matching

The [font matching algorithm](https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm) selects the fonts of an
element from its computed font properties:

- `FontDescription` is the key of the matching: the computed `font-family`, `font-weight`, `font-style`, `font-width`,
  `font-size-adjust`, `font-synthesis-weight`/`-style`, `font-variation-settings`, `font-optical-sizing`, and the shadow
  root whose `@font-face` rules the family names reference (see below). The font size is not part of it.
- `FontSelector` (one per `Document`, `Document.FontSelector`) creates and caches a `FontFallbackList` per description.
  The lists are discarded when the font faces or the environment change (`FontSelector.Version` is
  incremented when a font face is loaded or fails to load, is added to or removed from the font face set, or when the
  descriptors of a font face created by a script change; `StyleEngine` recomputes the styles when it changes). When the DOM
  or a style sheet of the document changes (`Document.StyleSheetsVersion`), the
  font faces of the trees are collected again and the lists are kept when they are the same faces with the same descriptors
  and fonts (`HasCurrentFaces`, `WebFontFace.IsCurrent`): the styles keep sharing the lists, which the inline layout compares
  by reference to shape the texts of sibling inline boxes together, and the lists keep their caches of shaped texts.
- For each family of the list: a generic family is mapped to a bundled family (`BundledFonts.GetGenericFamily`; the
  generic families without bundled font are skipped), followed by the fallback fonts installed by the host (a generic
  family is a composite face that covers the scripts of the system: the characters that the bundled family does not
  support are rendered by these fonts before the next families of the list); a family name is first searched in the font faces (`WebFontFace`:
  the faces of the font face set whose `@font-face` rule applies, ordered by cascade layer, then the faces added by
  scripts), then in the bundled fonts, then in the families installed by the host: they are the installed fonts. A family defined by font faces hides the installed
  family of the same name; its faces that failed to load are not present, and a family without faces is missing.
- `FontMatcher.Select` narrows the faces of a family (`FontSelectionCapabilities`: ranges of weights, widths, italic
  values and oblique angles, from the descriptors of the font face, or from `OS/2` and the `wght`, `wdth`, `ital` and
  `slnt` axes of the font for a bundled font and for the `auto` descriptors of a loaded face) by `font-width`, then
  `font-style`, then `font-weight`, with the search orders of the specification and the resolutions of the CSS working
  group that its tests expect: an oblique style does not fall back to the italic faces
  ([csswg-drafts#9389](https://github.com/w3c/csswg-drafts/issues/9389): the upright face is used, and sheared when
  `font-synthesis-style` allows it; an italic face is only used when the family has no other face), and
  `font-synthesis-style: oblique-only` prevents the use of the oblique faces as a fallback for italic
  ([csswg-drafts#9390](https://github.com/w3c/csswg-drafts/issues/9390): they are only used when the family has no upright
  face). The oblique angles that replace italic are searched from the default angle (14deg: the angles above it in
  ascending order, then the ones below it) as in Chrome and Safari, which match italic as `oblique 14deg`, where the
  specification starts at 11deg. A range supports all its values: the
  selected values are applied to the variation axes. The web faces with the same descriptors are a composite face, whose
  faces are searched in the reverse order of their definition, with their `unicode-range` (`UnicodeRangeSet`).
- `MatchedFont` is a selected face: the `OpenTypeFont`, the variation instance (the selected `wght`, `wdth`, `slnt`/`ital`,
  then `opsz` from the used font size when `font-optical-sizing` is auto, then the `font-variation-settings` of the font
  face and of the element), the synthesized bold and oblique (when the desired weight is 600 or more and the selected one
  is not, or when the desired style is italic or oblique and the selected face is upright, as allowed by
  `font-synthesis`; as in Chrome and Safari, not for a face that supports a range of weights, respectively of oblique
  angles, such as a variable font whose `@font-face` descriptor is `auto` or a range: nothing is synthesized outside of
  the range of its designer, [csswg-drafts#7999](https://github.com/w3c/csswg-drafts/issues/7999); only flags for the
  renderer: the advances and the metrics are not changed, as in the browsers), and the
  `size-adjust`, `ascent-override`, `descent-override` and `line-gap-override` descriptors. `GetEmSize(fontSize)` is the
  used font size (`size-adjust`, or the size that makes the aspect value of the font equal to `font-size-adjust`),
  `GetMetrics(fontSize)` the `UsedFontMetrics` in CSS px (`GetMetricsForEmSize` for an em size that is already adjusted,
  such as the one of a shaped font run), `GetGlyph`, `HasGlyph` and `GetAdvance` the glyphs and their advances.
- `FontFallbackList.PrimaryFont` is the [first available font](https://drafts.csswg.org/css-fonts-4/#first-available-font)
  (the first font whose `unicode-range` includes U+0020, else the default font), and `GetFontForCharacter(codePoint)`
  the per-character fallback: the first family whose matched face supports the character, then the installed font
  fallback (`BundledFonts.FallbackFamilies`: Tinos, Arimo, Cousine, STIX Two Math; Ahem only when it is named; then the
  fallback fonts installed by the host, for the characters of their ranges), else the primary font.
  The results are cached per code point (in an array for the Latin-1 characters).
- A lookup that needs a web font whose `unicode-range` includes the character starts its load (`FontFace.Load()`, in a
  document with a browsing context). While it loads, the bundled family of the same name, if any, is the substituted
  font (the web-platform-tests load Ahem with `/fonts/ahem.css`, so the metrics are the same before and after the load),
  else the next families are used; the styles are computed again once it is loaded.
- `@font-face` rules of a shadow root apply to the `font-family` declarations of its tree
  ([tree-scoped names](https://drafts.csswg.org/css-scoping-1/#shadow-names)): the tree scope of the declaration of
  `font-family` (an inherited value keeps it) selects the innermost shadow root of its chain that has `@font-face`
  rules, and the family names are searched in its rules, then in the outer trees up to the document. The font faces of
  the rules of the shadow roots are created and kept by the `FontSelector` (they are not in `document.fonts`).

`ComputedStyle.Fonts` is the list of an element, `PrimaryFontMetrics` the metrics of its first available font at its font
size, and `GetFontRelativeLength` the length of `ex`, `cap`, `ch` and `ic`. The font-relative units of the font
properties use the fonts of the parent; the `r*` units the fonts of the root element; the media queries and the `sizes`
attribute the ratios of the default font (`BundledFonts.DefaultRatios`). `font-size-adjust: from-font` computes to the
aspect value of the first available font (matched without `font-size-adjust`).

### Text shaping

`TextShaper` (`Css/Fonts`) converts a text run (characters of one style and one bidi level) to positioned glyphs with the
OpenType Layout tables (`GSUB`, `GPOS`, `GDEF`). Each run is shaped by the shaper of `Meziantou.Framework.Fonts`
(`Meziantou.Framework.Fonts.TextShaper`), shared with `Meziantou.Framework.Imaging` and written from the specifications (HarfBuzz
is its test oracle): no native code, and the shaping models of the complex scripts (Arabic, Hebrew, Indic, Khmer, Myanmar,
Thai and Lao, Hangul, and the Universal Shaping Engine). The Myanmar Zawgyi encoding and the Arabic fallback shaping of the
Windows-1256 fonts are not supported.

- `TextShapingOptions` is the style of a run: the computed font size, the direction (the bidi level of the run, given by
  the inline layout), the orientation (`TextShapingOrientation`: horizontal, or vertical with `text-orientation: mixed`,
  `upright` or `sideways`; the `sideways-*` writing modes are sideways), `letter-spacing` and `word-spacing` in px (`normal`
  is 0, the percentages refer to the font size), whether the kerning applies, the OpenType features (`Features`), the
  content language (`Language`: the `lang` of the element, for `locl` and the language system), `font-language-override`
  (`LanguageOverride`) and the characters around the text (`Context`: the joining of the Arabic letters across the inline
  boxes). `TextShapingOptions.FromStyle` reads them from the computed style (`ComputedStyle.ShapingOptions`, cached on the
  style). A style that declares none of the properties they depend on (nor `font-family`, which changes an inherited font
  size, and is not animated), whose element has no `lang` attribute and the element of its parent style as parent (a slotted
  element inherits its style from its slot, its language from its parent), shares the options of its parent; `ComputedStyle.Fonts`
  shares the fonts of the parent in the same way when no property of the `FontDescription` is declared.
- The features follow the [feature precedence](https://drafts.csswg.org/css-fonts-4/#feature-precedence): the
  `font-feature-settings` descriptor of the `@font-face` rule (`MatchedFont.FaceFeatures`), then the features of
  `font-kerning`, `font-variant-ligatures`, `font-variant-caps`, `font-variant-numeric`, `font-variant-east-asian`,
  `font-variant-position`, `font-variant-alternates` (`FontFeatureValues`: the names of the `@font-feature-values` rules of
  the document that apply for the first family that defines them; the definition of the strongest
  [cascade layer](https://drafts.csswg.org/css-cascade-5/#layering) wins, then the last one) and then `font-feature-settings`. `font-kerning: auto` applies the
  kerning except with `text-rendering: optimizeSpeed`, which also disables `liga` and `clig` (like Chrome); a non-zero
  `letter-spacing` and `text-justify: inter-character` disable the optional ligatures (`liga`, `clig`, `dlig`, `hlig`,
  `calt`), as [css-text](https://drafts.csswg.org/css-text-3/#letter-spacing-property) recommends. As Chrome, the CJK runs
  (Han, kana, Bopomofo) are kerned only with `font-kerning: normal`, with the proportional alternates (`palt`, `vpal`) whose
  kerning is designed for them, and the vertical runs get `vkrn` with `font-kerning: normal`.
- The clusters (the typographic character units) are a base character followed by its combining marks (General_Category M,
  which includes the variation selectors), the joiners, the emoji modifiers and the tag characters. The font of a cluster is
  the first font that supports its variation sequence (`FontFallbackList.FindFontForVariationSequence`: the base character
  and the variation selector that follows it, with format 14 of `cmap`, as Chrome), else
  `FontFallbackList.FindFontForCharacter` of its base character (the per-character fallback with the `unicode-range` of the
  font faces). [`font-variant-emoji`](https://drafts.csswg.org/css-fonts-4/#font-variant-emoji-prop) (`TextShapingOptions.Emoji`)
  appends a presentation selector to the emoji characters (the Emoji property, as Chrome) that have none: VS15 for `text`,
  VS16 for `emoji`, the one of the Emoji_Presentation property for `unicode` (generated from emoji-data.txt), none for
  `normal`; so a font with the emoji sequences in its `cmap` is preferred (the bundled fonts have no color emoji). A mark uses the font of its base when it supports it
  ([cluster matching](https://drafts.csswg.org/css-fonts-4/#cluster-matching)), else its own fallback (the canonical
  decompositions are not tried by the fallback: a precomposed character that the first font only has as a base and a mark
  uses another font). A character that no font supports has the missing glyph (0) of the first available font. The installed
  font fallback (the bundled fonts) is not used for the space characters (U+2000 to U+200A, U+202F, U+205F, U+3000): the
  shaping synthesizes the ones that the fonts of the family list do not support from the space glyph (a fallback font may
  draw a picture for them); U+2011 uses a font of the family list that only has U+2010. The control characters other than
  the tab, the line feed and the carriage return "must be rendered as a visible glyph"
  ([CSS Text §4](https://drafts.csswg.org/css-text-4/#white-space-processing)): the symbol of the Control Pictures block
  (U+2400...) when a font has it, else U+FFFD REPLACEMENT CHARACTER. A lone
  surrogate is U+FFFD.
- The script runs (`ScriptItemizer`, as Chrome's ScriptRunIterator): the Script and Script_Extensions properties
  (`UnicodeScripts.GetScript`, `GetScriptExtensions` of Meziantou.Framework.Unicode; a character used by several scripts
  that excludes the current one takes the first of them by ISO 15924 code); the
  Common and Inherited characters take the script of the run unless their extensions exclude it, a closing paired bracket
  takes the script of its opening bracket, the leading Common characters take the first script, and Hiragana and Katakana
  are one run (their OpenType script is `kana`). A run is a sequence of code points of the same font, orientation and
  script; the runs of the same font are merged in `ShapedText.Runs`.
- **The shaping of a run** (`TextShaper.ShapeRun`): the characters of the run (a line feed is given as a zero width space:
  a segment break is not rendered), with the characters around it (`GlyphBuffer.SetContext`: the joining of the Arabic
  letters across the runs and the inline boxes), are shaped with the face of the font, or the instance of its variations
  (`MatchedFont.GetVariationInstance`), the script of the run (by its ISO 15924 code, `ShapingWorkspace.GetScript`), the
  language systems (`font-language-override`, then the tags of the content language, `ShapingWorkspace.GetLanguages`) and the
  features in the order of their precedence (a later value of a feature wins): `vkrn` when `font-kerning: normal` requests
  the kerning of an upright run, the features of the font face, `palt` or `vpal`, the features of the run, and `kern` (or
  `vkrn`) turned off when the run is not kerned. A run is shaped in the direction of its script, as HarfBuzz and the
  browsers: a run of a left-to-right script in a right-to-left run (`unicode-bidi: bidi-override`), and the converse
  (unless it only has numbers), is given to the shaper in the reverse order of its typographic character units. A ZWJ after a
  space starts a run, as Chrome, which shapes the words between the spaces separately: the soft wrap opportunity after the
  space stays between two clusters. The text is already mirrored by the inline layout (the shaper only applies `rtlm`), and
  the glyphs of a right-to-left run are put back in the order of the text. The positions are in font units,
  scaled to CSS px. The glyph buffer, the scratch memory of the outlines and the plans of the fonts (`ShapingWorkspace`) are
  kept by the `TextShapeCache` of the font list for the next text. When the layout tables of a font cannot be used
  (malformed, or too much work for the text: `FontException`), the run is drawn with the glyphs of `cmap` and the advances
  of the font (`ShapingWorkspace.ShapeWithoutLayout`).
- The queries of the layout and of the MathML layout outside of the shaping of a text (`OpenTypeFont.Layout.cs`:
  `HasVerticalAlternate`, `HasPositioningFeature` for `halt` and `vhal`, `GetKerning`, `GetPairAdjustment`,
  `GetSingleSubstitution` for `rtlm`, and `OpenTypeFont.Synthesis.cs`) use `Meziantou.Framework.Fonts.LayoutQueries`; a
  malformed table answers as a font without the table.
- **Clusters** are many-to-many: a ligature is one glyph for several characters, a multiple substitution several glyphs for
  one character, merged into monotone clusters. `ShapedText.LigatureCarets` are the caret positions inside a cluster of
  several typographic character units: the `GDEF` ligature caret values (formats 1 and 3, `FontFace.GetLigatureCarets`)
  when the ligature glyph has one per unit, else an equal division of the advance (like Chrome).
- **Unsafe to break**: the glyphs whose shaping depends on the text before them have `ShapedGlyphFlags.UnsafeToBreak`
  (`GlyphInfo.IsUnsafeToBreak` of the shaper: in a ligature, a contextual substitution, a kerned pair, a cursive
  attachment, a joining, a syllable). The
  inline layout reshapes the text at the edges of a line only when the break is unsafe or inside a cluster (`ShapeLineEdges`).
- In vertical text, the clusters whose base character has the Vertical_Orientation
  ([UAX #50](https://www.unicode.org/reports/tr50/)) U or Tu are upright with `text-orientation: mixed` (all of them with
  `upright`, none with `sideways`), and the characters Tr are upright when the font substitutes a vertical alternate for
  them (`OpenTypeFont.HasVerticalAlternate`), else rotated. The upright runs are shaped top to bottom with `vert` (or the
  vertical presentation forms of Unicode when the font has no `vert`) (and `vkrn`); their advance is the vertical advance (`vmtx`, or the ascent plus the descent of the font when it has
  none); the vertical origins (`VORG`, `vmtx` top side bearings) do not move the glyphs across the line.
- `word-spacing` is added to the advance of the [word-separator characters](https://drafts.csswg.org/css-text-4/#word-separator)
  (`ShapedGlyphFlags.WordSeparator`), and `letter-spacing` to each visible cluster, the last one included (the inline layout
  removes it at the end of a line); in a ligature, the spacing of each unit is added. The spacing goes to the advance of the
  last glyph of the cluster. A right-to-left run is drawn from its right edge and the glyphs of a cluster do not move (a mark
  stays on its base): the letter spacing goes to the advance of the first glyph of the cluster, on its right as in Chrome and
  Firefox (`css/CSS2/bidi-005`; the inline layout removes it at the end of a line before the character), and the word spacing
  to the last glyph, whose offset grows as much, on the left of the word separator as in Firefox. As Chrome and Firefox, the letters of the joining (cursive) scripts get no `letter-spacing`
  ([cursive tracking](https://drafts.csswg.org/css-text-4/#cursive-tracking)).
- The bidi mirroring of the right-to-left runs is done by the inline layout before the shaping (with the bidi data of
  UAX #9), like `text-transform` and the white space processing.
- **Font synthesis** (`TextShaper.Synthesis.cs`, [css-fonts-4 §2.8](https://drafts.csswg.org/css-fonts-4/#font-synthesis-small-caps)):
  after the font fallback, the code points of a text with `font-variant-caps` or `font-variant-position` are classified, and the
  runs of the same synthesis are shaped separately (no kerning nor ligature across them) with their own em size
  (`ShapedFontRun.SynthesisScale`; `BaseEmSize` is the em size of the text, whose metrics place the glyphs in the line).
  - The small capitals: whether the font supports a feature is decided per font and script (`OpenTypeFont.HasSubstitutionFeature`:
    the feature is in the default language system of the script), not per character, as the specification requires and as
    Chrome's OpenTypeCapsSupport. `petite-caps` and `all-petite-caps` fall back to `smcp` (and `c2sc`); `unicase` lowercases
    the uppercase letters and applies `smcp`; `all-small-caps` needs both `smcp` and `c2sc`. When the font has none of them and
    `font-synthesis-small-caps` is `auto`, the small capitals are synthesized as Chrome does: the lowercase letters (the
    characters that change when uppercased) are shaped with their uppercase mapping (the mappings of `text-transform`, e.g.
    `ß` → `SS`, with the Turkic i) at 70% of the font size (Blink's `kSmallCapsFontSizeMultiplier`), and the uppercase letters
    too for `all-small-caps`/`all-petite-caps` (and for `unicase`); the features of `font-variant-caps` are removed from these
    runs (`FontFeatureList.WithSynthesis`); the caseless characters keep the features. `font-feature-settings: "smcp" 0`
    disables the small capitals of a font that has them without synthesis. The characters added by a case mapping (`ß` → `SS`)
    are in the cluster of the character and get the letter spacing between them.
  - The subscripts and superscripts: when the font has no `subs`/`sups` glyph for a character of a run of the font
    (`OpenTypeFont.WouldSubstitute`: a lookup of the feature has a rule whose input is the glyph alone, whatever its context,
    as Firefox), the whole run is synthesized when
    `font-synthesis-position` is `auto` ("simulated glyphs should be synthesized for all characters"): the glyphs without
    the feature, reduced as Firefox (82% of the font size up to 20px, 66.7% from 45px, interpolated between them, as
    `font-size: smaller` would) and shifted along the block axis (0.34em up, 0.2em down, in the glyph offsets: the line box
    does not change). Chrome does not synthesize them.

`ShapedText` is the result (immutable): `Glyphs` (`ShapedGlyph`: the glyph id, the cluster as the UTF-16 index of its first
character, the advance along the inline axis in CSS px including the kerning and the spacing, the offsets, and flags:
`ClusterStart`, `Upright`, `Invisible`, `WordSeparator`, `Mark`, `UnsafeToBreak`), `Runs` (`ShapedFontRun`: the font, its
em size and the range of glyphs with the same font and orientation, which a renderer draws together), `LigatureCarets`
and `Width`. The glyphs are in logical order, also in a right-to-left run (a renderer draws it from its right edge).
`GetOffset(index)` is the distance from the inline-start edge to the caret position before a character (a caret inside a
ligature, else snapped to the start of its cluster), `GetWidth(start, end)` the advance of a range (to measure the segments
between the line break opportunities without reshaping), `GetIndexAtOffset(offset)` the nearest caret position (hit
testing), `IsCaretPosition`, `IsClusterBoundary` and `GetClusterStart` the clusters, `IsUnsafeToBreak(index)` whether a
break before a character needs a reshaping, `Slice` and `Replace` the glyphs of a range (the boundary shaping and the
reshaping of the line edges).

`TextShaper.Shape(text, fonts, options)` returns the cached `ShapedText` of a run and `TextShaper.MeasureWidth` only its
width (without allocating the glyphs); `ComputedStyle.ShapeText(text)` and `ComputedStyle.MeasureText(text)` use the fonts
and the options of an element. The cache (`TextShapeCache`) belongs to the `FontFallbackList`, so it is discarded with
the lists of the document when its fonts change (a font face is loaded, added or removed, the style sheets, the DOM or the
environment change: see `FontSelector`), and it needs no other invalidation. The lookups take spans (no allocation on a
hit, and the entries of the last options are kept to skip the hashing of the options); the cache is cleared when it holds 16,384 entries or 1 M characters, and the runs longer than 65,536 characters are
not cached. `TextMeasurementBenchmarks` measures the shaping of the text runs of the Wikipedia article (about 3.5 ms) and
the measurement of its words with a cold cache (about 2.1 ms) and a warm one (about 0.22 ms); `LayoutBenchmarks`
measures the layout of the article.

## Text segmentation

The inline layout segments the text with the Unicode algorithms of `src/Meziantou.Framework.WebPlatform/Unicode/` (namespace
`Meziantou.Framework.WebPlatform.Unicode`), tailored for CSS, over the properties of the `Meziantou.Framework.Unicode`
package, so the results do not depend on the OS, ICU or the globalization mode.

### Data

The properties of the code points come from the `Meziantou.Framework.Unicode` package (`UnicodeProperties`,
`UnicodeScripts`, `UnicodeEmoji`), generated from the Unicode Character Database of one version of the standard and read
in constant time. `CharacterProperties` gives the ones the layout reads in another form: the classes of UAX #9 and UAX #14
with their short names (`BidiClass`, `LineBreakClass`, whose values are the ones of the package, so a cast converts them),
the mirroring glyph with a -1 sentinel, and the derived properties (`IsMark`, `IsHan`, `IsHangul`...). The conformance
test files of the UCD (`LineBreakTest.txt`, `GraphemeBreakTest.txt`, `WordBreakTest.txt`) are the ones of the package
(`tests/Meziantou.Framework.Unicode.Tests/files`), so they always match the version of the data; they run as unit tests
(`tests/Meziantou.Framework.WebPlatform.Tests/Unicode/`).

### Grapheme clusters

`GraphemeClusters` implements the extended grapheme clusters of
[UAX #29](https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundaries), the typographic character units of
[CSS Text](https://drafts.csswg.org/css-text-4/#characters) (caret movement, `::first-letter`, `line-break: anywhere`,
`overflow-wrap`): `GetNextBoundary`, `GetPreviousBoundary`, `Count` and `GetBoundaries`, over
`Unicode.GetGraphemeClusterLength` of Meziantou.Framework.Unicode. The line breaker reads the boundaries cluster by cluster
with the same method.

### Word boundaries

`WordBoundaries.GetBoundaries(text, boundaries, tailored)` implements the word boundaries of
[UAX #29](https://www.unicode.org/reports/tr29/#Word_Boundaries) (WB1 to WB999, with the "effective" sequence of WB4), for the
caret movements by words. The tailoring of the browsers (ICU) breaks between Hangul and the other letters; there is no
dictionary (each ideograph is a word, the runs of Thai letters are one word: the word lists of the line breaking are not used). `IsWordLike` tells the segments the movements
stop at (with a letter, a number or an ideograph: the spaces and the punctuation are crossed, e.g. `~>>p+++` is one word for
the caret), and `Move(text, index, forward)` is the movement of macOS and Linux (to the end of the word forward, to its start
backward).

### Line breaking

`LineBreaker.FindOpportunities(text, runs, opportunities)` computes the line break opportunity before each UTF-16 code unit of
the text of an inline formatting context, after the white space processing:

- `None`; `Emergency`: a grapheme cluster boundary usable only with `overflow-wrap: anywhere | break-word` when the line has no
  other opportunity (the layout includes them in the min-content size only for `anywhere`); `Allowed`: a soft wrap
  opportunity; `Hyphen`: after a soft hyphen (U+00AD), the layout displays `hyphenate-character` when it breaks there;
  `AutoHyphen`: an automatic hyphenation opportunity of `hyphens: auto`, added by the inline layout (see
  [Hyphenation](#hyphenation)); `Mandatory`: a forced break after BK, CR, LF, NL (preserved segment breaks); `Phrase`: an
  opportunity inside a phrase of `word-break: auto-phrase`, which the layout only takes when no other opportunity fits.
- The runs (`LineBreakOptionsRun`) give the `LineBreakOptions` of the ranges of the text: `line-break` (`auto` is `Normal`),
  `word-break` (`break-word` is `Normal` with `overflow-wrap: anywhere`), `overflow-wrap`, `hyphens`, `BreakSpaces`
  (`white-space-collapse: break-spaces`) and whether the content language is Chinese or Japanese, or Japanese. The class of a character is
  resolved with its own options; a break uses the options of the character after it (the properties that control a break
  between two elements are undefined in CSS Text), except the break after a soft hyphen.
- An atomic inline is represented by U+FFFC (class CB: breaks before and after it, LB20). CSS Text requires these
  opportunities even next to a character that forbids them (e.g. U+00A0), which the inline layout forces itself.
- A `LineBreaker` reuses its buffers: one instance per layout engine (not thread-safe).

The rules of [UAX #14](https://www.unicode.org/reports/tr14/) are applied in order on the combining character sequences
(LB9/LB10), as written in the annex (the current rules depend on more context than a pair table). `LineBreakOptions.UnicodeDefault`
gives the untailored algorithm, which passes `LineBreakTest.txt`. The CSS tailorings
([CSS Text 4 §6](https://drafts.csswg.org/css-text-4/#line-breaking)):

- No soft wrap opportunity inside a grapheme cluster.
- A space (U+0020) is a soft wrap opportunity whatever follows it, as in the browsers: UAX #14 forbids the breaks after
  spaces before a word joiner (LB11), a closing punctuation, an exclamation mark, a solidus (LB13) or an infix separator
  (LB15d), and after an opening punctuation (LB14) or an initial quotation mark (LB15a) followed by spaces (e.g. it keeps
  "the" with ".NET" in "the .NET runtime").
- `line-break`: CJ (small kana) is ID with `normal` and `loose`, NS with `strict`, as in the browsers (the specification
  forbids the breaks before the small kana with `normal` since w3c/csswg-drafts#10363, which no browser follows: the WPT tests
  `css/css-text/i18n/*-cj-normal` and `line-break-normal-011`/`-012` expect them). With `loose`: IN and
  the iteration marks are ID, breaks before U+2010/U+2013 after an ID; in Chinese or Japanese, the centered punctuation marks
  and the PO/PR characters whose East_Asian_Width is A, F or W are ID. With `normal` or `loose` in Chinese or Japanese, U+301C
  and U+30A0 are ID. `anywhere`: an opportunity at every grapheme cluster boundary, except before a forced break.
- `word-break: break-all`: the letters and numbers, and the AL, HL, NU and SA characters are ID (except the Hangul jamo and the
  Brahmic aksaras, which keep their syllables); `keep-all`: no opportunity between two letters/numbers or NU, AL, AI, ID
  characters, except the explicit ones (U+200B) and the word boundaries of the lexical word breaking; `manual`: SA is AL;
  `auto-phrase`: see below.
- The [lexical word breaking](https://drafts.csswg.org/css-text-4/#lexical-breaking) of the Southeast Asian scripts (SA,
  `LineBreaker.GetWordBreakEngine`): the runs of the SA characters of Thai, Lao, Myanmar and Khmer are segmented into words with the
  word lists of ICU, as Chrome does (`IcuDictionaryBreakEngine` of Meziantou.Framework.Unicode.Dictionaries, a port of the
  dictionary break engines of ICU 78, `dictbe.cpp`: the longest dictionary
  words followed by other dictionary words, up to three words of lookahead; the characters that start no dictionary word are
  attached to the word before them up to a plausible word start; never before a combining mark), and the opportunities between
  the letters of a run are its word boundaries (LB28), kept by `word-break: keep-all`. The other SA scripts (Tai Tham, Tai
  Viet, New Tai Lue...) use the [fallback breaking](https://drafts.csswg.org/css-text-4/#fallback-breaking) (an opportunity
  between the typographic letter units). With `word-break: manual`, there is no break between SA letters. The word lists
  are embedded in Meziantou.Framework.Unicode.Dictionaries (about 570 KB compressed for the four scripts: Thai 69 KB, Lao
  89 KB, Burmese 146 KB, Khmer 262 KB; generated by `tools/Meziantou.Framework.Unicode.Dictionaries.Generator` from the
  data release of ICU) and loaded on first use of their script (`IcuWordDictionary`, immutable, from 0.5 MB in memory for
  Thai to 2 MB for Khmer). The browsers differ on the words: Chrome and Firefox fail the Lao test `word-break-normal-lo-000`, which these
  word lists pass (as Safari).
- `word-break: auto-phrase` (`JapanesePhraseBreaker`): when the content language is Japanese, the runs of kanji and kana are
  segmented into phrases with the machine-learned model of ICU (`jaml.txt`, a model of [BudouX](https://github.com/google/budoux),
  used by Chrome for `lw=phrase`; a port of `MlBreakEngine`, `mlbe.cpp`: the scores of the characters, pairs and triples around
  each position; the text is not normalized to NFKC first). The opportunities inside a phrase, and between a letter or a digit
  and the start of a phrase (`abc漢字` is one phrase, as with `lw=phrase` in ICU), are `Phrase`: the line breaking takes them
  only when no other opportunity fits the line (Chrome breaks the line again without the phrases when it overflows: WPT
  `word-break-auto-phrase-overflow-001`), and they do not count for the min-content size. The opportunities of U+200B, `wbr`
  and the spaces are kept. Without a supported language (Chinese, Korean, or no language), `auto-phrase` is `normal`. With
  `auto-phrase`, the hyphenation opportunities (the soft hyphens and the ones of `hyphens: auto`) are taken like the `Phrase`
  ones: "hyphenation opportunities are suppressed as if hyphens: none had been specified", unless the line would overflow
  (WPT `word-break-auto-phrase-008`, as Chrome).
- For `LineBreaker`, `hyphens: auto` only uses the soft hyphens, like `manual` (the inline layout adds the automatic
  hyphenation opportunities); `hyphens: none` removes the opportunity after a soft hyphen.
- The deviations from UAX #14 that CSS Text suggests for compatibility (e.g. no break in "-13" or "!important") are not applied.

`LineBreaker.IsSegmentBreakRemoved(before, after)` is the context-dependent part of the
[segment break transformation](https://drafts.csswg.org/css-text-4/#line-break-transform), which CSS Text leaves UA-defined:
the rules of the previous drafts of CSS Text, as Firefox applies them (`nsTextFrameUtils.cpp`): the segment break is removed
next to U+200B, or between two F/W/H East Asian characters that are not Hangul (the wide emoji and U+20A9 WON SIGN are not, WPT
`segment-break-transformation-currency-001`), checked by the tentative WPT tests `css/css-text/white-space/seg-break-transformation-*`.
The characters around the segment break are found after skipping the default ignorable characters other than U+200B (a
variation selector, a bidi mark: WPT `segment-break-transformation-ignorable-1`). When the content writing system is Chinese,
Japanese or Yi (`InlineLayoutAlgorithm.IsSegmentBreakRemoved`, from the script subtag of the language when it has one),
`LineBreaker.IsSegmentBreakRemovedInChineseJapaneseOrYi` also removes it when one side is a F/W/H punctuation mark (or U+FF5E,
U+3000), as Firefox does (WPT `segment-break-transformation-punctuation-*`), or a punctuation mark or a non-currency symbol whose
East Asian Width is A (or an emoji) next to a F/W/H character that is not Hangul (the rule of the previous drafts of CSS Text 4,
WPT `writing-system-segment-break-001`, which no browser passes).

The word boundaries (see [Word boundaries](#word-boundaries)) are only used by the caret movements: `Intl.Segmenter` is not exposed.

### Hyphenation

`HyphenationDictionary` (`Unicode/Hyphenation/`) is the automatic hyphenation of `hyphens: auto`
([CSS Text 4 §5.4](https://drafts.csswg.org/css-text-4/#hyphenation)): [Liang's algorithm](https://tug.org/docs/liang/) with the
patterns and the exceptions of [hyph-utf8](https://github.com/hyphenation/tex-hyphen) (the source of the dictionaries of Chrome
on Android, Linux and ChromeOS and of Firefox), as Chrome does (`hyphenation.cc`, `hyphenation_minikin.cc` and the port of the
hyphenator of Android `hyphenator_aosp.cc`):

- **Dictionaries**: `tools/Meziantou.Framework.WebPlatform.SpecGen` (`HyphenationGenerator`) compiles the patterns of `tests/Meziantou.Framework.WebPlatform.SpecData/hyphenation` (the `hyphenation`
  source) into a packed trie per language (the letters are numbered, the nodes are in breadth-first order so that the children
  of a node are consecutive; the exceptions are whole-word patterns whose values override the patterns), compressed with
  Brotli: the embedded resources `Unicode/Hyphenation/*.hyph` (about 470 KB for 46 dictionaries, see the
  [README](../Unicode/Hyphenation/README.md) for the languages and their licenses). A
  dictionary is loaded on first use (`HyphenationDictionary.Get`, at most about 1 ms) and is immutable.
- **Language** (`HyphenationDictionary.Get`): the content language of the text (`HtmlElementStates.GetLanguage` of the element of the text),
  matched case-insensitively with the fallbacks of Chrome (`HyphenationMinikin::MapLocale`, from CLDR): the locale and its
  prefixes (`de-DE` is `de`, which is `de-1996`; `pt-BR` is `pt`), `en` is en-us and the other English locales en-gb (except
  the locales of the United States territories), `no` is nb, `mn` is mn-cyrl, `sr` is sr-cyrl. Without a language or a
  dictionary, the text is not hyphenated automatically (Chrome).
- **Words** (`FindOpportunities`): the leading spaces, opening punctuation and quotes (Line_Break OP and QU) and the trailing
  spaces, punctuation and controls are not part of the word (`hyphens-punctuation-001`); the uppercase letters are matched with
  their lowercase letters (the alphabet of a dictionary maps the characters whose simple lowercase mapping is a letter); a word
  with a character that is not a letter of the dictionary is not hyphenated, nor, in English, a word that starts with an
  uppercase letter (Chrome, w3c/csswg-drafts#5157). The limits are Chrome's: `hyphenate-limit-chars: auto` is a word of 5
  characters, 2 before the hyphen and 2 after (an explicit value before the hyphen is also the value after the hyphen when it
  is `auto`), and the patterns never hyphenate in the first 2 or the last 3 characters of a word, nor a word longer than 62
  characters (Android's hyphenator). The lengths are in UTF-16 code units.
- **Line breaking** (`InlineLayoutAlgorithm.Hyphenation.cs`): `ComputeBreakOpportunities` hyphenates the words of the text whose
  style has `hyphens: auto` (with `word-break: auto-phrase`, the line only breaks at them when nothing else fits): a word is the text between two `Allowed` or `Mandatory`
  opportunities (as Chrome's line breaker, so a word may span inline boxes, `hyphens-span-002`), without the spaces after it, with
  the dictionary and `hyphenate-limit-chars` of the style of its first character; an opportunity is kept when the character
  before it has `hyphens: auto`. A word with a soft hyphen is not hyphenated automatically ("Automatic hyphenation opportunities
  within a word must be ignored if the word contains a conditional hyphen"; Chrome only ignores them before the last soft
  hyphen). The opportunities (`AutoHyphen`) are taken as the soft hyphens: the greedy line breaking measures the hyphen
  (`hyphenate-character`) with the content before it, `hyphenate-limit-lines` and `hyphenate-limit-last` apply, the min-content
  size takes them all (the widest part with its hyphen, `hyphens-auto-min-content`), `word-break: break-all` and
  `line-break: anywhere` leave no word to hyphenate, `text-wrap-mode: nowrap` removes them, and they replace the emergency
  opportunities of `overflow-wrap`. As in Chrome (`ShouldHyphenate` of `shaping_line_breaker.cc`), the last word of the content
  is not hyphenated unless it starts the line or is the only word (`hyphens-auto-last-word-001`, `IsAutomaticHyphenationAllowed`).
- **Language-specific hyphenation** (`InlineLayoutAlgorithm.HyphenationSpelling.cs`): "The UA must use the appropriate
  language-specific hyphenation character(s) and should apply any appropriate spelling changes just as for automatic
  hyphenation at the same point" ([`hyphens`](https://drafts.csswg.org/css-text-4/#hyphens-property)). At a soft hyphen and at
  an automatic opportunity (`GetHyphenationBreak`): in Dutch (`nl`), the diminutive of a word in -é written with `ee` is `é`
  again (`cafee|tje` is `café-tje`); in Hungarian (`hu`), a long double consonant written with a digraph is two digraphs
  (`Ös|szeg` is `Ösz-szeg`, also `cs`, `dz`, `dzs`, `gy`, `ly`, `ny`, `ty`, `zs`); in Pinyin (`zh-*-pinyin`), the apostrophe that
  separates two syllables is removed at the start of the next line (`tú|’àn` is `tú-` `àn`), and it is a hyphenation
  opportunity of `hyphens: auto` (and of a soft hyphen before it, which UAX #14 forbids before a quotation mark). The letters
  of the spelling change are shaped with the hyphen (the hyphen fragment), the characters they replace are removed from the end
  of the line, the line breaking measures them, and the next line starts after the removed characters (its `ContentStart`).
  No browser implements them (WPT `css/css-text/hyphens/i18n`).
- The edges of a hyphenated line are shaped again as at the other breaks (`ShapeLineEdges`, as Chrome's
  `ShapingLineBreaker`): when the opportunity is unsafe to break (a kerned pair, a joining) or inside a cluster (a ligature
  across the opportunity: "schiff-fahrt" with an "ff" ligature, or a soft hyphen between "f" and "i", which does not prevent the
  ligature, as in HarfBuzz), the characters on each side of the break are shaped alone, with the joining of the cursive scripts
  continued across the break (`hyphens-shaping-001`). The hyphen is shaped alone, in the direction of the text it follows (the
  characters of `hyphenate-character` after a right-to-left word are in the right-to-left order). The greedy line breaking measures the part
  before the opportunity with the glyphs of the whole item (the caret position inside a ligature).

## Bidirectional text

`BidiResolver` (`src/Meziantou.Framework.WebPlatform/Unicode/`) implements the
[Unicode Bidirectional Algorithm](https://www.unicode.org/reports/tr9/) (UAX #9), and `BidiControls` its use by CSS
([CSS Writing Modes §2.4](https://drafts.csswg.org/css-writing-modes-4/#text-direction)).

### Data

The bidi properties come from the `Meziantou.Framework.Unicode` package: Bidi_Class (with the defaults of the unassigned
code points), Bidi_Paired_Bracket_Type and Bidi_Paired_Bracket, Bidi_Mirrored and Bidi_Mirroring_Glyph
(`CharacterProperties.GetBidiClass`, `GetBidiPairedBracket`, `GetBidiMirroringGlyph`). `CharacterProperties.GetBidiClass` is
also used by the HTML directionality (`dir=auto`). `BidiTest.txt` and `BidiCharacterTest.txt` run as unit tests
(`BidiConformanceTests`) and pass.

### Algorithm

- `Resolve(text, direction)` resolves the levels of a text (rules P1 to I2), indexed by UTF-16 code unit (the low surrogate of a
  pair has the level of its high surrogate). `Resolve(text, classes, direction)` takes the Bidi_Class of each code unit, so the
  layout can override the class of a character (an atomic inline). The text is split into paragraphs at the paragraph separators
  (Bidi_Class B: U+2029, LF, CR, U+001C-U+001E, U+0085; P1); `Paragraphs` gives their ranges and levels.
- `BidiParagraphDirection`: `LeftToRight`/`RightToLeft` set the paragraph level (clause HL1, the CSS `direction` of the block
  container); `AutoLeftToRight`/`AutoRightToLeft` apply rules P2 and P3 to each paragraph, with the level used when it has no
  strong character (`unicode-bidi: plaintext`).
- The explicit levels (X1 to X8) use the directional status stack (max_depth 125); the characters removed by X9 (embedding and
  override initiators, PDF, BN) are kept as described in UAX #9 §5.2: they are skipped by the isolating run sequences and get the
  level of the preceding character. The isolating run sequences (BD13) are resolved with W1 to W7, N0 (bracket pairs, BD16 with
  the 63-entry stack and the canonical equivalents U+2329/U+232A, and the NSMs that follow a resolved bracket), N1, N2, then I1
  and I2.
- `GetLineLevels(start, end, levels)` applies rule L1 to a line: the segment separators (tab), paragraph separators and the
  sequences of whitespace, isolate controls and removed characters before them or at the end of the line get the paragraph level.
  The inline layout calls it for each line after the line breaking, on the text of the line (the trailing spaces hang at the
  end of the line in the paragraph direction); `preservedTrailingLength` excludes the last characters of the line from its
  trailing whitespace (the spaces of `break-spaces`).
- `ReorderVisual(levels, visualToLogical)` applies rule L2 to any items (characters, or the fragments of a line with their
  levels); `GetVisualRuns(lineLevels, runs)` gives the level runs of a line in visual order (left to right), each displayed in
  reverse order when its level is odd.
- L3 (combining marks) is not applied, as in browsers. L4 (mirroring) belongs to the shaping: a character at an odd level is
  displayed with its `Bidi_Mirroring_Glyph`, or with the OpenType `rtlm` feature for the Bidi_Mirrored characters that have none.
- A `BidiResolver` reuses its buffers: one instance per layout engine (not thread-safe).

### CSS

CSS applies the algorithm to each sequence of inline-level content of a block container that is not interrupted by a block
boundary or a forced paragraph break. `BidiControls` gives the control codes that represent `unicode-bidi` and `direction`
([HL3](https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection)): the inline layout inserts
`GetInlineStart(unicodeBidi, isRtl)` before the content of an inline box and `GetInlineEnd(unicodeBidi)` after it
(`embed`: LRE/RLE…PDF, `isolate`: LRI/RLI…PDI, `bidi-override`: LRO/RLO…PDF, `isolate-override`: FSI LRO/RLO…PDF PDI, `plaintext`:
FSI…PDI), and `GetBlockContainerStart`/`GetBlockContainerEnd` around the content of a block container with `bidi-override` or
`isolate-override`. The paragraph level is `GetParagraphDirection(unicodeBidi, isRtl)` of the block container. When an inline
box is split by a forced paragraph break, its end codes are inserted before the break and its start codes after it
([§2.4.4](https://drafts.csswg.org/css-writing-modes-4/#bidi-paragraph-breaks)). An atomic inline (U+FFFC) is neutral, except
a replaced element with `unicode-bidi: embed | bidi-override` (`GetAtomicInlineClass`, passed to `Resolve` with the classes).
The control codes are zero-width and are never displayed; the line breaker treats them as combining marks (class CM).

The helpers take the typed `unicode-bidi` of the layout (`UnicodeBidi`, `LayoutStyle.UnicodeBidi`); the HTML `dir=auto` directionality stays computed by the
HTML algorithm (the first strong character of the text, `HtmlElementStates`).
