namespace Meziantou.Framework;

/// <summary>The exception thrown when a hunk of a unified diff does not match the text it is applied to.</summary>
public sealed class UnifiedDiffApplyException : Exception
{
    public UnifiedDiffApplyException()
    {
    }

    public UnifiedDiffApplyException(string message)
        : base(message)
    {
    }

    public UnifiedDiffApplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal UnifiedDiffApplyException(int hunkIndex, string message)
        : base(message)
    {
        HunkIndex = hunkIndex;
    }

    /// <summary>Gets the 0-based index of the hunk that does not match, in <see cref="UnifiedDiffFile.Hunks"/>.</summary>
    public int HunkIndex { get; } = -1;
}
