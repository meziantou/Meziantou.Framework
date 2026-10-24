using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A real number (ISO 32000-2, 7.3.3). It is written without exponent, with at most six decimals.</summary>
public sealed class PdfReal : PdfNumber, IEquatable<PdfReal>
{
    private PdfReal(double value)
    {
        Value = value;
    }

    /// <summary>Gets the value.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override double DoubleValue => Value;

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Real;

    /// <summary>Creates a real number.</summary>
    /// <param name="value">The value, a finite number.</param>
    /// <returns>The real number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite.</exception>
    public static PdfReal Get(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A PDF number must be finite");

        return new PdfReal(value);
    }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] PdfReal? other) => other is not null && other.Value.Equals(Value);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PdfReal);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}
