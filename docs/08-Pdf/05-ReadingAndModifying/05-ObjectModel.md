---
title: The object model
description: Read and change the dictionaries, arrays and streams of a document directly, for what the high-level API does not cover.
---

A PDF file is a graph of objects: dictionaries, arrays, streams, names, strings, numbers and references between them. The
high-level members (`Pages`, `Information`, `Annotations`...) read and write these objects; anything they do not cover
can be done with the objects themselves. The types are in the `Meziantou.Framework.Pdf.Objects` namespace.

## Read objects

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_InspectFonts;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);

// The fonts of the resources of the pages: the /BaseFont of each font dictionary
var fonts = new SortedSet<string>(StringComparer.Ordinal);
foreach (var page in document.Pages)
{
    if (page.Resources?.GetResolved("Font") is not PdfDictionary pageFonts)
        continue;

    foreach (var name in pageFonts.Keys)
    {
        if (pageFonts.GetResolved(name) is PdfDictionary font && font.GetName("BaseFont") is { } baseFont)
        {
            fonts.Add(baseFont.Value);
        }
    }
}
```
<!-- endref -->

- `PdfPage.Dictionary` is the page object, `PdfPage.Resources` its resources (inherited from the page tree when the page
  has none), `PdfDocument.Catalog` the root of the document, and `PdfDocument.Trailer` the trailer of the file.
- The value of an entry is often a `PdfReference` to an indirect object. The indexer returns the value as it is stored;
  `GetResolved` follows the reference, and `Get<T>` and `TryGetResolved<T>` also check the type of the value.
- `GetName`, `GetText`, `GetNumber`, `GetInteger` and `GetBoolean` read the common values. A `string` converts to a
  `PdfName`, so `"Font"` is the name `/Font`.
- `PdfPage.GetContentData` returns the decoded content of the page, and `PdfStream.GetDecodedData` the decoded data of
  any stream.

## Change objects

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_LowLevelObjects;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.Letter);

// The catalog is a dictionary of the object model: entries the API has no member for can be set directly
document.Catalog[PdfName.Create("PageLayout")] = PdfName.Create("TwoColumnLeft");
var viewerPreferences = new PdfDictionary
{
    [PdfName.Create("DisplayDocTitle")] = PdfBoolean.True,
};
document.Catalog[PdfName.Create("ViewerPreferences")] = document.Objects.Add(viewerPreferences);
```
<!-- endref -->

- Set an entry of a dictionary with its indexer, or with `SetName`, `SetText` and `SetNumber`; remove it with `Remove`.
- `PdfDocument.Objects.Add` makes an object indirect and returns its reference, which other objects can point to.
- `PdfStream.SetData` replaces the data of a stream with data without filter, which the save compresses
  (`CompressStreams`); `SetEncodedData` sets data that is already encoded, with its filter.
- The high-level members read and write the same objects: a change made through either is visible through both, and an
  incremental save writes the objects that changed.

## The types

| Type | PDF object |
| --- | --- |
| `PdfDictionary` | A dictionary, whose entries keep their order |
| `PdfArray` | An array; `FromNumbers` creates an array of numbers, such as a rectangle |
| `PdfStream` | A dictionary with data, such as a content stream, an image or a font file |
| `PdfName` | A name, such as `/Type` |
| `PdfString` | A string: `FromText` and `ToText` convert text, `FromBytes` and `Bytes` raw bytes |
| `PdfInteger`, `PdfReal` | Numbers, which derive from `PdfNumber` |
| `PdfBoolean`, `PdfNull` | `true` and `false`, and the null object |
| `PdfReference` | A reference to an indirect object; `Resolve` returns the object |

`PdfRectangle.ToArray` and `PdfRectangle.TryRead` convert rectangles from and to arrays.
