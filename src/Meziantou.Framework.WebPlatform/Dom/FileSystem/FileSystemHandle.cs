using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of a file system entry (https://fs.spec.whatwg.org/#enumdef-filesystemhandlekind).</summary>
/// <seealso href="https://fs.spec.whatwg.org/#enumdef-filesystemhandlekind"/>
[IdlInterface("FileSystemHandleKind", "https://fs.spec.whatwg.org/#enumdef-filesystemhandlekind")]
public enum FileSystemHandleKind
{
    /// <summary>A file.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemhandlekind-file"/>
    [IdlMember("file", "https://fs.spec.whatwg.org/#dom-filesystemhandlekind-file")]
    File,

    /// <summary>A directory.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemhandlekind-directory"/>
    [IdlMember("directory", "https://fs.spec.whatwg.org/#dom-filesystemhandlekind-directory")]
    Directory,
}

/// <summary>
/// A handle to a file or a directory of the origin private file system (https://fs.spec.whatwg.org/#api-filesystemhandle),
/// obtained from <see cref="StorageManager.GetDirectory"/>. A handle is a locator (the path of the entry in the file system
/// of its origin): the entry may not exist, and several handles can locate the same entry.
/// </summary>
/// <seealso href="https://fs.spec.whatwg.org/#filesystemhandle"/>
[IdlInterface("FileSystemHandle", "https://fs.spec.whatwg.org/#filesystemhandle")]
public abstract class FileSystemHandle : IStructuredSerializable
{
    private protected FileSystemHandle(GlobalScope global, FileSystemBackend backend, string origin, string[] path)
    {
        Global = global;
        Backend = backend;
        Origin = origin;
        Path = path;
    }

    /// <summary>The window or the worker of the handle (its relevant global object).</summary>
    internal GlobalScope Global { get; }

    /// <summary>The file system root of the locator (https://fs.spec.whatwg.org/#locator-root): the backend and the origin of the bucket file system.</summary>
    internal FileSystemBackend Backend { get; }

    internal string Origin { get; }

    /// <summary>https://fs.spec.whatwg.org/#locator-path, without the empty string of the root directory.</summary>
    internal string[] Path { get; private protected set; }

    /// <summary>Gets the kind of the entry.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemhandle-kind"/>
    [IdlMember("kind", "https://fs.spec.whatwg.org/#dom-filesystemhandle-kind")]
    public abstract FileSystemHandleKind Kind { get; }

    /// <summary>Gets the name of the entry: the last component of its path (the empty string for the root directory).</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemhandle-name"/>
    [IdlMember("name", "https://fs.spec.whatwg.org/#dom-filesystemhandle-name")]
    public string Name => Path.Length == 0 ? "" : Path[^1];

    /// <summary>Returns whether the two handles locate the same entry (https://fs.spec.whatwg.org/#dom-filesystemhandle-issameentry).</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemhandle-issameentry"/>
    [IdlMember("isSameEntry", "https://fs.spec.whatwg.org/#dom-filesystemhandle-issameentry")]
    public Task<bool> IsSameEntry(FileSystemHandle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Invoke(() => IsSameLocator(other));
    }

    /// <summary>
    /// Runs the steps that an operation enqueues on the file system queue (https://fs.spec.whatwg.org/#file-system-queue),
    /// now, then settles its promise from a task of the event loop of the global object ("queue a storage task"): the
    /// errors of the operation reject the promise.
    /// </summary>
    private protected Task<T> Invoke<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>();
        T? result = default;
        Exception? error = null;
        try
        {
            result = operation();
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException)
        {
            error = ex;
        }

        Global.QueueGlobalTask(() =>
        {
            if (error is not null)
            {
                completion.TrySetException(error);
            }
            else
            {
                completion.TrySetResult(result!);
            }
        });
        return completion.Task;
    }

    /// <summary>https://fs.spec.whatwg.org/#locator-the-same-locator-as</summary>
    internal bool IsSameLocator(FileSystemHandle other)
        => Kind == other.Kind && HasSameRoot(other) && Path.AsSpan().SequenceEqual(other.Path);

    internal bool HasSameRoot(FileSystemHandle other) => ReferenceEquals(Backend, other.Backend) && Origin == other.Origin;

    internal FileSystemCoordinator Coordinator => Backend.Coordinator;

    /// <summary>The current time of the clock of the global object, in milliseconds since the Unix epoch.</summary>
    internal long CurrentTime => Global.EventLoop.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>
    /// A valid file name (https://fs.spec.whatwg.org/#valid-file-name): not empty, not <c>.</c> nor <c>..</c>, without path
    /// separator. As in the browsers, <c>\</c> is a path separator on every platform.
    /// </summary>
    internal static bool IsValidFileName(string name) => name.Length > 0 && name is not ("." or "..") && !name.AsSpan().ContainsAny('/', '\\');

    /// <summary>Runs an operation of the backend: its I/O errors reject the promise with an <c>InvalidStateError</c>.</summary>
    internal static T Run<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw DomException.InvalidState("The file system operation failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Removes the entry (the <c>remove()</c> method that the browsers implement for the handles of the origin private file
    /// system; it is not in the File System Standard yet, see https://github.com/whatwg/fs/pull/9): as
    /// <see cref="FileSystemDirectoryHandle.RemoveEntry"/> called on its parent directory. Removing the root directory
    /// removes all its entries.
    /// </summary>
    /// <param name="options">The options: <see cref="FileSystemRemoveOptions.Recursive"/> removes a directory that is not empty.</param>
    [NotInIdl("The remove() method of the browsers, which is not in the File System Standard yet (https://github.com/whatwg/fs/pull/9): the bindings define it")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public Task Remove(FileSystemRemoveOptions? options = null)
        => Invoke<object?>(() =>
        {
            // The root directory is removed with its entries
            RemoveCore(Backend, Origin, Path, Kind, recursive: Path.Length == 0 || (options?.Recursive ?? false));
            return null;
        });

    /// <summary>The removal of an entry (https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-removeentry, from the step that finds the child).</summary>
    internal static void RemoveCore(FileSystemBackend backend, string origin, string[] path, FileSystemHandleKind? expectedKind, bool recursive)
    {
        var coordinator = backend.Coordinator;
        lock (coordinator.Gate)
        {
            var kind = Run(() => backend.GetEntryKind(origin, path));
            if (kind is null || (expectedKind is not null && kind != expectedKind))
                throw DomException.NotFound("The entry does not exist");

            // As in the browsers, an entry is not removed while a writable stream or an access handle of the file, or of a
            // file of the directory, is open
            if (coordinator.IsLocked(origin, path))
                throw DomException.NoModificationAllowed("A file is locked by a writable stream or by an access handle");

            if (kind == FileSystemHandleKind.Directory && !recursive && Run(() => backend.GetEntries(origin, path)) is { Count: > 0 })
                throw new DomException("The directory is not empty", "InvalidModificationError");

            if (!Run(() => backend.RemoveEntry(origin, path)) && path.Length > 0)
                throw DomException.NotFound("The entry does not exist");
        }
    }

    /// <summary>
    /// The serialization steps (https://fs.spec.whatwg.org/#filesystemhandle-serialization-steps): the origin of the relevant
    /// settings object and the locator. The deserialization steps run in <see cref="SerializedFileSystemHandle.Deserialize"/>.
    /// </summary>
    object IStructuredSerializable.CloneForStructuredSerialization() => new SerializedFileSystemHandle(Global.EnvironmentDocument.Origin.Serialize(), Backend, Origin, Path, Kind);
}

/// <summary>
/// The serialized form of a <see cref="FileSystemHandle"/>: the origin of its settings object and its locator. A handle
/// read from a storage (IndexedDB) has no backend: its file system root is the bucket file system of the global object
/// that deserializes it.
/// </summary>
internal sealed class SerializedFileSystemHandle(string settingsOrigin, FileSystemBackend? backend, string origin, string[] path, FileSystemHandleKind kind) : IStructuredSerializable
{
    public string SettingsOrigin => settingsOrigin;

    public string Origin => origin;

    public IReadOnlyList<string> Path => path;

    public FileSystemHandleKind Kind => kind;

    /// <summary>
    /// https://fs.spec.whatwg.org/#filesystemhandle-deserialization-steps: a handle of the target global object, or a
    /// <c>DataCloneError</c> when its origin is not the one of the serialized handle.
    /// </summary>
    public FileSystemHandle Deserialize(GlobalScope target)
    {
        var targetOrigin = target.EnvironmentDocument.Origin;
        if (targetOrigin.IsOpaque || targetOrigin.Serialize() != settingsOrigin)
            throw new DomException("A FileSystemHandle cannot be deserialized in another origin", "DataCloneError");

        var root = backend ?? StorageManager.ResolveFileSystem(target)?.Backend ?? throw new DomException("The file system of the FileSystemHandle is not available", "DataCloneError");
        return kind == FileSystemHandleKind.Directory
            ? new FileSystemDirectoryHandle(target, root, origin, path)
            : new FileSystemFileHandle(target, root, origin, path);
    }

    // The serialized form is cloned when a message is delivered to several targets, and deserialized by the bindings (or
    // here, in the current global object, without script engine)
    object IStructuredSerializable.CloneForStructuredSerialization()
        => GlobalScope.Current is { } current ? Deserialize(current) : throw new DomException("A FileSystemHandle is deserialized in a global object", "DataCloneError");
}

/// <summary>The options of <see cref="FileSystemDirectoryHandle.GetFileHandle"/> (https://fs.spec.whatwg.org/#dictdef-filesystemgetfileoptions).</summary>
/// <seealso href="https://fs.spec.whatwg.org/#dictdef-filesystemgetfileoptions"/>
[IdlInterface("FileSystemGetFileOptions", "https://fs.spec.whatwg.org/#dictdef-filesystemgetfileoptions")]
public sealed class FileSystemGetFileOptions
{
    /// <summary>Gets whether the file is created when it does not exist.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemgetfileoptions-create"/>
    [IdlMember("create", "https://fs.spec.whatwg.org/#dom-filesystemgetfileoptions-create")]
    public bool Create { get; init; }
}

/// <summary>The options of <see cref="FileSystemDirectoryHandle.GetDirectoryHandle"/> (https://fs.spec.whatwg.org/#dictdef-filesystemgetdirectoryoptions).</summary>
/// <seealso href="https://fs.spec.whatwg.org/#dictdef-filesystemgetdirectoryoptions"/>
[IdlInterface("FileSystemGetDirectoryOptions", "https://fs.spec.whatwg.org/#dictdef-filesystemgetdirectoryoptions")]
public sealed class FileSystemGetDirectoryOptions
{
    /// <summary>Gets whether the directory is created when it does not exist.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemgetdirectoryoptions-create"/>
    [IdlMember("create", "https://fs.spec.whatwg.org/#dom-filesystemgetdirectoryoptions-create")]
    public bool Create { get; init; }
}

/// <summary>The options of <see cref="FileSystemDirectoryHandle.RemoveEntry"/> (https://fs.spec.whatwg.org/#dictdef-filesystemremoveoptions).</summary>
/// <seealso href="https://fs.spec.whatwg.org/#dictdef-filesystemremoveoptions"/>
[IdlInterface("FileSystemRemoveOptions", "https://fs.spec.whatwg.org/#dictdef-filesystemremoveoptions")]
public sealed class FileSystemRemoveOptions
{
    /// <summary>Gets whether a directory is removed with its entries.</summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemremoveoptions-recursive"/>
    [IdlMember("recursive", "https://fs.spec.whatwg.org/#dom-filesystemremoveoptions-recursive")]
    public bool Recursive { get; init; }
}

/// <summary>
/// A handle to a directory (https://fs.spec.whatwg.org/#api-filesystemdirectoryhandle). In the scripts, it is an async
/// iterable of its entries (<c>entries()</c>, <c>keys()</c>, <c>values()</c>); in .NET, see <see cref="GetEntriesAsync"/>.
/// </summary>
/// <seealso href="https://fs.spec.whatwg.org/#filesystemdirectoryhandle"/>
[IdlInterface("FileSystemDirectoryHandle", "https://fs.spec.whatwg.org/#filesystemdirectoryhandle")]
public sealed class FileSystemDirectoryHandle : FileSystemHandle
{
    internal FileSystemDirectoryHandle(GlobalScope global, FileSystemBackend backend, string origin, string[] path)
        : base(global, backend, origin, path)
    {
    }

    /// <inheritdoc/>
    public override FileSystemHandleKind Kind => FileSystemHandleKind.Directory;

    /// <summary>
    /// Returns a handle to the file named <paramref name="name"/> of the directory
    /// (https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getfilehandle), created when it does not exist and the
    /// <see cref="FileSystemGetFileOptions.Create"/> option is set. The task fails with a <see cref="TypeErrorException"/>
    /// for an invalid name, a <c>NotFoundError</c> when the directory or the file does not exist, and a
    /// <c>TypeMismatchError</c> when the name is the one of a directory.
    /// </summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getfilehandle"/>
    [IdlMember("getFileHandle", "https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getfilehandle")]
    public Task<FileSystemFileHandle> GetFileHandle(string name, FileSystemGetFileOptions? options = null)
        => Invoke(() => (FileSystemFileHandle)GetChild(name, FileSystemHandleKind.File, options?.Create ?? false));

    /// <summary>
    /// Returns a handle to the directory named <paramref name="name"/> of the directory
    /// (https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getdirectoryhandle), created when it does not exist and the
    /// <see cref="FileSystemGetDirectoryOptions.Create"/> option is set. The task fails as the one of <see cref="GetFileHandle"/>.
    /// </summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getdirectoryhandle"/>
    [IdlMember("getDirectoryHandle", "https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-getdirectoryhandle")]
    public Task<FileSystemDirectoryHandle> GetDirectoryHandle(string name, FileSystemGetDirectoryOptions? options = null)
        => Invoke(() => (FileSystemDirectoryHandle)GetChild(name, FileSystemHandleKind.Directory, options?.Create ?? false));

    private FileSystemHandle GetChild(string name, FileSystemHandleKind kind, bool create)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!IsValidFileName(name))
            throw new TypeErrorException($"'{name}' is not a valid file name");

        string[] childPath = [.. Path, name];
        lock (Coordinator.Gate)
        {
            if (Run(() => Backend.GetEntryKind(Origin, Path)) != FileSystemHandleKind.Directory)
                throw DomException.NotFound("The directory does not exist");

            var existing = Run(() => Backend.GetEntryKind(Origin, childPath));
            if (existing is not null && existing != kind)
                throw new DomException(kind == FileSystemHandleKind.File ? "The entry is a directory" : "The entry is a file", "TypeMismatchError");

            if (existing is null)
            {
                if (!create)
                    throw DomException.NotFound("The entry does not exist");

                // The entry counts for the quota of the storage
                StorageManager.CheckQuota(Global, Backend, Origin, additionalBytes: 0);
                var time = CurrentTime;
                if (!Run(() => Backend.CreateEntry(Origin, childPath, kind, time)))
                    throw DomException.InvalidState("The entry could not be created");
            }
        }

        return kind == FileSystemHandleKind.File ? new FileSystemFileHandle(Global, Backend, Origin, childPath) : new FileSystemDirectoryHandle(Global, Backend, Origin, childPath);
    }

    /// <summary>
    /// Removes the entry named <paramref name="name"/> of the directory
    /// (https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-removeentry). The task fails with a
    /// <see cref="TypeErrorException"/> for an invalid name, a <c>NotFoundError</c> when the directory or the entry does not
    /// exist, an <c>InvalidModificationError</c> for a directory that is not empty without the
    /// <see cref="FileSystemRemoveOptions.Recursive"/> option, and (as in the browsers) a
    /// <c>NoModificationAllowedError</c> while a writable stream or an access handle of the file, or of a file of the
    /// directory, is open.
    /// </summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-removeentry"/>
    [IdlMember("removeEntry", "https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-removeentry")]
    public Task RemoveEntry(string name, FileSystemRemoveOptions? options = null)
        => Invoke<object?>(() =>
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!IsValidFileName(name))
                throw new TypeErrorException($"'{name}' is not a valid file name");

            lock (Coordinator.Gate)
            {
                if (Run(() => Backend.GetEntryKind(Origin, Path)) != FileSystemHandleKind.Directory)
                    throw DomException.NotFound("The directory does not exist");

                RemoveCore(Backend, Origin, [.. Path, name], expectedKind: null, options?.Recursive ?? false);
            }

            return null;
        });

    /// <summary>
    /// Returns the names of the directories from this directory to <paramref name="possibleDescendant"/>, then its name
    /// (https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-resolve): an empty list for the directory itself, and null
    /// when the entry is not in the directory.
    /// </summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-resolve"/>
    [IdlMember("resolve", "https://fs.spec.whatwg.org/#dom-filesystemdirectoryhandle-resolve")]
    public Task<IReadOnlyList<string>?> Resolve(FileSystemHandle possibleDescendant)
    {
        ArgumentNullException.ThrowIfNull(possibleDescendant);

        // https://fs.spec.whatwg.org/#locator-resolve
        var childPath = possibleDescendant.Path;
        return Invoke<IReadOnlyList<string>?>(() =>
            !HasSameRoot(possibleDescendant) || Path.Length > childPath.Length || !childPath.AsSpan(0, Path.Length).SequenceEqual(Path) ? null : childPath[Path.Length..]);
    }

    /// <summary>
    /// Iterates over the entries of the directory (https://fs.spec.whatwg.org/#api-filesystemdirectoryhandle-asynciterable),
    /// in no particular order: the entries created or removed during the iteration may or may not be returned.
    /// </summary>
    [NotInIdl("The .NET form of the async iterable declaration: the bindings define entries(), keys(), values() and @@asyncIterator")]
    public async IAsyncEnumerable<KeyValuePair<string, FileSystemHandle>> GetEntriesAsync()
    {
        var pastResults = new HashSet<string>(StringComparer.Ordinal);
        while (await GetNextEntry(pastResults).ConfigureAwait(false) is { } entry)
        {
            yield return entry;
        }
    }

    /// <summary>
    /// https://fs.spec.whatwg.org/#filesystemdirectoryhandle-get-the-next-iteration-result: an entry of the directory whose
    /// name is not in <paramref name="pastResults"/> (the past results of the iterator), or null at the end of the iteration.
    /// </summary>
    internal Task<KeyValuePair<string, FileSystemHandle>?> GetNextEntry(HashSet<string> pastResults)
        => Invoke<KeyValuePair<string, FileSystemHandle>?>(() =>
        {
            if (Run(() => Backend.GetEntries(Origin, Path)) is not { } entries)
                throw DomException.NotFound("The directory does not exist");

            foreach (var entry in entries)
            {
                if (!pastResults.Add(entry.Name))
                    continue;

                string[] childPath = [.. Path, entry.Name];
                FileSystemHandle handle = entry.Kind == FileSystemHandleKind.File ? new FileSystemFileHandle(Global, Backend, Origin, childPath) : new FileSystemDirectoryHandle(Global, Backend, Origin, childPath);
                return new KeyValuePair<string, FileSystemHandle>(entry.Name, handle);
            }

            return null;
        });
}
