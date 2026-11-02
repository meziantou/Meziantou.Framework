---
title: Images
description: Draw JPEG, PNG, black and white and other images in a page, keep their aspect ratio and their transparency, and reuse them.
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

- A JPEG file is kept as it is: it is not decoded and compressed again, so no quality is lost.
- The other images are stored as gray or RGB samples of 8 or 16 bits, compressed without loss. Their transparent pixels
  become a soft mask ([HasSoftMask](cref:Meziantou.Framework.Pdf.PdfImage.HasSoftMask)), so the page shows through them as in the example.
- The ICC profile of an image is kept, so that its colors are shown as they were meant.
- [FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage) throws `ArgumentException` for an image with floating-point pixels: convert it to an integer pixel format
  first.

To resize, crop or convert an image before drawing it, process it with `Meziantou.Framework.Imaging`, then pass it to
[FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage). A large photo drawn small still stores all its pixels: resize it to the resolution the page needs.

## Black and white images

A scan, a fax or a line drawing has one bit per pixel. [FromBilevel](cref:Meziantou.Framework.Pdf.PdfImage.FromBilevel) stores it in one bit per pixel with a compression
made for such images, which is much smaller than the gray samples of [FromImage](cref:Meziantou.Framework.Pdf.PdfImage.FromImage):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=PdfImage_FromBilevel;format=md-fenced-code-block;dedent=true -->
```csharp
// A 16 x 2 image, one bit per pixel: a set bit is a black pixel
byte[] pixels = [0b1111_0000, 0b0000_1111, 0b1010_1010, 0b0101_0101];
var image = PdfImage.FromBilevel(pixels, width: 16, height: 2, PdfBilevelCompression.CcittGroup4);

var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawImage(image, 50, 700, 160, 20);
var data = document.ToByteArray();
```
<!-- endref -->

The pixels are the rows of the image from top to bottom, each of `(width + 7) / 8` bytes, the most significant bit of
a byte first: a set bit is a black pixel. The bits after the last pixel of a row are ignored.

| [PdfBilevelCompression](cref:Meziantou.Framework.Pdf.PdfBilevelCompression) | Stores |
| --- | --- |
| [CcittGroup4](cref:Meziantou.Framework.Pdf.PdfBilevelCompression.CcittGroup4) (default) | The Group 4 coding of fax machines and scanners (`CCITTFaxDecode`), which every reader decodes |
| [Jbig2](cref:Meziantou.Framework.Pdf.PdfBilevelCompression.Jbig2) | JBIG2 arithmetic coding (`JBIG2Decode`, PDF 1.4): usually smaller, especially for dithered images and halftones |
| [Flate](cref:Meziantou.Framework.Pdf.PdfBilevelCompression.Flate) | The rows of bits compressed with `FlateDecode` |

- Every compression is lossless: a reader shows the pixels of the image. The JBIG2 image is one generic region; the
  symbol coding of text, which some encoders use to replace similar glyphs by one another, is not used.
- The image is compressed when [FromBilevel](cref:Meziantou.Framework.Pdf.PdfImage.FromBilevel) is called. Group 4 coding supports images of up to 1048576 pixels per
  row.
