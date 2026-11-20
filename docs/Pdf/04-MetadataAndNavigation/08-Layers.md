---
title: Layers
description: Put content and annotations in optional content groups that viewers let the user show or hide, and read the layers of a file.
---

## Add layers

An optional content group is a layer: viewers list the groups and let the user show or hide them. Add a group with
[AddGroup](cref:Meziantou.Framework.Pdf.PdfOptionalContentProperties.AddGroup), and draw its content between [BeginOptionalContent](cref:Meziantou.Framework.Pdf.PdfContentBuilder.BeginOptionalContent) and [EndMarkedContent](cref:Meziantou.Framework.Pdf.PdfContentBuilder.EndMarkedContent):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_Layers;format=md-fenced-code-block;dedent=true -->
```csharp
var notes = document.OptionalContent.AddGroup("Notes");
var draft = document.OptionalContent.AddGroup("Draft watermark", visible: false);
page.CreateContentBuilder()
    .BeginOptionalContent(notes).SetFillColor(PdfColor.FromRgb(0, 0, 1)).Rectangle(0, 0, 50, 50).Fill().EndMarkedContent()
    .BeginOptionalContent(draft).SetFillColor(PdfColor.FromRgb(1, 0, 0)).Rectangle(50, 50, 50, 50).Fill().EndMarkedContent();

// The user cannot hide the notes, and the viewer shows the list of layers
document.OptionalContent.DefaultConfiguration!.SetLocked(notes, locked: true);
document.PageMode = PdfPageMode.UseOptionalContent;
```
<!-- endref -->

- A group added hidden is off when the document is opened: [IsVisible](cref:Meziantou.Framework.Pdf.PdfOptionalContentGroup.IsVisible) reads and changes its state in the default
  configuration.
- An annotation belongs to a group with [PdfAnnotation.OptionalContent](cref:Meziantou.Framework.Pdf.PdfAnnotation.OptionalContent).
- [PdfPageMode.UseOptionalContent](cref:Meziantou.Framework.Pdf.PdfPageMode.UseOptionalContent) opens the list of layers when the document is opened.
- A [SetOptionalContentState](cref:Meziantou.Framework.Pdf.PdfAction.SetOptionalContentState) action turns groups on or off, for a link or a button: see [Actions](07-Actions.md).

## Configurations

The default configuration ([DefaultConfiguration](cref:Meziantou.Framework.Pdf.PdfOptionalContentProperties.DefaultConfiguration)) gives the state of the groups when the document is opened:

- [BaseState](cref:Meziantou.Framework.Pdf.PdfOptionalContentConfiguration.BaseState) is the state of the groups the configuration does not list; [IsVisible](cref:Meziantou.Framework.Pdf.PdfOptionalContentConfiguration.IsVisible) and [SetVisible](cref:Meziantou.Framework.Pdf.PdfOptionalContentConfiguration.SetVisible) read and
  change the state of a group.
- [SetLocked](cref:Meziantou.Framework.Pdf.PdfOptionalContentConfiguration.SetLocked) keeps the user from changing a group.
- [AddRadioButtonGroup](cref:Meziantou.Framework.Pdf.PdfOptionalContentConfiguration.AddRadioButtonGroup) makes groups behave as radio buttons: turning one on turns the others off.

The other configurations of a file, which viewers can offer, are in [Configurations](cref:Meziantou.Framework.Pdf.PdfOptionalContentProperties.Configurations).

## Render and flatten layers

The renderer draws the groups that are on in the default configuration, or in a configuration of
[PdfRenderOptions](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions): see [Layers and annotations](../06-Rendering/04-LayersAndAnnotations.md). The optimizer can remove the hidden
content and the groups: see [Optimizing](../09-InspectingAndTransforming/03-Optimizing.md).
