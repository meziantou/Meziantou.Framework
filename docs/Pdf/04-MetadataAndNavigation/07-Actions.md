---
title: Actions
description: Read and set what viewers do when a link is clicked, a bookmark is chosen, the document or a page is opened, or a form field gets the focus.
---

## Set actions

An action is what a viewer does on an event: go to a page, open a URI, run a script, reset a form... The static methods
of [PdfAction](cref:Meziantou.Framework.Pdf.PdfAction) create them, and the objects that have actions take them:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_Actions;format=md-fenced-code-block;dedent=true -->
```csharp
// Open the document on its last page, and run a script before it is printed
document.OpenAction = PdfAction.GoTo(PdfDestination.Fit(last));
document.AdditionalActions[PdfActionTrigger.DocumentPrint] = PdfAction.JavaScript("app.alert('Printing')");

// A link that goes to the next page, then opens a website
var link = page.Annotations.AddLink(new PdfRectangle(72, 700, 200, 720), new Uri("https://example.com"));
var next = PdfAction.Named("NextPage");
next.SetNext(PdfAction.OpenUri(new Uri("https://www.meziantou.net")));
link.Action = next;

// A bookmark that resets the form
document.Outlines.Add("Reset").Action = PdfAction.ResetForm();
```
<!-- endref -->

| Method | Action |
| --- | --- |
| [GoTo](cref:Meziantou.Framework.Pdf.PdfAction.GoTo) | Go to a destination of the document |
| [GoToRemote](cref:Meziantou.Framework.Pdf.PdfAction.GoToRemote) | Open a page of another PDF file |
| [Launch](cref:Meziantou.Framework.Pdf.PdfAction.Launch) | Open a file with the application of the system |
| [OpenUri](cref:Meziantou.Framework.Pdf.PdfAction.OpenUri) | Open a URI |
| [Named](cref:Meziantou.Framework.Pdf.PdfAction.Named) | A command of the viewer: `NextPage`, `PrevPage`, `FirstPage`, `LastPage` |
| [SubmitForm](cref:Meziantou.Framework.Pdf.PdfAction.SubmitForm), [ResetForm](cref:Meziantou.Framework.Pdf.PdfAction.ResetForm), [ImportData](cref:Meziantou.Framework.Pdf.PdfAction.ImportData) | Send, reset or import the values of the form fields |
| [Hide](cref:Meziantou.Framework.Pdf.PdfAction.Hide) | Hide or show form fields |
| [SetOptionalContentState](cref:Meziantou.Framework.Pdf.PdfAction.SetOptionalContentState) | Turn layers on or off: see [Layers](08-Layers.md) |
| [JavaScript](cref:Meziantou.Framework.Pdf.PdfAction.JavaScript) | Run a script |

[SetNext](cref:Meziantou.Framework.Pdf.PdfAction.SetNext) chains actions: they run in order.

## Where actions go

| Object | Property |
| --- | --- |
| The document, when it is opened | [PdfDocument.OpenAction](cref:Meziantou.Framework.Pdf.PdfDocument.OpenAction) |
| The document, before it is closed, saved or printed | [PdfDocument.AdditionalActions](cref:Meziantou.Framework.Pdf.PdfDocument.AdditionalActions) |
| A page, when it is opened or closed | [PdfPage.AdditionalActions](cref:Meziantou.Framework.Pdf.PdfPage.AdditionalActions) |
| A link | [PdfLinkAnnotation.Action](cref:Meziantou.Framework.Pdf.PdfLinkAnnotation.Action) |
| A bookmark | [PdfOutlineItem.Action](cref:Meziantou.Framework.Pdf.PdfOutlineItem.Action) |
| A widget, such as a push button | [PdfWidgetAnnotation.Action](cref:Meziantou.Framework.Pdf.PdfWidgetAnnotation.Action) |
| An annotation, when the mouse enters it, it gets the focus... | [PdfAnnotation.AdditionalActions](cref:Meziantou.Framework.Pdf.PdfAnnotation.AdditionalActions) |

- The additional actions are indexed by [PdfActionTrigger](cref:Meziantou.Framework.Pdf.PdfActionTrigger); [SupportedTriggers](cref:Meziantou.Framework.Pdf.PdfAdditionalActions.SupportedTriggers) lists the events of the
  object, and [Triggers](cref:Meziantou.Framework.Pdf.PdfAdditionalActions.Triggers) those that have an action. Setting `null` removes an action.
- A link or a bookmark has a destination or an action: setting one removes the other. The destination of a link, or
  of the open action, is read as a [PdfGoToAction](cref:Meziantou.Framework.Pdf.PdfGoToAction).
- An action that refers to the pages or the layers of another document is refused.

## Read the actions of a file

Each action is an instance of the class of its type: [PdfGoToAction](cref:Meziantou.Framework.Pdf.PdfGoToAction), [PdfGoToRemoteAction](cref:Meziantou.Framework.Pdf.PdfGoToRemoteAction),
[PdfGoToEmbeddedAction](cref:Meziantou.Framework.Pdf.PdfGoToEmbeddedAction), [PdfLaunchAction](cref:Meziantou.Framework.Pdf.PdfLaunchAction), [PdfUriAction](cref:Meziantou.Framework.Pdf.PdfUriAction), [PdfNamedAction](cref:Meziantou.Framework.Pdf.PdfNamedAction),
[PdfSubmitFormAction](cref:Meziantou.Framework.Pdf.PdfSubmitFormAction), [PdfResetFormAction](cref:Meziantou.Framework.Pdf.PdfResetFormAction), [PdfImportDataAction](cref:Meziantou.Framework.Pdf.PdfImportDataAction), [PdfHideAction](cref:Meziantou.Framework.Pdf.PdfHideAction),
[PdfSetOptionalContentStateAction](cref:Meziantou.Framework.Pdf.PdfSetOptionalContentStateAction) and [PdfJavaScriptAction](cref:Meziantou.Framework.Pdf.PdfJavaScriptAction). The threads, sounds, movies, renditions, transitions,
3D views and rich media are [PdfUnknownAction](cref:Meziantou.Framework.Pdf.PdfUnknownAction): read their [Dictionary](cref:Meziantou.Framework.Pdf.PdfAction.Dictionary).

[Next](cref:Meziantou.Framework.Pdf.PdfAction.Next) reads the actions that follow an action. The sequence is bounded: an action reached twice ends it.
To run the scripts of the form fields, see [Form scripts](../05-ReadingAndModifying/10-FormScripts.md).
