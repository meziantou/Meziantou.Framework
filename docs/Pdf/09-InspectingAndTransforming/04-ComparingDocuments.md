---
title: Comparing documents
description: Compare what the pages of two documents show, by kind of change, and read the changes as a diff.
---

[PdfDocumentComparer](cref:Meziantou.Framework.Pdf.PdfDocumentComparer) compares what the pages of two documents show, rather than their bytes or their pixels:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_CompareDocuments;format=md-fenced-code-block;dedent=true -->
```csharp
var expected = PdfDocument.Open(expectedFile);
var actual = PdfDocument.Open(actualFile);
var comparison = PdfDocumentComparer.Compare(expected, actual);
var diff = comparison.ToString();
```
<!-- endref -->

[ToString](cref:Meziantou.Framework.Pdf.PdfComparisonResult.ToString) writes the changes as a diff, a header per page that changed, then a line per change: `-` for an
element of the first document, `+` for an element of the second one, `~` for an element that moved:

For instance, `- text "Second line"`, followed by its bounding box, is a line of the first document that the second
one does not have, `+ stroke 0 G [0 0 200 200]` a path the second document strokes, and
`~ fill 1 0 0 rg [50 50 90 90] -> [60 50 100 90]` a path that moved.

## What is compared

The pages are compared in order, the page n of a document with the page n of the other one; the pages left over are
[Added](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Added) or [Removed](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Removed). [Changes](cref:Meziantou.Framework.Pdf.PdfPageComparison.Changes) combines the kinds of changes of a page:

| [PdfPageChangeKinds](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds) | Change |
| --- | --- |
| [Size](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Size), [Rotation](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Rotation) | The size of the crop box, the rotation |
| [Text](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Text) | Lines of text added or removed: a diff of the lines |
| [TextGeometry](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.TextGeometry) | The same lines, at other places |
| [Images](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Images), [ImagePlacements](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.ImagePlacements) | Images added, removed or changed (size in pixels, data); the same images moved |
| [Paths](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Paths) | Paths added, removed, moved, or painted with other colors |
| [Shadings](cref:Meziantou.Framework.Pdf.PdfPageChangeKinds.Shadings) | Shadings added, removed, changed or moved |

- Two documents written differently (object numbers, compression, object streams, linearization) that show the same
  elements are equivalent ([AreEquivalent](cref:Meziantou.Framework.Pdf.PdfComparisonResult.AreEquivalent)).
- Positions closer than [Tolerance](cref:Meziantou.Framework.Pdf.PdfComparisonOptions.Tolerance) (half a point by default) are the same. [MaxDetails](cref:Meziantou.Framework.Pdf.PdfComparisonOptions.MaxDetails)
  limits the lines given for each kind of change.
- The colors of the paths are compared by the operators that set them (`1 0 0 rg`), and the elements by their bounding
  boxes: a path whose shape changes inside the same box is not reported. Render the pages to compare their pixels.

## The elements of a page

[PdfPageElements.Create](cref:Meziantou.Framework.Pdf.PdfPageElements.Create) gives what the comparer compares: the lines of [text](cref:Meziantou.Framework.Pdf.PdfTextElement), the [images](cref:Meziantou.Framework.Pdf.PdfImageElement)
(size, digest of their data, position), the [paths](cref:Meziantou.Framework.Pdf.PdfPathElement) painted (bounding box, fill or stroke, colors) and the
[shadings](cref:Meziantou.Framework.Pdf.PdfShadingElement), in default user space, forms included. The snapshots of
`Meziantou.Framework.SnapshotTesting.Pdf` write them with the `Content` metadata, so that the diff of a snapshot shows
the same changes.
