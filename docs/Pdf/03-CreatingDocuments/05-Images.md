---
title: Images
description: Draw JPEG, PNG and other images in a page, keep their aspect ratio and their transparency, and reuse them.
---

[PdfImage.Load](cref:Meziantou.Framework.Pdf.PdfImage.Load) reads an image file, and [DrawImage](cref:Meziantou.Framework.Pdf.PdfContentBuilder.DrawImage) draws it in a rectangle of the page:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfContentBuilderTests.cs;region=Pdf_DrawImage;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawImage(string imagePath, string outputPath)
{
    // A JPEG file is embedded as it is; another format (PNG, WebP...) is decoded, with its transparency
    var image = PdfImage.Load(imagePath);

    var document = new PdfDocument();
    var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder()
        .SetFillRgb(0.9, 0.93, 0.97).Rectangle(0, 0, 300, 150).Fill();

    // The image is stretched to the rectangle it is drawn in: compute the width that keeps its aspect ratio
    var height = 120.0;
    var width = height * image.Width / image.Height;
    builder.DrawImage(image, 15, 15, width, height)
        .DrawImage(image, 30 + width, 15, width / 2, height / 2); // drawn again: the file stores it once

    document.Save(outputPath);
}
```
<!-- endref -->

![A landscape picture with rounded transparent corners on a light blue background, and a smaller copy of it](images/DrawImage.png)

- The image fills the rectangle it is drawn in: compute the width or the height from [Width](cref:Meziantou.Framework.Pdf.PdfImage.Width) and [Height](cref:Meziantou.Framework.Pdf.PdfImage.Height), its size in
  pixels, to keep its aspect ratio.
- An image drawn several times, on one page or on several, is stored once in the file.
- A [PdfImage](cref:Meziantou.Framework.Pdf.PdfImage) does not belong to a document and is thread-safe: each document that uses it gets its own copy.

## Where images come from

| Method | Stores |
| --- | --- |
| `PdfImage.Load(path)`, `PdfImage.Load(bytes)` | A JPEG file as it is; any other format `Meziantou.Framework.Imaging` reads (PNG, WebP, GIF, TIFF...) decoded, as with [FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage) |
| `PdfImage.FromJpeg(bytes)` | A JPEG file as it is (gray, YCbCr or CMYK, 8 bits per sample), without decoding it |
| `PdfImage.FromImage(image)` | The first frame of an [Image](cref:Meziantou.Framework.Imaging.Image) of `Meziantou.Framework.Imaging`, whose pixels are copied |
| `PdfImage.FromImageAsJpeg(image, encoder)` | The first frame of an [Image](cref:Meziantou.Framework.Imaging.Image), compressed as JPEG: see [Photos](#photos) |

- A JPEG file is kept as it is: it is not decoded and compressed again, so no quality is lost.
- The other images are stored as gray or RGB samples of 8 or 16 bits, compressed without loss. The save applies the PNG
  predictors to the samples when they make them smaller, as they do for most photos and gradients. Their transparent pixels
  become a soft mask ([HasSoftMask](cref:Meziantou.Framework.Pdf.PdfImage.HasSoftMask)), so the page shows through them as in the example.
- The ICC profile of an image is kept, so that its colors are shown as they were meant.
- [FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage) throws `ArgumentException` for an image with floating-point pixels: convert it to an integer pixel format
  first.

To resize, crop or convert an image before drawing it, process it with `Meziantou.Framework.Imaging`, then pass it to
[FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage). A large photo drawn small still stores all its pixels: resize it to the resolution the page needs.

## Photos

The samples of a photo compressed without loss are large. [FromImageAsJpeg](cref:Meziantou.Framework.Pdf.PdfImage.FromImageAsJpeg) compresses the image as JPEG
instead, with the settings of a [JpegEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder) (quality, chroma subsampling, progressive coding):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_FromImageAsJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
private static void DrawPhotoAsJpeg(Image photo, string outputPath)
{
    // A photo is much smaller as JPEG than as samples compressed without loss; its transparency stays a soft mask
    var image = PdfImage.FromImageAsJpeg(photo, new Imaging.Formats.JpegEncoder { Quality = 85 });

    var document = new PdfDocument();
    document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder().DrawImage(image, 10, 10, 128, 128);
    document.Save(outputPath);
}
```
<!-- endref -->

- JPEG loses details, and blurs sharp edges: keep [FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage) for drawings, screenshots and text.
- The samples of 16 bits are rounded to 8 bits, the precision of JPEG in PDF.
- The transparent pixels still become a soft mask, compressed without loss, and the ICC profile of the image is kept.
- A JPEG file you already have is better drawn with [Load](cref:Meziantou.Framework.Pdf.PdfImage.Load) or [FromJpeg](cref:Meziantou.Framework.Pdf.PdfImage.FromJpeg): it is kept as it is, without
  a second loss.
