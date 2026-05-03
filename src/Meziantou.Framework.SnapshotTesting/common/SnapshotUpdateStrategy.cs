#if INLINE_SNAPSHOT_TESTING
using Meziantou.Framework;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using SnapshotSettingsType = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings;
#else
using Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.SnapshotTesting.Utils;
using SnapshotSettingsType = Meziantou.Framework.SnapshotTesting.SnapshotSettings;
#endif
using System.Reflection;

#if INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting;
#else
namespace Meziantou.Framework.SnapshotTesting;
#endif

public abstract class SnapshotUpdateStrategy
{
#if INLINE_SNAPSHOT_TESTING
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "INLINESNAPSHOTTESTING_STRATEGY";
#else
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "SNAPSHOTTESTING_STRATEGY";
#endif

    private static readonly IReadOnlyList<PropertyInfo> SnapshotUpdateStrategyProperties = typeof(SnapshotUpdateStrategy).GetProperties(BindingFlags.Public | BindingFlags.Static);

    /// <summary>Do not update the snapshots and fail the tests if the snapshots are different.</summary>
    public static SnapshotUpdateStrategy Disallow { get; } = new DisallowStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots.
    /// The test fails if the snapshots are different.
    /// </summary>
    public static SnapshotUpdateStrategy MergeTool { get; } = new MergeToolStrategy();

    /// <summary>
    /// Open a merge tool to update the snapshots and wait for it to close before continuing the test execution.
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
            var strategyFromEnvironmentVariable = GetStrategyFromEnvironmentVariable();
            if (strategyFromEnvironmentVariable is not null)
                return strategyFromEnvironmentVariable;

            return Disallow;
        }
    }

    public virtual bool ReuseTemporaryFile => true;

    internal bool CanUpdateSnapshotInternal(SnapshotSettingsType settings, string path, string? expectedSnapshot, string? actualSnapshot)
    {
        if (settings.AutoDetectContinuousEnvironment && SnapshotSettingsType.IsRunningOnContinuousIntegration())
            return false;

        return CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);
    }

    /// <summary>Indicates if a snapshot must be updated.</summary>
    public abstract bool CanUpdateSnapshot(SnapshotSettingsType settings, string path, string? expectedSnapshot, string? actualSnapshot);

#if !INLINE_SNAPSHOT_TESTING
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
#endif

    public abstract void UpdateFile(SnapshotSettingsType settings, string targetFile, string tempFile);

    /// <summary>Indicates if an exception must be thrown when the snapshots differ.</summary>
    public abstract bool MustReportError(SnapshotSettingsType settings, string path);

    private protected static void MoveFile(string source, string destination)
    {
#if INLINE_SNAPSHOT_TESTING
        var sourcePath = FullPath.FromPath(source).Value;
        var destinationPath = FullPath.FromPath(destination).Value;
#else
        var sourcePath = source;
        var destinationPath = destination;
#endif

        if (sourcePath == destinationPath)
            return;

        var fi = new FileInfo(sourcePath);
        fi.TrySetReadOnly(false);

#if NETCOREAPP3_0_OR_GREATER
        File.Move(sourcePath, destinationPath, overwrite: true);
#else
        File.Copy(sourcePath, destinationPath, overwrite: true);
        TryDeleteFile(sourcePath);
#endif
    }

#if !INLINE_SNAPSHOT_TESTING
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
#endif

    private protected static void TryDeleteFile(string path)
    {
        try
        {
#if INLINE_SNAPSHOT_TESTING
            var filePath = FullPath.FromPath(path).Value;
#else
            var filePath = path;
#endif
            var fi = new FileInfo(filePath);
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

    private static SnapshotUpdateStrategy? GetStrategyFromEnvironmentVariable()
    {
        var variable = Environment.GetEnvironmentVariable(SnapshotUpdateStrategyEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(variable))
            return null;

        var strategyName = variable.Trim();

        foreach (var property in SnapshotUpdateStrategyProperties)
        {
            if (!typeof(SnapshotUpdateStrategy).IsAssignableFrom(property.PropertyType))
                continue;

            if (!string.Equals(property.Name, strategyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return (SnapshotUpdateStrategy?)property.GetValue(null);
        }

        return null;
    }
}
