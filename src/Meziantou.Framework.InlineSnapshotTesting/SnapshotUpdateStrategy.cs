using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class SnapshotUpdateStrategy
{
    /// <summary>
    /// Do not update the snapshots and fail the tests if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy Disallow { get; } = new DisallowStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots. You can specify the merge tool to use using <see cref="InlineSnapshotSettings.MergeTool" />.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeTool { get; } = new MergeToolStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots and wait for it to close before continuing the test execution. You can specify the merge tool to use using <see cref="InlineSnapshotSettings.MergeTool" />.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeToolSync { get; } = new BlockingDiffToolStrategy();

    /// <summary>
    /// Overwrite the source file with the new snapshot. The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy Overwrite { get; } = new AlwaysStrategy();

    /// <summary>
    /// Overwrite the source file with the new snapshot. The test won't fail.
    /// </summary>
    public static SnapshotUpdateStrategy OverwriteWithoutFailure { get; } = new AlwaysWithoutFailureStrategy();

#if WINDOWS
    /// <summary>
    /// Ask the user what to do when the snapshots are different. You can persist the choice for the current session.
    /// </summary>
    public static SnapshotUpdateStrategy Prompt { get; } = new PromptStrategy();
#endif

    internal static SnapshotUpdateStrategy Default
    {
        get
        {
#if WINDOWS
            return Prompt;
#else
            return MergeTool;
#endif
        }
    }

    public abstract bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path);

    public abstract void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile);

    public abstract bool MustReportError(InlineSnapshotSettings settings, string path);

    private protected static void MoveFile(string source, string destination)
    {
        if (source == destination)
            return;

#if NETCOREAPP3_0_OR_GREATER
        File.Move(source, destination, overwrite: true);
#else
        File.Copy(source, destination, overwrite: true);
        File.Delete(source);
#endif
    }
}