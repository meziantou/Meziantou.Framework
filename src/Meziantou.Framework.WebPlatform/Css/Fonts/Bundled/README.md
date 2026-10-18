# Bundled fonts

The fonts of this folder are embedded in the library (`BundledFonts`). The layout never uses the fonts installed on the
machine, so that its results are the same everywhere. `eng/webplatform/update-fonts.sh` downloads them from the pinned commits of
their official sources and subsets the fallbacks with [fontTools](https://github.com/fonttools/fonttools).

| File | Family | Source | License |
|---|---|---|---|
| `Ahem.ttf` | Ahem (the test font of the web-platform-tests: 1em square glyphs, ascent 0.8em, descent 0.2em) | [web-platform-tests/wpt `fonts/Ahem.ttf`](https://github.com/web-platform-tests/wpt/blob/1d99362b6bca46cc06e731ece8e022f0f1c21695/fonts/Ahem.ttf), unmodified | Public domain / CC0 ([`Ahem-COPYING.txt`](Ahem-COPYING.txt), from https://www.w3.org/Style/CSS/Test/Fonts/Ahem/COPYING) |
| `Tinos-*.woff2` | Tinos: `serif` (metric-compatible with Times New Roman), regular, bold, italic and bold italic | [google/fonts `ofl/tinos`](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/tinos) | SIL Open Font License 1.1 ([`Tinos-OFL.txt`](Tinos-OFL.txt), from [googlefonts/tinos](https://github.com/googlefonts/tinos/blob/3b4482a99b80ea5fc75f187b1be3120a3f5905b3/OFL.txt)) |
| `Arimo-VF.woff2`, `Arimo-Italic-VF.woff2` | Arimo: `sans-serif` (metric-compatible with Arial), variable fonts with a `wght` axis from 400 to 700 | [google/fonts `ofl/arimo`](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/arimo) | SIL Open Font License 1.1 ([`Arimo-OFL.txt`](Arimo-OFL.txt)) |
| `STIXTwoMath-Regular.woff2` | STIX Two Math: `math`, the font of the MathML elements (its OpenType MATH table gives the layout constants, the italic corrections and the stretchy glyphs) | [google/fonts `ofl/stixtwomath`](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/stixtwomath) | SIL Open Font License 1.1 ([`STIXTwoMath-OFL.txt`](STIXTwoMath-OFL.txt)) |
| `Cousine-*.woff2` | Cousine: `monospace` (metric-compatible with Courier New), regular, bold, italic and bold italic | [google/fonts `ofl/cousine`](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/cousine) | SIL Open Font License 1.1 ([`Cousine-OFL.txt`](Cousine-OFL.txt)) |

Tinos, Arimo and Cousine (the Chrome OS core fonts) were chosen because their metrics match the default fonts of the
browsers on Windows and macOS, so the text measured by the layout has the widths and the line heights that most web
pages and tests expect, and because their license (OFL 1.1 without Reserved Font Name) allows the redistribution of
modified versions. The fallbacks are subsetted to keep the package small (about 370 KB for the 10 files):

- the characters of Basic Latin, Latin-1 Supplement, Latin Extended-A, Greek and Coptic, Cyrillic, the general
  punctuation (spaces, dashes, quotes...), and a few symbols (euro sign, trade mark sign, arrows, minus sign, BOM,
  replacement character);
- the `kern` feature only (the shaper only applies kerning), without hinting (the rasterizer does not hint the glyphs);
- all the names (including the copyright and the license), in the WOFF2 format (which exercises the WOFF2 decoder).

The characters that are not in these subsets have no glyph in the bundled fonts (only a web font can display them).

STIX Two Math (a Times-like design, consistent with Tinos) is subsetted the same way with, in addition, the combining
marks, the letterlike symbols, the arrows, the mathematical operators, the miscellaneous technical symbols (the parts of
the stretchy brackets), the geometric shapes, the miscellaneous mathematical symbols, the supplemental arrows and
operators and the mathematical alphanumeric symbols (the italic identifiers of `text-transform: math-auto`). It keeps its
`MATH` table (the subsetter keeps the size variants and the assembly parts of the kept glyphs) and the features of the
math layout (`ssty`, `rtlm`, `dtls`, `flac`), about 260 KB.
