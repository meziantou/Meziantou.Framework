namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Represents the result of launching a merge tool process.</summary>
public abstract class MergeToolResult : IDisposable
{
    /// <summary>Releases the resources used by the merge tool process.</summary>
    public abstract void Dispose();

    /// <summary>Waits for the merge tool process to exit.</summary>
    public abstract void WaitForExit();
}
