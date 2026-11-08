namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a group of radio buttons created by <see cref="PdfInteractiveForm.AddRadioButtons"/>.</summary>
public sealed class PdfRadioButtonOptions : PdfFormFieldOptions
{
    /// <summary>Gets or sets the option that is on, or <see langword="null"/> when none is.</summary>
    public string? Value { get; set; }

    /// <summary>Gets or sets a value indicating whether clicking the button that is on turns it off, so that no option is on. By default, one option stays on once chosen.</summary>
    public bool AllowToggleOff { get; set; }

    /// <summary>Gets or sets the symbol shown in the button that is on. The default is <see cref="PdfCheckBoxStyle.Circle"/>.</summary>
    public PdfCheckBoxStyle Style { get; set; } = PdfCheckBoxStyle.Circle;
}
