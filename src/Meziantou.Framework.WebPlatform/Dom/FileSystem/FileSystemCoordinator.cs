namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The lock of a file entry (https://fs.spec.whatwg.org/#file-entry-lock). The specification has an exclusive lock (the
/// access handles) and a shared lock (the writable streams); the modes of the locking proposal that Chromium implements
/// (https://github.com/whatwg/fs/blob/main/proposals/MultipleReadersWriters.md) refine them: a lock is shared by the
/// primitives of the same shareable mode, and conflicts with every other one.
/// </summary>
internal enum FileSystemLockMode
{
    /// <summary>A writable stream (the shared lock of the specification): the "siloed" mode.</summary>
    WritableSiloed,

    /// <summary>A writable stream of the "exclusive" mode.</summary>
    WritableExclusive,

    /// <summary>An access handle (the exclusive lock of the specification): the "readwrite" mode.</summary>
    AccessHandleReadWrite,

    /// <summary>An access handle of the "read-only" mode, shared by the access handles of that mode.</summary>
    AccessHandleReadOnly,

    /// <summary>An access handle of the "readwrite-unsafe" mode, shared by the access handles of that mode.</summary>
    AccessHandleReadWriteUnsafe,
}

/// <summary>A lock taken on a file entry, released by <see cref="FileSystemCoordinator.ReleaseLock"/>.</summary>
internal sealed class FileSystemLock(string origin, string pathKey)
{
    public string Origin { get; } = origin;

    public string PathKey { get; } = pathKey;

    public bool IsReleased { get; set; }
}

/// <summary>
/// The state that the windows and the workers that use a <see cref="FileSystemBackend"/> share: the locks of its file
/// entries. The steps that the specification enqueues on the file system queue run under a mutex, on the thread of the
/// caller (the thread of the event loop of a window or of a worker).
/// </summary>
internal sealed class FileSystemCoordinator(FileSystemBackend backend)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Origin, string PathKey), (FileSystemLockMode Mode, int Count)> _locks = [];

    public FileSystemBackend Backend { get; } = backend;

    /// <summary>The mutex of the file system queue: the checks and the changes of an operation are atomic for the other threads.</summary>
    public Lock Gate => _gate;

    // The names do not contain '/'
    private static string GetPathKey(IReadOnlyList<string> path) => string.Join('/', path);

    /// <summary>https://fs.spec.whatwg.org/#file-entry-lock-take: returns the lock, or null (failure).</summary>
    public FileSystemLock? TakeLock(string origin, IReadOnlyList<string> path, FileSystemLockMode mode)
    {
        var key = (origin, GetPathKey(path));
        lock (_gate)
        {
            if (_locks.TryGetValue(key, out var state))
            {
                if (state.Mode != mode || mode is FileSystemLockMode.WritableExclusive or FileSystemLockMode.AccessHandleReadWrite)
                    return null;

                _locks[key] = (mode, state.Count + 1);
            }
            else
            {
                _locks.Add(key, (mode, 1));
            }

            return new FileSystemLock(origin, key.Item2);
        }
    }

    /// <summary>https://fs.spec.whatwg.org/#file-entry-lock-release</summary>
    public void ReleaseLock(FileSystemLock fileLock)
    {
        lock (_gate)
        {
            if (fileLock.IsReleased)
                return;

            fileLock.IsReleased = true;
            var key = (fileLock.Origin, fileLock.PathKey);
            if (_locks.TryGetValue(key, out var state))
            {
                if (state.Count <= 1)
                {
                    _locks.Remove(key);
                }
                else
                {
                    _locks[key] = (state.Mode, state.Count - 1);
                }
            }
        }
    }

    /// <summary>Whether the entry at <paramref name="path"/>, or a file of the directory at that path, is locked: such an entry is not removed nor replaced.</summary>
    public bool IsLocked(string origin, IReadOnlyList<string> path)
    {
        var pathKey = GetPathKey(path);
        lock (_gate)
        {
            foreach (var (lockOrigin, lockPathKey) in _locks.Keys)
            {
                if (lockOrigin != origin)
                    continue;

                if (pathKey.Length == 0 || lockPathKey == pathKey || (lockPathKey.Length > pathKey.Length && lockPathKey[pathKey.Length] == '/' && lockPathKey.StartsWith(pathKey, StringComparison.Ordinal)))
                    return true;
            }
        }

        return false;
    }
}
