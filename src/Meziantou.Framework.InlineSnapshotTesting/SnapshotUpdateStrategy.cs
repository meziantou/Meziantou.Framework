using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class SnapshotUpdateStrategy
{
    /// <summary>
    /// Do not update the snapshots and fail the tests if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy Disallow { get; } = new DisallowStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots. You can specify the merge tools to use using <see cref="InlineSnapshotSettings.MergeTools" />.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeTool { get; } = new MergeToolStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots and wait for it to close before continuing the test execution. You can specify the merge tools to use using <see cref="InlineSnapshotSettings.MergeTools" />.
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

    public static SnapshotUpdateStrategy Default
    {
        get
        {
            if (TaskDialogPrompt.IsSupported())
                return new PromptStrategy(new TaskDialogPrompt());

            return MergeTool;
        }
    }

    public virtual bool ReuseTemporaryFile => true;

    internal bool CanUpdateSnapshotInternal(InlineSnapshotSettings settings, string path, string expectedSnapshot, string actualSnapshot)
    {
        if (settings.AutoDetectContinuousEnvironment && settings.IsRunningOnContinuousIntegration())
            return false;

        return CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);
    }


    /// <summary>
    /// Indicates if an an inline snapshot must be updated
    /// </summary>
    public abstract bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string expectedSnapshot, string actualSnapshot);

    public abstract void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile);

    /// <summary>
    /// Indicates if an exception must be thrown when the snapshots differ.
    /// </summary>
    public abstract bool MustReportError(InlineSnapshotSettings settings, string path);

    private protected static void MoveFile(string source, string destination)
    {
        if (source == destination)
            return;

        var fi = new FileInfo(source);
        fi.TrySetReadOnly(false);

#if NETCOREAPP3_0_OR_GREATER
        File.Move(source, destination, overwrite: true);
#else
        File.Copy(source, destination, overwrite: true);
        TryDeleteFile(source);
#endif
    }

    private protected static void TryDeleteFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                fi.TrySetReadOnly(false);
                fi.Delete();
            }
        }
        catch
        {
        }
    }

    public override string ToString()
    {
        var name = this.GetType().Name;
        if (name.EndsWith("Strategy", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Strategy".Length);
        }

        return name;
    }
}