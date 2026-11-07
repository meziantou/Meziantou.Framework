---
title: Text
description: Load fonts, lay out text and draw it on images, in any script and direction, the same way on every platform.
---

The library draws text with fonts it reads itself: TrueType, OpenType, WOFF and WOFF2 files. It lays out the text
(shaping, line breaking, bidirectional ordering, vertical lines) and fills the outlines of the glyphs like any other
shape of the [drawing API](../07-Drawing/01-Overview.md). Nothing comes from the operating system unless you ask for an
installed font, so a text gives the same pixels on every machine.

The text types are in the [Meziantou.Framework.Imaging.Typography](cref:Meziantou.Framework.Imaging.Typography) namespace:

| Type | What it is |
| --- | --- |
| [Font](cref:Meziantou.Framework.Imaging.Typography.Font) | One face of a font file, immutable and thread-safe |
| [FontStack](cref:Meziantou.Framework.Imaging.Typography.FontStack) | A font and the fonts used for the characters it does not have |
| [TextStyle](cref:Meziantou.Framework.Imaging.Typography.TextStyle) | How a text is set: its fonts, its size, its paint, its features and decorations |
| [RichText](cref:Meziantou.Framework.Imaging.Typography.RichText) | A text whose parts have their own style, built with [RichTextBuilder](cref:Meziantou.Framework.Imaging.Typography.RichTextBuilder) |
| [TextLayoutOptions](cref:Meziantou.Framework.Imaging.Typography.TextLayoutOptions) | How lines are wrapped, aligned, cut and directed |
| [TextLayout](cref:Meziantou.Framework.Imaging.Typography.TextLayout) | A text laid out: what is measured is what [FillText](cref:Meziantou.Framework.Imaging.Drawing.DrawingCanvas.FillText) and [StrokeText](cref:Meziantou.Framework.Imaging.Drawing.DrawingCanvas.StrokeText) draw |

The usual flow is: load a font once, create a [TextLayout](cref:Meziantou.Framework.Imaging.Typography.TextLayout), measure it if you need to place it, then draw it with
`canvas.FillText` inside `image.Draw`.

## Pages of this section

- [Fonts](02-Fonts.md): load font files and installed fonts, variable fonts and their instances, color glyphs, and the
  limits that protect you from hostile fonts.
- [Drawing text](03-DrawingText.md): draw a string, place it by a point of its box, wrap paragraphs, give parts of the
  text their own style, and fit a text in a box.
- [Text layout](04-TextLayout.md): fallback fonts, right-to-left and vertical text, alignment and hyphenation,
  synthetic bold and oblique, the language of a text and how it is shaped.
- [Text on paths and measuring](05-TextOnPathsAndMeasuring.md): make a text follow a curve, measure it, find the
  character under a point, draw carets and selections, draw your own glyph runs, and hint small text.
