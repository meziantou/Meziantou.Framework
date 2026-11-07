namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the bucket file systems (https://fs.spec.whatwg.org/#bucket-file-system) of the browsing contexts: the
/// origin private file system of each origin (<c>navigator.storage.getDirectory()</c>), e.g. a directory, a database or
/// <see cref="InMemoryFileSystemBackend"/> (see <see cref="BrowsingContextOptions.FileSystem"/>). The backend stores a tree
/// of directories and files per origin; the library implements the API (the handles, the name validation, the locks of
/// the files, the writable streams, which replace the content of a file when they are closed, and the access handles,
/// which read and write a file in place).
/// </summary>
/// <remarks>
/// <para>
/// An entry is identified by the serialization of its origin (e.g. <c>https://example.com</c>) and by its path: the names
/// of the directories from the root directory of the origin, then its own name (an empty path is the root directory,
/// which always exists). The names are valid file names (https://fs.spec.whatwg.org/#valid-file-name): they are not empty,
/// are not <c>.</c> nor <c>..</c>, and contain neither <c>/</c> nor <c>\</c>; they are compared ordinally.
/// </para>
/// <para>
/// The methods are called on the threads of the event loops of the windows and the workers that use the file system: an
/// implementation must be thread-safe. The library guarantees that a file is not written while a writable stream replaces
/// its content, and that a file with an open access handle is not replaced, moved nor removed. The exceptions reject the
/// promises of the API (an <see cref="IOException"/> is reported as an <c>InvalidStateError</c>).
/// </para>
/// </remarks>
public abstract class FileSystemBackend
{
    internal FileSystemCoordinator Coordinator => field ??= new FileSystemCoordinator(this);

    /// <summary>Returns the kind of the entry at a path, or null when there is none.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the entry (empty for the root directory).</param>
    public abstract FileSystemHandleKind? GetEntryKind(string origin, IReadOnlyList<string> path);

    /// <summary>Returns the entries of a directory (in any order), or null when the directory does not exist.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the directory (empty for the root directory).</param>
    public abstract IReadOnlyList<FileSystemEntry>? GetEntries(string origin, IReadOnlyList<string> path);

    /// <summary>
    /// Creates an empty file or an empty directory, and returns true; returns false when its parent directory does not exist
    /// or when an entry already has its name.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the new entry.</param>
    /// <param name="kind">The kind of the entry.</param>
    /// <param name="modificationTime">The modification timestamp of a new file, in milliseconds since the Unix epoch.</param>
    public abstract bool CreateEntry(string origin, IReadOnlyList<string> path, FileSystemHandleKind kind, long modificationTime);

    /// <summary>
    /// Removes a file, or a directory with all its entries, and returns true; returns false when the entry does not exist.
    /// The root directory is emptied, not removed.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the entry.</param>
    public abstract bool RemoveEntry(string origin, IReadOnlyList<string> path);

    /// <summary>
    /// Moves a file to another path (another name, another directory, or both), replacing the file that has the new path
    /// when there is one, and returns true; returns false when the file or the new parent directory does not exist, or when
    /// the new path is the one of a directory.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="newPath">The new path of the file.</param>
    public abstract bool MoveFile(string origin, IReadOnlyList<string> path, IReadOnlyList<string> newPath);

    /// <summary>Returns the size and the modification timestamp of a file, or null when the file does not exist.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    public abstract FileSystemFileInfo? GetFileInfo(string origin, IReadOnlyList<string> path);

    /// <summary>
    /// Reads bytes of a file from a position, and returns the number of bytes read (less than the length of the buffer at the
    /// end of the file; 0 when the file does not exist).
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="position">The position of the first byte to read.</param>
    /// <param name="buffer">The buffer to fill.</param>
    public abstract int ReadFile(string origin, IReadOnlyList<string> path, long position, Span<byte> buffer);

    /// <summary>
    /// Writes bytes into a file at a position (the access handles write in place): the file grows when the bytes end after
    /// its end, and the bytes between its end and a greater position are zeros.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="position">The position of the first byte to write.</param>
    /// <param name="data">The bytes to write.</param>
    /// <param name="modificationTime">The new modification timestamp of the file, in milliseconds since the Unix epoch.</param>
    public abstract void WriteFile(string origin, IReadOnlyList<string> path, long position, ReadOnlySpan<byte> data, long modificationTime);

    /// <summary>Sets the size of a file: the file is truncated, or grows with zeros.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="size">The new size.</param>
    /// <param name="modificationTime">The new modification timestamp of the file, in milliseconds since the Unix epoch.</param>
    public abstract void SetFileSize(string origin, IReadOnlyList<string> path, long size, long modificationTime);

    /// <summary>
    /// Replaces the content of a file atomically (a writable stream is closed), and returns true; returns false when the file
    /// does not exist.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The new content.</param>
    /// <param name="modificationTime">The new modification timestamp of the file, in milliseconds since the Unix epoch.</param>
    public abstract bool ReplaceFileContent(string origin, IReadOnlyList<string> path, ReadOnlyMemory<byte> content, long modificationTime);

    /// <summary>Transfers the cached modifications of a file to the storage device (<c>FileSystemSyncAccessHandle.flush()</c>). The default does nothing.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="path">The path of the file.</param>
    public virtual void FlushFile(string origin, IReadOnlyList<string> path)
    {
    }

    /// <summary>
    /// Returns an estimation of the number of bytes used by the file system of an origin (<c>navigator.storage.estimate()</c>).
    /// The default is 0: the usage is not reported.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    public virtual long GetUsage(string origin) => 0;
}

/// <summary>An entry of a directory of a <see cref="FileSystemBackend"/>.</summary>
/// <param name="Name">The name of the entry.</param>
/// <param name="Kind">The kind of the entry.</param>
public readonly record struct FileSystemEntry(string Name, FileSystemHandleKind Kind);

/// <summary>The state of a file of a <see cref="FileSystemBackend"/>.</summary>
/// <param name="Size">The size of the file, in bytes.</param>
/// <param name="ModificationTime">The modification timestamp of the file, in milliseconds since the Unix epoch.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FileSystemFileInfo(long Size, long ModificationTime);
