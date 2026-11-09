namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Specifies the shapes a Data Matrix symbol can have.
/// </summary>
public enum DataMatrixShape
{
    /// <summary>A square symbol, from 10 × 10 to 144 × 144 modules.</summary>
    Square,

    /// <summary>A rectangular symbol: 8 × 18, 8 × 32, 12 × 26, 12 × 36, 16 × 36 or 16 × 48 modules (height × width).</summary>
    Rectangle,

    /// <summary>A square or rectangular symbol, whichever has the fewest modules.</summary>
    Any,
}
