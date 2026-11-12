---
title: Limitations
description: What each renderer of the scenes draws differently, or does not draw.
---

The SVG, image and PDF renderers draw the same scene the same way, except in the cases below.

## PDF

- **Filters are not supported.** PDF has no blur, no drop shadow, no color matrix and no filter graph that it draws as vectors:
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
  clipping path cut by another one) are soft masks, which the viewers draw with the antialiasing of the transparency.
- **The opacity and the mask of a shape** apply to its fill and to its stroke separately: where they overlap, the shape
  is a little more opaque than in SVG. A group is drawn as a whole.
- **Fonts.** Without the fonts of the scene, the texts are drawn with the standard Helvetica fonts, which are not
  embedded and only have the characters of Western European languages: the other characters are drawn as question
  marks. A face that cannot be embedded (a license that forbids it, CFF2 or bitmap outlines) is replaced by Helvetica.
- The tooltips, the CSS classes, the identifiers and the data attributes are not drawn; the hyperlinks of the elements
  are link annotations of the page, for absolute addresses only. The hyperlinks of the contents of the masks, the
  patterns and the markers are not links.
- **Text along a path.** The decorations are drawn under each character, turned with it, rather than along the curve.
- **Text outlines.** A text with both a fill and an outline painted under it
  ([PaintOrder](cref:Meziantou.Framework.VectorGraphics.SceneText.PaintOrder)) is shown twice, once outlined and once
  filled: the outline has an empty replacement text, so the text is extracted once.
- **Baselines.** The baselines of a text drawn with Helvetica (without the fonts of the scene) are computed from the
  metrics of Helvetica, whose ascent is smaller than the one of Liberation Sans, the font of the images.

## Images

- The hyperlinks, the tooltips, the CSS classes, the identifiers and the data attributes are not drawn.
- A text longer than a closed path goes around it again, where SVG does not draw the characters beyond its end.
- A text fitted to its [TextLength](cref:Meziantou.Framework.VectorGraphics.SceneText.TextLength) by its spacing, whose
  last span mixes directions in the other direction than the text (Latin letters at the end of an Arabic word, in a text
  from left to right), spreads the difference over all its characters, the last one included, to keep the order of its
  letters: PDF leaves the space out after the last character.
- The values of the primitives of a filter graph beyond the limits of the canvas are brought within them: the
  coordinates within 16777216, the standard deviations within 10000, the specular exponents of the spot lights within
  128. The weights of a convolution matrix are one pixel of the image apart, whatever its scale.
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
- **Filter graphs.** An [image primitive](cref:Meziantou.Framework.VectorGraphics.SceneImagePrimitive) whose element is
  not an image without effects, or whose subregion is not the rectangle of its image, is an `feImage` that refers to the
  element: Firefox does not draw it. The edge modes of
  the blurs (`edgeMode` of `feGaussianBlur`), the `lighter` composite and the `mirror` edge mode are features of
  "Filter Effects Module Level 1" that some viewers do not draw. The region of a graph without
  [Region](cref:Meziantou.Framework.VectorGraphics.SceneFilterGraph.Region) on a text, or on a group that has a text, is
  the bounding box of the element enlarged by half its size on each side, as for the other filters.
- The lines of a text whose paint differs from its glyphs
  ([DecorationFill](cref:Meziantou.Framework.VectorGraphics.SceneText.DecorationFill)) are written on an element whose
  fill is that paint, and the glyphs in a `tspan` of their own fill, as SVG paints the lines with the fill of the element
  that declares them. A viewer that paints the lines with the fill of the characters draws them in the color of the
  glyphs.
- The viewer computes the baselines (`dominant-baseline`) from the metrics of its own fonts, and stretches a text to its
  `textLength` its own way: the positions can differ by a pixel or two from the image and the PDF.

## All renderers

- The spans of a text are measured and drawn one after the other: the kerning between the last letter of a span and
  the first letter of the next one is not applied.
- The renderers do not form the optional ligatures of a text with a letter spacing, as browsers do, and add the spacing
  after each cluster of glyphs: a ligature a script requires gets one spacing. The measurers of font files
  ([FontFaceTextMeasurer](cref:Meziantou.Framework.VectorGraphics.FontFaceTextMeasurer), the measurer of the images)
  measure the texts the same way; [AfmTextMeasurer](cref:Meziantou.Framework.VectorGraphics.AfmTextMeasurer) adds the
  spacing after each grapheme cluster. A browser drawing the SVG with its own fonts can space a text differently.
- **Text direction.** A text is written from left to right or from right to left
  ([Direction](cref:Meziantou.Framework.VectorGraphics.SceneText.Direction)), on a horizontal line: there is no
  vertical writing mode (SVG `writing-mode`). The Unicode Bidirectional Algorithm orders the characters of each span,
  not the spans themselves: a span of Arabic in a text from left to right is reversed on its own, but a sentence that
  mixes directions across several spans keeps its spans in their order.
- The characters of a span turned by [GlyphRotation](cref:Meziantou.Framework.VectorGraphics.SceneTextSpan.GlyphRotation)
  are shaped one grapheme cluster at a time, without kerning between them. On a path, the rotation and the absolute
  positions of the spans are ignored, and a text fitted to its length with its glyphs
  ([SceneTextLengthAdjust.SpacingAndGlyphs](cref:Meziantou.Framework.VectorGraphics.SceneTextLengthAdjust.SpacingAndGlyphs))
  changes its spacing only. The image and PDF renderers do not outline the lines of a text with its
  [Stroke](cref:Meziantou.Framework.VectorGraphics.SceneText.Stroke); SVG viewers do.

- An image that the image or PDF renderer cannot decode is not drawn; the SVG embeds its bytes as they are.
- The bounds of a shape whose stroke does not scale
  ([NonScalingStroke](cref:Meziantou.Framework.VectorGraphics.SceneShape.NonScalingStroke)) count the width of its
  stroke in the coordinates of the shape: in a group scaled down, the filter region of SVG, the layers of the images and
  the transparency groups of PDF can cut the stroke.
- A value that is not valid, such as a negative blur or a miter limit below 1, falls back to the default of SVG.
