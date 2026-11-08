using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>The width of a column of a <see cref="PdfTable"/>: fixed, relative or automatic. The default value is <see cref="Auto"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfTableColumnWidth : IEquatable<PdfTableColumnWidth>
{
    private PdfTableColumnWidth(PdfTableColumnWidthType type, double value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Gets the width of the content of the column.</summary>
    public static PdfTableColumnWidth Auto => default;

    /// <summary>Gets how the width is computed.</summary>
    public PdfTableColumnWidthType Type { get; }

    /// <summary>Gets the width in points of a fixed column, the weight of a relative column, or 0 for an automatic column.</summary>
    public double Value { get; }

    /// <summary>Indicates whether two widths are equal.</summary>
    /// <param name="left">The first width.</param>
    /// <param name="right">The second width.</param>
    /// <returns><see langword="true"/> when the widths are equal.</returns>
    public static bool operator ==(PdfTableColumnWidth left, PdfTableColumnWidth right) => left.Equals(right);

    /// <summary>Indicates whether two widths are different.</summary>
    /// <param name="left">The first width.</param>
    /// <param name="right">The second width.</param>
    /// <returns><see langword="true"/> when the widths are different.</returns>
    public static bool operator !=(PdfTableColumnWidth left, PdfTableColumnWidth right) => !left.Equals(right);

    /// <summary>Creates a width in points.</summary>
    /// <param name="points">The width, 0 or more.</param>
    /// <returns>The width.</returns>
    public static PdfTableColumnWidth Fixed(double points)
    {
        if (!double.IsFinite(points) || points < 0)
            throw new ArgumentOutOfRangeException(nameof(points), points, "The width must be a finite number, 0 or more");

        return new PdfTableColumnWidth(PdfTableColumnWidthType.Fixed, points);
    }

    /// <summary>Creates a share of the width left by the fixed and automatic columns.</summary>
    /// <param name="weight">The weight of the column, more than 0: a column of weight 2 is twice as wide as a column of weight 1.</param>
    /// <returns>The width.</returns>
    public static PdfTableColumnWidth Relative(double weight = 1)
    {
        if (!double.IsFinite(weight) || weight <= 0)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "The weight must be a finite number, more than 0");

        return new PdfTableColumnWidth(PdfTableColumnWidthType.Relative, weight);
    }

    /// <inheritdoc/>
    public bool Equals(PdfTableColumnWidth other) => Type == other.Type && Value.Equals(other.Value);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfTableColumnWidth other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, Value);

    /// <inheritdoc/>
    public override string ToString() => Type switch
    {
        PdfTableColumnWidthType.Fixed => Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "pt",
        PdfTableColumnWidthType.Relative => Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "*",
        _ => "auto",
    };
}
