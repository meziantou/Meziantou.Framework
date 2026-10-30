---
title: The object model
description: Read and change the dictionaries, arrays and streams of a document directly, for what the high-level API does not cover.
---

A PDF file is a graph of objects: dictionaries, arrays, streams, names, strings, numbers and references between them. The
high-level members ([Pages](cref:Meziantou.Framework.Pdf.PdfDocument.Pages), [Information](cref:Meziantou.Framework.Pdf.PdfDocument.Information), [Annotations](cref:Meziantou.Framework.Pdf.PdfPage.Annotations)...) read and write these objects; anything they do not cover
can be done with the objects themselves. The types are in the [Meziantou.Framework.Pdf.Objects](cref:Meziantou.Framework.Pdf.Objects) namespace.

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

- [PdfPage.Dictionary](cref:Meziantou.Framework.Pdf.PdfPage.Dictionary) is the page object, [PdfPage.Resources](cref:Meziantou.Framework.Pdf.PdfPage.Resources) its resources (inherited from the page tree when the page
  has none), [PdfDocument.Catalog](cref:Meziantou.Framework.Pdf.PdfDocument.Catalog) the root of the document, and [PdfDocument.Trailer](cref:Meziantou.Framework.Pdf.PdfDocument.Trailer) the trailer of the file.
- The value of an entry is often a [PdfReference](cref:Meziantou.Framework.Pdf.Objects.PdfReference) to an indirect object. The indexer returns the value as it is stored;
  [GetResolved](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetResolved) follows the reference, and [Get\<T\>](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.Get) and [TryGetResolved\<T\>](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.TryGetResolved) also check the type of the value.
- [GetName](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetName), [GetText](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetText), [GetNumber](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetNumber), [GetInteger](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetInteger) and [GetBoolean](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.GetBoolean) read the common values. A `string` converts to a
  [PdfName](cref:Meziantou.Framework.Pdf.Objects.PdfName), so `"Font"` is the name `/Font`.
- [PdfPage.GetContentData](cref:Meziantou.Framework.Pdf.PdfPage.GetContentData) returns the decoded content of the page, and [PdfStream.GetDecodedData](cref:Meziantou.Framework.Pdf.Objects.PdfStream.GetDecodedData) the decoded data of
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

- Set an entry of a dictionary with its indexer, or with [SetName](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.SetName), [SetText](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.SetText) and [SetNumber](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.SetNumber); remove it with [Remove](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary.Remove).
- `PdfDocument.Objects.Add` makes an object indirect and returns its reference, which other objects can point to.
- [PdfStream.SetData](cref:Meziantou.Framework.Pdf.Objects.PdfStream.SetData) replaces the data of a stream with data without filter, which the save compresses
  ([CompressStreams](cref:Meziantou.Framework.Pdf.PdfSaveOptions.CompressStreams)); [SetEncodedData](cref:Meziantou.Framework.Pdf.Objects.PdfStream.SetEncodedData) sets data that is already encoded, with its filter.
- The high-level members read and write the same objects: a change made through either is visible through both, and an
  incremental save writes the objects that changed.

## The types

| Type | PDF object |
| --- | --- |
| [PdfDictionary](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary) | A dictionary, whose entries keep their order |
| [PdfArray](cref:Meziantou.Framework.Pdf.Objects.PdfArray) | An array; [FromNumbers](cref:Meziantou.Framework.Pdf.Objects.PdfArray.FromNumbers) creates an array of numbers, such as a rectangle |
| [PdfStream](cref:Meziantou.Framework.Pdf.Objects.PdfStream) | A dictionary with data, such as a content stream, an image or a font file |
| [PdfName](cref:Meziantou.Framework.Pdf.Objects.PdfName) | A name, such as `/Type` |
| [PdfString](cref:Meziantou.Framework.Pdf.Objects.PdfString) | A string: [FromText](cref:Meziantou.Framework.Pdf.Objects.PdfString.FromText) and [ToText](cref:Meziantou.Framework.Pdf.Objects.PdfString.ToText) convert text, [FromBytes](cref:Meziantou.Framework.Pdf.Objects.PdfString.FromBytes) and [Bytes](cref:Meziantou.Framework.Pdf.Objects.PdfString.Bytes) raw bytes |
| [PdfInteger](cref:Meziantou.Framework.Pdf.Objects.PdfInteger), [PdfReal](cref:Meziantou.Framework.Pdf.Objects.PdfReal) | Numbers, which derive from [PdfNumber](cref:Meziantou.Framework.Pdf.Objects.PdfNumber) |
| [PdfBoolean](cref:Meziantou.Framework.Pdf.Objects.PdfBoolean), [PdfNull](cref:Meziantou.Framework.Pdf.Objects.PdfNull) | `true` and `false`, and the null object |
| [PdfReference](cref:Meziantou.Framework.Pdf.Objects.PdfReference) | A reference to an indirect object; [Resolve](cref:Meziantou.Framework.Pdf.Objects.PdfReference.Resolve) returns the object |

[PdfRectangle.ToArray](cref:Meziantou.Framework.Pdf.PdfRectangle.ToArray) and [PdfRectangle.TryRead](cref:Meziantou.Framework.Pdf.PdfRectangle.TryRead) convert rectangles from and to arrays.
