using Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Framework.SnapshotTesting.Utils;
using System.ComponentModel;
using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public abstract class SnapshotUpdateStrategy
{
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "SNAPSHOTTESTING_STRATEGY";
    private const int MaxFileOperationAttemptCount = 8;

    // Default is excluded on purpose: its getter calls GetStrategyFromEnvironmentVariable, so resolving it from
    // here would re-enter the getter and recurse until the process died with an uncatchable StackOverflowException.
    private static readonly IReadOnlyList<PropertyInfo> SnapshotUpdateStrategyProperties =
        [.. typeof(SnapshotUpdateStrategy).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.Name is not nameof(Default))];

    // Content of the actual files of the assertion whose snapshots are being updated on this thread, by actual file path.
    [ThreadStatic]
    private static Dictionary<string, byte[]>? s_actualFileContents;

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

    /// <summary>Overwrite the verified snapshot files with the new snapshots. The test fails if the snapshots are different.</summary>
    public static SnapshotUpdateStrategy Overwrite { get; } = new AlwaysStrategy();

    /// <summary>Overwrite the verified snapshot files with the new snapshots. The test won't fail.</summary>
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

    /// <summary>
    /// Not used by file snapshots: the actual file always lives next to the verified file, and it is removed once it is
    /// no longer relevant.
    /// </summary>
    /// <remarks>This property has no effect and will be removed in the next major version.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual bool ReuseTemporaryFile => true;

    internal bool CanUpdateSnapshotInternal(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot)
    {
        if (settings.AutoDetectContinuousEnvironment && SnapshotSettings.IsRunningOnContinuousIntegration())
            return false;

        return CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);
    }

    /// <summary>
    /// Indicates whether the verified snapshot files of an assertion can be updated. It is called when the snapshots
    /// differ, or when <see cref="SnapshotSettings.ForceUpdateSnapshots" /> is set.
    /// </summary>
    /// <param name="settings">The settings of the assertion.</param>
    /// <param name="path">The path of the source file that contains the test.</param>
    /// <param name="expectedSnapshot">
    /// The paths of the verified snapshot files found on disk for the assertion, sorted and separated by <c>\n</c>. It is
    /// not the content of the files. It is <see langword="null" /> when there is no such file, or when the snapshots do
    /// not differ.
    /// </param>
    /// <param name="actualSnapshot">
    /// The paths of the verified snapshot files the assertion produces, sorted and separated by <c>\n</c>. It is not the
    /// content of the files. When a single snapshot changed, it is the same path as <paramref name="expectedSnapshot" />.
    /// It is <see langword="null" /> when the snapshots do not differ.
    /// </param>
    /// <remarks>
    /// Use <see cref="UpdateFiles" /> to inspect the files: it receives each verified file with the actual file that
    /// contains the new snapshot.
    /// </remarks>
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

    /// <summary>
    /// Replaces the verified file with the content of the actual file, and removes the actual file so that a later
    /// approval cannot promote it again.
    /// </summary>
    /// <remarks>
    /// Several processes can update the same snapshot at the same time: a test project that targets several frameworks
    /// runs one process per framework. The verified file is replaced atomically, so a reader never sees a partial file,
    /// a verified file that already has the expected content is left untouched, and an actual file that another process
    /// already promoted is not an error.
    /// </remarks>
    private protected static void PromoteFile(string actualFilePath, string verifiedFilePath)
    {
        if (actualFilePath == verifiedFilePath)
            return;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                PromoteFileOnce(actualFilePath, verifiedFilePath);
                return;
            }
            catch (IOException) when (attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
        }
    }

    private static void PromoteFileOnce(string actualFilePath, string verifiedFilePath)
    {
        // During an assertion, the engine knows what it wrote to the actual file a moment ago. Another process validating
        // the same snapshot may have replaced or removed the file since: one that promoted it first, one whose output
        // differs, or one whose own snapshot matched and that took the file for a leftover. A missing file therefore does
        // not mean that the verified file has this assertion's content, so the verified file is written from memory. A
        // verified file that already has the content is left untouched.
        if (s_actualFileContents is null || !s_actualFileContents.TryGetValue(actualFilePath, out var content))
        {
            try
            {
                content = ReadAllBytesWithRetry(actualFilePath);
            }
            catch (FileNotFoundException) when (File.Exists(verifiedFilePath))
            {
                // The strategy was called outside of an assertion, so nothing tells what the actual file contained.
                // Another process validating the same snapshot most likely promoted it first.
                return;
            }
        }

        WriteAllBytesAtomically(verifiedFilePath, content);
        DeleteFileIfContentEquals(actualFilePath, content);
    }

    /// <summary>
    /// Makes the content of the actual files available to <see cref="PromoteFile" /> while a strategy updates the
    /// snapshots of an assertion on the current thread, and returns the previous value.
    /// </summary>
    internal static Dictionary<string, byte[]>? SetActualFileContents(Dictionary<string, byte[]>? contents)
    {
        var previous = s_actualFileContents;
        s_actualFileContents = contents;
        return previous;
    }

    /// <summary>
    /// Writes a file so that a concurrent reader sees either its previous content or the new one, never a partial
    /// file. A file that already has the content is not rewritten. Sharing violations are retried.
    /// </summary>
    internal static void WriteAllBytesWithRetry(FullPath path, byte[] data)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                WriteAllBytesAtomically(path, data);
                return;
            }
            catch (IOException) when (attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
        }
    }

    private static void WriteAllBytesAtomically(string path, byte[] data)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            if (fileInfo.Length == data.Length && HasContent(path, data))
                return;

            fileInfo.TrySetReadOnly(false);
        }
        else
        {
            fileInfo.Directory?.Create();
        }

        // The temporary file is in the same directory, so the rename cannot cross volumes. Its name matches neither the
        // verified nor the actual naming pattern, so neither the engine nor the approval tool can mistake a file left
        // behind by a crashed process for a snapshot.
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path) ?? "", "." + Guid.NewGuid().ToString("N") + ".snapshot.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private static bool HasContent(string path, byte[] data)
    {
        try
        {
            return ReadAllBytesWithRetry(path).AsSpan().SequenceEqual(data);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static void DeleteFileIfContentEquals(string path, byte[] data)
    {
        // Another process may have written a different result in the meantime; that one is not ours to remove.
        try
        {
            if (HasContent(path, data))
            {
                TryDeleteFile(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads a snapshot file that another process may be replacing at the same time. The file is opened with
    /// <see cref="FileShare.Delete" /> so the reader does not prevent the replacement, and the sharing violation a
    /// reader gets on Windows while the file is being replaced is retried. A missing file is not retried.
    /// </summary>
    internal static byte[] ReadAllBytesWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
            catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException && attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxFileOperationAttemptCount)
            {
                WaitBeforeRetry(attempt);
            }
        }
    }

    /// <summary>Called with the number of the failed attempt before a file operation is retried. Only meant for tests.</summary>
    internal static Action<int>? RetryingFileOperation { get; set; }

    private static void WaitBeforeRetry(int attempt)
    {
        RetryingFileOperation?.Invoke(attempt);
        Thread.Sleep(TimeSpan.FromMilliseconds(30 * attempt));
    }

    internal static void TryDeleteFile(string path)
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
