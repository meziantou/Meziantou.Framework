# Meziantou.Framework.Pdf.Rendering

A fully managed renderer of the pages of PDF documents to images, written from ISO 32000-2 (PDF 2.0). The pages are read
by `Meziantou.Framework.Pdf` and drawn on the canvas of `Meziantou.Framework.Imaging`.

- **Graphics**: paths, line styles and dashes, clipping (paths and text), the page boxes and rotation, optional content
  (layers), the appearance streams of the annotations
- **Text**: embedded TrueType, CFF (`Type1C`, `CIDFontType0C`, OpenType) and Type 1 fonts, Type 3 fonts, the fonts that
  are not embedded replaced by a font of the system or of the application, the text rendering modes
- **Colors**: the device color spaces, `ICCBased` (through the ICC profiles of Imaging), `CalRGB`, `CalGray`, `Lab`,
  `Indexed`, `Separation` and `DeviceN`; `DeviceCMYK` is converted with a CMYK profile
- **Images**: every filter (`DCTDecode` and `JPXDecode` through the codecs of Imaging, CMYK JPEG images included,
  `CCITTFaxDecode`, `JBIG2Decode`), 1 to 16 bits per component, `/Decode`, stencil masks, explicit masks, color key masks and soft masks, inline images
- **Shadings and patterns**: functions (sampled, exponential, stitching, PostScript calculator), the seven shading
  types, shading patterns and colored and uncolored tiling patterns
- **Transparency**: constant alpha, the blend modes, transparency groups (drawn as isolated, non-knockout groups), soft
  masks (alpha and luminosity, with their backdrop and transfer function), the transfer functions of the graphics state

## Render a page

<!-- ref:../../tests/Meziantou.Framework.Pdf.Rendering.Tests/PdfRendererTests.cs;region=PdfRendering_RenderPage;format=md-fenced-code-block;dedent=true -->
```csharp
var page = document.Pages[0];

// 144 dpi: two pixels per point
using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = 144 });
using var png = new MemoryStream();
image.Save(png, new Imaging.Formats.PngEncoder());
```
<!-- endref -->

The image is the crop box of the page, rotated as the page is shown, on a white background (`PdfRenderOptions.Background`,
`null` for a transparent one). `PdfRenderOptions.RenderAnnotations` draws the appearances of the annotations.

## Fonts that are not embedded

A font that the document does not embed is replaced, in this order, by:

1. the font `PdfRenderOptions.FontResolver` returns for it (`PdfFontRequest`: its name, weight, style and kind);
2. for a standard font (Helvetica, Times, Courier and their aliases, such as Arial), the bundled Liberation font of its
   kind and style, whose glyphs have the same widths;
3. a font of the system that matches its name, weight and style, then one of its kind (serif, sans serif, fixed
   pitch), unless `PdfRenderOptions.UseSystemFonts` is `false`;
4. the bundled Liberation font of its kind.

The bundled fonts (SIL Open Font License 1.1, see `THIRD-PARTY-NOTICES.md`) have the characters of the Latin encodings
of PDF. With `UseSystemFonts` set to `false`, the rendering does not depend on the fonts of the machine. Symbol and
ZapfDingbats are only replaced by the resolver or the fonts of the system.

## Limits

The rendering of a page is bounded: `PdfRenderOptions.MaxOperations` limits the operators, glyphs and pixels it
processes, and the images, shadings and nested forms have limits of their own. A page beyond a limit throws a
`PdfResourceLimitException`. The errors of the content (an unknown operator, a damaged image or font) skip what they
affect, as viewers do.
