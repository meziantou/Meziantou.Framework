---
title: Introduction
description: A fully managed OpenType font reader and text shaper, the font engine of the Imaging, WebPlatform and Pdf libraries.
---

`Meziantou.Framework.Fonts` reads font files and turns text into glyphs. It is the font engine that
[Meziantou.Framework.Imaging](../Imaging/08-Text/01-Overview.md) uses to draw text, and that the WebPlatform and Pdf
libraries use to lay out and embed text. It is written entirely in C#, without native dependency, and is trimmable and
compatible with NativeAOT.

## Do you need this package?

To draw text on images, use [the text API of Meziantou.Framework.Imaging](../Imaging/08-Text/01-Overview.md): it loads
fonts, lays out paragraphs in any script and direction, and draws them. Use this package directly when you write your
own text renderer or document writer and need what is under that API:

- the names, metrics and tables of a font, and the outlines of its glyphs;
- the glyphs of a text and their positions, after the substitutions and the positioning of the font;
- the fonts installed on the machine;
- a subset of a font to embed in a document;
- the metrics of PostScript fonts from AFM files, such as the 14 standard fonts of PDF.

The public API is low level: it is the one these libraries need. The packages that use it depend on its exact version,
so update them together.

## What it supports

| | Supported |
| --- | --- |
| Containers | TrueType and OpenType fonts and collections, WOFF 1.0 and WOFF 2.0 |
| Outlines | TrueType, CFF and CFF2 |
| Variable fonts | The axes and named instances, and the variations of the outlines, the metrics and the layout tables |
| Color fonts | `COLR` versions 0 and 1, `CPAL`, and glyphs that are images (`sbix`, `CBDT`) |
| Metrics | Horizontal and vertical metrics, `BASE` baselines, the constants and glyph constructions of the `MATH` table |
| Hinting | Outlines fitted to the pixel grid of a size, with or without the TrueType instructions of the font |
| Shaping | The `GSUB` and `GPOS` tables, and the shaping models of complex scripts: Arabic, Hebrew, Indic, Khmer, Myanmar, Thai and Lao, Hangul, and the universal shaping engine |
| Installed fonts | Windows, macOS and Linux |
| Other | Subsets of TrueType and CFF outlines, AFM files |

The types are in the `Meziantou.Framework.Fonts` namespace.

## Principles

- **Fonts are untrusted input.** Every read is bounds-checked, and the work of loading a font, decoding a glyph and
  shaping a text is bounded by limits you choose: a hostile font cannot make the application run out of memory or time.
- **A face is immutable and thread-safe.** It keeps the bytes of its file and reads its tables in place. It has no
  cache and no state: the working storage of a glyph is yours.
- **Explicit errors.** A file that is not a font, a malformed font, an unsupported feature and an exceeded limit throw
  distinct exceptions.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and read a font.
