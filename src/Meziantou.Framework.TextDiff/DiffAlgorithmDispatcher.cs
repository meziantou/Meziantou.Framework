namespace Meziantou.Framework;

internal static class DiffAlgorithmDispatcher
{
    internal static DiffComputationResult Compute(TextDiffAlgorithm algorithm, string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        return algorithm switch
        {
            TextDiffAlgorithm.Myers => MyersDiff.Compute(left, right, comparer),
            TextDiffAlgorithm.Patience => PatienceDiff.Compute(left, right, comparer),
            TextDiffAlgorithm.Histogram => HistogramDiff.Compute(left, right, comparer),
            TextDiffAlgorithm.HuntSzymanski => HuntSzymanskiDiff.Compute(left, right, comparer),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };
    }
}
