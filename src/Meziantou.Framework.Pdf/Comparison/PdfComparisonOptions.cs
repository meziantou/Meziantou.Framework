namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfDocumentComparer"/>.</summary>
public sealed class PdfComparisonOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfComparisonOptions Default { get; } = new();

    /// <summary>Gets the distance, in points, below which two positions are the same. The default is 0.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Tolerance
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 0.5;

    /// <summary>Gets the most details given for each kind of change of a page. The default is 20.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxDetails
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 20;
}
