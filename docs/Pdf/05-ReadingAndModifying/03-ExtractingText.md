---
title: Extracting text
description: Get the text of the pages of a document, search a document for a word, find where the words are, and know what the extraction can and cannot do.
---

[PdfPage.ExtractText](cref:Meziantou.Framework.Pdf.PdfPage.ExtractText) returns the text of a page, with a line break between the lines:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var pageCount = document.Pages.Count;
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

## Search a document

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_SearchText;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(path);

// The numbers of the pages whose text contains a word
var pages = new List<int>();
for (var i = 0; i < document.Pages.Count; i++)
{
    var text = document.Pages[i].ExtractText();
    if (text.Contains("revenue", StringComparison.OrdinalIgnoreCase))
    {
        pages.Add(i + 1);
    }
}
```
<!-- endref -->

## Find where the text is

[PdfPage.ExtractTextLayout](cref:Meziantou.Framework.Pdf.PdfPage.ExtractTextLayout) returns the same text with its
position: the [lines](cref:Meziantou.Framework.Pdf.PdfTextLine), their [words](cref:Meziantou.Framework.Pdf.PdfTextWord)
and their [glyphs](cref:Meziantou.Framework.Pdf.PdfTextGlyph), each with its bounding box. Use it to highlight the
results of a search, to redact a word or to group the cells of a table:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_FindTextPositions;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var layout = document.Pages[0].ExtractTextLayout();

// The boxes of a word, in the coordinates of the page (points, from its bottom left corner)
var boxes = new List<PdfRectangle>();
foreach (var line in layout.Lines)
{
    foreach (var word in line.Words)
    {
        if (word.Text.Equals("grew", StringComparison.OrdinalIgnoreCase))
        {
            boxes.Add(word.Bounds);
        }
    }
}
```
<!-- endref -->

- The coordinates are in the default user space of the page: points, from the origin of its media box, with the y
  axis upward, before the rotation of the page ([PdfPage.Rotation](cref:Meziantou.Framework.Pdf.PdfPage.Rotation)).
- A glyph has its origin on the baseline, the point where the next glyph goes, its font size in user space, the name of
  its font and the angle of the writing direction (0 for horizontal text, -90 for text written vertically).
- The box of a glyph goes from its origin to the next one, and from the descent to the ascent of its font. Text that is
  rotated or written vertically gets the smallest rectangle that contains its box.
- The box of a line or a word contains the boxes of its glyphs. The words are the glyphs between two spaces.

## Search text written with ligatures

A font can show "fi" with one glyph whose character is the ligature `ﬁ` (U+FB01): the extracted word does not contain
the letters "fi". [PdfTextExtractionOptions.ExpandLigatures](cref:Meziantou.Framework.Pdf.PdfTextExtractionOptions.ExpandLigatures)
replaces the Latin ligatures (U+FB00 to U+FB06) by their letters:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ExpandLigatures;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var options = new PdfTextExtractionOptions { ExpandLigatures = true };
var text = document.Pages[0].ExtractText(options);
```
<!-- endref -->

By default, the text is exactly what the fonts give.

## How the text is extracted

- The glyphs are read in the order of the content, and turned back into characters with the encoding of their font,
  its `ToUnicode` map or the names of its glyphs. The documents the library writes have what the extraction needs,
  embedded fonts included.
- The Chinese, Japanese and Korean fonts that use a predefined encoding (such as `90ms-RKSJ-H`, `GBK-EUC-H`,
  `UniJIS-UCS2-H` or `KSCms-UHC-H`) give their text without a `ToUnicode` map: the codes are split with the code space
  of the encoding and decoded with its character set (Shift-JIS, EUC, GBK, GB 18030, Big Five, UHC or Unicode).
- The text written vertically follows the vertical metrics of its font (`/W2` and `/DW2`).
- The glyphs are joined into lines by their positions. A gap between two glyphs of a line becomes a space, and a new
  line becomes a line break, even when the document draws each word separately.
- A replacement text (`/ActualText`) replaces the glyphs it covers, such as a ligature or a right-to-left run.
- The text of the forms the page paints is included.

## Limits

- The order is the order of the content: the extraction does not detect columns, and a document that draws the text in
  another order than it is read gives it in that order.
- A text drawn as paths or as an image (a scanned page) has no characters to extract.
- A font without encoding information, such as some embedded fonts of old documents, can give wrong characters.
- The predefined encodings of the CNS 11643 and GB/T 12345 character sets (`CNS-EUC-H`, `GBT-EUC-H`, ...) and the
  characters of the Hong Kong extensions of Big Five have no text without a `ToUnicode` map. The widths of the glyphs of
  a font with a predefined encoding other than `Identity-H` and `Identity-V` are its default width: the positions of its
  glyphs, and the spaces found from them, can be off.
- The boxes of the glyphs use the ascent and descent of the font, not the outline of each glyph.
- A page whose content runs too many operators, such as forms that paint forms many times, throws
  [PdfResourceLimitException](cref:Meziantou.Framework.Pdf.PdfResourceLimitException) instead of running for a long time.
