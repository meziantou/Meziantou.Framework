using System.Collections.Concurrent;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

/// <summary>
/// An empty verified file created so a merge tool has two sides to compare when a snapshot has no verified
/// file yet. It is only a scaffold: a merge that is abandoned or that never starts must not leave it behind,
/// or the next run would compare against it and silently record "empty" as the expectation of the test.
/// </summary>
internal sealed class VerifiedFilePlaceholder
{
    /// <summary>
    /// The timestamp given to a placeholder. Writing the file - even with no content, which is a meaningful
    /// snapshot - moves its timestamp to the present, so an untouched placeholder can be recognized without
    /// depending on the resolution of the file system clock.
    /// </summary>
    private static readonly DateTime PlaceholderLastWriteTimeUtc = DateTime.UnixEpoch;

    private static readonly ConcurrentQueue<VerifiedFilePlaceholder> PlaceholdersToDeleteOnProcessExit = new();
    private static int s_processExitHandlerRegistered;

    private readonly string _path;
    private readonly DateTime _lastWriteTimeUtc;

    private VerifiedFilePlaceholder(string path, DateTime lastWriteTimeUtc)
    {
        _path = path;
        _lastWriteTimeUtc = lastWriteTimeUtc;
    }

    /// <summary>
    /// Creates an empty verified file when there is none. Returns <see langword="null" /> when the file
    /// already exists, as its content is the recorded expectation and must be left untouched, or when the
    /// file cannot be created, in which case the merge tool reports the missing file itself.
    /// </summary>
    public static VerifiedFilePlaceholder? TryCreate(string path)
    {
        try
        {
            if (File.Exists(path))
                return null;

            new FileInfo(path).Directory?.Create();
            using (File.Create(path))
            {
            }

            File.SetLastWriteTimeUtc(path, PlaceholderLastWriteTimeUtc);
            return new VerifiedFilePlaceholder(path, File.GetLastWriteTimeUtc(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the placeholder unless the merge tool wrote to it.</summary>
    public void DeleteIfUnused()
    {
        try
        {
            var fileInfo = new FileInfo(_path);
            if (!fileInfo.Exists || fileInfo.Length != 0 || fileInfo.LastWriteTimeUtc != _lastWriteTimeUtc)
                return;

            fileInfo.TrySetReadOnly(false);
            fileInfo.Delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Defers <see cref="DeleteIfUnused" /> to the end of the process. A merge tool that runs without
    /// blocking the test is still open when the assertion returns and expects to write to the verified file
    /// when the developer saves, so the placeholder cannot be removed right away. Removing it when the
    /// process ends is enough to keep an abandoned merge from leaving one behind for the next run, and a
    /// merge that is saved afterwards recreates the file.
    /// </summary>
    public void DeleteOnProcessExitIfUnused()
    {
        PlaceholdersToDeleteOnProcessExit.Enqueue(this);
        if (Interlocked.Exchange(ref s_processExitHandlerRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += DeletePendingPlaceholders;
        }
    }

    private static void DeletePendingPlaceholders(object? sender, EventArgs e)
    {
        while (PlaceholdersToDeleteOnProcessExit.TryDequeue(out var placeholder))
        {
            placeholder.DeleteIfUnused();
        }
    }
}
