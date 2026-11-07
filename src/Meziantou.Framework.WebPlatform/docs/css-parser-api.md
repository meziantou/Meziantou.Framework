# CSS Parser API

The [CSS Parser API](https://wicg.github.io/css-parser-api/) exposes the parser of CSS Syntax to the scripts and to .NET:
it parses CSS-like text into rules, declarations and component values, without interpreting them (the properties, the
selectors and the at-rules are not validated). It is an unofficial draft that no browser implements, so the scripts can
hide it with `WebPlatformFeatures.CssParserApiEnabled` (see
[Disabling features](browsing-context.md#disabling-features)).

The operations run the entry points of [CSS Syntax](https://drafts.csswg.org/css-syntax-3/#parser-entry-points) on the
parser of the library (`Css/Syntax`), and convert the results to the objects of the API (`Css/ParserApi`):

| Operation | CSS Syntax entry point | Result |
| --- | --- | --- |
| `CSS.parseStylesheet(css, options)` | [parse a stylesheet](https://drafts.csswg.org/css-syntax-3/#parse-stylesheet) | A promise of the rules |
| `CSS.parseRuleList(css, options)` | [parse a stylesheet's contents](https://drafts.csswg.org/css-syntax-3/#parse-stylesheet-contents) | A promise of the rules |
| `CSS.parseRule(css, options)` | [parse a rule](https://drafts.csswg.org/css-syntax-3/#parse-rule) | A promise of the rule, rejected with a `SyntaxError` |
| `CSS.parseDeclarationList(css, options)` | [parse a list of declarations](https://drafts.csswg.org/css-syntax-3/#parse-list-of-declarations) | A promise of the declarations (the at-rules are dropped) |
| `CSS.parseDeclaration(css, options)` | [parse a declaration](https://drafts.csswg.org/css-syntax-3/#parse-declaration) | A `CSSParserDeclaration`, or a `SyntaxError` |
| `CSS.parseValue(css)` | [parse a component value](https://drafts.csswg.org/css-syntax-3/#parse-component-value) | A token, or a `SyntaxError` |
| `CSS.parseValueList(css)` | [parse a list of component values](https://drafts.csswg.org/css-syntax-3/#parse-list-of-component-values) | The tokens |
| `CSS.parseCommaValueList(css)` | [parse a comma-separated list of component values](https://drafts.csswg.org/css-syntax-3/#parse-comma-separated-list-of-component-values) | The lists of tokens between the commas |

```js
const [rule] = await CSS.parseStylesheet('a:hover { color: red !important; b { c: d } }');
rule.prelude;                // [CSSKeywordValue("a"), ":", CSSKeywordValue("hover"), " "]
rule.body[0].name;           // "color"
String(rule);                // "a:hover { color: red !important; b { c: d; } }"
CSS.parseValue('rgb(1, 2)'); // CSSParserFunction: name "rgb", args [[CSSUnitValue(1)], [" ", CSSUnitValue(2)]]
```

The .NET API is the `Css` class (`Css.ParseStylesheet()`, `Css.ParseValue()`...) and the types of the
`Meziantou.Framework.WebPlatform.Css.ParserApi` namespace.

## Results

- The rules are `CSSParserAtRule` (a name, a prelude and a body, null for a statement such as `@import url(a.css);`),
  `CSSParserQualifiedRule` (a prelude and a body) and `CSSParserDeclaration` (a name and a body, its value) objects. The
  body of a block is parsed with [consume a block's contents](https://drafts.csswg.org/css-syntax-3/#consume-block-contents):
  the declarations and the rules, in order.
- The component values (the `CSSToken` typedef) are `CSSParserFunction` objects (a name and the arguments between the
  top-level commas), `CSSParserBlock` objects (`"[]"`, `"()"` or `"{}"` and the body), `CSSKeywordValue` objects for the
  idents, `CSSUnitValue` objects for the numbers, the percentages and the dimensions whose unit is a CSS unit, `" "` for the
  whitespace, and the text of the other tokens (e.g. `","`, `"#fff"`, `"'a'"`, `"url(a.png)"`, `"10foo"`), with its
  escapes. The comments are dropped. The preludes and the bodies keep their whitespace, except the bodies of the
  declarations, which CSS Syntax trims.
- The lists of the objects are frozen (immutable in .NET); the stringifiers serialize the objects (an empty comment separates the tokens that would
  otherwise be parsed as one token).
- `CSS.parseStylesheet()`, `CSS.parseRuleList()`, `CSS.parseRule()` and `CSS.parseDeclarationList()` also take a
  `ReadableStream` of `Uint8Array` chunks: its bytes are decoded as a style sheet for `parseStylesheet()` (the byte order
  mark, then the `@charset` rule, then UTF-8), and as UTF-8 for the others. A locked stream, or a chunk that is not a
  `Uint8Array`, rejects the promise with a `TypeError`.

## Differences from the draft

The draft defines the IDL only, and parts of it contradict its own model or CSS Syntax; the library follows CSS Syntax:

- The preludes, the bodies of the declarations and the blocks and the arguments of the functions are lists of
  component values (`FrozenArray<CSSToken>`), which contain strings and `CSSStyleValue` objects. The draft types them as
  `FrozenArray<CSSParserValue>`, and the constructor of `CSSParserDeclaration` takes a `sequence<CSSParserRule>`: the IDL
  is fixed by `tools/Meziantou.Framework.WebPlatform.SpecGen/IdlModel.cs` (`FixCssParserApiTypes`).
- `atRules` (the option of the parsing operations) is a `record<DOMString, DOMString>` from an at-rule name (ASCII
  case-insensitive, without `@`) to the [production of its block](https://drafts.csswg.org/css-syntax-3/#block-contents):
  `"block-contents"` (the default, also for the at-rules that are not in the record), `"declaration-list"`,
  `"qualified-rule-list"`, `"at-rule-list"`, `"declaration-rule-list"` or `"rule-list"`. The items that the production
  does not allow are dropped (e.g. `{ atRules: { "font-face": "declaration-list" } }` drops the rules of `@font-face`).
  Another value is a `TypeError`. The draft describes it as an untyped "dict of at-rule name => at-rule type".
- `CSSParserDeclaration` has no attribute for `!important`: the flag of CSS Syntax is removed from the body, kept by the
  .NET `Important` property, and serialized by the stringifier.
- `CSS.parseDeclaration('a: b;')` is a `SyntaxError`: parse a declaration does not allow input after the declaration.
- `CSS.parseDeclarationList()` returns only the declarations ("parse a list of declarations" drops the at-rules); parse
  a block with `CSS.parseRule('x { ... }')` to get its declarations and nested rules.
- The constructor of `CSSParserBlock` throws a `TypeError` when the name is not `"[]"`, `"()"` or `"{}"`.
- The arguments of a function are split at its top-level commas: `f(a,)` has two arguments (the second is empty), so
  that the function serializes back to the same text; `f()` has none.
