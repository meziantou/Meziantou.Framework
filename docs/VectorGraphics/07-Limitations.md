---
title: Limitations
description: What each renderer of the scenes draws differently, or does not draw.
---

The SVG, image and PDF renderers draw the same scene the same way, except in the cases below.

## PDF

- **Filters are not supported.** PDF has no blur and no drop shadow that it draws as vectors:
  [ToPdf](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.ToPdf) and
  [DrawScene](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.DrawScene) throw
  `NotSupportedException` for a scene that has a [Filter](cref:Meziantou.Framework.VectorGraphics.SceneElement.Filter),
  in an element or in the content of a mask, before they draw anything. Remove the filters, or draw the scene as an
  [image](03-ImageRendering.md).
- **Gradients.** The opacity of a gradient is the average opacity of its stops: a gradient from opaque to transparent
  is drawn half transparent. A repeated or reflected gradient is drawn with its stops repeated over the periods the
  shape covers, at most 256: beyond them, its end colors extend. A radial gradient whose focal point is not inside its
  circle is padded instead of repeated.
- **The opacity and the mask of a shape** apply to its fill and to its stroke separately: where they overlap, the shape
  is a little more opaque than in SVG. A group is drawn as a whole.
- **Fonts.** Without the fonts of the scene, the texts are drawn with the standard Helvetica fonts, which are not
  embedded and only have the characters of Western European languages: the other characters are drawn as question
  marks. A face that cannot be embedded (a license that forbids it, CFF2 or bitmap outlines) is replaced by Helvetica.
- The tooltips, the CSS classes and the data attributes are not drawn; the hyperlinks of the groups are link
  annotations of the page, for absolute addresses only.

## Images

- The hyperlinks, the tooltips, the CSS classes and the data attributes are not drawn.
- Without the fonts of the scene, the texts are drawn with the embedded Liberation Sans, subset to the Latin characters.

## SVG

- The SVG is drawn by the viewer, with its fonts: without embedded fonts
  ([EmbedFonts](cref:Meziantou.Framework.VectorGraphics.SvgRenderOptions.EmbedFonts)), a viewer that does not have
  the fonts of the scene draws the texts with others, which can be wider or narrower than the layout measured them.
  The faces of font collections (`.ttc`) are not embedded.
- The region of the filter of a text, or of a group that has a text, is the bounding box of the element enlarged by
  half its size on each side, because the size of a text is not known without measuring it: a large blur or a large
  shadow offset can be cut.
- The alpha masks (`mask-type`) and the blend modes (`mix-blend-mode`) are features of SVG 2 and CSS: the current
  browsers draw them, some older viewers and editors do not.

## All renderers

- An image that the image or PDF renderer cannot decode is not drawn; the SVG embeds its bytes as they are.
- A value that is not valid, such as a negative blur or a miter limit below 1, falls back to the default of SVG.
