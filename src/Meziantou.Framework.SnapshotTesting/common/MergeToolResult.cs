#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting;
#else
namespace Meziantou.Framework.SnapshotTesting;
#endif

/// <summary>Represents the result of launching a merge tool process.</summary>
public abstract class MergeToolResult : IDisposable
{
    /// <summary>Releases the resources used by the merge tool process.</summary>
    public abstract void Dispose();

    /// <summary>Waits for the merge tool process to exit.</summary>
    public abstract void WaitForExit();

    /// <summary>
    /// Gets a value indicating whether <see cref="WaitForExit" /> returns only once the developer is done with the merge.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true" />. Override it to return <see langword="false" /> when the tool hands
    /// the files over to an already running application and exits right away, while the diff is still open. The
    /// <c>MergeToolSync</c> strategy then keeps the files the developer may still save to instead of cleaning them up
    /// when <see cref="WaitForExit" /> returns.
    /// </remarks>
    public virtual bool WaitsForMerge => true;
}
