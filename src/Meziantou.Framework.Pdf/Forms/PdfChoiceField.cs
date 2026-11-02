using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A choice field (ISO 32000-2, 12.7.5.4): a list box or a combo box.</summary>
public sealed class PdfChoiceField : PdfFormField
{
    private static readonly PdfName OptName = PdfName.Create("Opt");
    private static readonly PdfName IName = PdfName.Create("I");

    internal PdfChoiceField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
        : base(form, dictionary, reference, parent)
    {
    }

    /// <summary>Gets a value indicating whether the field is a combo box, a drop-down list (bit 18 of <see cref="PdfFormField.Flags"/>); otherwise it is a list box.</summary>
    public bool IsComboBox => HasFlag(18);

    /// <summary>Gets a value indicating whether a combo box also accepts a text that is not an option (bit 19 of <see cref="PdfFormField.Flags"/>).</summary>
    public bool IsEditable => HasFlag(19);

    /// <summary>Gets a value indicating whether several options can be chosen (bit 22 of <see cref="PdfFormField.Flags"/>).</summary>
    public bool IsMultiSelect => HasFlag(22);

    /// <summary>Gets the options (<c>/Opt</c>, inherited).</summary>
    public IReadOnlyList<PdfChoiceOption> Options
    {
        get
        {
            var result = new List<PdfChoiceOption>();
            if (GetInherited(OptName) is PdfArray options)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    switch (options.GetResolved(i))
                    {
                        case PdfArray { Count: >= 2 } pair when ToText(pair.GetResolved(0)) is { } value:
                            result.Add(new PdfChoiceOption(value, ToText(pair.GetResolved(1)) ?? value));
                            break;
                        case var item when ToText(item) is { } text:
                            result.Add(new PdfChoiceOption(text, text));
                            break;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Gets the chosen values: the value (<c>/V</c>), a text or an array of texts.</summary>
    public IReadOnlyList<string> SelectedValues => GetInherited(VName) switch
    {
        PdfArray array => [.. array.Select(item => ToText(PdfReference.ResolveIfReference(item))).OfType<string>()],
        var value when ToText(value) is { } text => [text],
        _ => [],
    };

    /// <summary>Gets or sets the chosen value, or the first of them; setting it writes the appearances of the widgets.</summary>
    /// <exception cref="ArgumentException">The value is not an option, and the field is not an editable combo box.</exception>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public override string? Value
    {
        get => SelectedValues.Count > 0 ? SelectedValues[0] : null;
        set => SetSelectedValues(value is null ? [] : [value]);
    }

    /// <summary>Chooses values: the value (<c>/V</c>), the indexes of the options (<c>/I</c>) and the appearances of the widgets are written.</summary>
    /// <param name="values">The values: the export values of options, or a text for an editable combo box.</param>
    /// <exception cref="ArgumentException">A value is not an option and the field is not an editable combo box, or several values are given to a field that is not multi-select.</exception>
    /// <exception cref="InvalidOperationException">The field is read-only.</exception>
    public void SetSelectedValues(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (IsReadOnly)
            throw new InvalidOperationException($"The field '{FullName}' is read-only");

        var list = values.ToList();
        if (list.Count > 1 && !IsMultiSelect)
            throw new ArgumentException($"The field '{FullName}' accepts one value", nameof(values));

        var options = Options;
        var indexes = new PdfArray();
        foreach (var value in list)
        {
            var index = options.ToList().FindIndex(option => option.Value == value);
            if (index < 0 && !(IsComboBox && IsEditable))
                throw new ArgumentException($"'{value}' is not an option of the field '{FullName}'", nameof(values));

            if (index >= 0)
            {
                indexes.Add(PdfInteger.Get(index));
            }
        }

        Dictionary[VName] = list.Count switch
        {
            0 => null,
            1 => PdfString.FromText(list[0]),
            _ => new PdfArray(list.Select(PdfString.FromText)),
        };
        Dictionary[IName] = indexes.Count > 0 ? indexes : null;
        PdfFieldAppearance.Update(this);
    }
}
