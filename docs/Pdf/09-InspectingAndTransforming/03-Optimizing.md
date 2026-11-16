---
title: Optimizing documents
description: Plan the changes that make a document smaller or remove what it does not need, inspect the plan, apply it, and repair harmless issues of a file.
---

[PdfOptimizer](cref:Meziantou.Framework.Pdf.PdfOptimizer) works in two steps: [CreatePlan](cref:Meziantou.Framework.Pdf.PdfOptimizer.CreatePlan) lists the changes the options ask for without making
them, and [Apply](cref:Meziantou.Framework.Pdf.PdfOptimizer.Apply) makes those that are enabled:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OptimizeDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var plan = PdfOptimizer.CreatePlan(document, new PdfOptimizationOptions
{
    RemoveMetadata = true,
    RemoveJavaScript = true,
    RemoveAttachments = true,
    RemovePageThumbnails = true,
    RemoveComments = true,
    RemoveBookmarks = true,
    PruneUnusedResources = true,
    PruneUnreachableObjects = true,
});

// Inspect the plan, and leave out what must be kept
var json = plan.ToJson(indented: true);
foreach (var action in plan.Actions.Where(action => action.Kind == PdfOptimizationActionKind.RemoveBookmarks))
{
    action.IsEnabled = false;
}

var applied = PdfOptimizer.Apply(plan);
var optimized = document.ToByteArray();
```
<!-- endref -->

The plan is a list of [PdfOptimizationAction](cref:Meziantou.Framework.Pdf.PdfOptimizationAction): the [kind](cref:Meziantou.Framework.Pdf.PdfOptimizationAction.Kind) of change, a description, and the page,
object or resource it concerns. Show it to a user, log it, or keep it with [ToJson](cref:Meziantou.Framework.Pdf.PdfOptimizationPlan.ToJson); set
[IsEnabled](cref:Meziantou.Framework.Pdf.PdfOptimizationAction.IsEnabled) to `false` to leave an action out. [Apply](cref:Meziantou.Framework.Pdf.PdfOptimizer.Apply) returns the actions that changed the document.
The changes are made to the document: save it to write them.

## What can be planned

Every option of [PdfOptimizationOptions](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions) is off by default:

| Option | Changes |
| --- | --- |
| [RemoveMetadata](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveMetadata) | The document information dictionary and the XMP metadata |
| [RemoveJavaScript](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveJavaScript) | The scripts of the document, and the actions that run scripts: of the document, its pages, annotations and fields |
| [RemoveAttachments](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveAttachments) | The embedded files and the file attachment annotations |
| [RemovePageThumbnails](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemovePageThumbnails) | The thumbnail images of the pages |
| [RemoveComments](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveComments) | The markup annotations (notes, highlights, stamps, drawings...) and their pop-ups; links and widgets are kept |
| [RemoveOpenAction](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveOpenAction) | The action run when the document is opened |
| [RemoveXfa](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveXfa) | The XFA form; the AcroForm fields are kept |
| [RemoveFormFields](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveFormFields) | The interactive form and the widgets of its fields. To keep the values on the pages, [flatten the form](../05-ReadingAndModifying/09-Forms.md#flatten-a-form) instead |
| [RemoveBookmarks](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.RemoveBookmarks) | The outline |
| [FlattenOptionalContent](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.FlattenOptionalContent) | The layers: the content hidden in the default configuration is removed, the visible content is kept as ordinary content |
| [PruneUnusedResources](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.PruneUnusedResources) | The fonts, images, graphics states... of the pages that their content does not use |
| [PruneUnreachableObjects](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.PruneUnreachableObjects) | The objects that nothing refers to |
| [CompressStreams](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.CompressStreams) | The streams without filter, compressed with `FlateDecode` when it makes them smaller |
| [MaxImageResolution](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.MaxImageResolution) | The images shown at a higher resolution than this one, resampled to it |

- A full save writes only the objects that can be reached and compresses the streams
  ([CompressStreams](cref:Meziantou.Framework.Pdf.PdfSaveOptions.CompressStreams)): pruning the unreachable objects and compressing the streams matter for an
  [incremental save](../05-ReadingAndModifying/04-ModifyingDocuments.md), which writes the objects removed as free
  entries and the streams as they are.
- The unused resources are removed from the resource dictionaries that only pages use, by the names of their content.
  A dictionary that a form, a pattern or an annotation shares with the pages is kept as it is, and so are the resources
  of a page whose content cannot be read.
- The resolution of an image is the highest of its placements on the pages. The images of 8 bits per component in
  gray or RGB (device or ICC-based color spaces) are resampled; the others (CMYK, indexed, masks, decode arrays) are
  kept. A JPEG image is compressed as JPEG again, the others without loss. A resampled image is stored in sRGB.
- The optional content is evaluated with the default configuration of the document, without its visibility
  expressions. The hidden content still changes the graphics state (a transformation, a clipping, a color): only its
  painting is removed. The layers of the document are removed only when the optional content of every page was
  flattened: not when a page action is disabled, or when visible text continues hidden text on the same line, whose
  position would change.
- The default color spaces (`DefaultGray`, `DefaultRGB`, `DefaultCMYK`) are always kept: the content uses them
  without naming them.

## Signed documents

Any change to a signed document invalidates its signatures. [HasSignatures](cref:Meziantou.Framework.Pdf.PdfOptimizationPlan.HasSignatures) tells whether the document has
signed fields, and [InvalidatesSignatures](cref:Meziantou.Framework.Pdf.PdfOptimizationPlan.InvalidatesSignatures) whether the plan changes it: [Apply](cref:Meziantou.Framework.Pdf.PdfOptimizer.Apply) then throws
`InvalidOperationException`, unless the plan was created with
[AllowSignatureInvalidation](cref:Meziantou.Framework.Pdf.PdfOptimizationOptions.AllowSignatureInvalidation).

## Repair harmless issues

[PdfSaveSanitizer](cref:Meziantou.Framework.Pdf.PdfSaveSanitizer) repairs issues that are harmless but that some readers handle badly, with one incremental
update that keeps the bytes of the file:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_SanitizeFile;format=md-fenced-code-block;dedent=true -->
```csharp
var result = PdfSaveSanitizer.Sanitize(data);
var repaired = result.Output;
```
<!-- endref -->

| [PdfSanitizerRepairKind](cref:Meziantou.Framework.Pdf.PdfSanitizerRepairKind) | Repair |
| --- | --- |
| [DanglingOutlineRoot](cref:Meziantou.Framework.Pdf.PdfSanitizerRepairKind.DanglingOutlineRoot) | The outline entry of the catalog refers to something that is not an outline: it is removed |
| [EmptyOutlineRoot](cref:Meziantou.Framework.Pdf.PdfSanitizerRepairKind.EmptyOutlineRoot) | The outline has no item: it is removed |
| [DegenerateCropBox](cref:Meziantou.Framework.Pdf.PdfSanitizerRepairKind.DegenerateCropBox) | The crop box of a page has no area inside the media box: it is removed, so the page shows its media box |
| [CropBoxOutsideMediaBox](cref:Meziantou.Framework.Pdf.PdfSanitizerRepairKind.CropBoxOutsideMediaBox) | The crop box extends outside the media box: it becomes its intersection with the media box, which is what readers show |

A file without these issues is returned as it is, byte for byte ([HasChanges](cref:Meziantou.Framework.Pdf.PdfSanitizationResult.HasChanges) is `false`). A damaged file, whose
cross-reference information the reader rebuilt, cannot be updated incrementally: [Sanitize](cref:Meziantou.Framework.Pdf.PdfSaveSanitizer.Sanitize) throws
`InvalidOperationException` when it needs repairs.
