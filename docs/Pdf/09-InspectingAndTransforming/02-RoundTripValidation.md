---
title: Round-trip validation
description: Check that the writer is deterministic and keeps the pages of a document, with a typed failure when it does not.
---

[PdfRoundTripValidator](cref:Meziantou.Framework.Pdf.PdfRoundTripValidator) writes a document, reads the output back, writes it again, and compares the two
outputs:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfWriterTests.cs;region=Pdf_ValidateRoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var result = PdfRoundTripValidator.Validate(file);
Assert.True(result.Succeeded, result.Failure?.ToString()); // Failure.Kind tells which check failed
```
<!-- endref -->

Use it in the tests of an application that changes documents: a change that the writer cannot write back the same way
(a damaged object, a value of the wrong type) shows up as a failure rather than as a file that changes each time it is
saved.

## What is checked

The checks run in order, and [Failure](cref:Meziantou.Framework.Pdf.PdfRoundTripResult.Failure) is the first one that fails:

| [PdfRoundTripFailureKind](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind) | Meaning |
| --- | --- |
| [ReadBackFailed](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.ReadBackFailed) | An output cannot be read, for instance an encrypted output read without its password |
| [ReadBackRepaired](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.ReadBackRepaired) | An output is damaged: the reader repairs it |
| [PageCountChanged](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.PageCountChanged) | An output does not have the pages of the document it was written from |
| [PageChanged](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.PageChanged) | A page changed: its media box or crop box, rotation, number of annotations or decoded content ([PageIndex](cref:Meziantou.Framework.Pdf.PdfRoundTripFailure.PageIndex)) |
| [ObjectGraphChanged](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.ObjectGraphChanged) | The two outputs do not have the same objects |
| [OutputsDiffer](cref:Meziantou.Framework.Pdf.PdfRoundTripFailureKind.OutputsDiffer) | The two outputs have the same objects but different bytes |

- The boxes of the first output may differ from those of the document by the rounding of the numbers: the writer
  writes at most six decimals.
- The bytes of an encrypted document are not compared ([ComparesBytes](cref:Meziantou.Framework.Pdf.PdfRoundTripResult.ComparesBytes) is `false`): each save encrypts with new
  random initialization vectors. Pass the password in the read options to read the outputs.
- [Validate](cref:Meziantou.Framework.Pdf.PdfRoundTripValidator.Validate) takes the options of both saves, so that a linearized file or a file without object streams
  can be checked too.

## Summaries of documents

The result has a [PdfDocumentSummary](cref:Meziantou.Framework.Pdf.PdfDocumentSummary) of the document and of each output: the [pages](cref:Meziantou.Framework.Pdf.PdfDocumentSummary.Pages) (boxes,
rotation, annotations and a digest of the content), the number of objects by kind and by `/Type`, and a
[Digest](cref:Meziantou.Framework.Pdf.PdfDocumentSummary.Digest) of the object graph. The digest does not depend on the numbers of the objects or on the way they are
stored (object streams, cross-reference streams, linearization): two documents with the same digest have the same
objects. [PdfDocumentSummary.Create](cref:Meziantou.Framework.Pdf.PdfDocumentSummary.Create) summarizes any document.
