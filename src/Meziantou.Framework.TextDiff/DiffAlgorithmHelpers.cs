namespace Meziantou.Framework;

internal static class DiffAlgorithmHelpers
{
    internal static void ApplySubDiff(DiffComputationResult subDiff, bool[] leftModified, int leftOffset, bool[] rightModified, int rightOffset)
    {
        for (var i = 0; i < subDiff.LeftLength; i++)
        {
            leftModified[leftOffset + i] = subDiff.LeftModified[i];
        }

        for (var i = 0; i < subDiff.RightLength; i++)
        {
            rightModified[rightOffset + i] = subDiff.RightModified[i];
        }
    }

    internal static int LowerBound(List<int> values, int value)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (values[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
