---
title: Limitations
description: What the font reader and the shaper do not do.
---

- **No text layout.** The package shapes runs of text; it does not split a text into runs, break lines, align
  paragraphs or order bidirectional text. [Meziantou.Framework.Imaging](../01-Imaging/08-Text/04-TextLayout.md) does, and
  [Meziantou.Framework.Unicode](../36-Unicode/01-Introduction.md) has the algorithms to write your own.
- **No rasterization.** Outlines are given as lines and curves; filling them with pixels is up to the renderer.
- **No decoding of bitmap glyphs.** The images of `sbix` and `CBDT` glyphs are given as encoded data.
- **Not supported**: glyphs in SVG (`SVG ` table), fonts without outlines (bitmap-only fonts), PostScript Type 1 fonts,
  the hints of CFF outlines, the variation of the TrueType hinting values (`cvar`), and the metadata and private blocks
  of WOFF and WOFF2 files.
- **AFM files**: the multiple master (AMFM) and composite (ACFM) metrics files are not supported.
- **Low-level API.** The public API is the one the Imaging, WebPlatform and Pdf libraries use; the packages that use it
  depend on its exact version.
