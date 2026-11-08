namespace Meziantou.Framework;

internal static class DiffAlgorithmDispatcher
{
    internal static DiffComputationResult Compute(TextDiffAlgorithm algorithm, string[] left, string[] right, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        return algorithm switch
        {
            TextDiffAlgorithm.Myers => MyersDiff.Compute(left, right, comparer, budget),
            TextDiffAlgorithm.Patience => PatienceDiff.Compute(left, right, comparer, budget),
            TextDiffAlgorithm.Histogram => HistogramDiff.Compute(left, right, comparer, budget),
            TextDiffAlgorithm.HuntSzymanski => HuntSzymanskiDiff.Compute(left, right, comparer, budget),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, message: null),
        };
    }
}
