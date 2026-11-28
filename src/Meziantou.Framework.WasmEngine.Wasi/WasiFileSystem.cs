namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// A file system that a WASI module reads and writes through a preopened directory (see
/// <see cref="WasiPreopenedDirectory"/>): <see cref="WasiHostFileSystem"/> for a directory of the disk, or
/// <see cref="WasiInMemoryFileSystem"/> for files in memory.
/// </summary>
/// <remarks>
/// <para>
/// The paths are relative to the root of the file system, separated by <c>/</c>, without empty, <c>.</c> or <c>..</c>
/// segments; the root is the empty string. WASI resolves the paths of the module before calling the file system: it
/// follows the symbolic links (<see cref="ReadSymbolicLink"/>) and rejects the paths that leave the directory they are
/// relative to, so that the module cannot reach the files outside of the file system.
/// </para>
/// <para>
/// WASI checks the preconditions of the operations before calling the methods (the type of the file, an existing
/// destination, a directory that is not empty, ...). The methods throw a <see cref="WasiException"/> to report an error
/// with a precise code; the other exceptions of the file system (<see cref="IOException"/>,
/// <see cref="UnauthorizedAccessException"/>, ...) are converted to the closest code.
/// </para>
/// </remarks>
public abstract class WasiFileSystem
{
    private static long s_lastDevice;

    /// <summary>Initializes a new instance of the <see cref="WasiFileSystem"/> class.</summary>
    protected WasiFileSystem()
    {
        Device = (ulong)Interlocked.Increment(ref s_lastDevice);
    }

    /// <summary>The device of the files of the file system, distinct for each file system.</summary>
    internal ulong Device { get; }

    /// <summary>Gets whether the module can only read the file system: the operations that change it return <see cref="WasiErrno.ReadOnlyFileSystem"/>.</summary>
    public virtual bool IsReadOnly => false;

    /// <summary>Returns the status of a file, of a directory or of a symbolic link (not of its target), or null when it does not exist.</summary>
    /// <param name="path">The path of the file.</param>
    public abstract WasiFileStat? GetStat(string path);

    /// <summary>Returns the target of a symbolic link, with <c>/</c> separators. A target that starts with <c>/</c> is absolute: WASI does not follow it.</summary>
    /// <param name="path">The path of the symbolic link.</param>
    public abstract string ReadSymbolicLink(string path);

    /// <summary>Opens a regular file. The stream of a regular file must be seekable.</summary>
    /// <param name="path">The path of the file. Its parent is an existing directory.</param>
    /// <param name="mode">How the file is opened, or created.</param>
    /// <param name="access">The access to the file.</param>
    public abstract Stream OpenFile(string path, FileMode mode, FileAccess access);

    /// <summary>Returns the entries of a directory, without <c>.</c> and <c>..</c>.</summary>
    /// <param name="path">The path of an existing directory.</param>
    public abstract IEnumerable<WasiDirectoryEntry> EnumerateDirectory(string path);

    /// <summary>Creates a directory.</summary>
    /// <param name="path">The path of the directory, which does not exist. Its parent is an existing directory.</param>
    public abstract void CreateDirectory(string path);

    /// <summary>Deletes a directory.</summary>
    /// <param name="path">The path of an existing empty directory, which is not the root.</param>
    public abstract void DeleteDirectory(string path);

    /// <summary>Deletes a file or a symbolic link (not its target).</summary>
    /// <param name="path">The path of an existing file or symbolic link.</param>
    public abstract void DeleteFile(string path);

    /// <summary>Moves a file, a directory or a symbolic link.</summary>
    /// <param name="sourcePath">The path of the existing file, which is not the root.</param>
    /// <param name="destinationPath">The new path of the file, which does not exist. Its parent is an existing directory, which is not inside <paramref name="sourcePath"/>.</param>
    public abstract void Move(string sourcePath, string destinationPath);

    /// <summary>
    /// Moves a file or a symbolic link over an existing file or symbolic link, which it replaces (a rename of POSIX). The
    /// default implementation deletes the destination, then moves the source: override it to replace the destination in
    /// one operation, so that a move that fails does not lose the destination.
    /// </summary>
    /// <param name="sourcePath">The path of the existing file or symbolic link, which is not the root.</param>
    /// <param name="destinationPath">The path of the existing file or symbolic link to replace.</param>
    public virtual void Replace(string sourcePath, string destinationPath)
    {
        DeleteFile(destinationPath);
        Move(sourcePath, destinationPath);
    }

    /// <summary>Changes the times of a file or of a directory.</summary>
    /// <param name="path">The path of an existing file or directory.</param>
    /// <param name="lastAccessTime">The new time of the last access, or null to keep it.</param>
    /// <param name="lastWriteTime">The new time of the last write, or null to keep it.</param>
    public abstract void SetTimes(string path, DateTimeOffset? lastAccessTime, DateTimeOffset? lastWriteTime);

    /// <summary>Creates a symbolic link. The default implementation throws a <see cref="WasiException"/> whose code is <see cref="WasiErrno.NotSupported"/>.</summary>
    /// <param name="path">The path of the link, which does not exist. Its parent is an existing directory.</param>
    /// <param name="target">The target of the link, relative to the directory of the link, with <c>/</c> separators.</param>
    public virtual void CreateSymbolicLink(string path, string target) => throw new WasiException(WasiErrno.NotSupported);

    /// <summary>Creates a hard link to a file. The default implementation throws a <see cref="WasiException"/> whose code is <see cref="WasiErrno.NotSupported"/>.</summary>
    /// <param name="path">The path of the link, which does not exist. Its parent is an existing directory.</param>
    /// <param name="existingPath">The path of an existing regular file.</param>
    public virtual void CreateHardLink(string path, string existingPath) => throw new WasiException(WasiErrno.NotSupported);
}

/// <summary>The type of a file (<c>filetype</c>).</summary>
#pragma warning disable CA1028 // The values are the 8-bit codes of WASI
public enum WasiFileType : byte
#pragma warning restore CA1028
{
    /// <summary>The type of the file is unknown or is not one of the others.</summary>
    Unknown = 0,

    /// <summary>A block device.</summary>
    BlockDevice = 1,

    /// <summary>A character device, such as a terminal or a standard stream.</summary>
    CharacterDevice = 2,

    /// <summary>A directory.</summary>
    Directory = 3,

    /// <summary>A regular file.</summary>
    RegularFile = 4,

    /// <summary>A datagram socket.</summary>
    SocketDatagram = 5,

    /// <summary>A byte-stream socket.</summary>
    SocketStream = 6,

    /// <summary>A symbolic link.</summary>
    SymbolicLink = 7,
}

/// <summary>The status of a file (<c>filestat</c>), returned by <see cref="WasiFileSystem.GetStat"/>.</summary>
/// <param name="Type">The type of the file.</param>
/// <param name="Size">The size of a regular file in bytes, or the length of the target of a symbolic link.</param>
/// <param name="LastAccessTime">The time of the last access.</param>
/// <param name="LastWriteTime">The time of the last write.</param>
/// <param name="ChangeTime">The time of the last change of the status.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct WasiFileStat(WasiFileType Type, ulong Size, DateTimeOffset LastAccessTime, DateTimeOffset LastWriteTime, DateTimeOffset ChangeTime)
{
    /// <summary>Gets the serial number of the file, distinct for each file of the file system (0 when unknown).</summary>
    public ulong Inode { get; init; }

    /// <summary>Gets the number of hard links to the file.</summary>
    public ulong LinkCount { get; init; } = 1;
}

/// <summary>An entry of a directory, returned by <see cref="WasiFileSystem.EnumerateDirectory"/>.</summary>
/// <param name="Name">The name of the entry.</param>
/// <param name="Type">The type of the entry (not of the target of a symbolic link).</param>
/// <param name="Inode">The serial number of the entry (0 when unknown).</param>
public readonly record struct WasiDirectoryEntry(string Name, WasiFileType Type, ulong Inode);
