---
title: Errors and limits
description: The exceptions the library throws, and how to open the files of untrusted users with bounded memory and time.
---

## Handle the errors of a file

Each kind of problem throws its own exception, and they all derive from `PdfException`. An application that accepts
files from its users can tell them what is wrong:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_DescribeUpload;format=md-fenced-code-block;dedent=true -->
```csharp
public static async Task<string> DescribeUploadAsync(Stream upload, CancellationToken cancellationToken)
{
    // Lower limits for the files of unknown users: 16 MiB per decoded stream
    var options = new PdfReadOptions { MaxDecodedStreamLength = 16 * 1024 * 1024 };
    try
    {
        var document = await PdfDocument.OpenAsync(upload, options, cancellationToken);
        return $"{document.Pages.Count} pages";
    }
    catch (PdfPasswordException)
    {
        return "The document needs a password";
    }
    catch (PdfResourceLimitException)
    {
        return "The document is too large";
    }
    catch (PdfException)
    {
        // InvalidPdfContentException: not a PDF document, or damaged beyond repair
        // UnsupportedPdfFeatureException: a feature the library does not support
        return "The file is not a PDF document that can be read";
    }
}
```
<!-- endref -->

| Exception | Thrown when |
| --- | --- |
| `InvalidPdfContentException` | The data is not a PDF document, or it is damaged beyond repair |
| `UnsupportedPdfFeatureException` | The document uses a feature the library does not support, such as a security handler other than the standard one; or a font cannot be embedded |
| `PdfResourceLimitException` | The document exceeds a limit of `PdfReadOptions`, or a page exceeds a limit of the rendering |
| `PdfPasswordException` | The document is encrypted, and the password of the options is neither its user nor its owner password |

The mistakes of the code that uses the library throw the usual exceptions: `ArgumentException` and its derived types
for a wrong argument, `InvalidOperationException` for a call that is not allowed in the current state (a text operator
outside a text object, an incremental save of a repaired document).

## Untrusted files

A file can be written to make a reader use a lot of memory or time: a small stream that decodes to gigabytes, objects
nested very deeply, a page tree or an outline that loops. Everything a file can make grow is bounded, so that a file
takes time and memory in proportion to its size, and the loops are detected.

- Lower `MaxDecodedStreamLength` (256 MiB by default) to the size of the documents you expect: it is checked while a
  stream is decoded, so the data is never decoded past the limit.
- A page that is rendered is bounded by `PdfRenderOptions.MaxOperations`, and the extraction of its text by a limit of
  its own: see [Rendering](../06-Rendering/01-Overview.md).
- No other exception than those above escapes the reader on a damaged or hostile file: the library is tested against
  public corpora of broken files and by fuzzing.
- The embedded files and the text of a document are data: never execute them, and escape them before showing them in
  a web page.
