using System.Buffers;

namespace Meziantou.Framework;

internal static class DiffAlgorithmDispatcher
{
    private const int StackAllocationThreshold = 128;

    internal static DiffComputationResult Compute(TextDiffAlgorithm algorithm, string[] left, string[] right, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        if (algorithm is not (TextDiffAlgorithm.Myers or TextDiffAlgorithm.Patience or TextDiffAlgorithm.Histogram or TextDiffAlgorithm.HuntSzymanski))
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, message: null);

        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];

        // Every algorithm starts by skipping the common prefix and suffix. Skipping them here keeps the unchanged chunks
        // out of the dictionary that gives each chunk a code, and sizes the buffers of the algorithms on the changed region.
        var prefixLength = DiffAlgorithmHelpers.CommonPrefixLength(left, right, comparer);
        var suffixLength = DiffAlgorithmHelpers.CommonSuffixLength(left.AsSpan(prefixLength), right.AsSpan(prefixLength), comparer);
        var leftLength = left.Length - prefixLength - suffixLength;
        var rightLength = right.Length - prefixLength - suffixLength;
        var leftRegionModified = leftModified.AsSpan(prefixLength, leftLength);
        var rightRegionModified = rightModified.AsSpan(prefixLength, rightLength);

        int[]? leftCodesArray = null;
        int[]? rightCodesArray = null;
        try
        {
            if (leftLength is 0 || rightLength is 0)
            {
                leftRegionModified.Fill(true);
                rightRegionModified.Fill(true);
            }
            else
            {
                var leftCodes = leftLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : (leftCodesArray = ArrayPool<int>.Shared.Rent(leftLength));
                var rightCodes = rightLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : (rightCodesArray = ArrayPool<int>.Shared.Rent(rightLength));
                leftCodes = leftCodes[..leftLength];
                rightCodes = rightCodes[..rightLength];

                // Comparing codes is cheaper than comparing strings, and lets the algorithms index arrays by chunk
                // instead of building dictionaries of strings for each region they search.
                var codeCount = DiffAlgorithmHelpers.ComputeCodes(left.AsSpan(prefixLength, leftLength), right.AsSpan(prefixLength, rightLength), comparer, leftCodes, rightCodes);
                switch (algorithm)
                {
                    case TextDiffAlgorithm.Myers:
                        MyersDiff.FindModified(leftCodes, rightCodes, budget, leftRegionModified, rightRegionModified);
                        break;

                    case TextDiffAlgorithm.Patience:
                        PatienceDiff.Compute(leftCodes, rightCodes, codeCount, budget, leftRegionModified, rightRegionModified);
                        break;

                    case TextDiffAlgorithm.Histogram:
                        HistogramDiff.Compute(leftCodes, rightCodes, codeCount, budget, leftRegionModified, rightRegionModified);
                        break;

                    default:
                        HuntSzymanskiDiff.Compute(leftCodes, rightCodes, codeCount, budget, leftRegionModified, rightRegionModified);
                        break;
                }
            }
        }
        finally
        {
            DiffAlgorithmHelpers.Return(leftCodesArray);
            DiffAlgorithmHelpers.Return(rightCodesArray);
        }

        // Myers slides the changes over the whole texts, so a change can move into the common suffix
        if (algorithm is TextDiffAlgorithm.Myers)
        {
            MyersDiff.Optimize(left, comparer, leftModified);
            MyersDiff.Optimize(right, comparer, rightModified);
        }

        return new DiffComputationResult(leftModified, rightModified);
    }
}
