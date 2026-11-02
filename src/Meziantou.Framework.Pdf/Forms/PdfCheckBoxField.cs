using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A check box (ISO 32000-2, 12.7.5.2.3): a button that is on or off.</summary>
public sealed class PdfCheckBoxField : PdfFormField
{
    internal PdfCheckBoxField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }

    /// <summary>Gets the name of the on state: the appearance state of the widgets that is not <c>Off</c>, such as <c>Yes</c>.</summary>
    public string OnValue => PdfFieldAppearance.GetOnStates(this).FirstOrDefault() ?? "Yes";

    /// <summary>Gets or sets the value: the on state when the box is checked, <c>Off</c> otherwise. Setting <see langword="null"/> or <c>Off</c> unchecks it.</summary>
    /// <exception cref="ArgumentException">The value is neither the on state nor <c>Off</c>.</exception>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public override string? Value
    {
        get => base.Value;
        set
        {
            if (value is not null and not "Off" && !PdfFieldAppearance.GetOnStates(this).Contains(value, StringComparer.Ordinal) && value != OnValue)
                throw new ArgumentException($"'{value}' is not the on state of the check box '{FullName}'", nameof(value));

            IsChecked = value is not null and not "Off";
        }
    }

    /// <summary>Gets or sets a value indicating whether the box is checked: its value (<c>/V</c>) and the appearance state of its widgets (<c>/AS</c>) are the on state, or <c>Off</c>.</summary>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public bool IsChecked
    {
        get => Value is { } value && value != "Off";
        set
        {
            if (IsReadOnly)
                throw new InvalidOperationException($"The field '{FullName}' is read-only");

            var state = value ? PdfName.Create(OnValue) : OffName;
            Dictionary[VName] = state;
            foreach (var (widget, _) in GetWidgetDictionaries())
            {
                widget[ASName] = PdfFieldAppearance.HasState(widget, state) || !value ? state : OffName;
            }
        }
    }
}
