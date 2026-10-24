# Meziantou.Framework.Fonts

The OpenType font reader and the text shaper shared by `Meziantou.Framework.Imaging` (which draws text),
`Meziantou.Framework.WebPlatform` (which lays out HTML, SVG and MathML text) and `Meziantou.Framework.Pdf` (which
embeds fonts): TrueType, CFF and CFF2 outlines, WOFF and WOFF2, variable fonts, color fonts (COLR, CPAL, sbix, CBDT),
BASE baselines, the shaping of complex scripts, and the subsetting of TrueType and CFF outlines (`FontSubsetter`).

These instructions complete the repository-level `AGENTS.md`.

## Hard constraints

- **The public API is what the packages that use the code need.** A type is `public` when `Meziantou.Framework.Imaging`,
  `Meziantou.Framework.WebPlatform`, `Meziantou.Framework.WebPlatform.Imaging` or `Meziantou.Framework.Pdf` uses it
  (`FontSubsetter` for the font subsets of PDF, `CffFont` and `TrueTypeGlyphOutlines` for the font programs its renderer
  draws), and everything else stays `internal` (the tests, the fuzzing and the benchmarks see it through
  `InternalsVisibleTo`). The packages that use internal types depend on the exact version of this package
  (`UseExactProjectReferenceVersions` in their project file) and are released together with it;
  `Meziantou.Framework.Pdf` uses the public API only and has a normal dependency, so do not give it access to the
  internals again. Every public member is documented (`DisableDocumentationWarnings` is
  `false`), and the public API is recorded in `ref/` by the build. The one exception is the AFM reader
  (`AfmFontMetrics` and the `Afm*` types it returns): it is a standalone reader of font metrics, public so that
  applications (PDF writers, text measurement) can read AFM files; `Meziantou.Framework.Pdf` reads the metrics of the
  standard fonts with `AfmFontMetrics.GetStandardFont`.
- `Adler32`, `DeterministicAngle` (`Infrastructure/`) and `FontTags` are compiled into `Meziantou.Framework.Imaging`
  too, in its namespace (`MEZIANTOU_FRAMEWORK_IMAGING`): they are helpers, not font API, and stay internal to both
  assemblies. WebPlatform has its own tag constants (`OpenTypeTag`).
- **The code depends on nothing of the packages that use it.** Its only dependency is `Meziantou.Framework.Unicode`.
  What it needs from its caller is a `FontEnvironment`: the `FontResourceLimits`, and a `FontAllocator` for the
  temporary buffers (the allocation scopes of Imaging). It throws its own exceptions
  (`InvalidFontException`, `UnsupportedFontFeatureException`, `UnknownFontFormatException`,
  `FontResourceLimitException`, raised by `FontErrors`); each caller converts them at its public boundary (Imaging:
  `FontExceptionTranslation`). Decoding the image of a bitmap glyph needs image codecs: the reader gives the bytes and a
  `BitmapGlyphFormat`, the caller decodes them.
- **The callers.** Imaging wraps a face in its public `Font` (see its AGENTS.md). WebPlatform reads fonts through
  `Css/Fonts/FontFile.cs` and `OpenTypeFont.cs`, which add the rules of CSS (the metrics of the browsers, the family names
  of every language, the style) and catch the exceptions of the reader: a malformed glyph gives no value there. WebPlatform
  and its test and fuzz projects set `DisableTransitiveProjectReferences`: the types of `Meziantou.Framework.Unicode`
  (`UnicodeScript`, `UnicodeProperties`, `UnicodeBidi`, `Hyphenator`, in the `Meziantou.Framework` namespace) would hide
  the types of the same names of WebPlatform. A change of behavior of the reader is checked with the tests of both
  libraries, and with the web-platform-tests of WebPlatform (`css/WOFF2`, `css/css-fonts`).
- Write the code from the specifications (OpenType, WOFF 1.0 and 2.0, Adobe Technical Notes #5176 and #5177, Adobe
  Technical Note #5004 for AFM files, the script development specifications of Microsoft, Unicode). Never copy or port code from font libraries (FreeType,
  HarfBuzz, the woff2 reference code, fontTools, ...): they are test oracles only, their source is never read to write
  ours.
- Fully managed, trimmable and NativeAOT-compatible: no reflection.
- The tests are in `tests/Meziantou.Framework.Imaging.Tests` (`FontTests`, `FontVariationTests`, `ColorFontTests`,
  `TextShaperTests`, `Conformance/`), the fuzzing in `fuzz/Meziantou.Framework.Imaging.FuzzTests` (`FontFuzzTests`), and
  the corpus and the generated tables in `tools/Meziantou.Framework.Imaging.CorpusGenerator`.

## Font reader

`FontLoader` reads the file
and expands the containers (`WoffReader`, `Woff2Reader` and `Woff2GlyfTransform`), `FontFace` validates the table
directory and the fixed-size tables, `CmapTable` maps characters, `GlyfOutlineReader` and `CffTable` with
`CffCharstringInterpreter` decode outlines into an `OutlineSink`.

- Written from the OpenType specification, the WOFF 1.0 and 2.0 recommendations and Adobe Technical Notes #5176 (CFF)
  and #5177 (Type 2 charstrings); cite the section in the code.
- **A font is immutable, thread-safe and not disposable.** It keeps one exact garbage-collected `byte[]` (`FontData`,
  like `MetadataBlob`), shared by the faces of a collection, bounded by `MaxFontBytes` before it is allocated; tables are
  read in place, never copied. It has no cache and no state: the working storage of a glyph is a caller-owned
  `GlyphScratch` charged to a `FontAllocator` (an allocation scope in the library), and a `GlyphBudget` per glyph.
- **Untrusted input.** Every read goes through `FontBinary` (bounds-checked, `InvalidFontException`), table sizes
  use 64-bit arithmetic, and nothing is sized from a declared length before it is checked against a limit. Anything that
  can expand (composite glyphs, subroutines, later paint graphs and lookups) charges `GlyphBudget` per elementary
  operation: recursion depth alone does not bound the work. Outline coordinates are bounded (`FontErrors.MaxCoordinate`)
  so that no value that is not a number leaves a reader. Only the documented exceptions may escape (`FontFuzzTests`).
- **Validate eagerly what every use needs** (directory, required tables, headers, the structure of `cmap` and of the CFF
  INDEX and DICT structures) and lazily what one glyph needs (its data, its charstring).
- A compressed container is expanded to an sfnt in memory, then read as one. The temporary storage of the expansion is
  rented from an allocation scope; the font itself is not (it outlives every scope). `ZLibStream` and `BrotliDecoder`
  are given known-size destinations and must fill them exactly; a decompressor error of any type is malformed content.
- **Outlines are what the reference readers emit.** A TrueType contour is always closed by a line to its first point and
  a contour of one point is kept; a CFF contour is closed by a line only when its last point is another one, and a
  contour that draws nothing is not emitted. A TrueType outline is moved so that its origin (the left of its bounding
  box minus its left side bearing, or the origin of the component whose metrics a composite glyph uses) is at zero. The
  recorded outlines of the default instances compare as strings: keep that exact.
- **Variable fonts.** `FontFace.WithCoordinates` gives the face of an instance: a shallow copy of the default face with
  a `FontInstance` (the values of the axes, their normalized coordinates, and the scalar of each region of each item
  variation store, computed once) and the metrics `MVAR` varies. Nothing else is cached; `Font.WithVariations` is the
  public form.
  - Normalization follows "Coordinate scales and normalization" in its order: normalize as 16.16, map with `avar` at
    that precision, then round to 2.14 (add 2, drop two bits). Rounding before `avar`, or keeping 16.16 coordinates,
    both differ from the reference readers by a visible fraction of a unit. Version 2 of `avar` then adds the deltas
    of its variation store to the 2.14 values (`AvarTable.AddDeltas`): all the deltas are computed from the values
    before any is changed, rounded half away from zero, and the result is clamped to the range of an axis.
  - Outlines are not rounded (`gvar` deltas, inferred deltas, the offsets of components, `blend` in CFF2); what is a
    whole number of font units in the font stays one in an instance: advances (`HVAR`, else the phantom points of
    `gvar`), `MVAR` metrics and `GPOS` deltas are rounded with `ItemVariationStore.Round` (halves upward).
  - `GvarTable.AddDeltas` reads the packed points and deltas as streams, without storing them, into sums that live in
    the work stack of `GlyphScratch` (`RentWork`; spans of it do not survive another rental). Tuple variations charge
    `GlyphBudget` per tuple and per point.
  - The charstrings of `CFF2` are run by the same interpreter as CFF (`blend`, `vsindex`, no width); the tag of the
    table says which format it is, never its version byte.
  - The shaper applies `rvrn` before every other feature and resolves feature variations for every instance,
    including the default one (a condition can contain it).
  - The instance values of the corpus are the outlines HarfBuzz gives to three decimals (`--precision=3`), once
    FreeType has drawn the same segments within two units; the tests accept 0.005 unit, the precision of the single
    precision numbers of HarfBuzz. Advances and metrics are exact. `FontVariationTests` has the literals, computed by
    hand from `FontSamples.Variable` and `VariableCharstrings`.
- **Mathematical layout** (`MathTable`): the constants, the italics corrections, the top accent attachments, the
  extended shapes and the constructions (variants and assemblies) of the `MATH` table. A value whose device table is a
  variation index varies with the instance (the store of `GDEF`, as `GPOS`); the device tables of one size and
  `MathKernInfo` are not read. The header and the constants are validated when the face is created, the subtables of
  a glyph when it is looked up.
- **Bitmap strikes.** The images of `sbix` (PNG, JPEG, TIFF) and of `CBDT` (PNG) are color glyphs: `BitmapGlyph` says
  which format the font gives for the data, and `BitmapGlyphDecoder` rejects data of another one. The bitmaps without colors
  of `EBLC` and `EBDT` are outlines: with `FontLoadOptions.UseEmbeddedBitmaps`, `FontFace.DecodeOutline(glyph, size,
  ...)` emits a rectangle per run of pixels of a strike of one bit per pixel made for exactly that size, so nothing of
  the drawing pipeline knows about them. Every caller that decodes an outline for a text passes the size; a cache of
  what depends on the outline of a glyph has the size in its key. The option is off by default: the pixels of a text
  do not change with the tables a font happens to have.
- Tests have three sources: literals computed by hand on fonts built in memory (`FontTests`, with `Common/FontBuilder.cs`,
  `FontCffWriter.cs`, `FontCharstring.cs` and `FontWoff2Writer.cs` of the corpus generator, linked into the tests), the
  values HarfBuzz and FreeType both give for the fonts of the corpus (`Conformance/FontConformanceTests`: names,
  metrics, the whole character map, every advance and outline; a container must give the tables of its original font),
  and mutation fuzzing (`FontFuzzTests`). The test-side writers are not oracles: a file they write is a fixture only
  after the corpus generator has read it back with the reference decoders.

## AFM reader

`AfmFontMetrics` (`Afm/`) reads AFM files (Adobe Font Metrics, Adobe Technical Note #5004, version 4.1); `AfmParser`
reads the text, `AfmFontMetrics` indexes the characters and the kerning pairs.

- Written from TN #5004; cite its section in the code. fontTools (`afmLib`) is an oracle only: the widths, boxes,
  codes and kerning pairs of the Core 14 files compare equal with it.
- **Text.** The file is ASCII; the bytes are read as ISO-8859-1 because the notices of some files have such
  characters. A line ends with CR, LF or CRLF; spaces and tabs separate the values. What follows `EndFontMetrics` is
  ignored.
- **Lenient where files are, strict on values.** The keys the reader does not know are ignored (section 3 requires
  it), and so are the lines of a section that have none of its keys. The counts after the `Start...` keys are not
  checked and nothing is sized from them. A key it knows with a malformed value, a delimiter outside its section, and
  a missing `EndFontMetrics` are an `InvalidFontException` with the line (`FontErrors.InvalidAfm`). Numbers are
  bounded by `FontErrors.MaxCoordinate`; no value that is not a number leaves the reader.
- A name or a code that two characters have is the one of the first character, and the first of two identical
  kerning pairs applies. `KPH` pairs give codes: the reader fills the names from the characters, and the codes of the
  other pairs.
- AMFM (`StartMasterFontMetrics`) and ACFM (`StartCompFontMetrics`) files are `UnsupportedFontFeatureException`.
  `FontLoader` recognizes the three signatures and says to use `AfmFontMetrics`.
- **Standard fonts.** `Afm/StandardFonts` holds the 14 AFM files of `Core14_AFMs.zip` as Adobe published them (CRLF line
  endings, see `THIRD-PARTY-NOTICES.md`) and `MustRead.html`, which their license requires next to them: both are
  embedded resources (`Meziantou.Framework.Fonts.Afm.<file>`). It is the only copy of these files in the repository;
  never modify them. `GetStandardFont` reads a file once and shares the metrics; the tests read the raw files with the
  internal `OpenStandardFontFile`.
- Tests: `FontTests` (literals of the specification, and the embedded standard fonts), and
  `FontFuzzTests.MutatedAfmFilesFailOnlyWithDocumentedErrors` (mutations of bytes and of lines of the standard fonts).

## System fonts

`SystemFonts/` indexes the fonts installed on the machine: `SystemFontCollection` (`Default`, scanned once per process,
or `Create` for chosen directories) holds a `SystemFontDescriptor` per face (file, index in the file, names, style,
axes). Imaging users load the face with `FontCollection.Load(FilePath)[FaceIndex]`; WebPlatform wraps the collection
in `InstalledFontCollection.FromSystemFonts` when `BrowsingContextOptions.UseSystemFonts` is set.

- **Where the fonts are** (`SystemFontDirectories`). Windows: `SpecialFolder.Fonts`, `%LOCALAPPDATA%\Microsoft\Windows\Fonts`,
  and the full paths of the values of `HKLM` and `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`. macOS: the
  `Library/Fonts` directories of the user, the computer, the network and the system, and the
  `com_apple_MobileAsset_Font*` directories of `/System/Library/AssetsV2` (downloaded fonts). Other systems: the `dir`
  elements of the configuration of fontconfig (`FontConfigFile`: `fonts.conf`, its `include`s, `reset-dirs`; the other
  elements are ignored, nothing is run), and the XDG font directories. The fonts reserved by macOS for its interface
  (`PingFangUI.ttc`) are not scanned: their outlines are in a format the reader does not support.
- **A scan reads no font.** `SystemFontScanner` reads the table directories and the `head`, `OS/2`, `post`, `name` and
  `fvar` tables with `RandomAccess` (a few kilobytes per file, 786 faces in about 30 ms on macOS). Only a WOFF or WOFF2
  file is loaded whole. The names and the style come from `FaceStyle`, which `FontFace` uses too: the index and a loaded
  face always agree (`FontTests.SystemFontsAreIndexedWithoutLoadingThem` compares them).
- **A file the font code would not load is left out whole**: an invalid face, a face without outlines nor color glyphs (the
  rule of WebPlatform's `FontFile`), a file that cannot be read. Errors never escape a scan.
- `Match` is the font matching of CSS Fonts 4 §5.2 (width, then style, then weight) on the descriptors; WebPlatform has
  its own (`FontMatcher`), fed with `FontSelectionCapabilities.FromSystemFont`.
- The paths are compared with the rules of the file system (ignoring case on Windows and macOS), the names with
  `OrdinalIgnoreCase`; the order of `Fonts` (family, path, index) does not depend on the order of the file system.

## Shaping

`TextShaper` (`Shaping/`) turns the characters of a run (one font, script, language and direction) into
glyphs and positions in a `GlyphBuffer`; `LayoutApplier` (`OpenType/`) applies the lookups of `GSUB` and
`GPOS`, with `GdefTable`, `LayoutTable` and `LayoutCommon` for the tables they share.

- Written from the OpenType specification (GSUB, GPOS, GDEF and the common table formats) and, for what a script
  needs, from the script development specifications of Microsoft. HarfBuzz is the oracle and is never the source: where
  the specification leaves a behavior to the client (what a lookup sees through, how marks follow a ligature, the
  default features), write the rule in the terms of the specification and of Unicode, and let the corpus decide.
- **Everything is an integer in font units** until `TextLayout` scales it. Positions are an advance and an offset per
  glyph; an attached glyph records what it is attached to, and `FinishPositions` resolves the chains once.
- **Clusters are the contract with the rest of the layout**: every glyph has the index of the first character of its
  cluster, a cluster never splits what shaping tied together (a ligature, a character and its marks), and clusters never
  decrease along a run. Merge clusters whenever glyphs are merged, reordered or removed.
- **Bounds.** The buffer refuses to grow beyond `MaxTextGlyphs`. A pass of a lookup over the glyphs is what shaping
  is: it is not charged, and the subtables of the lookups of one run are limited in number instead (a font can have
  several thousand, tried at each glyph; it cannot have millions by pointing every offset at one table). What a rule
  adds to that, the lookups it applies, is charged to the operation budget of the buffer, which is in proportion
  with the text. Nested lookups are limited in depth; sequences are limited in length. A layout table is read
  through `FontBinary` like any other: a reference outside the table is `InvalidFontException`, raised when the
  layout is created.
- A feature value lives in bits of the mask of each glyph, so a feature can differ along a run without splitting it.
- **Languages** (`OpenTypeLanguage`). `TextStyle.Language` is a BCP 47 language tag; the shaper takes the tags of the
  language systems it can use, by order of preference, and uses the first one the font has for the script. The table
  (`OpenTypeLanguageTable.g.cs`) is generated by the `language-tags` command of the corpus generator from three
  sources that are not committed and are pinned by a hash of what is read from them: the registry of language system
  tags of the OpenType specification, the code list of ISO 639-2 and the language subtag registry of BCP 47 (see the
  remarks of `LanguageTagTable` in the generator). Never edit it by hand. What the codes alone do not say is a rule
  of `OpenTypeLanguage.cs`, with its reason from the registry (written forms, Chinese, the tags without code): add
  one there, never to the generated table. The font `fonts/synthetic-languages` has a language system per tag of the
  registry, and the corpus has what the reference shaper selects for some 1,500 language tags: a difference is an
  entry of `KnownDivergences`, by group, with the reason.
- **Shaping models** (`ScriptShaperKind`, `ShaperPlan`). A script that needs more than the lookups of the font has a
  model: an analysis of the characters that chooses the glyphs each of its features applies to (one mask bit per
  feature, after the bits of the default features), and its features in **stages**. The lookups of a stage are
  applied together in the order of the lookup list, and a stage sees the result of the stages before it; a feature
  belongs to the first stage that names it; the features every script has and those of the caller are the last
  stage. Stage 0 is `rvrn`, stage 1 the direction and fraction features. A feature the language system requires is
  applied in the stage of its tag, or in stage 0.
  - `ArabicShaper` (Arabic and Syriac): forms from the Joining_Type and Joining_Group properties, the stages of the
    Microsoft specifications, the order of marks of UTR #53, and the stretching glyphs of `stch`. A font with `rclt`
    gets `calt` in the last stage. The other joining scripts (N'Ko, Mongolian, Adlam, ...) belong to the universal
    model, not to this one.
  - `HebrewShaper`: the precomposed letters for a font that does not place points, and the meteg between the points
    of a vowel.
  - `ThaiShaper` (Thai and Lao): the sara am, decomposed before normalization, its ring moved before the marks above
    the consonant that precede it in the text (not the ones normalization would put there later).
  - `HangulShaper`: composition and decomposition of syllable blocks from what the font has, a feature per position
    for the blocks drawn with letters, tone marks. It does the work of normalization for its letters, which
    normalization then leaves alone. The dotted circle of a tone mark keeps the class of the mark, so that the marks
    around them are ordered as if it were not there.
  - Tibetan has no model of its own: Windows and the reference shaper both shape it with the universal model.
  - `IndicShaper` (nine scripts; Sinhala belongs to the universal model): the steps of the Microsoft
    specifications, with their names in the comments. `IndicCategories` derives what a character is from
    Indic_Syllabic_Category and Indic_Positional_Category, with a short list of exceptions, each one found by
    comparing with the reference shaper; `SyllablePattern` compiles the syllable grammar, written as text, into an
    automaton (reuse it for the other syllabic models). The model has an instance per run (`IndicShaper` object):
    what the font does with a consonant and a halant is asked with `LayoutApplier.WouldSubstitute` on a scratch
    buffer and remembered for the run. The reordering runs between stages (`InitialReorderingStage`,
    `FinalReorderingStage`), whether the font has lookups in them or not. The features of the model work inside a
    syllable (`ShaperFeatureFlags.PerSyllable`, `GlyphInfo.Syllable`) and a rule only replaces glyphs that have the
    mask of its feature. Marks keep their advance in these scripts and in Hangul.
  - A font whose substitution table has the script only as `DFLT` or `latn`, or under a tag that ends with 3, is not
    shaped with the Indic model (`GetShaper`).
  - `KhmerShaper` and `MyanmarShaper` are the same kind of model with their own categories and grammar, and no
    question asked to the font: the base is the first consonant. Khmer reorders before any feature and applies the
    forms of its subscripts in one stage, to what follows the first character of each syllable; the vowels with two
    parts are decomposed by the model (the pre-base vowel, then the vowel itself), not by Unicode. Myanmar reorders
    after `locl` and `ccmp` by giving each character a place and sorting, and zeroes the advance of its marks before
    positioning. A Myanmar font with the tag `mymr` only is shaped with the default model, as the reference shaper
    does. These syllabic models decompose every character before shaping and do not compose onto marks.
  - `UniversalShaper`: the model of the scripts registered for the Universal Shaping Engine, and of an Indic font
    whose script tag ends with 3. The class of a character is derived at run time from Indic_Syllabic_Category,
    Indic_Positional_Category, General_Category and Joining_Type, with the values of the two data files of the
    specification transcribed in `GetSyllabicCategory` and `GetPositionalCategory` (update them when the files are
    updated for a new version of Unicode) and a few classes of its own (the al-lakuna, the sakot). The cluster
    schemas are `SyllablePattern`s; the joiners and the variation selectors are taken out before matching. The
    glyphs `rphf` and `pref` make are found with `GlyphInfo.Substituted`, which the model clears between its stages
    (an ignorable glyph a feature replaced is ignorable again after that, as in the reference shaper). The forms
    of the joining scripts come from `ArabicShaper.SetFormMasks`. Two things are the reference shaper's and not the
    specification's: a symbol cluster (a space, a digit) joins its neighbors for the topographical features of a
    script that does not join, which the specification leaves unspecified; a code point that is no character is a
    symbol. One thing is the specification's and not the reference shaper's: U+2015, U+2022 and U+25FB to U+25FE
    are generic bases (`KnownDivergences` in the conformance tests).
  - `GetShaper`: a model is not used for a font that has features for a default script only (`DFLT`, `dflt`,
    `latn`); a font without the script and without default script, or without substitution table, gets the model.
  - `ShapingCombiningClass` orders marks for shaping (Hebrew points, Arabic vowel marks, the vowel signs of
    Tibetan, and three marks by their code point); every script goes through it, and composition is blocked by
    these classes, not by the canonical ones.
- **Variation sequences.** A selector after a character whose sequence the font has (format 14 of `cmap`, with a
  glyph of its own or with the default one) leaves the buffer in `MapGlyphs`: emoji fonts list every emoji with
  U+FE0F, and a selector that stayed would be a glyph the reference shaper does not have.
- **Vertical shaping** (`isVertical`) is what the reference shaper does for the direction `ttb`, and is checked
  against it by the vectors that have a `direction` in the corpus. The initial positions are an advance of minus the
  advance height and an offset of minus the vertical origin (`FontFace.GetAdvanceHeight`, `GetVerticalOrigin`); the
  engine turns them into an advance down the line. Rules no specification states, found by comparing: the origin is
  half of the advance width, and `VORG`, or the top of the glyph plus the top side bearing of `vmtx`, or the top of
  the glyph plus half of what the ascent and the descent have more than its height (an arithmetic shift, so it
  rounds down); a glyph without outline is an empty box at the origin; a font without `vmtx` advances by the ascent
  plus the descent of the horizontal line. Only `vert` is added to the features that do not depend on the direction,
  and it is looked for in every script of the table; the joining of Arabic and Syriac, the `kern` table and the
  marks placed from the shapes of the glyphs are for horizontal text only. In `GPOS`, the advance of a value record
  is `YAdvance`, subtracted.
- **Fonts without layout tables.** Three fallbacks, each for a font that has no table (or no script) for what it
  replaces, each compared with the reference shaper on synthetic fonts of the corpus and on random texts:
  - `ArabicShaper.ApplyPresentationForms`: the forms and the lam-alef ligatures from the characters of the Arabic
    Presentation Forms blocks. The table (`ArabicPresentationForms.g.cs`) is generated from `UnicodeData.txt` by the
    `arabic-forms` command of the corpus generator; never edit it by hand.
  - `ThaiShaper.RecordPrivateUseForms` and `ApplyPrivateUseForms`: the forms of the private use area, from the
    public description of Thai shaping by Theppitak Karoonboonyanan; what it leaves open (several marks at one
    place, marks on what is no consonant) is what the reference shaper does. The forms are found in the order of the
    text, before normalization reorders the marks.
  - `FallbackMarkPositioner`: the marks of a font without `GPOS`. No specification describes it: the places come from
    the combining classes of Unicode, and the distances and the two rules about marks a font already draws far
    enough were found by comparing with the reference shaper. Say so in the code when a rule is added.
- **Normalization** follows the text: a character without marks is kept when the font has it and decomposed (as far
  as needed) when it does not; a character followed by marks is decomposed as far as the font allows, with its marks,
  then ordered and composed again. A run without any mark is neither ordered nor composed.
- **What a lookup sees through** (`LayoutApplier.IsTransparent`): characters without visible form, with three
  exceptions. The zero width non-joiner separates the glyphs a substitution replaces (the components of a ligature,
  the input of a contextual rule) and is seen through around them and by positioning. The zero width joiner is seen
  through, except by the features of a shaping model that say otherwise and by `mark` and `mkmk`: a joiner between
  a letter and a mark keeps the mark off the letter (`abvm` and `blwm` see through it). A combining
  grapheme joiner that kept two marks from being reordered separates them for substitutions. The glyphs of these
  characters are the ones of the font until the positions are made, and are hidden afterward. The free variation
  selectors of Mongolian and the tag characters are `Hidden` from the start: substitutions see them. The fillers of
  Hangul and the format controls of the shorthands (U+1BCA0 to U+1BCA3) are not ignorable at all.
- **Ligature bookkeeping** (`GlyphInfo.Props`, `LigatureId`, `LigatureComponent`): a real ligature has
  `LigatureWithComponents` (a base with its marks, or marks together, are not one); becoming a ligature clears
  `Multiplied`; the glyphs that replace a glyph tied to a ligature keep its identifier instead of being numbered.
  Mark attachment depends on all three: a glyph of a multiplied sequence carries marks when it is the first one, when
  a mark precedes it, when the sequence lost the glyph before it, or when the lookup has it as a base.
- **Where a mark is placed.** A mark is placed vertically when it is attached, from where its target is at that
  point of the lookups, the glyphs a cursive attachment connects the target to included
  (`GetVerticalOffset`); horizontally it follows its target to the end. A later lookup that moves the target
  vertically (another mark lookup, a cursive attachment made afterward) does not move the mark: the reference
  shaper does the same, and the fonts are made with it.
- **Cursive chains.** A glyph follows one other glyph. When a lookup connects a glyph that already followed
  another one, the links of its old chain are reversed (`ReverseCursiveChain`), so that what was connected stays
  connected; two glyphs that would follow each other are separated.
- In a mark-to-ligature lookup, the glyphs a substitution put after another one are passed over when the ligature
  is looked for.
- A mark is attached to the first of the glyphs one glyph was replaced by, unless a later one is a base of the mark
  in the lookup (nastaliq fonts insert a spacing glyph before a letter).
- Tests have three sources: the Annotated OpenType Specification tests of Adobe (`Conformance/ShapingSuiteTests`, the
  `font-suites/aots` directory of the corpus: every format of every lookup, with the results the specification
  gives), the glyphs and positions `hb-shape` gives for texts set in the fonts of the corpus
  (`Conformance/ShapingConformanceTests`: glyphs, clusters, the place each glyph is drawn at, the advance of the text),
  and literals computed by hand (`TextShaperTests`). A deliberate difference with a reference is an entry of the
  `KnownDivergences` map of the test, with its reason; an entry that no longer differs fails the test.
- **Compare random texts before trusting a shaping model.** The corpus has the cases someone thought of. Shaping
  thousands of random sequences of the letters, marks, joiners and punctuation of a script with several fonts, with
  the library and with `hb-shape --shapers=ot` (same script and direction), found what the written cases did not:
  when a character is decomposed, when a joiner blocks a mark, which glyph of a multiplied sequence is a base. Add
  the text that differs to the cases of `FontCorpus.cs` once it is understood. For a new script, build the texts
  from the characters the font has: each one alone, around a letter, after a halant, a joiner and a space, the
  pairs of marks, then random sequences; and give them to `hb-shape` in one call (`--text-file`).
- The reference shaper numbers the syllables of a run from 1 to 15 and starts again; it shows no dotted circle for a
  syllable without base that has the number of the last one it showed a circle for. The library shows it. Keep the
  lines of the corpus that have such syllables short, so that the two agree, rather than listing a divergence.
