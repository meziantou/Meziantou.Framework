namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A boolean (ISO 32000-2, 7.3.2).</summary>
public sealed class PdfBoolean : PdfObject
{
    private PdfBoolean(bool value)
    {
        Value = value;
    }

    /// <summary>Gets the <see langword="true"/> object.</summary>
    public static PdfBoolean True { get; } = new(value: true);

    /// <summary>Gets the <see langword="false"/> object.</summary>
    public static PdfBoolean False { get; } = new(value: false);

    /// <summary>Gets the value.</summary>
    public bool Value { get; }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Boolean;

    /// <summary>Gets the object of a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see cref="True"/> or <see cref="False"/>.</returns>
    public static PdfBoolean Get(bool value) => value ? True : False;
}
