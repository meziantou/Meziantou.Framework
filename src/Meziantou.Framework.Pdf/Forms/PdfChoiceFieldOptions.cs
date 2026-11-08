namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a choice field created by <see cref="PdfInteractiveForm.AddComboBox"/> or <see cref="PdfInteractiveForm.AddListBox"/>.</summary>
public sealed class PdfChoiceFieldOptions : PdfFormFieldOptions
{
    /// <summary>Gets or sets the chosen values: export values of options, or a text for an editable combo box. Several values need <see cref="IsMultiSelect"/>.</summary>
    public IReadOnlyList<string>? SelectedValues { get; set; }

    /// <summary>Gets or sets a value indicating whether a combo box also accepts a text that is not an option.</summary>
    public bool IsEditable { get; set; }

    /// <summary>Gets or sets a value indicating whether several options of a list box can be chosen.</summary>
    public bool IsMultiSelect { get; set; }

    /// <summary>Gets or sets the alignment of the text: left, centered or right. The default is <see cref="PdfTextAlignment.Left"/>.</summary>
    public PdfTextAlignment Alignment { get; set; }
}
