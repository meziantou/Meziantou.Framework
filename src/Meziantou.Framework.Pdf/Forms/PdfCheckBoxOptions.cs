namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a check box created by <see cref="PdfInteractiveForm.AddCheckBox"/>.</summary>
public sealed class PdfCheckBoxOptions : PdfFormFieldOptions
{
    /// <summary>Gets or sets a value indicating whether the box is checked.</summary>
    public bool IsChecked { get; set; }

    /// <summary>Gets or sets the name of the on state, the value of the field when the box is checked. The default is <c>Yes</c>.</summary>
    public string OnValue { get; set; } = "Yes";

    /// <summary>Gets or sets the symbol shown when the box is checked. The default is <see cref="PdfCheckBoxStyle.Check"/>.</summary>
    public PdfCheckBoxStyle Style { get; set; }
}
