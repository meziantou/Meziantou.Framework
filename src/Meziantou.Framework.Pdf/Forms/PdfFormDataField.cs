namespace Meziantou.Framework.Pdf;

/// <summary>The value of a field in <see cref="PdfFormData"/>.</summary>
public sealed class PdfFormDataField
{
    /// <summary>Initializes a field with one value, or none.</summary>
    /// <param name="name">The fully qualified name of the field, such as <c>address.city</c>.</param>
    /// <param name="value">The value, or <see langword="null"/> for none.</param>
    public PdfFormDataField(string name, string? value)
        : this(name, value is null ? [] : [value])
    {
    }

    /// <summary>Initializes a field with several values, such as the choices of a multi-select list box.</summary>
    /// <param name="name">The fully qualified name of the field, such as <c>address.city</c>.</param>
    /// <param name="values">The values.</param>
    public PdfFormDataField(string name, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);
        Name = name;
        Values = values.ToArray();
    }

    /// <summary>Gets the fully qualified name of the field.</summary>
    public string Name { get; }

    /// <summary>Gets the values of the field: none, one, or several for a multi-select list box.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Gets the first value, or <see langword="null"/> when the field has none.</summary>
    public string? Value => Values.Count > 0 ? Values[0] : null;

    /// <summary>
    /// Gets a value indicating whether FDF writes the value as a name rather than as a text string: the states of check
    /// boxes and radio buttons are names (ISO 32000-2, 12.7.5.2). XFDF does not make the distinction.
    /// </summary>
    public bool IsName { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} = {string.Join(", ", Values)}";
}
