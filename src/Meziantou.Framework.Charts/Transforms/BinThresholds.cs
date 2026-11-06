namespace Meziantou.Framework.Charts;

/// <summary>The rule that chooses the number of bins of a <see cref="BinTransform"/>.</summary>
public enum BinThresholds
{
    /// <summary>Sturges' formula: log2(n) + 1 bins, for n values.</summary>
    Sturges,

    /// <summary>Scott's normal reference rule: bins of 3.49 σ n^(-1/3).</summary>
    Scott,

    /// <summary>The Freedman–Diaconis rule: bins of 2 IQR n^(-1/3), robust to outliers.</summary>
    FreedmanDiaconis,
}
