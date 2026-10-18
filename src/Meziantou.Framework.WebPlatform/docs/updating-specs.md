# Updating the specifications

HTML, DOM, URL, Encoding, CSSOM and the other specifications implemented by this project are living standards.
Everything needed to follow them is in this repository.

## Inputs

| Path | Content |
|---|---|
| `tests/Meziantou.Framework.WebPlatform.SpecData/sources.json` | Where each input comes from (GitHub repository and paths, or URLs). A `*` matches any part of a path segment (e.g. `css/*/parsing`) |
| `tests/Meziantou.Framework.WebPlatform.SpecData/versions.json` | The resolved revision (commit SHA or download date) and SHA-256 of every file. The paths of the test suites are only in `sources.json`, so adding a path does not change the pinned commit |
| `tests/Meziantou.Framework.WebPlatform.SpecData/<source>/` | The machine-readable data used by the code generator |
| `tests/Meziantou.Framework.WebPlatform.External/` | The external test suites (not committed; materialized from the pinned revisions) |

Sources:

- `encoding`: WHATWG Encoding Standard (`encodings.json`, `indexes.json`)
- `html`: named character references (`entities.json`)
- `html-source`: the source of the HTML Standard (`source`), used for the data that only exists in prose: the
  keywords, states and defaults of the enumerated attributes, and the IDL attributes limited to only known values
- `mathml-core`: the source of [MathML Core](https://w3c.github.io/mathml-core/) (`spec.html`) and the files of its user
  agent style sheet (`user-agent-stylesheet`)
- `webref`: curated WebIDL, parsed WebIDL, element definitions and CSS definitions from [w3c/webref](https://github.com/w3c/webref)
- `unicode`: Unicode Character Database files, the UTS #46 IDNA mapping table, and the conformance tests of the text
  segmentation algorithms (`LineBreakTest.txt`, `GraphemeBreakTest.txt`) and of the bidirectional algorithm (`BidiTest.txt`,
  `BidiCharacterTest.txt`), which must match the version of the data
- `chardetng`: the data of [chardetng](https://github.com/hsivonen/chardetng), the encoding detector of Firefox (Apache-2.0
  or MIT license: `COPYRIGHT`, `LICENSE-MIT`), pinned to a release tag: `src/data.rs` has the classes of the bytes of the
  single-byte encodings, the scores of the pairs of classes of each family of languages, and the most frequent ideographs
  and hangul syllables, from which `EncodingDetectorGenerator` generates `EncodingDetectorData.g.cs`. The detector itself
  (`Encodings/LegacyEncodingDetector*.cs`) is a manual port of `src/lib.rs` of the same release: a new release needs its
  changes to be ported, see [browsing-context.md](browsing-context.md#encoding-detection)
- `hyphenation`: the hyphenation patterns (`hyph-*.pat.txt`), exceptions (`hyph-*.hyp.txt`) and source files (`hyph-*.tex`,
  whose headers have the copyright and license notices) of [hyph-utf8](https://github.com/hyphenation/tex-hyphen), for the
  languages of the hyphenation dictionaries whose license allows their redistribution (the list is in `sources.json` and in
  `HyphenationGenerator`)
- `cldr`: the delimiters (the quotation marks) of the locales of the [Unicode CLDR](https://cldr.unicode.org/), from the JSON
  distribution [unicode-org/cldr-json](https://github.com/unicode-org/cldr-json) (`cldr-misc-full/main/*/delimiters.json`,
  Unicode License v3: `LICENSE`), for the quotation marks of `quotes: auto` (`LanguageQuotes.g.cs`)
- `icu`: data of [ICU](https://github.com/unicode-org/icu) (Unicode License v3: `LICENSE`), pinned to a release tag: the word
  lists of the Southeast Asian scripts (`icu4c/source/data/brkitr/dictionaries/{thai,lao,burmese,khmer}dict.txt`) and the model
  of the Japanese phrases (`icu4c/source/data/brkitr/adaboost/jaml.txt`), compiled by `SegmentationGenerator` for the line
  breaking (see [layout.md](layout.md#line-breaking)). The break engines that use them (`SoutheastAsianWordBreaker`,
  `JapanesePhraseBreaker`) are manual ports of `dictbe.cpp` and `mlbe.cpp` of the same release: a new release needs their
  changes to be ported
- `fonts`: fallback fonts of the WPT runner for the scripts that have no font in the web-platform-tests (Noto fonts
  of [google/fonts](https://github.com/google/fonts), SIL Open Font License 1.1; see
  [Fonts](web-platform-tests.md#fonts)). They are test data: pinned and fetched like a test suite (`tests/Meziantou.Framework.WebPlatform.External/fonts`),
  not committed and not distributed with the library
- `html5lib-tests`, `wpt`: pinned test suites. `wpt` is pinned to the commit of the latest
  [release](https://github.com/web-platform-tests/wpt/releases) (`"manifest": true`), which provides the manifest of the
  test suite (the list of the tests and their types, used by `wpt-coverage`)

## Tools

- `tools/Meziantou.Framework.WebPlatform.SpecUpdate`
  - `update [source...] [--except source...]`: resolves the latest revision of each source, downloads the data and
    rewrites `versions.json`
  - `fetch-tests`: materializes `tests/Meziantou.Framework.WebPlatform.External/` at the commits pinned in `versions.json` and the paths of
    `sources.json`, and downloads the manifest of the test suites that have one (`tests/Meziantou.Framework.WebPlatform.External/wpt/MANIFEST.json.gz`).
    Each test suite is a shallow, blob-less sparse clone (`tests/Meziantou.Framework.WebPlatform.External/<name>/.git`), so a change of the commit or of
    the paths only downloads the files that changed. The symbolic links of the test suites are not materialized. The
    references of the WPT reftests of the paths that are outside of them (read from the manifest, e.g. `css/reference`)
    are fetched too.
  - `verify`: checks that `tests/Meziantou.Framework.WebPlatform.SpecData/` matches the checksums in `versions.json`
  - `wpt-coverage`: generates [wpt-coverage.md](wpt-coverage.md) and the pages of its sections (`docs/wpt-coverage/`)
    from the WPT manifest and the expectations and reasons of `tests/Meziantou.Framework.WebPlatform.Wpt`. `--check`
    fails if the committed files are outdated or if a test that does not pass has no reason.
- `tools/Meziantou.Framework.WebPlatform.SpecGen`: regenerates every `*.g.cs` file from `tests/Meziantou.Framework.WebPlatform.SpecData/`. `--check` fails if the committed files are outdated.
  - `ElementInterfacesGenerator` generates the HTML, SVG and MathML element classes (from the webref IDL and element
    definitions), the element factories, the reflected IDL attributes (`[Reflect]`, `[ReflectURL]`...), the enumerated
    attributes (`EnumeratedAttributes.g.cs`), the event handler IDL attributes and the constants. The other members are
    hand-written; `[ReflectSetter]` getters are partial methods (`Compute*`) that must be implemented.
  - `WebIdlBindingsGenerator` generates the engine-agnostic WebIDL bindings of the DOM
    (`src/Meziantou.Framework.WebPlatform/Scripting/WebIdl/Generated/`) from the webref IDL and the `[IdlInterface]`/`[IdlMember]`
    annotations. It fails when a member of an in-scope specification has no .NET member and no exclusion in
    `tests/Meziantou.Framework.WebPlatform.Wpt/idl-exclusions.json`, or when no .NET overload matches an IDL overload.
  - `--dump-html` prints the data extracted from the HTML source (useful when the markup of the specification changes).
  - `CssPropertiesGenerator` generates the CSS property registry from the webref CSS definitions. When a property or a
    type is defined by several levels of a specification, the highest level is used (e.g. css-images-4 for
    `<linear-gradient-syntax>`), except for the definitions listed in the generator (early drafts, levels that turn a
    longhand into a shorthand). The types defined in the prose of a property (e.g. `<grid-line>`, `blur()`) are included.
  - `QuotesGenerator` lists the quotation marks of the locales of the CLDR that differ from the ones of the locale without
    its last subtag (`Css/Layout/BoxTree/LanguageQuotes.g.cs`), for `quotes: auto`.
  - `HyphenationGenerator` compiles the hyphenation patterns of each language into a packed trie, the embedded resources
    `Unicode/Hyphenation/*.hyph` (Brotli-compressed binary files, compared after decompression by `--check`), with the table of
    the dictionaries (`Hyphenator.Dictionaries.g.cs`) and the license notices of the sources (`LICENSES.txt`). See
    [layout.md](layout.md#hyphenation). To add a language, add its files to `sources.json` and to the `Dictionaries` of the
    generator after checking its license, then run `update hyphenation`.
  - `SegmentationGenerator` compiles the word lists of the `icu` source into the embedded resources
    `Unicode/Segmentation/*.dict` (a trie per script, compressed with Brotli and compared after decompression by `--check`),
    the model of the Japanese phrases into `JapanesePhraseBreaker.Model.g.cs`, and the license notices into `LICENSES.txt`.
  - `UserAgentStyleSheetGenerator` generates the user agent style sheets: HTML from the CSS blocks of the rendering
    section of the HTML source, MathML from the appendix of MathML Core, whose rules are included from the files of
    `user-agent-stylesheet`.

## Updating

```bash
./eng/webplatform/update-specs.sh                 # everything
./eng/webplatform/update-specs.sh encoding        # a single source
./eng/webplatform/update-specs.sh --except wpt    # everything but WPT
./eng/webplatform/update-wpt.sh                   # WPT only, with its expectations and coverage
```

The script updates the inputs, fetches the test suites, regenerates the code, runs the tests and regenerates
[wpt-coverage.md](wpt-coverage.md). Review the diff of `tests/Meziantou.Framework.WebPlatform.SpecData/` and of the generated files, fix the failing tests,
update the WPT expectations, add the reasons of the new failures to `wpt-reasons.json`
([Reasons](web-platform-tests.md#reasons)), regenerate `wpt-coverage.md`, then commit.

`eng/webplatform/update-wpt.sh` updates WPT to its latest release, rewrites the WPT expectations from the new results
(`WPT_UPDATE_EXPECTATIONS=1`) and regenerates the coverage, so the diff of `expectations/` lists the tests whose result
changed. Add the reasons of the new failures to `wpt-reasons.json`; the script fails while a test that does not pass has
no reason.

To add WPT tests, add their directory to the `paths` of `wpt` in `sources.json` and run `fetch-tests`: the pinned commit
does not change. The reftests of a fetched directory are run by `WptReftests` (see
[web-platform-tests.md](web-platform-tests.md#reftests)).

The `Update specifications` workflow runs weekly and opens two pull requests: `automation/update-specs`
(`eng/webplatform/update-specs.sh --except wpt`) and `automation/update-wpt` (`eng/webplatform/update-wpt.sh`). It needs the workflow to be
enabled, and "Allow GitHub Actions to create and approve pull requests" in the settings of the repository. The pull
requests created with `GITHUB_TOKEN` do not trigger the CI workflow: set the `SPEC_UPDATE_TOKEN` secret (a fine-grained
token with the contents and pull requests write permissions) to run it.

## Implementation notes

- Unicode data (IDNA mapping, NFC, Joining_Type, Vertical_Orientation, Default_Ignorable_Code_Point, Script and
  Script_Extensions, the Arabic joining types of `ArabicShaping.txt`, the bidi properties,
  see [layout.md](layout.md#bidirectional-text), and the Line_Break, East_Asian_Width, Grapheme_Cluster_Break,
  General_Category... of the text segmentation, see [layout.md](layout.md#text-segmentation)) is generated from the UCD
  instead of using `string.Normalize`, `IdnMapping` or `CharUnicodeInfo`, whose behavior depends on the OS, the ICU
  version, the runtime and the globalization mode.
- When a test suite lags behind the specification data (for instance, IDNA tests generated from an older Unicode
  version), the affected tests are skipped with an explicit reason instead of pinning older data.
