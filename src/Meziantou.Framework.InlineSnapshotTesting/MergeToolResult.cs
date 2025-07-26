namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class MergeToolResult : IDisposable
{
    public abstract void Dispose();
    public abstract void WaitForExit();
}
