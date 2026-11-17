---
title: Print production
description: Simulate overprinting, render the plate of each colorant, list the colorants of a page, and report the page boxes and output intents of a document.
---

The renderer composites the pages in RGB, for a screen. A printing press lays inks on paper instead: each object paints
the plates of the colorants of its color, and erases the other plates under it, unless it overprints. The options of
[PdfRenderOptions](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions) render a page as a press would print it:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=Pdf_SimulateOverprint;format=md-fenced-code-block;dedent=true -->
```csharp
using var preview = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { SimulateOverprint = true });
using var cyanPlate = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { Plate = "Cyan" });
```
<!-- endref -->

## Simulate overprinting

[SimulateOverprint](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.SimulateOverprint) renders the plate of each colorant of the page, then combines their inks on white paper: cyan,
magenta, yellow and black for the process colorants, and the color of its alternate color space for a spot colorant.
Where an object overprints, the inks under it show through, as in the overprint preview of a prepress application.

- The overprint parameters are those of the graphics state: `OP` for the strokes, `op` for the fills, the text and the
  images, and the overprint mode `OPM`. With the nonzero overprint mode (`OPM 1`), a component of 0 of a CMYK color
  leaves its plate unchanged.
- The page is rendered once per colorant: the simulation takes several times as long as a rendering. The background of
  the options is not painted.
- The colors of images and shadings are separated into the process colorants from their RGB colors: their spot
  colorants are not separated.

## Render a plate

[Plate](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Plate) renders the plate of one colorant, in gray, as a film: black where the ink is full, white where there is
none. The colorant is `Cyan`, `Magenta`, `Yellow`, `Black`, or the name of a spot colorant. Gray colors are separated as
black, and RGB colors with a full gray component replacement. [SimulateOverprint](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.SimulateOverprint) is ignored when a plate is set.

## List the colorants of a page

[PdfSeparations.GetColorants](cref:Meziantou.Framework.Pdf.PdfSeparations.GetColorants) lists the colorants a page paints, in its content, its forms, images, shadings and
patterns, and the appearances of its annotations:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=Pdf_ListColorants;format=md-fenced-code-block;dedent=true -->
```csharp
var colorants = PdfSeparations.GetColorants(page);
```
<!-- endref -->

[PdfColorantUsage](cref:Meziantou.Framework.Pdf.PdfColorantUsage) has the [process colorants](cref:Meziantou.Framework.Pdf.PdfColorantUsage.ProcessColorants) (those the CMYK, gray and RGB colors ink), the
[spot colorants](cref:Meziantou.Framework.Pdf.PdfColorantUsage.SpotColorants), the families of the color spaces, and whether a graphics state overprints.

## Report the print production of a document

[PdfPrintProductionReport](cref:Meziantou.Framework.Pdf.PdfPrintProductionReport) gathers what a printer checks before printing a document:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=Pdf_PrintProductionReport;format=md-fenced-code-block;dedent=true -->
```csharp
var report = PdfPrintProductionReport.Create(document);
```
<!-- endref -->

- The [output intents](cref:Meziantou.Framework.Pdf.PdfOutputIntent) of the document: the printing condition (such as `FOGRA39`), its registry, and the number
  of components of its profile.
- For each page ([PdfPageProductionInfo](cref:Meziantou.Framework.Pdf.PdfPageProductionInfo)): the media box and the crop box, the bleed, trim and art boxes the page
  declares, its colorants, and its issues: no trim box nor art box, a box that extends outside the box that should
  contain it, a trim box and an art box together.
- The trapping state of the document information, and the spot colorants of all the pages.

The report does not change the document.
