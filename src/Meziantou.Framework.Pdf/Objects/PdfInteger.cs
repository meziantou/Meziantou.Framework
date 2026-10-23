using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>An integer (ISO 32000-2, 7.3.3).</summary>
public sealed class PdfInteger : PdfNumber, IEquatable<PdfInteger>
{
    private static readonly PdfInteger[] SmallValues = CreateSmallValues();

    private PdfInteger(long value)
    {
        Value = value;
    }

    /// <summary>Gets the value.</summary>
    public long Value { get; }

    /// <inheritdoc/>
    public override double DoubleValue => Value;

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Integer;

    /// <summary>Gets the object of a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The integer.</returns>
    public static PdfInteger Get(long value) => value is >= -1 and < 255 ? SmallValues[value + 1] : new PdfInteger(value);

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] PdfInteger? other) => other is not null && other.Value == Value;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PdfInteger);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    private static PdfInteger[] CreateSmallValues()
    {
        var values = new PdfInteger[256];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new PdfInteger(i - 1);
        }

        return values;
    }
}
