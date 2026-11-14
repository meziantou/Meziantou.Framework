---
title: Limitations
description: What the font reader and the shaper do not do.
---

- **No text layout.** The package shapes runs of text; it does not split a text into runs, break lines, align
  paragraphs or order bidirectional text. [Meziantou.Framework.Imaging](../Imaging/08-Text/04-TextLayout.md) does, and
  [Meziantou.Framework.Unicode](../Unicode/01-Introduction.md) has the algorithms to write your own.
- **No rasterization.** Outlines are given as lines and curves; filling them with pixels is up to the renderer.
- **No decoding of bitmap glyphs.** The images of `sbix` and `CBDT` glyphs are given as encoded data.
- **Not supported**: fonts without outlines (bitmap-only fonts), PostScript Type 1 fonts,
  the hints of CFF outlines, and the variation of the TrueType hinting values (`cvar`).
- **WOFF metadata**: the metadata block of a WOFF or WOFF2 file is given as an XML document; it is not parsed.
- **Apple Advanced Typography**: the `mort` table (the version before `morx`), the state tables of the legacy `kern`
  table (formats 1 to 3, pairs only are applied), the variations of `kerx`, its anchors that are control points of the
  outlines, the tracks other than the normal one, and the features of a range of a run (the `morx` features apply to
  the whole run) are not supported.
- **AFM files**: the multiple master (AMFM) and composite (ACFM) metrics files are not supported.
- **Low-level API.** The public API is the one the Imaging, WebPlatform and Pdf libraries use; update the packages that
  use it together.
