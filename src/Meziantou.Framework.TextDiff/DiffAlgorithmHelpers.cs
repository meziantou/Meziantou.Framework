namespace Meziantou.Framework;

internal static class DiffAlgorithmHelpers
{
    internal static void ApplySubDiff(DiffComputationResult subDiff, bool[] leftModified, int leftOffset, bool[] rightModified, int rightOffset)
    {
        Array.Copy(subDiff.LeftModified, 0, leftModified, leftOffset, subDiff.LeftLength);
        Array.Copy(subDiff.RightModified, 0, rightModified, rightOffset, subDiff.RightLength);
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
