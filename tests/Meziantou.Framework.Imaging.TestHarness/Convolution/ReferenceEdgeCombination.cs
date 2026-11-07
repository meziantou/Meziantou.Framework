namespace Meziantou.Framework.Imaging.TestHarness.Convolution;

/// <summary>How <see cref="ReferenceConvolver.DetectEdges"/> combines the sums of its matrices.</summary>
public enum ReferenceEdgeCombination
{
    /// <summary>The square root of the sum of the squares of the sums (the magnitude of a gradient).</summary>
    Magnitude,

    /// <summary>The largest sum (a compass operator).</summary>
    Maximum,

    /// <summary>The absolute value of the sum of the only matrix.</summary>
    Absolute,
}
