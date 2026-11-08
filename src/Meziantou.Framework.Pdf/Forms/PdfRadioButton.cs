namespace Meziantou.Framework.Pdf;

/// <summary>A button of a group of radio buttons (<see cref="PdfInteractiveForm.AddRadioButtons"/>): a widget, and the option it chooses.</summary>
/// <param name="Page">The page of the widget.</param>
/// <param name="Rectangle">The rectangle of the widget, in default user space.</param>
/// <param name="Value">The option: the value of the field when the button is on.</param>
public sealed record PdfRadioButton(PdfPage Page, PdfRectangle Rectangle, string Value);
