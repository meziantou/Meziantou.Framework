---
title: Forms
description: Create the fields of an interactive form, read and fill them, and flatten the form into the content of its pages.
---

[PdfDocument.Form](cref:Meziantou.Framework.Pdf.PdfDocument.Form) is the interactive form of a document (AcroForm): its fields, the values of the fields and the
widgets that show them on the pages.

## Create a form

The `Add` methods of [PdfInteractiveForm](cref:Meziantou.Framework.Pdf.PdfInteractiveForm) create a field, its widget on a page, and its appearance:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_CreateForm;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var page = document.Pages.Add(PdfSize.A4);
var form = document.Form;
form.AddTextField(page, "name", new PdfRectangle(50, 780, 300, 800), new PdfTextFieldOptions { Value = "Jane Doe", IsRequired = true });
form.AddTextField(page, "address.city", new PdfRectangle(50, 750, 300, 770));
form.AddCheckBox(page, "agree", new PdfRectangle(50, 720, 65, 735), new PdfCheckBoxOptions { IsChecked = true });
form.AddRadioButtons("size",
[
    new PdfRadioButton(page, new PdfRectangle(50, 690, 65, 705), "S"),
    new PdfRadioButton(page, new PdfRectangle(80, 690, 95, 705), "L"),
], new PdfRadioButtonOptions { Value = "S" });
form.AddComboBox(page, "country", new PdfRectangle(50, 650, 200, 670), [new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], new PdfChoiceFieldOptions { SelectedValues = ["fr"] });
form.AddSignatureField(page, "signature", new PdfRectangle(50, 550, 250, 620));

var data = document.ToByteArray();
```
<!-- endref -->

| Method | Field | Options |
| --- | --- | --- |
| [AddTextField](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddTextField) | Text field | [PdfTextFieldOptions](cref:Meziantou.Framework.Pdf.PdfTextFieldOptions): value, multiline, password, maximum length, comb, alignment |
| [AddCheckBox](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddCheckBox) | Check box | [PdfCheckBoxOptions](cref:Meziantou.Framework.Pdf.PdfCheckBoxOptions): checked, on state, symbol |
| [AddRadioButtons](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddRadioButtons) | Radio buttons, one widget per [PdfRadioButton](cref:Meziantou.Framework.Pdf.PdfRadioButton) | [PdfRadioButtonOptions](cref:Meziantou.Framework.Pdf.PdfRadioButtonOptions): chosen option, toggle off, symbol |
| [AddComboBox](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddComboBox), [AddListBox](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddListBox) | Choice field | [PdfChoiceFieldOptions](cref:Meziantou.Framework.Pdf.PdfChoiceFieldOptions): chosen values, editable, multi-select, alignment |
| [AddSignatureField](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddSignatureField) | Signature field, not signed | [PdfFormFieldOptions](cref:Meziantou.Framework.Pdf.PdfFormFieldOptions) |

- A name with periods (`address.city`) creates the field in a hierarchy: the parent fields are created when the form
  has none. A name that the form already has throws `ArgumentException`.
- Every options class derives from [PdfFormFieldOptions](cref:Meziantou.Framework.Pdf.PdfFormFieldOptions): the alternate name shown as a tooltip, read-only, required,
  the font, its size and the color of the text, the border and the background. The text is in Helvetica 12 by
  default; a font size of 0 fits the text in the widget.
- Check boxes and radio buttons show a check mark, a circle, a cross, a diamond, a square or a star
  ([PdfCheckBoxStyle](cref:Meziantou.Framework.Pdf.PdfCheckBoxStyle)). Their appearances draw the symbol as a path, so no font is needed to show it.
- [PdfSigner](cref:Meziantou.Framework.Pdf.Signing.PdfSigner) signs a signature field created this way when [FieldName](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureOptions.FieldName) is its name: see
  [Digital signatures](../07-Security/04-DigitalSignatures.md).
- The fields can be added to a document that has a form, and the created fields are read, filled and flattened like
  the others.

### Use an embedded font

The [Font](cref:Meziantou.Framework.Pdf.PdfFormFieldOptions.Font) of the options can be any font, such as a font loaded with [PdfFont.Load](cref:Meziantou.Framework.Pdf.PdfFont.Load), for the characters the
standard 14 fonts cannot show:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_CreateFieldWithEmbeddedFont;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontPath);
var document = new PdfDocument();
var page = document.Pages.Add(PdfSize.A4);

// The font is added to the default resources of the form, embedded whole so that viewers can show what users type
document.Form.AddTextField(page, "comment", new PdfRectangle(50, 780, 300, 800), new PdfTextFieldOptions { Font = font, Value = "Été “fine” — 1€" });
var data = document.ToByteArray();

// The fonts of a saved document cannot show new text: give the font again to fill the fields that use it
var filled = PdfDocument.Open(data);
filled.Form.AddFont(font);
filled.Form.FindField("comment")!.Value = "AVATAR Office";
```
<!-- endref -->

- The font is added to the default resources of the form ([AddFont](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddFont)), and the default appearance of the field names
  it, so that viewers draw the text typed in the field with it.
- The font of a field that can be edited is embedded whole, so that viewers can show any character of the font. The
  font of a read-only field keeps the glyphs of the value only; set [SubsetFont](cref:Meziantou.Framework.Pdf.PdfFormFieldOptions.SubsetFont) to keep a subset for an
  editable field too, when its file size matters more than the characters users can type.
- A saved document cannot draw new text with the fonts of its file. Before filling the fields of a document you open
  again, call [AddFont](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.AddFont) with the same font: the font of the file is found by its name, and the new values are drawn
  with the font you give. Without it, they are drawn with Helvetica.

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
  size and color of its default appearance, the state of a check box or of radio buttons, the chosen options of a
  choice field. The other fields do not change. The text is drawn with the embedded font of the default appearance
  when the form knows it (see [Use an embedded font](#use-an-embedded-font)), otherwise with the standard 14 font that
  matches it.
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
