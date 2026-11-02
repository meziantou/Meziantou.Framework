namespace Meziantou.Framework.Pdf;

/// <summary>An option of a choice field (ISO 32000-2, 12.7.5.4, <c>/Opt</c>).</summary>
/// <param name="Value">The value of the field when the option is chosen (the export value).</param>
/// <param name="Text">The text the field shows for the option.</param>
public sealed record PdfChoiceOption(string Value, string Text);
