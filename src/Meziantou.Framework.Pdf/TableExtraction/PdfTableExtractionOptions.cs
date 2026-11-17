namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfTableExtractor"/>.</summary>
public sealed class PdfTableExtractionOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfTableExtractionOptions Default { get; } = new();

    /// <summary>Gets the distance, in points, within which two rules are the same line of the grid. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Tolerance
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 2;

    /// <summary>Gets the greatest thickness, in points, of a filled rectangle that is a rule of the grid. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double MaxRuleThickness
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 2;
}
