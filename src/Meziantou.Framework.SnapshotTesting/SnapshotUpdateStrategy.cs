using Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting;

public abstract class SnapshotUpdateStrategy
{
    /// <summary>Do not update the snapshots and fail the tests if the snapshots are different.</summary>
    public static SnapshotUpdateStrategy Disallow { get; } = new DisallowStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots. You can specify the merge tools to use using <see cref="SnapshotSettings.MergeTools" />.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeTool { get; } = new MergeToolStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots and wait for it to close before continuing the test execution. You can specify the merge tools to use using <see cref="SnapshotSettings.MergeTools" />.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeToolSync { get; } = new BlockingDiffToolStrategy();

    /// <summary>Overwrite the source file with the new snapshot. The test fails if the snapshots are different.</summary>
    public static SnapshotUpdateStrategy Overwrite { get; } = new AlwaysStrategy();

    /// <summary>Overwrite the source file with the new snapshot. The test won't fail.</summary>
    public static SnapshotUpdateStrategy OverwriteWithoutFailure { get; } = new AlwaysWithoutFailureStrategy();

    public static SnapshotUpdateStrategy Default
    {
        get
        {
            return Disallow;
        }
    }

    public virtual bool ReuseTemporaryFile => true;

    internal bool CanUpdateSnapshotInternal(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot)
    {
        if (settings.AutoDetectContinuousEnvironment && SnapshotSettings.IsRunningOnContinuousIntegration())
            return false;

        return CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);
    }


    /// <summary>Indicates if an an inline snapshot must be updated</summary>
    public abstract bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot);

    /// <summary>Updates one or more snapshot files and deletes obsolete verified files.</summary>
    public virtual void UpdateFiles(SnapshotSettings settings, IReadOnlyList<SnapshotUpdateFile> filesToUpdate, IReadOnlyList<string> filesToDelete)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filesToUpdate);
        ArgumentNullException.ThrowIfNull(filesToDelete);

        foreach (var fileToUpdate in filesToUpdate)
        {
            UpdateFile(settings, fileToUpdate.VerifiedFilePath, fileToUpdate.ActualFilePath);
        }

        foreach (var fileToDelete in filesToDelete)
        {
            TryDeleteFile(fileToDelete);
        }
    }

    public abstract void UpdateFile(SnapshotSettings settings, string verifiedFilePath, string actualFilePath);

    /// <summary>Indicates if an exception must be thrown when the snapshots differ.</summary>
    public abstract bool MustReportError(SnapshotSettings settings, string path);

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

    private protected static void CopyFile(string source, string destination)
    {
        if (source == destination)
            return;

        var sourceInfo = new FileInfo(source);
        sourceInfo.TrySetReadOnly(false);

        var destinationInfo = new FileInfo(destination);
        destinationInfo.Directory?.Create();
        if (destinationInfo.Exists)
        {
            destinationInfo.TrySetReadOnly(false);
        }

        File.Copy(source, destination, overwrite: true);
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
        var name = GetType().Name;
        if (name.EndsWith("Strategy", StringComparison.Ordinal))
        {
            name = name[..^"Strategy".Length];
        }

        return name;
    }
}

public sealed record SnapshotUpdateFile(string VerifiedFilePath, string ActualFilePath);
