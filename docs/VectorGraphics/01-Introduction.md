---
title: Introduction
description: What the vector graphics libraries do, and how the charts and diagrams use them.
---

`Meziantou.Framework.VectorGraphics` describes a drawing as a [Scene](cref:Meziantou.Framework.VectorGraphics.Scene):
rectangles, circles, lines, paths, texts, images and groups, in pixels, with fills, strokes, linear and radial
gradients, opacity, clipping, masks, blend modes, blurs, drop shadows and transforms. It writes a scene as SVG, and measures texts so a layout fits the font that draws it.
`Meziantou.Framework.Imaging.VectorGraphics` draws the same scene as an image (PNG, WebP, JPEG...) or on the canvas of
[Meziantou.Framework.Imaging](../Imaging/01-Introduction.md). `Meziantou.Framework.Pdf.VectorGraphics` draws it as a
PDF document or on a page of [Meziantou.Framework.Pdf](../Pdf/01-Introduction.md).

The [charts](../Charts/01-Introduction.md) and the diagrams lay themselves out into a scene: they implement
[ISceneSource](cref:Meziantou.Framework.VectorGraphics.ISceneSource), so one renderer draws them all. Use the scene
directly to draw your own graphics, or to read the layout of a chart.

## What you can do with it

- **Build a scene** of shapes and texts, and write it as SVG: the output is deterministic, with a fixed number of
  decimals, numbered identifiers and one element per line, so you can compare drawings in snapshot tests. The SVG
  has a `role="img"`, a title, a description, tooltips, CSS classes, identifiers, `data-*` attributes and links. See
  [getting started](02-GettingStarted.md).
- **Draw more than shapes**: gradients and patterns, markers such as arrowheads, texts along a path, spaced and
  decorated texts, view boxes, and elements drawn again, which the SVG writes once.
- **Draw a scene as an image** or on an existing image, at any scale, with the embedded Liberation Sans fonts. See
  [image rendering](03-ImageRendering.md).
- **Draw a scene as PDF**, as vector shapes and text, with the fonts of the scene embedded. See
  [PDF rendering](06-PdfRendering.md).
- **Give the scene its fonts**, so each text is measured and drawn with the same font, with a family per text. See
  [fonts of a scene](05-Fonts.md).
- **Know what each renderer draws differently.** See the [limitations](07-Limitations.md).
- **Measure, wrap and truncate texts** with the metrics of Helvetica, which need no font file, or with any OpenType
  font. See [text measurement](04-TextMeasurement.md).

## Namespaces

The types are in the [Meziantou.Framework.VectorGraphics](cref:Meziantou.Framework.VectorGraphics),
[Meziantou.Framework.Imaging.VectorGraphics](cref:Meziantou.Framework.Imaging.VectorGraphics) and
[Meziantou.Framework.Pdf.VectorGraphics](cref:Meziantou.Framework.Pdf.VectorGraphics) namespaces.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, build a scene and write it as SVG.
