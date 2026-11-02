---
title: Forms
description: Read the fields of an interactive form, fill them, and flatten the form into the content of its pages.
---

[PdfDocument.Form](cref:Meziantou.Framework.Pdf.PdfDocument.Form) is the interactive form of a document (AcroForm): its fields, the values of the fields and the
widgets that show them on the pages.

## Fill a form

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_FillForm;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(data);
var form = document.Form;
form.FindField("name")!.Value = "Jane Doe";
((PdfCheckBoxField)form.FindField("agree")!).IsChecked = true;
form.FindField("size")!.Value = "L";
form.FindField("country")!.Value = "de";
((PdfChoiceField)form.FindField("colors")!).SetSelectedValues(["red", "blue"]);

// The values and the appearances of the fields are appended: the signatures stay valid
using var output = new MemoryStream();
document.SaveIncremental(output);
```
<!-- endref -->

- [FindField](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.FindField) finds a field by its fully qualified name, the names of the field and its parents separated by
  periods (`address.city`). [GetFields](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.GetFields) returns the fields that have a value, [Fields](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.Fields) the fields at the top of the
  hierarchy.
- Setting a value writes the value and the appearance of the widgets of the field: the text of a text field in the font,
  size and color of its default appearance (a standard 14 font), the state of a check box or of radio buttons, the
  chosen options of a choice field. The other fields do not change.
- A read-only field throws `InvalidOperationException`; a value that is not an option of a choice field or of radio
  buttons throws `ArgumentException`.
- An incremental save appends the changes: the signatures of the document stay valid.

| Field | Class | Value |
| --- | --- | --- |
| Text field | [PdfTextField](cref:Meziantou.Framework.Pdf.PdfTextField) | The text; multiline, password and comb fields are drawn as such |
| Check box | [PdfCheckBoxField](cref:Meziantou.Framework.Pdf.PdfCheckBoxField) | [IsChecked](cref:Meziantou.Framework.Pdf.PdfCheckBoxField.IsChecked), or the on state and `Off` |
| Radio buttons | [PdfRadioButtonField](cref:Meziantou.Framework.Pdf.PdfRadioButtonField) | One of the [Options](cref:Meziantou.Framework.Pdf.PdfRadioButtonField.Options), or `null` |
| List box, combo box | [PdfChoiceField](cref:Meziantou.Framework.Pdf.PdfChoiceField) | The export value of an option; [SetSelectedValues](cref:Meziantou.Framework.Pdf.PdfChoiceField.SetSelectedValues) for several |
| Push button | [PdfPushButtonField](cref:Meziantou.Framework.Pdf.PdfPushButtonField) | None |
| Signature | [PdfSignatureField](cref:Meziantou.Framework.Pdf.PdfSignatureField) | [IsSigned](cref:Meziantou.Framework.Pdf.PdfSignatureField.IsSigned); sign with [PdfSigner](cref:Meziantou.Framework.Pdf.Signing.PdfSigner) |

## Flatten a form

[Flatten](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.Flatten) draws the appearance of each widget in the content of its page and removes the fields, so that the
values become content that cannot be changed:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_FlattenForm;format=md-fenced-code-block;dedent=true -->
```csharp
// The fields are drawn on their pages and removed: the values cannot be changed anymore
document.Form.Flatten();
```
<!-- endref -->

- The text and choice fields without appearance, or all of them when [NeedAppearances](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.NeedAppearances) is set, get the appearance of
  their value first. The hidden widgets are not drawn.
- The signed signature fields are kept, so that the signatures stay. The form is removed when no field is left.

The API does not create fields: see [Limitations](../09-Limitations.md).
