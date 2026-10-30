---
title: Reusable content
description: Write a logo, a stamp or a symbol once as a form, and paint it as many times as needed, at any position and size.
---

A form (a form XObject) is content that is written once in the file and painted as many times as needed: a logo on every
page, a symbol of a chart, a stamp. Painting it again adds a few bytes, not its content.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfContentBuilderTests.cs;region=Pdf_DrawReusableContent;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawReusableContent(string outputPath)
{
    var document = new PdfDocument();
    var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder();

    // A form: content written once in the file, painted as many times as needed
    var star = document.CreateForm(new PdfRectangle(0, 0, 40, 40));
    star.CreateContentBuilder()
        .SetFillRgb(1, 0.75, 0.1).SetStrokeRgb(0.75, 0.45, 0).SetLineWidth(1.5).SetLineJoin(PdfLineJoin.Round)
        .MoveTo(20, 38).LineTo(25, 25).LineTo(38, 25).LineTo(27, 17).LineTo(31, 3)
        .LineTo(20, 11).LineTo(9, 3).LineTo(13, 17).LineTo(2, 25).LineTo(15, 25).ClosePath()
        .FillAndStroke();

    // Each copy is placed and sized by the transformation of the graphics state
    for (var i = 0; i < 5; i++)
    {
        var scale = 0.6 + (i * 0.2);
        builder.SaveGraphicsState()
            .Translate(15 + (i * 55), 20 + (i * 15))
            .Scale(scale, scale)
            .DrawForm(star)
            .RestoreGraphicsState();
    }

    document.Save(outputPath);
}
```
<!-- endref -->

![Five yellow stars with an orange outline, growing in size from the lower left to the upper right](images/DrawReusableContent.png)

- [CreateForm](cref:Meziantou.Framework.Pdf.PdfDocument.CreateForm) creates a form with its bounding box, in the coordinates of the form: what is drawn outside of it is
  clipped. Its content is written with its own content builder ([CreateContentBuilder](cref:Meziantou.Framework.Pdf.PdfForm.CreateContentBuilder)), with the same methods as a
  page.
- [DrawForm](cref:Meziantou.Framework.Pdf.PdfContentBuilder.DrawForm) paints the form with the current graphics state: its transformation places and sizes it, its colors are
  those the form does not set. The optional matrix of [CreateForm](cref:Meziantou.Framework.Pdf.PdfDocument.CreateForm) is applied to the form every time it is painted.
- A form belongs to the document that created it: painting it in another document throws `ArgumentException`.
- A form can also be a transparency group (`group` of [CreateForm](cref:Meziantou.Framework.Pdf.PdfDocument.CreateForm)), composited as one object, or the source of a soft
  mask: see [Transparency](08-Transparency.md).
