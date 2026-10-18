using Meziantou.Framework.WebPlatform.Scripting;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="FileSystemFileHandle.CreateWritable"/> (https://fs.spec.whatwg.org/#dictdef-filesystemcreatewritableoptions).</summary>
[IdlInterface("FileSystemCreateWritableOptions", "https://fs.spec.whatwg.org/#dictdef-filesystemcreatewritableoptions")]
public sealed class FileSystemCreateWritableOptions
{
    /// <summary>Gets whether the stream starts with the content of the file (it starts empty by default).</summary>
    [IdlMember("keepExistingData", "https://fs.spec.whatwg.org/#dom-filesystemcreatewritableoptions-keepexistingdata")]
    public bool KeepExistingData { get; init; }

    /// <summary>
    /// Gets the locking mode of the stream, a member that Chromium implements and that is not in the File System Standard
    /// yet: <c>siloed</c> (the default, the shared lock of the specification: several streams can write the file, and the
    /// last one that is closed wins) or <c>exclusive</c> (no other stream can be created).
    /// </summary>
    [NotInIdl("The mode member of the locking proposal that Chromium implements (whatwg/fs, proposals/MultipleReadersWriters.md): the bindings read it")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public string Mode { get; init; } = "siloed";
}

/// <summary>The options of <see cref="FileSystemFileHandle.CreateSyncAccessHandle(FileSystemCreateSyncAccessHandleOptions?)"/>, which Chromium implements and which are not in the File System Standard yet.</summary>
[Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class FileSystemCreateSyncAccessHandleOptions
{
    /// <summary>
    /// Gets the locking mode of the access handle: <c>readwrite</c> (the default, the exclusive lock of the specification),
    /// <c>read-only</c> (several access handles can read the file; they cannot write it) or <c>readwrite-unsafe</c> (several
    /// access handles can read and write the file).
    /// </summary>
    public string Mode { get; init; } = "readwrite";
}

/// <summary>A handle to a file (https://fs.spec.whatwg.org/#api-filesystemfilehandle).</summary>
[IdlInterface("FileSystemFileHandle", "https://fs.spec.whatwg.org/#filesystemfilehandle")]
public sealed class FileSystemFileHandle : FileSystemHandle
{
    internal FileSystemFileHandle(GlobalScope global, FileSystemBackend backend, string origin, string[] path)
        : base(global, backend, origin, path)
    {
    }

    /// <inheritdoc/>
    public override FileSystemHandleKind Kind => FileSystemHandleKind.File;

    /// <summary>
    /// Returns a <see cref="File"/> with the current content of the file (https://fs.spec.whatwg.org/#dom-filesystemfilehandle-getfile):
    /// a snapshot, which the later writes do not change. The task fails with a <c>NotFoundError</c> when the file does not exist.
    /// </summary>
    [IdlMember("getFile", "https://fs.spec.whatwg.org/#dom-filesystemfilehandle-getfile")]
    public Task<File> GetFile()
        => Invoke(() =>
        {
            lock (Coordinator.Gate)
            {
                if (Run(() => Backend.GetFileInfo(Origin, Path)) is not { } info)
                    throw DomException.NotFound("The file does not exist");

                // "If the file on disk changes or is removed after this method is called, the returned File object will likely
                // be no longer readable": the file is a copy, which a writable stream refuses once its entry is removed
                var backend = Backend;
                var origin = Origin;
                var path = Path;
                var file = File.Create(ReadContent(info.Size), type: "", Name, info.ModificationTime);
                file.IsSnapshotReadable = () => backend.GetEntryKind(origin, path) == FileSystemHandleKind.File;
                return file;
            }
        });

    internal byte[] ReadContent(long size)
    {
        if (size > Array.MaxLength)
            throw DomException.InvalidState("The file is too large");

        var bytes = new byte[size];
        var read = Run(() => Backend.ReadFile(Origin, Path, 0, bytes));
        return read == bytes.Length ? bytes : bytes[..read];
    }

    /// <summary>
    /// Creates a stream that writes the file (https://fs.spec.whatwg.org/#dom-filesystemfilehandle-createwritable): the
    /// writes go to a buffer, which replaces the content of the file when the stream is closed. The stream holds a lock on
    /// the file until it is closed or aborted. The task fails with a <c>NotFoundError</c> when the file does not exist, and
    /// with a <c>NoModificationAllowedError</c> when the lock cannot be taken (an access handle of the file is open).
    /// </summary>
    /// <remarks>In the scripts, the result is a <c>FileSystemWritableFileStream</c>, a <c>WritableStream</c> whose sink is the returned object.</remarks>
    [NotInIdl("The bindings define createWritable(), whose result is a WritableStream of the scripts (Streams.js)")]
    public Task<FileSystemWritableFileStream> CreateWritable(FileSystemCreateWritableOptions? options = null)
        => Invoke(() =>
        {
            var mode = (options?.Mode ?? "siloed") switch
            {
                "siloed" => FileSystemLockMode.WritableSiloed,
                "exclusive" => FileSystemLockMode.WritableExclusive,
                var value => throw new TypeErrorException($"'{value}' is not a valid value of FileSystemWritableFileStreamMode"),
            };

            lock (Coordinator.Gate)
            {
                if (Run(() => Backend.GetFileInfo(Origin, Path)) is not { } info)
                    throw DomException.NotFound("The file does not exist");

                if (Coordinator.TakeLock(Origin, Path, mode) is not { } fileLock)
                    throw DomException.NoModificationAllowed("The file is locked by an access handle or by an exclusive writable stream");

                try
                {
                    return new FileSystemWritableFileStream(this, fileLock, options is { KeepExistingData: true } ? ReadContent(info.Size) : [], options?.Mode ?? "siloed");
                }
                catch
                {
                    Coordinator.ReleaseLock(fileLock);
                    throw;
                }
            }
        });

    /// <summary>
    /// Creates an access handle that reads and writes the file in place, synchronously
    /// (https://fs.spec.whatwg.org/#dom-filesystemfilehandle-createsyncaccesshandle), in a dedicated worker. The access handle
    /// holds an exclusive lock on the file until it is closed. The task fails with a <c>NotFoundError</c> when the file does
    /// not exist, and with a <c>NoModificationAllowedError</c> when the lock cannot be taken.
    /// </summary>
    [IdlMember("createSyncAccessHandle", "https://fs.spec.whatwg.org/#dom-filesystemfilehandle-createsyncaccesshandle")]
    public Task<FileSystemSyncAccessHandle> CreateSyncAccessHandle() => CreateSyncAccessHandle(options: null);

    /// <summary>Creates an access handle with a locking mode: see <see cref="FileSystemCreateSyncAccessHandleOptions.Mode"/>.</summary>
    [NotInIdl("The options of the locking proposal that Chromium implements (whatwg/fs, proposals/MultipleReadersWriters.md): the bindings read them")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public Task<FileSystemSyncAccessHandle> CreateSyncAccessHandle(FileSystemCreateSyncAccessHandleOptions? options)
        => Invoke(() =>
        {
            var mode = (options?.Mode ?? "readwrite") switch
            {
                "readwrite" => FileSystemLockMode.AccessHandleReadWrite,
                "read-only" => FileSystemLockMode.AccessHandleReadOnly,
                "readwrite-unsafe" => FileSystemLockMode.AccessHandleReadWriteUnsafe,
                var value => throw new TypeErrorException($"'{value}' is not a valid value of FileSystemSyncAccessHandleMode"),
            };

            lock (Coordinator.Gate)
            {
                if (Run(() => Backend.GetFileInfo(Origin, Path)) is null)
                    throw DomException.NotFound("The file does not exist");

                if (Coordinator.TakeLock(Origin, Path, mode) is not { } fileLock)
                    throw DomException.NoModificationAllowed("The file is locked by a writable stream or by an access handle");

                return new FileSystemSyncAccessHandle(this, fileLock, options?.Mode ?? "readwrite");
            }
        });

    /// <summary>
    /// Renames the file (the <c>move()</c> method that the browsers implement for the files of the origin private file
    /// system; it is not in the File System Standard yet, see https://github.com/whatwg/fs/pull/10): the handle then locates
    /// the file at its new path. A file that has the new name is replaced. The task fails with a
    /// <see cref="TypeErrorException"/> for an invalid name, a <c>NotFoundError</c> when the file does not exist, and a
    /// <c>NoModificationAllowedError</c> while the file, or the file it replaces, is locked.
    /// </summary>
    [NotInIdl("The move() method of the browsers, which is not in the File System Standard yet (https://github.com/whatwg/fs/pull/10): the bindings define it")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public Task Move(string newName) => Move(destinationDirectory: null, newName);

    /// <summary>Moves the file to another directory of the file system, with its name or with <paramref name="newName"/>: see <see cref="Move(string)"/>.</summary>
    [NotInIdl("The move() method of the browsers, which is not in the File System Standard yet (https://github.com/whatwg/fs/pull/10): the bindings define it")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public Task Move(FileSystemDirectoryHandle? destinationDirectory, string? newName = null)
        => Invoke<object?>(() =>
        {
            if (Path.Length == 0)
                throw DomException.InvalidState("The handle has no path");

            var name = newName ?? Name;
            if (!IsValidFileName(name))
                throw new TypeErrorException($"'{name}' is not a valid file name");

            if (destinationDirectory is not null && !HasSameRoot(destinationDirectory))
                throw DomException.NotSupported("The file cannot be moved to another file system");

            string[] newPath = [.. destinationDirectory?.Path ?? Path[..^1], name];
            lock (Coordinator.Gate)
            {
                if (Run(() => Backend.GetEntryKind(Origin, Path)) != FileSystemHandleKind.File)
                    throw DomException.NotFound("The file does not exist");

                if (Path.AsSpan().SequenceEqual(newPath))
                    return null;

                if (Run(() => Backend.GetEntryKind(Origin, newPath[..^1])) != FileSystemHandleKind.Directory)
                    throw DomException.NotFound("The destination directory does not exist");

                if (Run(() => Backend.GetEntryKind(Origin, newPath)) == FileSystemHandleKind.Directory)
                    throw new DomException("The destination is a directory", "TypeMismatchError");

                if (Coordinator.IsLocked(Origin, Path) || Coordinator.IsLocked(Origin, newPath))
                    throw DomException.NoModificationAllowed("The file is locked by a writable stream or by an access handle");

                if (!Run(() => Backend.MoveFile(Origin, Path, newPath)))
                    throw DomException.InvalidState("The file could not be moved");

                Path = newPath;
            }

            return null;
        });
}

/// <summary>The type of a command of a <see cref="FileSystemWritableFileStream"/> (https://fs.spec.whatwg.org/#enumdef-writecommandtype).</summary>
public enum WriteCommandType
{
    /// <summary>Writes data at the position of the cursor, or at a given position.</summary>
    Write,

    /// <summary>Moves the cursor.</summary>
    Seek,

    /// <summary>Sets the size of the file.</summary>
    Truncate,
}

/// <summary>
/// The state of a stream that writes a file (https://fs.spec.whatwg.org/#api-filesystemwritablefilestream), created by
/// <see cref="FileSystemFileHandle.CreateWritable"/>: a buffer and a cursor. The buffer replaces the content of the file when
/// the stream is closed (<see cref="Close"/>); <see cref="Abort"/> discards it. In the scripts, the
/// <c>FileSystemWritableFileStream</c> interface is a <c>WritableStream</c> (Streams.js) whose underlying sink is this object.
/// </summary>
public sealed class FileSystemWritableFileStream
{
    private readonly FileSystemFileHandle _handle;
    private readonly string[] _path;
    private readonly FileSystemLock _lock;
    private byte[] _buffer;
    private long _length;
    private long _seekOffset;

    internal FileSystemWritableFileStream(FileSystemFileHandle handle, FileSystemLock fileLock, byte[] buffer, string mode)
    {
        _handle = handle;
        Mode = mode;

        // The stream writes the file entry it was created for, even when the handle is moved meanwhile
        _path = handle.Path;
        _lock = fileLock;
        _buffer = buffer;
        _length = buffer.Length;
        handle.Global.RegisterFileSystemResource(this);
    }

    /// <summary>Gets whether the stream was closed or aborted: its lock is released, and it cannot be written.</summary>
    public bool IsClosed => _lock.IsReleased;

    /// <summary>Gets the locking mode of the stream: see <see cref="FileSystemCreateWritableOptions.Mode"/>.</summary>
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public string Mode { get; }

    /// <summary>
    /// Writes data at the position of the cursor, or at <paramref name="position"/> (https://fs.spec.whatwg.org/#write-a-chunk),
    /// and moves the cursor after the data. The file grows with zeros when the position is after its end.
    /// </summary>
    /// <exception cref="QuotaExceededError">The write exceeds the quota of the storage.</exception>
    public void Write(ReadOnlySpan<byte> data, long? position = null)
    {
        ThrowIfClosed();
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        var writePosition = position ?? _seekOffset;
        var end = checked(writePosition + data.Length);
        EnsureLength(Math.Max(end, _length));
        data.CopyTo(_buffer.AsSpan((int)writePosition));
        _seekOffset = end;
    }

    /// <summary>Moves the cursor (the "seek" command).</summary>
    public void Seek(long position)
    {
        ThrowIfClosed();
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        _seekOffset = position;
    }

    /// <summary>Sets the size of the file (the "truncate" command): the file is truncated or grows with zeros, and a cursor after its end moves to its end.</summary>
    /// <exception cref="QuotaExceededError">The new size exceeds the quota of the storage.</exception>
    public void Truncate(long size)
    {
        ThrowIfClosed();
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size > _length)
        {
            EnsureLength(size);
        }
        else
        {
            _buffer.AsSpan((int)size, (int)(_length - size)).Clear();
            _length = size;
        }

        _seekOffset = Math.Min(_seekOffset, size);
    }

    /// <summary>
    /// The close algorithm of the stream: the buffer replaces the content of the file, and the lock is released. The file
    /// that was removed meanwhile is not created again (a <c>NotFoundError</c>).
    /// </summary>
    public void Close()
    {
        ThrowIfClosed();
        var coordinator = _handle.Coordinator;
        try
        {
            lock (coordinator.Gate)
            {
                var time = _handle.CurrentTime;
                var content = _buffer.AsMemory(0, (int)_length);
                if (!FileSystemHandle.Run(() => _handle.Backend.ReplaceFileContent(_handle.Origin, _path, content, time)))
                    throw DomException.NotFound("The file does not exist");
            }
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The abort algorithm of the stream: the buffer is discarded and the lock is released. Does nothing when the stream is closed.</summary>
    public void Abort() => Release();

    internal void Release()
    {
        _handle.Coordinator.ReleaseLock(_lock);
        _buffer = [];
        _length = 0;
        _handle.Global.UnregisterFileSystemResource(this);
    }

    private void ThrowIfClosed()
    {
        if (_lock.IsReleased)
            throw DomException.InvalidState("The stream is closed");
    }

    // The buffer grows by doubling: the bytes after the length are zeros
    private void EnsureLength(long length)
    {
        if (length > Array.MaxLength)
            throw new QuotaExceededError("The file is too large");

        if (length > _length)
        {
            // https://fs.spec.whatwg.org/#write-a-chunk: "If the operations modifying stream's [[buffer]] in the previous steps
            // failed due to exceeding the storage quota, reject p with a QuotaExceededError"
            StorageManager.CheckQuota(_handle.Global, _handle.Backend, _handle.Origin, length);
        }

        if (length > _buffer.Length)
        {
            var buffer = new byte[(int)Math.Min(Array.MaxLength, Math.Max(length, _buffer.Length * 2L))];
            _buffer.AsSpan(0, (int)_length).CopyTo(buffer);
            _buffer = buffer;
        }

        _length = length;
    }
}

/// <summary>The options of <see cref="FileSystemSyncAccessHandle.Read"/> and <see cref="FileSystemSyncAccessHandle.Write"/> (https://fs.spec.whatwg.org/#dictdef-filesystemreadwriteoptions).</summary>
[IdlInterface("FileSystemReadWriteOptions", "https://fs.spec.whatwg.org/#dictdef-filesystemreadwriteoptions")]
public sealed class FileSystemReadWriteOptions
{
    /// <summary>Gets the position of the first byte that is read or written (the position of the cursor by default).</summary>
    [IdlMember("at", "https://fs.spec.whatwg.org/#dom-filesystemreadwriteoptions-at")]
    public ulong? At { get; init; }
}

/// <summary>
/// An access handle (https://fs.spec.whatwg.org/#api-filesystemsyncaccesshandle), created by
/// <see cref="FileSystemFileHandle.CreateSyncAccessHandle()"/> in a dedicated worker: it reads and writes a file in place,
/// synchronously, and holds a lock on the file until it is closed.
/// </summary>
[IdlInterface("FileSystemSyncAccessHandle", "https://fs.spec.whatwg.org/#filesystemsyncaccesshandle")]
public sealed class FileSystemSyncAccessHandle
{
    private readonly FileSystemFileHandle _handle;
    private readonly string[] _path;
    private readonly FileSystemLock _lock;
    private readonly bool _readOnly;
    private long _cursor;

    internal FileSystemSyncAccessHandle(FileSystemFileHandle handle, FileSystemLock fileLock, string mode)
    {
        _handle = handle;
        _path = handle.Path;
        _lock = fileLock;
        _readOnly = mode == "read-only";
        Mode = mode;
        handle.Global.RegisterFileSystemResource(this);
    }

    private FileSystemBackend Backend => _handle.Backend;

    /// <summary>Gets the locking mode of the access handle: see <see cref="FileSystemCreateSyncAccessHandleOptions.Mode"/>.</summary>
    [NotInIdl("The mode attribute of the locking proposal that Chromium implements (whatwg/fs, proposals/MultipleReadersWriters.md): the bindings define it")]
    [Experimental(ExperimentalApis.FileSystemProposals, UrlFormat = ExperimentalApis.UrlFormat)]
    public string Mode { get; }

    /// <summary>
    /// Reads the file into <paramref name="buffer"/>, from the position of the cursor or from <see cref="FileSystemReadWriteOptions.At"/>
    /// (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-read), moves the cursor after the bytes read and returns
    /// their number.
    /// </summary>
    [NotInIdl("The .NET form of read(): the bindings convert the buffer source of the scripts")]
    public long Read(Span<byte> buffer, FileSystemReadWriteOptions? options = null)
    {
        ThrowIfClosed();
        var readStart = GetPosition(options);
        var size = (long)GetSize();
        if (readStart > size)
        {
            _cursor = size;
            return 0;
        }

        var read = 0;
        try
        {
            while (read < buffer.Length)
            {
                var count = Backend.ReadFile(_handle.Origin, _path, readStart + read, buffer[read..]);
                if (count <= 0)
                    break;

                read += count;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // "If there were partial reads and the number of bytes that were read into bytes is known, set result to the
            // number of read bytes"
        }

        _cursor = readStart + read;
        return read;
    }

    /// <summary>
    /// Writes <paramref name="buffer"/> into the file, at the position of the cursor or at <see cref="FileSystemReadWriteOptions.At"/>
    /// (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-write), moves the cursor after the bytes written and
    /// returns their number. The file grows with zeros when the position is after its end.
    /// </summary>
    /// <exception cref="QuotaExceededError">The write exceeds the quota of the storage.</exception>
    [NotInIdl("The .NET form of write(): the bindings convert the buffer source of the scripts")]
    public long Write(ReadOnlySpan<byte> buffer, FileSystemReadWriteOptions? options = null)
    {
        ThrowIfClosed();
        ThrowIfReadOnly();
        var writePosition = GetPosition(options);
        var oldSize = (long)GetSize();
        var newSize = Math.Max(oldSize, checked(writePosition + buffer.Length));
        if (newSize > oldSize)
        {
            StorageManager.CheckQuota(_handle.Global, Backend, _handle.Origin, newSize - oldSize);
        }

        try
        {
            Backend.WriteFile(_handle.Origin, _path, writePosition, buffer, _handle.CurrentTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw DomException.InvalidState("The file could not be written: " + ex.Message);
        }

        _cursor = writePosition + buffer.Length;
        return buffer.Length;
    }

    /// <summary>Sets the size of the file (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-truncate): the file is truncated or grows with zeros, and a cursor after its end moves to its end.</summary>
    /// <exception cref="QuotaExceededError">The new size exceeds the quota of the storage.</exception>
    [IdlMember("truncate", "https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-truncate")]
    public void Truncate(ulong newSize)
    {
        ThrowIfClosed();
        ThrowIfReadOnly();
        if (newSize > long.MaxValue)
            throw new TypeErrorException("The size is not supported by the file system");

        var size = (long)newSize;
        var oldSize = (long)GetSize();
        if (size > oldSize)
        {
            StorageManager.CheckQuota(_handle.Global, Backend, _handle.Origin, size - oldSize);
        }

        var time = _handle.CurrentTime;
        try
        {
            Backend.SetFileSize(_handle.Origin, _path, size, time);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw DomException.InvalidState("The size of the file could not be changed: " + ex.Message);
        }

        _cursor = Math.Min(_cursor, size);
    }

    /// <summary>Returns the size of the file, in bytes (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-getsize).</summary>
    [IdlMember("getSize", "https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-getsize")]
    public ulong GetSize()
    {
        ThrowIfClosed();
        return (ulong)(FileSystemHandle.Run(() => Backend.GetFileInfo(_handle.Origin, _path))?.Size ?? 0);
    }

    /// <summary>Transfers the modifications of the file to the storage (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-flush).</summary>
    [IdlMember("flush", "https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-flush")]
    public void Flush()
    {
        ThrowIfClosed();
        ThrowIfReadOnly();
        FileSystemHandle.Run<object?>(() =>
        {
            Backend.FlushFile(_handle.Origin, _path);
            return null;
        });
    }

    /// <summary>Closes the access handle (https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-close): its lock is released, and its other methods throw an <c>InvalidStateError</c>.</summary>
    [IdlMember("close", "https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-close")]
    public void Close() => Release();

    internal void Release()
    {
        _handle.Coordinator.ReleaseLock(_lock);
        _handle.Global.UnregisterFileSystemResource(this);
    }

    private long GetPosition(FileSystemReadWriteOptions? options)
    {
        if (options?.At is not { } at)
            return _cursor;

        // "If the underlying file system does not support reading from a file offset of readStart, throw a TypeError"
        if (at > long.MaxValue)
            throw new TypeErrorException("The position is not supported by the file system");

        return (long)at;
    }

    private void ThrowIfClosed()
    {
        if (_lock.IsReleased)
            throw DomException.InvalidState("The access handle is closed");
    }

    private void ThrowIfReadOnly()
    {
        if (_readOnly)
            throw DomException.NoModificationAllowed("The access handle is read-only");
    }
}
