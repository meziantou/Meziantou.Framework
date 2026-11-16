---
title: Form scripts
description: Read the scripts of a form, and run its keystroke, validation, calculation and format scripts in a sandbox when values are set.
---

Many forms calculate fields from others (a total from quantities and prices) and format values (currencies, dates) with
JavaScript. `Meziantou.Framework.Pdf` reads these scripts; `Meziantou.Framework.Pdf.JavaScript` runs them.

```bash
dotnet add package Meziantou.Framework.Pdf.JavaScript
```

## Read the scripts

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_ReadFormScripts;format=md-fenced-code-block;dedent=true -->
```csharp
var total = form.FindField("Total")!;
var calculate = total.Actions.Calculate?.Script;
var order = form.CalculationOrder;
var documentScripts = document.JavaScripts;
```
<!-- endref -->

- [Actions](cref:Meziantou.Framework.Pdf.PdfFormField.Actions) gives the [keystroke](cref:Meziantou.Framework.Pdf.PdfFieldActions.Keystroke), [format](cref:Meziantou.Framework.Pdf.PdfFieldActions.Format), [validation](cref:Meziantou.Framework.Pdf.PdfFieldActions.Validate) and
  [calculation](cref:Meziantou.Framework.Pdf.PdfFieldActions.Calculate) scripts of a field.
- [CalculationOrder](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.CalculationOrder) lists the calculated fields in the order a viewer calculates them.
- [JavaScripts](cref:Meziantou.Framework.Pdf.PdfDocument.JavaScripts) gives the document-level scripts, which usually declare the functions the fields call.
- [PdfTextField.SetValue](cref:Meziantou.Framework.Pdf.PdfTextField.SetValue) sets a value and the text its appearance shows, as a format script does: the value keeps
  `1234.5` while the field shows `$1,234.50`.

## Run the scripts

[CreateScriptSession](cref:Meziantou.Framework.Pdf.JavaScript.PdfInteractiveFormScriptExtensions.CreateScriptSession) creates a sandbox that runs the scripts when values are set through it:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.JavaScript.Tests/PdfFormScriptSessionTests.cs;region=PdfJavaScript_SetValue;format=md-fenced-code-block;dedent=true -->
```csharp
using var scripts = document.Form.CreateScriptSession(new PdfFormScriptOptions
{
    Timeout = TimeSpan.FromSeconds(1),
    MaxStatements = 1_000_000,
    MemoryLimit = 64 * 1024 * 1024,
});

scripts.SetValue("Price", "12.5");
var result = scripts.SetValue("Quantity", "3");
```
<!-- endref -->

[SetValue](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptSession.SetValue) runs the scripts as a viewer does when a user commits a value:

1. The keystroke script of the field, then its validation script: when one sets `event.rc = false`, the value is
   rejected ([IsAccepted](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptResult.IsAccepted) is `false`) and nothing changes.
2. The calculation scripts of the form, in the calculation order.
3. The format scripts of the fields that changed: their appearances show the text the script gives.

[Changes](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptResult.Changes) lists each field that changed, with its old value, new value and displayed text.
[SetValues](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptSession.SetValues) sets several values and calculates once at the end; [Recalculate](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptSession.Recalculate) runs the
calculations alone. Save the document to keep the values.

## What the scripts can do

- The scripts never run by themselves: opening a document or setting [Value](cref:Meziantou.Framework.Pdf.PdfFormField.Value) runs none. The document-level
  scripts run once, when the session is created; the open action never runs. The document-level scripts and the field
  scripts share the document object (`this`): what a document-level script declares or sets on `this` is seen by the
  field scripts.
- Setting a field to the value it already has changes nothing: its appearance, written by its format script, is kept.
- They see the `event` object, the fields (`this.getField`, with `value`, `valueAsString`, `type`, `readonly`...), the
  AF functions of the viewers (`AFSimple_Calculate`, `AFNumber_Format`, `AFNumber_Keystroke`, `AFPercent_*`,
  `AFDate_*`, `AFTime_*`, `AFSpecial_*`, `AFRange_Validate`, `AFMergeChange`), `util.printf`, `util.printd`,
  `util.scand` and `app.alert`, which calls [Alert](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptOptions.Alert).
- The calls that reach outside of the form (submitting, mailing, printing, opening a URL or a document, menu items) do
  nothing, and are reported in the [diagnostics](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptResult.Diagnostics).
- Each script is limited in time, statements and memory ([PdfFormScriptOptions](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptOptions)). An error or a limit exceeded is a
  diagnostic, and the other scripts run, as in the viewers; set [ThrowOnScriptError](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptOptions.ThrowOnScriptError) to throw instead.
- The numbers and the dates are formatted with fixed rules, not with the culture of the machine: the results are the
  same everywhere. A script may set a read-only field, as calculations do; [SetValue](cref:Meziantou.Framework.Pdf.JavaScript.PdfFormScriptSession.SetValue) refuses it.
- Only the commit of a value runs the keystroke script (`event.willCommit` is `true`): there is no typing.
- The appearance properties a script sets on a field (`textColor`, `display`...) are ignored.
