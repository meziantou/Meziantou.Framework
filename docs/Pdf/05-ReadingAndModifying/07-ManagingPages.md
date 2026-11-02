---
title: Managing pages
description: Move, duplicate, remove and merge pages, read and set the page boxes, draw on rotated pages, and stamp a page on other pages.
---

## Move and duplicate pages

[Move](cref:Meziantou.Framework.Pdf.PdfPageCollection.Move) changes the position of a page, and [Insert](cref:Meziantou.Framework.Pdf.PdfPageCollection.Insert) with a page of the document inserts a copy of it
([Duplicate](cref:Meziantou.Framework.Pdf.PdfPageCollection.Duplicate) inserts it after the page):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_ReorderPages;format=md-fenced-code-block;dedent=true -->
```csharp
// Move the table of contents after the cover, and repeat the cover at the end
document.Pages.Move(2, 1);
document.Pages.Insert(document.Pages.Count, document.Pages[0]);
```
<!-- endref -->

- A moved page keeps its object: the bookmarks, links and structure elements that go to it stay valid. A page that
  changes of node in the page tree of the file keeps its attributes: the resources, media box, crop box and rotation it
  inherited are copied to it first.
- A copy shares the content streams and the resources of its page. It has its annotations, except the form fields and
  the pop-up windows, and it is not tagged.
- A new page ([Add](cref:Meziantou.Framework.Pdf.PdfPageCollection.Add)) has the size it is created with: it does not inherit the rotation nor the crop box of
  the page tree.

## Remove pages

[RemoveAt](cref:Meziantou.Framework.Pdf.PdfPageCollection.RemoveAt) removes a page and the references of the document to it, so that a full save does not write it
anymore:

- the bookmarks and the links that go to the page lose their destination (they stay, without action);
- the named destinations of the page, and the open action that goes to it, are removed;
- its marked content and its annotations are removed from the structure tree;
- its form fields are removed from the form.

## Merge documents

[Add](cref:Meziantou.Framework.Pdf.PdfPageCollection.Add) and [Insert](cref:Meziantou.Framework.Pdf.PdfPageCollection.Insert) with a page of another document copy the page with the objects it uses:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_MergeDocuments;format=md-fenced-code-block;dedent=true -->
```csharp
public static byte[] Merge(IEnumerable<byte[]> files)
{
    var result = new PdfDocument();
    foreach (var file in files)
    {
        // The source documents must stay open until the result is saved
        var source = PdfDocument.Open(file);
        foreach (var page in source.Pages)
        {
            result.Pages.Add(page);
        }
    }

    return result.ToByteArray();
}
```
<!-- endref -->

- The objects are renumbered in the target document. The objects that several pages of a source document use, such as
  the fonts and the images, are copied once.
- The source documents must stay open until the result is saved: the objects are read from them. An encrypted source
  is copied without its encryption: the result has the encryption of its own document.
- The links go to the copies of their pages, even when the page is imported after the link. A link to a page that is
  not imported has no destination; a named destination is replaced by the destination it names.
- The copies are not tagged, and their form fields are left out: the structure tree and the form belong to the source
  document.

## Page boxes

| Box | Property | Default |
| --- | --- | --- |
| The medium the page is printed on | [MediaBox](cref:Meziantou.Framework.Pdf.PdfPage.MediaBox) | US Letter |
| The region that is displayed and printed | [CropBox](cref:Meziantou.Framework.Pdf.PdfPage.CropBox) | The media box |
| The region of a production environment, with the bleed | [BleedBox](cref:Meziantou.Framework.Pdf.PdfPage.BleedBox) | The crop box |
| The finished page, after trimming | [TrimBox](cref:Meziantou.Framework.Pdf.PdfPage.TrimBox) | The crop box |
| The meaningful content | [ArtBox](cref:Meziantou.Framework.Pdf.PdfPage.ArtBox) | The crop box |

The media box and the crop box are inherited from the page tree; the other boxes are not. A box must have finite
coordinates. [UserUnit](cref:Meziantou.Framework.Pdf.PdfPage.UserUnit) is the size of the unit of the page in 1/72 inch: a value that is not a positive number is read
as 1.

## Draw on a rotated or cropped page

The coordinates of the content builder are those of the page before it is cropped and turned. To draw on the page as it
is displayed, concatenate [DisplayToUserSpace](cref:Meziantou.Framework.Pdf.PdfPage.DisplayToUserSpace):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_DisplayCoordinates;format=md-fenced-code-block;dedent=true -->
```csharp
// Draw a stamp at the top-left corner of the page as it is displayed, whatever its rotation and crop box
var size = page.DisplaySize;
page.CreateContentBuilder()
    .Transform(page.DisplayToUserSpace)
    .DrawText("DRAFT", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 24, 20, size.Height - 40);
```
<!-- endref -->

- In the display space, the origin is the lower-left corner of the page as it is displayed, y goes up, and the unit is
  1/72 inch, whatever the [UserUnit](cref:Meziantou.Framework.Pdf.PdfPage.UserUnit) of the page. [DisplaySize](cref:Meziantou.Framework.Pdf.PdfPage.DisplaySize) is the size of the page in that space.
- [UserSpaceToDisplay](cref:Meziantou.Framework.Pdf.PdfPage.UserSpaceToDisplay) goes the other way: transform the rectangle of an annotation with it to find where it is
  displayed.

## Stamp a page on other pages

[CreateFormFromPage](cref:Meziantou.Framework.Pdf.PdfDocument.CreateFormFromPage) makes a form XObject of the content of a page, of the document or of another one, to paint
it as a letterhead, an overlay or an underlay:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_StampPage;format=md-fenced-code-block;dedent=true -->
```csharp
// The page of another document becomes a form: painted on each page, under or over their content
var form = document.CreateFormFromPage(sourcePage);
foreach (var page in document.Pages)
{
    page.CreateContentBuilder().DrawForm(form);
}
```
<!-- endref -->

The form is upright, with its lower-left corner at the origin and the size of the page as it is displayed, whatever the
rotation of the page; paint it after a transformation to place it. It is stored once, however many pages paint it. The
annotations of the page are not part of it.
