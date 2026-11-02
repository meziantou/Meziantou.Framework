---
title: Halftones
description: Render a page as a printer would halftone it, with the halftone screens of the document, and know which objects they apply to.
---

A printer that can only turn its inks on or off reproduces the other colors with halftones: patterns of dots that it
turns on or off pixel by pixel. A prepress document can choose its own halftones (the `/HT` and `/HTO` entries of its
graphics states). Viewers ignore them, and so does [PdfRenderer](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderer)
by default: the colors are drawn as they are, for a screen.

Set [UseHalftones](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.UseHalftones) to see the halftones of the
document instead, for instance to proof the screens of a prepress document:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_RenderHalftones;format=md-fenced-code-block;dedent=true -->
```csharp
var page = document.Pages[0];

// The halftones of the document turn the color components on or off, as a printer would
var options = new PdfRenderOptions
{
    UseHalftones = true,
    PathAntialiasing = false,
    TextAntialiasing = false,
};

using var image = page.Render(options);
```
<!-- endref -->

The red, green and blue components of the image are then each fully on or off where a halftone applies. Turn the
antialiasing off, as above: an antialiased edge mixes a pixel with what is behind it, as a printer does not.

## The halftones

The screens of all the halftone types of the specification (ISO 32000-2, 10.6) are drawn:

| Halftone | Screen |
| --- | --- |
| Type 1 | A frequency, an angle and a spot function: a function of the document, or one of the predefined spot functions (`SimpleDot`, `Round`, `Ellipse`, `Line` and the others) |
| Type 6 | A threshold array of 8-bit values |
| Type 10 | A threshold array of 8-bit values in two squares, for an angled screen |
| Type 16 | A threshold array of 16-bit values, in one rectangle or two |
| Type 5 | A halftone for each colorant: the `/Red`, `/Green` and `/Blue` entries, and `/Default` for the components without their own |

A component is on where its value is not below the threshold of its pixel; a threshold of 0 counts as 1, so that black
stays black. The transfer function of a halftone (`/TransferFunction`) replaces the one of the graphics state for its
component. The halftone origin (`/HTO`, PDF 2.0) moves the first threshold from the top left pixel of the image to
another pixel.

The screens are laid on the pixels of the image: the same page looks different at another resolution, and the cells
of a type 1 halftone are as large as the resolution divided by its frequency. Render at the resolution of the printer to
see what it would print.

## Where the halftones apply

The halftone of the graphics state applies to the objects painted while it is in effect, when they are fully opaque
(ISO 32000-2, 11.7.5.2): an alpha of 1, the Normal blend mode, no soft mask (an image with a `/SMask` has one), and the
same for the transparency groups they are in. The other objects keep the default halftone, and so do the patterns and
the soft masks, which are drawn by the objects that use them.

The default halftone of the device (`/Default`, and the halftone of a page that sets none) leaves the colors as they
are: only the halftones of the document turn the components on or off.

## Differences with a printer

- A type 1 halftone uses whole pixels: the size of its cells and its angle are rounded to the nearest cell whose
  corners are pixels, as the ordinary halftoning of the specification does. `AccurateScreens` is ignored, the largest
  cell is 256 pixels wide, and the pixels whose spot function gives the same value whiten in the order of the rows.
- The image has red, green and blue components: the screens of the other colorants of a type 5 halftone (`/Cyan`,
  `/Magenta`, spot colors) are not used, and the colors of a CMYK document are converted to RGB before they are
  halftoned.
- The halftones of named devices (`/HalftoneName`) are not known: the other entries of the halftone are used, and a
  halftone that has none, or that cannot be read, is the default halftone.
- A transparent object drawn over a halftoned one is composited with the halftoned colors, where a printer would
  composite the colors first and halftone the result with the default halftone.
