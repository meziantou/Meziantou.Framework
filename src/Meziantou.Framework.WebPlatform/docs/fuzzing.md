# Fuzzing

The parsers and the query engines process untrusted input. Coverage-guided fuzzing ([SharpFuzz](https://github.com/Metalnem/sharpfuzz)
with [libFuzzer](https://llvm.org/docs/LibFuzzer.html)) looks for the inputs that crash them (unexpected exceptions,
stack overflows, out of memory) or hang them.

## Targets

`fuzz/Meziantou.Framework.WebPlatform.Fuzz` defines the targets on the shared fuzzing host of the repository (see
[fuzz/README.md](../../../fuzz/README.md)). A target must process any input without throwing, except for the exceptions the
API documents for invalid input (e.g. a `DomException` for an invalid selector).

| Target | What it does with the input |
|---|---|
| `webplatform-html` | Parses a document and fragments in several contexts, serializes and formats them, parses the serialization again |
| `webplatform-xml` | Parses a document with `XmlDocumentParser` and with `DomParser` (XML and SVG), serializes it with `XmlSerializer` |
| `webplatform-css` | Parses a style sheet and a declaration block, serializes them and parses the serialization again, evaluates `CSS.supports()` |
| `webplatform-selectors` | Matches selectors with `QuerySelectorAll`, `Matches` and `Closest` |
| `webplatform-xpath` | Evaluates an XPath expression and reads its result |
| `webplatform-url` | Parses a URL with several base URLs, and checks that parsing its serialization gives the same URL |
| `webplatform-urlpattern` | Compiles a URL pattern (its regular expressions) and matches it |
| `webplatform-font` | Loads a font file (OpenType, collection, WOFF, WOFF2) with the managed font reader, and reads the metrics, names, glyphs, advances, kerning and variations of its faces (binary input) |

The initial corpus of each target is embedded in the project: the files of `Seeds/<target>`, and the bundled fonts of
the library for the `webplatform-font` target. `Meziantou.Framework.Fuzzing.Tests` runs every target on its seeds in
every CI build.

## Running

`eng/run-fuzz.ps1` publishes the targets, instruments the library, and runs each target with libFuzzer (Linux only, see
[fuzz/README.md](../../../fuzz/README.md#running-a-campaign)). The weekly `fuzz-sharpfuzz` workflow runs every target, keeps its
corpus from run to run, and opens an issue for each failed target:

```bash
pwsh eng/run-fuzz.ps1 -Project fuzz/Meziantou.Framework.WebPlatform.Fuzz -DurationSeconds 600 -LibFuzzer ./libfuzzer-dotnet
```

## Reproducing a crash

Run the target on the crashing input (without instrumentation, under the debugger if needed). When the bug is fixed, add
the input to a unit test (e.g. `HardeningTests`) or commit it under `fuzz/Meziantou.Framework.WebPlatform.Fuzz/Regressions/<target>/`:

```bash
dotnet run --project fuzz/Meziantou.Framework.WebPlatform.Fuzz --framework net10.0 -- webplatform-html artifacts/fuzz/results/webplatform-html/crash-...
```

## Limits

Some inputs are only bounded by limits that browsers also have:

- The CSS parser keeps at most 256 levels of nested blocks and functions (`CssParser.MaxNestingDepth`): the content of
  the deeper blocks is ignored, so deeply nested style sheets and selectors cannot exhaust the stack.
- The pretty formatters stop indenting after `MaxIndentationDepth` levels, so their output stays linear in the size of
  the input.

The cost of an XPath expression has no limit: it is polynomial in the size of the document, with the nesting of the
predicates as the exponent (e.g. `//*[//*[//*]]`). System.Xml.XPath also evaluates the right operand of a comparison
between two node-sets again for each node of the left operand, so nested comparisons multiply their costs (e.g.
`//*[//. > //*[//. > //*]]`; the 182-byte expression found by the `xpath` target takes seconds on a 30-node document).
An application that evaluates untrusted XPath expressions must bound their evaluation time itself. The `webplatform-xpath`
target truncates its inputs to 256 bytes so that the fuzzer explores the expressions instead of reporting this cost as
slow units.

## Differential testing with Chromium

`tools/Meziantou.Framework.WebPlatform.ChromiumDiff` parses each input with Chromium ([Playwright](https://playwright.dev/dotnet/)) and with the
library, and compares the results. Each input is parsed in several modes:

- as a document (a navigation in Chromium, `HtmlDocumentParser.ParseDocument` in the library);
- as a fragment, with the `innerHTML` setter of each context element of `FragmentContext.All`: the elements that change
  the state of the tokenizer or the insertion mode (`title`, `textarea`, `script`, `plaintext`, `table`, `tr`, `select`,
  `template`, `frameset`...), the special and formatting elements, the void elements, the SVG and MathML elements
  (the HTML integration points, the MathML text integration points, `annotation-xml` with and without an `encoding`,
  and the others), and a few elements of a quirks-mode document (`body`, `p`, `table`). The context element is not
  connected;
- with the scripting disabled (Chromium runs with JavaScript disabled) and enabled (Chromium runs with JavaScript enabled
  and a `script-src 'none'` Content Security Policy, so the scripts of the input do not run and the tree is the result of
  the parser only). This changes how `noscript` is parsed.

For each input and mode, three checks run in order, and the first one that differs is reported: the trees in the html5lib
format (including the template contents and the shadow roots), the serializations (`getHTML()` with the serializable
shadow roots; for a document, its children, as `outerHTML` without the closing tag of the root element), and the trees
parsed from the serializations (the round trip: `innerHTML` in the same context, or `DOMParser` for a document).

The inputs are all the tree construction tests of WPT (whatever their context and scripting), the seeds of the
`webplatform-html` fuzzing target, the reproductions of the known differences (see below), the real-world pages of
`tools/Meziantou.Framework.WebPlatform.ChromiumDiff/pages.txt` (`--pages`, only parsed as documents), any directory of files (`--corpus`, e.g. the
corpus of the `webplatform-html` fuzzing target), and generated inputs (`--generate <count> --seed <n>`). `InputGenerator`
generates random markup around the areas that the tree construction tests cover least: foreign content (the integration
points, the breakout tags, the adjusted names, CDATA sections), the template insertion modes, the adoption agency
algorithm and the reconstruction of the active formatting elements, the tables, `select`, `noscript`, the raw text
elements, and the texts and attribute values that the serialization escapes. The same seed gives the same inputs.

A difference fails the comparison, except the deviations listed with their reason in
`tools/Meziantou.Framework.WebPlatform.ChromiumDiff/known-differences.json`. Each entry gives the comparisons it explains (`differences`: an input
id, optionally followed by a mode and a check, e.g. `tests7.dat#17 fragment(select)`), minimal inputs that reproduce the
deviation (`reproductions`, with the prefix of the modes in which they differ), and regular expressions that match the
minimized inputs that have the deviation (`patterns`). A listed comparison or reproduction that is now the same also fails,
so the list stays accurate. The CI compares the tree construction tests, the seeds and the reproductions.

`--minimize` reduces the input of each unexpected difference (delta debugging over the tags and the texts, then over the
characters), and a difference whose minimized input is a reproduction or matches a pattern of a known difference is known.
This makes the comparison of generated inputs reproducible: `--generate 3000 --seed 1 --minimize` has no unexpected
difference. `--browser firefox` or `--browser webkit` compares with another browser (`known-differences.json` lists the
deviations of Chromium) to triage a difference: a deviation of Chromium only is usually the same in Firefox as in the
library.

```bash
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- install
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --report artifacts/chromium-diff.txt
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --no-tests --generate 3000 --seed 1 --minimize
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --no-tests --pages --corpus artifacts/fuzz-corpus/webplatform-html --exclude-nul --documents
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --filter tests7.dat --context select --context "svg path" --browser firefox
```

The known differences are deviations of Chromium (often shared by WebKit) from the HTML Standard and the tree construction
tests; Firefox agrees with the library on most of them:

- Chromium handles U+0000 in its input preprocessor instead of in the tokenizer and the tree builder: it drops the NULs in
  the data state (so a NUL before the DOCTYPE does not switch to quirks mode), in the RCDATA state of a `title` or
  `textarea` fragment, and in the foreign content of an SVG or MathML fragment, and it replaces them by U+FFFD in the
  other states (so `<` followed by a NUL in a text is `<\uFFFD` instead of `<`). The fuzzing corpora contain many NULs:
  `--exclude-nul` skips the inputs that contain U+0000. A U+FFFD character does not set the frameset-ok flag to
  "not ok" either.
- CDATA sections are bogus comments in the integration points, and in a fragment whose context element is an SVG or
  MathML element.
- The `noscript` element in `head` is closed by an ignored `<head>` start tag; `search` is not in the special category;
  a formatting element with an `is` attribute is not reconstructed.
- An end tag in foreign content when the stack of open elements has only the `html` element (fragment case) is processed
  with the rules of the insertion mode instead of being ignored; the end tags whose name is an SVG name with uppercase
  letters (`</foreignobject>`, `</clippath>`) are adjusted, so they do not close an element of the lowercase name.
- In a `template` fragment, the tokens after an element of the "in head" rules (`<title></title><td>`) are processed as
  in body, and the form element pointer is used as without a template element (also in Firefox and WebKit, but not in the
  tree construction tests); in a template, `</form>` is ignored when the form is not the current node, and whitespace in
  table text reconstructs the formatting elements; the characters in a table row of a template in a `colgroup` fragment
  are ignored; `input`, `keygen` and `textarea` are inserted in a `select` fragment.

The comparison found one bug in the library: the `encoding` attribute of an `annotation-xml` context element was ignored,
so the element was not an HTML integration point (`TreeConstructionTests.ParseFragmentFoundByChromiumDiff`). The tests of
`TreeConstructionTests.ParseWhereChromiumDiffers` and `ParseFragmentFoundByChromiumDiff` keep the specified behavior of
the other differences.

On a corpus of 19,000 inputs without NUL generated by the `webplatform-html` fuzzing target, the comparison also found the
`option` elements inside a `selectedcontent` element, which are not options of the `select` element in the library (a
deviation from the specification, see [whatwg/html#13038](https://github.com/whatwg/html/issues/13038)): Chromium replaces
them with the clone of the selected option.

### Comparing the layout with Chromium

`--layout` compares the layout of the pages of `pages.txt` (and of the HTML files of the `--corpus` directories, laid out
without subresources: a convenient way to check a reduced test case) in Chromium and in the library. Each page is laid out
in the same conditions in both:

- **The same resources.** The HTML of the page is the file downloaded once in `artifacts/chromium-diff/pages` (served as
  UTF-8). Its subresources (style sheets, images, fonts...) are recorded once from the network by Chromium in
  `artifacts/chromium-diff/resources/<page>` (`index.json` maps the URLs to the status, a few headers such as
  `Content-Type` and `Access-Control-Allow-Origin`, and the file of the body), then replayed in Chromium (Playwright routes)
  and in the library (a `DocumentLoader`); the other requests and the navigations of the iframes fail in both. `--record`
  records them again (e.g. after a recording that got an error status: it is replayed as is). The downloaded pages and
  resources are never committed.
- **No scripts.** Chromium runs with JavaScript disabled, as the library without script engine: the layout is the one of
  the parsed document and its style sheets, and the `noscript` elements are rendered. Scripts would make the comparison
  noisy (timers, network, randomness) and depend on the script engine.
- **The same viewport**: 1280x800 CSS pixels (`--viewport`), a device pixel ratio of 1, the light color scheme, and the
  classic scrollbars of 15px of the library (Playwright hides the scrollbars of headless Chromium unless they are styled
  with `::-webkit-scrollbar`: `--hide-scrollbars` is removed from its arguments).
- **The same font.** After the load of the page, a style sheet is injected in both: all the elements and their
  `::before`, `::after`, `::marker`, `::placeholder` and `::file-selector-button` pseudo-elements use the bundled Arimo of
  the library (as a web font from a `data:` URL, the same file in both, with `font-display: block`; both wait for
  `document.fonts`), and the animations and the transitions are disabled. So the default fonts of the generic families
  (and the 13px of `monospace`) are not compared, and the characters that Arimo does not have (CJK, emoji, icon fonts...)
  are rendered with the fallback fonts of the system in Chromium and of the library: these differences are accepted.

The border boxes of the elements (`getBoundingClientRect()` in both, in document coordinates) are compared with a
tolerance of 1px (`--tolerance`). The elements are matched by their position in the tree (the indexes of the element
children from the root element), so the elements that are not in both trees (a parse difference) are only counted; the
contents of `head`, `svg`, `math` and `select` elements are not compared. A difference usually propagates to many elements
(a wider parent changes the layout of its contents, a taller element moves the next ones, a taller child makes its
parent taller), so only the differences that no other difference explains are reported (`LayoutComparison.FindDifferences`):

- nothing inside an element whose x or width is different;
- a different x, width or y when no previous sibling is different (the y relative to the previous sibling, else to the
  parent: the small differences within the tolerance, e.g. the rounding of the line heights, add up in a long document),
  and a different x or width only for the first cell of a table (the columns);
- a different height when no child is different, unless the bottom of its last child moved by the same amount;
- the siblings and the parents skip the elements without box (`display: contents`).

Each reported difference shows a CSS path to the element (from the nearest ancestor with a unique id), its border box in
both, its computed `display`, `position` and `float` and the `display` of its parent in both, and its first different
descendant (the first different child, recursively), which often points to the origin of a difference that propagates
from the contents (e.g. the width of a float that depends on its text). `--json <file>` writes all the differences (one
JSON object per line) for statistics, and `--inspect <selector>` prints, for the elements that match the selector, their
markup, their client and scroll sizes and the computed values of a list of properties (`--properties`) in both:

```bash
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --layout --report artifacts/chromium-diff-layout.txt
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --layout --filter wikipedia --inspect "#mw-panel-toc" --properties width,overflow
dotnet run --project tools/Meziantou.Framework.WebPlatform.ChromiumDiff -c Release -- --layout --corpus path/to/reduced-test-cases
```

A difference fails the comparison, except the accepted ones: the rules of `tools/Meziantou.Framework.WebPlatform.ChromiumDiff/layout-known-differences.json`
(a CSS selector, matched by the element or one of its ancestors, or by its first different descendant or one of its
ancestors, an optional page, and the reason; a rule that matches no
difference when all the pages are compared fails, so the list stays accurate), and four kinds of differences recognized
by the element: the characters that the injected font does not have (its text or the generated content of its
pseudo-elements, see above); a solidus between two characters (e.g. a URL), after which Chromium does not break a line
before a letter or a digit (its line breaking table of the ASCII characters, as WebKit), where UAX #14, Firefox and the
library do; a `letter-spacing` (of the element or of a descendant), which the browsers add after the last character
of a line, where CSS Text 4 and the library do not; and an inline box with a block-level child, whose rectangle around
the child is the anonymous block (as wide as the containing block) in Chromium and WebKit, and the border box of the
child in Firefox and in the library. A page that the library does not lay out within 2 minutes fails.

The limits of the comparison: the pages are the version downloaded once (another machine downloads another version); the
boxes are compared, not the painting (colors, backgrounds, text positions inside a box); the transforms change the
rectangles of both; the elements inside a scroll container are compared at its initial scroll position; and an
accepted difference may hide another difference in the same element.
