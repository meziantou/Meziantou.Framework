namespace Meziantou.Framework;

internal sealed class DiffComputationResult
{
    internal DiffComputationResult(bool[] leftModified, bool[] rightModified)
    {
        LeftModified = leftModified;
        RightModified = rightModified;
    }

    internal bool[] LeftModified { get; }

    internal bool[] RightModified { get; }
}
