using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A text field (ISO 32000-2, 12.7.5.3): a box the user types a text in.</summary>
public sealed class PdfTextField : PdfFormField
{
    private static readonly PdfName MaxLenName = PdfName.Create("MaxLen");

    internal PdfTextField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }

    /// <summary>
    /// Gets or sets the text of the field (<c>/V</c>). Setting it writes the appearances of the widgets (12.7.4.3) with
    /// the font, size and color of the default appearance (<c>/DA</c>), so that the text is shown and printed, and
    /// flattened, by every viewer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public override string? Value
    {
        get => base.Value;
        set
        {
            if (IsReadOnly)
                throw new InvalidOperationException($"The field '{FullName}' is read-only");

            if (value is not null && MaxLength is { } maxLength && value.Length > maxLength)
            {
                value = value[..maxLength];
            }

            Dictionary[VName] = value is null ? null : PdfString.FromText(value);
            PdfFieldAppearance.Update(this);
        }
    }

    /// <summary>Gets a value indicating whether the field can have several lines (bit 13 of <see cref="PdfFormField.Flags"/>).</summary>
    public bool IsMultiline => HasFlag(13);

    /// <summary>Gets a value indicating whether the text is a password, shown as asterisks (bit 14 of <see cref="PdfFormField.Flags"/>).</summary>
    public bool IsPassword => HasFlag(14);

    /// <summary>Gets a value indicating whether the field is divided into <see cref="MaxLength"/> cells of one character (bit 25 of <see cref="PdfFormField.Flags"/>).</summary>
    public bool IsComb => HasFlag(25) && MaxLength is not null;

    /// <summary>Gets the maximum length of the text (<c>/MaxLen</c>, inherited), or <see langword="null"/> when it has none.</summary>
    public int? MaxLength => GetInherited(MaxLenName) is PdfInteger { Value: >= 0 and <= int.MaxValue } value ? (int)value.Value : null;
}
