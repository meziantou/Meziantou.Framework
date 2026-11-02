using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A group of radio buttons (ISO 32000-2, 12.7.5.2.4): one widget per option, of which one at most is on.</summary>
public sealed class PdfRadioButtonField : PdfFormField
{
    internal PdfRadioButtonField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }

    /// <summary>Gets the options: the on states of the widgets, in their order.</summary>
    public IReadOnlyList<string> Options => [.. PdfFieldAppearance.GetOnStates(this).Distinct(StringComparer.Ordinal)];

    /// <summary>Gets or sets the option that is on, or <see langword="null"/> when none is. Setting it sets the value (<c>/V</c>) and the appearance states of the widgets (<c>/AS</c>).</summary>
    /// <exception cref="ArgumentException">The value is not an option of the field.</exception>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public override string? Value
    {
        get => base.Value is { } value && value != "Off" ? value : null;
        set
        {
            if (IsReadOnly)
                throw new InvalidOperationException($"The field '{FullName}' is read-only");

            if (value is not null && !Options.Contains(value, StringComparer.Ordinal))
                throw new ArgumentException($"'{value}' is not an option of the field '{FullName}'", nameof(value));

            var state = value is null ? OffName : PdfName.Create(value);
            Dictionary[VName] = state;
            foreach (var (widget, _) in GetWidgetDictionaries())
            {
                widget[ASName] = PdfFieldAppearance.HasState(widget, state) ? state : OffName;
            }
        }
    }
}
