---
title: Extracting images
description: Find the images a page paints, where they are on the page, and get their encoded data or their pixels.
---

[GetImages](cref:Meziantou.Framework.Pdf.PdfPage.GetImages) returns the images the content of a page paints, in the order they are painted:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_ExtractImages;format=md-fenced-code-block;dedent=true -->
```csharp
foreach (var image in page.GetImages())
{
    // Where the image is on the page, and its pixels, with its color space and its soft mask applied
    var bounds = image.Bounds;
    using var decoded = image.Decode();
}
```
<!-- endref -->

- The image XObjects painted by the content, or by the forms it paints, and the inline images are included. An image
  painted several times is returned each time, with the same [Reference](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Reference).
- [Matrix](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Matrix) maps the unit square of the image to the page, and [Bounds](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Bounds) is the rectangle it covers.
- [EncodedData](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.EncodedData) and [Filters](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Filters) give the data as it is stored: the data of a `DCTDecode` image is a JPEG file.
  [Dictionary](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Dictionary), [ColorSpace](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.ColorSpace) and [SoftMask](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.SoftMask) give its attributes.
- [Decode](cref:Meziantou.Framework.Pdf.PdfImageOccurrence.Decode) returns the pixels in RGBA: the color space converted to sRGB, the decode array applied, and the mask or the
  soft mask as the alpha channel. It applies the limits of the [read options](02-OpeningDocuments.md#read-options) of the
  document. Dispose of the image.
- The images of the annotations, of the patterns and of the soft masks are not included.
