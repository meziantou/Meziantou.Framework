namespace Meziantou.Framework.Pdf;

/// <summary>
/// The attributes of a field created by <see cref="PdfInteractiveForm"/> and of its widgets: <see cref="PdfInteractiveForm.AddSignatureField"/>
/// uses them as they are; the other fields derive from it.
/// </summary>
public class PdfFormFieldOptions
{
    /// <summary>Gets or sets the alternate name of the field (<c>/TU</c>): the text the user interface shows, and assistive technologies read.</summary>
    public string? AlternateName { get; set; }

    /// <summary>Gets or sets a value indicating whether the user cannot change the value of the field.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Gets or sets a value indicating whether the field must have a value when the form is submitted.</summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Gets or sets the font of the text, or <see langword="null"/> for Helvetica. It is added to the default resources
    /// of the form (<see cref="PdfInteractiveForm.AddFont"/>). Check boxes and radio buttons ignore it: they draw their
    /// symbols as paths, and their default appearance names ZapfDingbats.
    /// </summary>
    /// <remarks>
    /// An embedded font is embedded whole when the field is not read-only, so that viewers can show the text typed in
    /// the field, unless <see cref="SubsetFont"/> is set.
    /// </remarks>
    public PdfFont? Font { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an embedded <see cref="Font"/> keeps only the glyphs the document shows,
    /// even when the field is not read-only. The file is smaller, but viewers may not show the characters typed in
    /// the field that are not in the subset.
    /// </summary>
    public bool SubsetFont { get; set; }

    /// <summary>Gets or sets the font size; 0 fits the text in the widget. The default is 12, and 0 for check boxes and radio buttons.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets the color of the text and of the symbol of check boxes and radio buttons, or <see langword="null"/> for black.</summary>
    public PdfColor? TextColor { get; set; }

    /// <summary>Gets or sets the color of the border, or <see langword="null"/> for no border. The default is black.</summary>
    public PdfColor? BorderColor { get; set; } = PdfColor.FromGray(0);

    /// <summary>Gets or sets the width of the border. The default is 1.</summary>
    public double BorderWidth { get; set; } = 1;

    /// <summary>Gets or sets the color of the background, or <see langword="null"/> for a transparent background, the default.</summary>
    public PdfColor? BackgroundColor { get; set; }
}
