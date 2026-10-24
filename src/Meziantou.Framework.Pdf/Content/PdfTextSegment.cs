using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>An element of a text shown with individual glyph positioning (the <c>TJ</c> operator, ISO 32000-2, 9.4.3): a text, or an adjustment of the position.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfTextSegment : IEquatable<PdfTextSegment>
{
    private PdfTextSegment(string? text, double adjustment)
    {
        Text = text;
        Adjustment = adjustment;
    }

    /// <summary>Gets the text, or <see langword="null"/> for an adjustment.</summary>
    public string? Text { get; }

    /// <summary>Gets the adjustment in thousandths of the font size: a positive value moves the next glyph to the left (horizontal writing).</summary>
    public double Adjustment { get; }

    /// <summary>Converts a text to a segment.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator PdfTextSegment(string text) => FromText(text);

    /// <summary>Converts an adjustment to a segment.</summary>
    /// <param name="adjustment">The adjustment, in thousandths of the font size.</param>
    public static implicit operator PdfTextSegment(double adjustment) => FromAdjustment(adjustment);

    /// <summary>Indicates whether two segments are equal.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when the segments are equal.</returns>
    public static bool operator ==(PdfTextSegment left, PdfTextSegment right) => left.Equals(right);

    /// <summary>Indicates whether two segments are different.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when the segments are different.</returns>
    public static bool operator !=(PdfTextSegment left, PdfTextSegment right) => !left.Equals(right);

    /// <summary>Creates a text segment.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The segment.</returns>
    public static PdfTextSegment FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new PdfTextSegment(text, 0);
    }

    /// <summary>Creates an adjustment.</summary>
    /// <param name="adjustment">The adjustment, in thousandths of the font size.</param>
    /// <returns>The segment.</returns>
    public static PdfTextSegment FromAdjustment(double adjustment)
    {
        if (!double.IsFinite(adjustment))
            throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment, "The adjustment must be finite");

        return new PdfTextSegment(text: null, adjustment);
    }

    /// <inheritdoc/>
    public bool Equals(PdfTextSegment other) => string.Equals(Text, other.Text, StringComparison.Ordinal) && Adjustment.Equals(other.Adjustment);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfTextSegment other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Text, Adjustment);
}
