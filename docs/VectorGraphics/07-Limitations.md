---
title: Limitations
description: What each renderer of the scenes draws differently, or does not draw.
---

The SVG, image and PDF renderers draw the same scene the same way, except in the cases below.

## PDF

- **Filters are not supported.** PDF has no blur, no drop shadow and no color matrix that it draws as vectors:
  [ToPdf](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.ToPdf) and
  [DrawScene](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.DrawScene) throw
  `NotSupportedException` for a scene that has a [Filter](cref:Meziantou.Framework.VectorGraphics.SceneElement.Filter),
  in an element or in the content of a mask, before they draw anything. Remove the filters, or draw the scene as an
  [image](03-ImageRendering.md).
- **Gradients.** The opacity of a gradient is the average opacity of its stops: a gradient from opaque to transparent
  is drawn half transparent. A repeated or reflected gradient is drawn with its stops repeated over the periods the
  shape covers, at most 256: beyond them, its end colors extend. A radial gradient whose focal point is not inside its
  circle is padded instead of repeated, and a repeated radial gradient has its first color inside the circle of its
  [FocalRadius](cref:Meziantou.Framework.VectorGraphics.RadialGradientPaint.FocalRadius).
- **Clipping paths.** A clipping path of one shape is a clipping path of PDF. The others (several shapes, a text, a
  clipping path cut by another one) are soft masks, which the viewers draw with the antialiasing of the transparency;
  an element that also has a mask is drawn in a transparency group, where its blend mode does not reach the drawing
  under it.
- **The opacity and the mask of a shape** apply to its fill and to its stroke separately: where they overlap, the shape
  is a little more opaque than in SVG. A group is drawn as a whole.
- **Fonts.** Without the fonts of the scene, the texts are drawn with the standard Helvetica fonts, which are not
  embedded and only have the characters of Western European languages: the other characters are drawn as question
  marks. A face that cannot be embedded (a license that forbids it, CFF2 or bitmap outlines) is replaced by Helvetica.
- The tooltips, the CSS classes, the identifiers and the data attributes are not drawn; the hyperlinks of the elements
  are link annotations of the page, for absolute addresses only. The hyperlinks of the contents of the masks, the
  patterns and the markers are not links.
- **Text along a path.** The decorations are drawn under each character, turned with it, rather than along the curve.

## Images

- The hyperlinks, the tooltips, the CSS classes, the identifiers and the data attributes are not drawn.
- A text longer than a closed path goes around it again, where SVG does not draw the characters beyond its end.
- Without the fonts of the scene, the texts are drawn with the embedded Liberation Sans, subset to the Latin characters.

## SVG

- The context paints (`context-fill`, `context-stroke`) and the radial gradients with a focal radius (`fr`) are
  features of SVG 2: some older viewers and editors paint nothing, or ignore the focal radius.

- The SVG is drawn by the viewer, with its fonts: without embedded fonts
  ([EmbedFonts](cref:Meziantou.Framework.VectorGraphics.SvgRenderOptions.EmbedFonts)), a viewer that does not have
  the fonts of the scene draws the texts with others, which can be wider or narrower than the layout measured them.
  The faces of font collections (`.ttc`) are not embedded.
- The region of the filter of a text, or of a group that has a text, is the bounding box of the element enlarged by
  half its size on each side, because the size of a text is not known without measuring it: a large blur or a large
  shadow offset can be cut.
- The alpha masks (`mask-type`) and the blend modes (`mix-blend-mode`) are features of SVG 2 and CSS: the current
  browsers draw them, some older viewers and editors do not.
- The lines of a text made of spans are written on each span (a span cannot remove the lines of its parent in SVG), so
  an underline is drawn in pieces, one per span.
- The markers turned back at the start of a path (`orient="auto-start-reverse"`) are a feature of SVG 2: some older
  viewers draw them turned as the other markers.

## All renderers

- The spans of a text are measured and drawn one after the other: the kerning between the last letter of a span and
  the first letter of the next one is not applied.
- The renderers do not form the optional ligatures of a text with a letter spacing, as browsers do, and add the spacing
  after each cluster of glyphs: a ligature a script requires gets one spacing. The measurers of font files
  ([FontFaceTextMeasurer](cref:Meziantou.Framework.VectorGraphics.FontFaceTextMeasurer), the measurer of the images)
  measure the texts the same way; [AfmTextMeasurer](cref:Meziantou.Framework.VectorGraphics.AfmTextMeasurer) adds the
  spacing after each grapheme cluster. A browser drawing the SVG with its own fonts can space a text differently.

- An image that the image or PDF renderer cannot decode is not drawn; the SVG embeds its bytes as they are.
- The bounds of a shape whose stroke does not scale
  ([NonScalingStroke](cref:Meziantou.Framework.VectorGraphics.SceneShape.NonScalingStroke)) count the width of its
  stroke in the coordinates of the shape: in a group scaled down, the filter region of SVG, the layers of the images and
  the transparency groups of PDF can cut the stroke.
- A value that is not valid, such as a negative blur or a miter limit below 1, falls back to the default of SVG.
