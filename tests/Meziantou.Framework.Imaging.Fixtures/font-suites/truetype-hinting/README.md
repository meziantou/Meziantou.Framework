# TrueType hinting suite

Three fonts hinted by hand with TrueType instructions, and the points FreeType gives for each of their glyphs once
hinted: the expectations of `HintingConformanceTests`.

| File | Font | Source (Apache License 2.0) |
| --- | --- | --- |
| `DroidSans.subset.ttf` | Droid Sans 1.001 | `apache/droidsans/DroidSans.ttf` of github.com/google/fonts at `bfd28d000f58ed495f01bf5a3c07197413ea84bf` |
| `OpenSans-Regular.subset.ttf` | Open Sans Regular 1.10 | `apache/opensans/OpenSans-Regular.ttf` of github.com/google/fonts at `092ac9e068723bb83f1e288075b7775c32ce0543` |
| `Roboto-Regular.subset.ttf` | Roboto Regular 2.137 | `apache/roboto/Roboto-Regular.ttf` of github.com/google/fonts at `7b64519fb0fd8b03b563b32ff55d2362db2c90bf` |

The fonts are Copyright Google Inc. and Christian Robertson (Roboto), licensed under the Apache License 2.0
(`LICENSES/Apache-2.0.txt`). They are modified: subset to Basic Latin (U+0020 to U+007E) by `hb-subset` of
HarfBuzz 14.6.0, which keeps the instructions.

`expected/<font>.txt` holds, for each size (9 to 16, 20 and 24 pixels per em) and each glyph, the advance and the
points (26.6 pixels, and whether each one is on the curve) that FreeType 2.13.2 gives with its version 40
interpreter for black and white pixels (`FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | FT_LOAD_TARGET_MONO`):

```
font <file> glyphs <count> upem <units per em>
size <pixels per em>
g <glyph> <advance> <point count> <index of the last point of each contour>
<x>,<y>,<on curve> ...
```

`tools/Meziantou.Framework.Imaging.CorpusGenerator/truetype_hinting.py` downloads the fonts, checks their SHA-256,
subsets them and writes the expectations.
