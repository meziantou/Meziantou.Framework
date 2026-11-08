namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a text field created by <see cref="PdfInteractiveForm.AddTextField"/>.</summary>
public sealed class PdfTextFieldOptions : PdfFormFieldOptions
{
    /// <summary>Gets or sets the text of the field, or <see langword="null"/> for an empty field.</summary>
    public string? Value { get; set; }

    /// <summary>Gets or sets a value indicating whether the field can have several lines.</summary>
    public bool IsMultiline { get; set; }

    /// <summary>Gets or sets a value indicating whether the text is a password, shown as asterisks.</summary>
    public bool IsPassword { get; set; }

    /// <summary>Gets or sets the maximum length of the text, or <see langword="null"/> for none.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Gets or sets a value indicating whether the field is divided into <see cref="MaxLength"/> cells of one character. It needs <see cref="MaxLength"/>, and the field cannot be multiline nor a password.</summary>
    public bool IsComb { get; set; }

    /// <summary>Gets or sets a value indicating whether viewers do not check the spelling of the text.</summary>
    public bool DoNotSpellCheck { get; set; }

    /// <summary>Gets or sets a value indicating whether viewers do not scroll the text when it does not fit in the field.</summary>
    public bool DoNotScroll { get; set; }

    /// <summary>Gets or sets the alignment of the text: left, centered or right. The default is <see cref="PdfTextAlignment.Left"/>.</summary>
    public PdfTextAlignment Alignment { get; set; }
}
