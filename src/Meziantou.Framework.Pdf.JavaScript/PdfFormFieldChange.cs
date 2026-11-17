namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>A field whose value or displayed text changed.</summary>
public sealed class PdfFormFieldChange
{
    internal PdfFormFieldChange(PdfFormField field, string? oldValue, string? newValue, string? displayText)
    {
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        DisplayText = displayText;
    }

    /// <summary>Gets the field.</summary>
    public PdfFormField Field { get; }

    /// <summary>Gets the value before the change.</summary>
    public string? OldValue { get; }

    /// <summary>Gets the value after the change.</summary>
    public string? NewValue { get; internal set; }

    /// <summary>Gets the text the format script of the field gives for the value, shown by the appearance; <see langword="null"/> when the field has no format script.</summary>
    public string? DisplayText { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() => $"{Field.FullName}: {OldValue} -> {NewValue}" + (DisplayText is null ? "" : $" ({DisplayText})");
}
