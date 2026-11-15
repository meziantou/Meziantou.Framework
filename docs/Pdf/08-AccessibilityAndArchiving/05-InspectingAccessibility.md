---
title: Inspecting accessibility
description: Find the accessibility issues of an existing document (structure tree, language, title, untagged content, alternate texts, annotations, reading order) without changing it.
---

[PdfAccessibilityInspector](cref:Meziantou.Framework.Pdf.PdfAccessibilityInspector) reports the accessibility issues of a document that can be found automatically, from the
requirements of PDF/UA and Tagged PDF. It reads the document and does not change it:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_InspectAccessibility;format=md-fenced-code-block;dedent=true -->
```csharp
var report = PdfAccessibilityInspector.Inspect(PdfDocument.Open(file));
foreach (var issue in report.Issues)
{
    Assert.Fail(issue.ToString()); // such as "MissingAlternateText (page 1): A Figure has no alternate text (/Alt)"
}
```
<!-- endref -->

| [PdfAccessibilityIssueKind](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind) | Issue |
| --- | --- |
| [MissingStructureTree](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.MissingStructureTree) | The document is not tagged: it has no structure tree |
| [NotMarkedAsTagged](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.NotMarkedAsTagged) | The catalog does not declare the document as tagged (`/MarkInfo`) |
| [MissingLanguage](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.MissingLanguage), [MissingTitle](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.MissingTitle) | The document has no natural language, or no title |
| [TitleNotDisplayed](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.TitleNotDisplayed) | The viewer shows the file name instead of the title |
| [UntaggedContent](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.UntaggedContent) | Content of a page is neither tagged nor an artifact |
| [MissingAlternateText](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.MissingAlternateText) | A figure or a formula has no alternate text |
| [UntaggedAnnotation](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.UntaggedAnnotation), [MissingTabOrder](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.MissingTabOrder) | An annotation is not in the structure tree; a page with annotations does not order them by the structure |
| [ReadingOrder](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.ReadingOrder) | The structure reads the content of a page in another order than the content draws it |
| [UnknownStructureType](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.UnknownStructureType) | A structure type is neither standard nor mapped to a standard type by the role map |
| [SkippedHeadingLevel](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssueKind.SkippedHeadingLevel) | A heading level is skipped, such as an `H3` after an `H1` |

- Each [PdfAccessibilityIssue](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssue) has its [page](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssue.PageIndex), or none for the issues of the document, and the
  [structure type](cref:Meziantou.Framework.Pdf.PdfAccessibilityIssue.StructureType) it concerns.
- The content of an untagged document is not checked page by page: the whole document is untagged.
- The reading order is a heuristic: a document may draw its content in another order than it should be read on
  purpose. Review the pages it reports.
- The inspector checks what can be checked automatically: it does not judge whether an alternate text describes its
  figure. A document that must conform to PDF/UA should be validated with [veraPDF](https://verapdf.org) too. To write
  tagged documents, see [Tagged PDF and PDF/UA](02-TaggedPdfAndPdfUA.md).
