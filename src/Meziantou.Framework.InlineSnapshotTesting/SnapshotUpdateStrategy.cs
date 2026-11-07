using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using System.Reflection;

namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class SnapshotUpdateStrategy
{
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "INLINESNAPSHOTTESTING_STRATEGY";

    // Default is excluded on purpose: its getter calls GetStrategyFromEnvironmentVariable, so resolving it from
    // here would re-enter the getter and recurse until the process died with an uncatchable StackOverflowException.
    private static readonly IReadOnlyList<PropertyInfo> SnapshotUpdateStrategyProperties =
        [.. typeof(SnapshotUpdateStrategy).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.Name is not nameof(Default))];

    /// <summary>Do not update the snapshots and fail the tests if the snapshots are different.</summary>
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

    internal bool CanUpdateSnapshotInternal(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot)
    {
        if (IsUpdateDisabledByEnvironment(settings))
            return false;

        return CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);
    }

    internal static bool IsUpdateDisabledByEnvironment(InlineSnapshotSettings settings) => settings.AutoDetectContinuousEnvironment && InlineSnapshotSettings.IsRunningOnContinuousIntegration();

    /// <summary>Indicates if an an inline snapshot must be updated</summary>
    public abstract bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot);

    public abstract void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile);

    /// <summary>Indicates if an exception must be thrown when the snapshots differ.</summary>
    public abstract bool MustReportError(InlineSnapshotSettings settings, string path);

    private protected static void MoveFile(string source, string destination)
    {
        var sourcePath = FullPath.FromPath(source);
        var destinationPath = FullPath.FromPath(destination);
        if (sourcePath == destinationPath)
            return;

        // Writing into the existing file, rather than moving the new file over it, keeps what belongs to the file itself:
        // the target of a symbolic link, hard links, the permissions, and on Windows the ACL, which a moved file would
        // bring from the temporary directory.
        var content = File.ReadAllBytes(sourcePath);

        // An editor or another test process can hold the source file open for a moment, which is a sharing violation on Windows.
        const int MaxAttemptCount = 8;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                WriteContent(destinationPath, content);
                TryDeleteFile(sourcePath);
                return;
            }
            catch (IOException ex) when (ex is not FileNotFoundException && attempt < MaxAttemptCount)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(30 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttemptCount)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(30 * attempt));
            }
        }

        static void WriteContent(FullPath path, byte[] content)
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            stream.Write(content);
            stream.SetLength(content.Length);
        }
    }

    private protected static void TryDeleteFile(string path)
    {
        try
        {
            var filePath = FullPath.FromPath(path);
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

    /// <summary>
    /// Returns a message when <c>INLINESNAPSHOTTESTING_STRATEGY</c> names no strategy. The variable is then ignored, which
    /// would otherwise go unnoticed as the default strategy does not update snapshots either.
    /// </summary>
    internal static string? GetUnknownStrategyEnvironmentVariableMessage() => GetUnknownStrategyMessage(Environment.GetEnvironmentVariable(SnapshotUpdateStrategyEnvironmentVariableName));

    internal static string? GetUnknownStrategyMessage(string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable) || FindStrategy(variable.Trim()) is not null)
            return null;

        var validNames = string.Join(", ", SnapshotUpdateStrategyProperties.Select(property => property.Name));
        return $"The {SnapshotUpdateStrategyEnvironmentVariableName} environment variable is ignored: '{variable}' is not a known strategy. Valid values are {validNames}.";
    }

    private static SnapshotUpdateStrategy? GetStrategyFromEnvironmentVariable()
    {
        var variable = Environment.GetEnvironmentVariable(SnapshotUpdateStrategyEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(variable))
            return null;

        return FindStrategy(variable.Trim());
    }

    private static SnapshotUpdateStrategy? FindStrategy(string strategyName)
    {
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
