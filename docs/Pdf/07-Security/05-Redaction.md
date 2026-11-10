---
title: Redaction
description: Remove text, graphics, image pixels, annotations and form values from areas of a page, so that they cannot be read back from the saved file.
---

Drawing a black rectangle over a text hides it on the screen, but the text is still in the file: it can be selected,
copied, extracted and found by a search of the bytes. [PdfPage.Redact](cref:Meziantou.Framework.Pdf.PdfPage.Redact) removes what the page shows in
areas, then paints the areas over.

## Redact an area

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_Redact;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
var page = document.Pages.Add(PdfSize.A4);
page.CreateContentBuilder()
    .DrawText("Name: John Smith", font, 12, 72, 720);

// Removes the content under the rectangle and paints it black; the document can then only be saved as a whole
var result = page.Redact([new PdfRectangle(108, 715, 200, 732)]);
var redacted = PdfDocument.Open(document.ToByteArray());

Assert.Equal("Name:", redacted.Pages[0].ExtractText().Trim());
```
<!-- endref -->

The areas are rectangles in the default user space of the page: points (1/72 inch) from the lower-left corner of the
media box, the coordinates of [ExtractTextLayout](cref:Meziantou.Framework.Pdf.PdfPage.ExtractTextLayout) and of [GetImages](cref:Meziantou.Framework.Pdf.PdfPage.GetImages).

## Redact words found in the text

The text layout gives the box of each word, line and glyph: redact the boxes of the words to remove.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_RedactWords;format=md-fenced-code-block;dedent=true -->
```csharp
// The boxes of the words to remove, found in the text of the page
var areas = page.ExtractTextLayout().Lines
    .SelectMany(line => line.Words)
    .Where(word => word.Text.All(char.IsAsciiDigit))
    .Select(word => word.Bounds);
page.Redact(areas);
```
<!-- endref -->

## What is removed

The content of the page is rewritten, and so is the content of the forms it paints:

- **Text**: the glyphs whose box touches an area, the invisible ones included (the text layer of a scanned page). The
  other glyphs stay in place, even on the same line.
- **Paths**: the subpaths that lie inside an area are removed. A path that crosses an area is kept, clipped so that
  nothing of it is painted in the area.
- **Images**: the pixels under an area are erased (transparent). The image is replaced by a copy without them, stored in
  sRGB, as JPEG when it was JPEG; an image without pixels left is removed.
- **Forms**: a form XObject the areas touch is replaced on this page by a redacted copy. The other pages that paint the
  form are not changed.
- **Annotations**: the annotations whose rectangle touches an area, with their pop-ups. A form field whose widgets are
  all removed is removed with its value. Set [RemoveAnnotations](cref:Meziantou.Framework.Pdf.PdfRedactionOptions.RemoveAnnotations) to `false` to keep them.
- **Replacement texts**: the `/ActualText`, `/Alt` and `/E` entries of the marked-content sequences that lose content,
  which could tell what was removed.

[PdfRedactionResult](cref:Meziantou.Framework.Pdf.PdfRedactionResult) counts what was removed. The areas are painted black; set [FillColor](cref:Meziantou.Framework.Pdf.PdfRedactionOptions.FillColor) to
another color, or to `null` to paint nothing.

## Save the redacted document

Save the document in full ([Save](cref:Meziantou.Framework.Pdf.PdfDocument.Save), [ToByteArray](cref:Meziantou.Framework.Pdf.PdfDocument.ToByteArray)): it only writes the objects the document uses, so the
removed content is not in the file. An incremental update keeps the bytes of the original file, which contain the removed
content: [SaveIncremental](cref:Meziantou.Framework.Pdf.PdfDocument.SaveIncremental) throws `InvalidOperationException` once a page is redacted. The signatures of the
document are not valid anymore.

## What is not removed

- The content of the tiling patterns, the soft masks, the glyphs of Type 3 fonts, and the appearances of the
  annotations that are kept.
- The text of the document outside the pages: its metadata, its bookmarks, the names of its files, the alternate
  descriptions of its structure elements.
- The parts of the paths that cross an area: their coordinates stay in the file, clipped out of the area.

When a content stream, or an image under an area, cannot be decoded, the method throws, and the page is not changed.
