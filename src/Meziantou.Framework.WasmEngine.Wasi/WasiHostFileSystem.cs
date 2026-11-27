using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>A file system whose files are the files of a directory of the disk, which the module cannot leave.</summary>
/// <remarks>
/// <para>
/// WASI resolves the paths of the module before calling the file system: it follows the symbolic links itself, and
/// rejects the paths and the links that leave the directory, the absolute paths and the absolute links. This file system
/// also rejects the names that are not portable on Windows (<c>\</c>, <c>:</c>, <c>*</c>, <c>?</c>, <c>"</c>,
/// <c>&lt;</c>, <c>&gt;</c>, <c>|</c>, the control characters, the reserved names such as <c>CON</c> or <c>NUL</c>, and the
/// names that end with a dot or a space) on Windows, and checks that the full path of each file is inside the directory.
/// </para>
/// <para>
/// The checks and the operations are not atomic: a process of the host that replaces a directory of the file system with
/// a link between the check and the operation could make the module access the target of the link. Do not give the
/// module a directory that untrusted processes can change.
/// </para>
/// <para>
/// The hard links are supported on Windows (NTFS, ReFS), Linux, macOS and FreeBSD: a hard link never follows a symbolic
/// link, so that the module cannot link a file outside of the directory. The serial numbers of the files (which tell the
/// hard links of a file) and their numbers of links come from the platform on Windows, Linux and macOS.
/// </para>
/// </remarks>
public sealed class WasiHostFileSystem : WasiFileSystem
{
    private readonly string _root;
    private readonly bool _isReadOnly;

    /// <summary>Initializes a new instance of the <see cref="WasiHostFileSystem"/> class.</summary>
    /// <param name="rootDirectory">The directory of the files. It must exist.</param>
    /// <param name="isReadOnly">Whether the module can only read the files.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public WasiHostFileSystem(string rootDirectory, bool isReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The directory '" + rootDirectory + "' does not exist");

        _root = root;
        _isReadOnly = isReadOnly;
    }

    /// <summary>Gets the full path of the directory of the files.</summary>
    public string RootDirectory => _root;

    /// <inheritdoc/>
    public override bool IsReadOnly => _isReadOnly;

    private void ThrowIfReadOnly()
    {
        if (_isReadOnly)
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);
    }

    /// <summary>Returns the full path of a canonical path, after checking its names and that it is inside the root.</summary>
    private string GetFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0)
            return _root;

        foreach (var segment in path.Split('/'))
        {
            ValidateName(segment);
        }

        // A root of a volume ("/", "C:\") keeps its separator: the names follow it directly
        var fullPath = Path.GetFullPath(Path.Join(_root, path));
        var rootEndsWithSeparator = Path.EndsInDirectorySeparator(_root);
        var separator = fullPath.Length > _root.Length ? fullPath[_root.Length] : '\0';
        if (!fullPath.StartsWith(_root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || fullPath.Length == _root.Length ||
            (!rootEndsWithSeparator && separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar))
        {
            throw new WasiException(WasiErrno.NotCapable);
        }

        return fullPath;
    }

    private static void ValidateName(string name)
    {
        if (name.Length is 0 or > 255 || name is "." or "..")
            throw new WasiException(name.Length > 255 ? WasiErrno.NameTooLong : WasiErrno.InvalidArgument);

        if (name.Contains('\0', StringComparison.Ordinal))
            throw new WasiException(WasiErrno.InvalidArgument);

        if (!OperatingSystem.IsWindows())
            return;

        foreach (var c in name)
        {
            if (c < 32 || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                throw new WasiException(WasiErrno.InvalidArgument);
        }

        // Windows removes the trailing dots and spaces, and gives the reserved names (with any extension) to devices
        if (name[^1] is '.' or ' ')
            throw new WasiException(WasiErrno.InvalidArgument);

        var stem = name.AsSpan();
        var dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        stem = stem.TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (char.IsAsciiDigit(stem[3]) || stem[3] is '\u00B9' or '\u00B2' or '\u00B3')))
        {
            throw new WasiException(WasiErrno.InvalidArgument);
        }
    }

    private static FileSystemInfo? GetInfo(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if ((int)info.Attributes == -1)
            return null;

        return info.LinkTarget is null && info.Attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(fullPath) : info;
    }

    private static WasiFileType GetFileType(FileSystemInfo info)
    {
        if (info.LinkTarget is not null)
            return WasiFileType.SymbolicLink;

        return info.Attributes.HasFlag(FileAttributes.Directory) ? WasiFileType.Directory : WasiFileType.RegularFile;
    }

    // The serial number of the platform tells the hard links of a file; without it, the hash of the full path is distinct
    // for each file of a process
    private static HostFileIdentity GetIdentity(string fullPath) => HostFileSystemInterop.GetIdentity(fullPath) ?? new HostFileIdentity((ulong)(uint)StringComparer.Ordinal.GetHashCode(OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath) + 1, 1);

    /// <inheritdoc/>
    public override WasiFileStat? GetStat(string path)
    {
        var fullPath = GetFullPath(path);
        if (GetInfo(fullPath) is not { } info)
            return null;

        var type = GetFileType(info);
        var size = type switch
        {
            WasiFileType.RegularFile => (ulong)((FileInfo)info).Length,
            WasiFileType.SymbolicLink => (ulong)System.Text.Encoding.UTF8.GetByteCount(info.LinkTarget!),
            _ => 0UL,
        };

        var identity = GetIdentity(fullPath);
        return new WasiFileStat(type, size, new DateTimeOffset(info.LastAccessTimeUtc, TimeSpan.Zero), new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
        {
            Inode = identity.Inode,
            LinkCount = identity.LinkCount,
        };
    }

    /// <inheritdoc/>
    public override string ReadSymbolicLink(string path)
    {
        var fullPath = GetFullPath(path);
        var info = GetInfo(fullPath) ?? throw new WasiException(WasiErrno.NoEntry);
        if (info.LinkTarget is not { } target)
            throw new WasiException(WasiErrno.InvalidArgument);

        // An absolute target (C:\, \\server\share, /) starts with '/': WASI does not follow it
        var rooted = Path.IsPathRooted(target);
        if (OperatingSystem.IsWindows())
        {
            target = target.Replace('\\', '/');
        }

        return rooted && target is not ['/', ..] ? "/" + target : target;
    }

    /// <inheritdoc/>
    public override Stream OpenFile(string path, FileMode mode, FileAccess access)
    {
        var fullPath = GetFullPath(path);
        if (_isReadOnly && (access.HasFlag(FileAccess.Write) || mode is FileMode.Create or FileMode.CreateNew or FileMode.Truncate or FileMode.Append || (mode is FileMode.OpenOrCreate && !File.Exists(fullPath))))
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);

        if (Directory.Exists(fullPath))
            throw new WasiException(WasiErrno.IsDirectory);

        // The module can delete or rename a file while it is open, as on POSIX systems
        const FileShare Share = FileShare.ReadWrite | FileShare.Delete;
        if (!access.HasFlag(FileAccess.Write) && mode is FileMode.Create or FileMode.CreateNew or FileMode.Truncate or FileMode.Append)
        {
            // A FileStream cannot create or truncate a file that it only reads: the file is created or truncated first
            new FileStream(fullPath, mode, FileAccess.Write, Share, bufferSize: 0).Dispose();
            mode = FileMode.Open;
        }

        return new FileStream(fullPath, mode, access, Share, bufferSize: 0);
    }

    /// <inheritdoc/>
    public override IEnumerable<WasiDirectoryEntry> EnumerateDirectory(string path)
    {
        var fullPath = GetFullPath(path);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            throw new WasiException(File.Exists(fullPath) ? WasiErrno.NotDirectory : WasiErrno.NoEntry);

        var entries = new List<WasiDirectoryEntry>();
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = FileAttributes.None, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false }))
        {
            entries.Add(new WasiDirectoryEntry(entry.Name, GetFileType(entry), GetIdentity(entry.FullName).Inode));
        }

        return entries;
    }

    /// <inheritdoc/>
    public override void CreateDirectory(string path)
    {
        ThrowIfReadOnly();
        var fullPath = GetFullPath(path);
        if (GetInfo(fullPath) is not null)
            throw new WasiException(WasiErrno.Exists);

        Directory.CreateDirectory(fullPath);
    }

    /// <inheritdoc/>
    public override void DeleteDirectory(string path)
    {
        ThrowIfReadOnly();
        if (path.Length == 0)
            throw new WasiException(WasiErrno.Busy);

        var fullPath = GetFullPath(path);
        Directory.Delete(fullPath, recursive: false);
    }

    /// <inheritdoc/>
    public override void DeleteFile(string path)
    {
        ThrowIfReadOnly();
        var fullPath = GetFullPath(path);
        var info = GetInfo(fullPath) ?? throw new WasiException(WasiErrno.NoEntry);
        var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
        if (info.LinkTarget is null && isDirectory)
            throw new WasiException(WasiErrno.IsDirectory);

        // A symbolic link to a directory is a directory for Windows: deleting it does not delete its target
        if (isDirectory && OperatingSystem.IsWindows())
        {
            Directory.Delete(fullPath);
        }
        else
        {
            File.Delete(fullPath);
        }
    }

    /// <inheritdoc/>
    public override void Move(string sourcePath, string destinationPath)
    {
        ThrowIfReadOnly();
        if (sourcePath.Length == 0)
            throw new WasiException(WasiErrno.Busy);

        var source = GetFullPath(sourcePath);
        var destination = GetFullPath(destinationPath);
        var info = GetInfo(source) ?? throw new WasiException(WasiErrno.NoEntry);
        if (GetInfo(destination) is not null)
            throw new WasiException(WasiErrno.Exists);

        if (info is DirectoryInfo directory)
        {
            directory.MoveTo(destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    /// <inheritdoc/>
    public override void Replace(string sourcePath, string destinationPath)
    {
        ThrowIfReadOnly();
        var source = GetFullPath(sourcePath);
        var destination = GetFullPath(destinationPath);
        var info = GetInfo(source) ?? throw new WasiException(WasiErrno.NoEntry);
        if (info is DirectoryInfo || GetInfo(destination) is DirectoryInfo)
            throw new WasiException(WasiErrno.IsDirectory);

        // One rename of the platform (rename, MoveFileEx with MOVEFILE_REPLACE_EXISTING): the destination is never lost
        File.Move(source, destination, overwrite: true);
    }

    /// <inheritdoc/>
    public override void SetTimes(string path, DateTimeOffset? lastAccessTime, DateTimeOffset? lastWriteTime)
    {
        ThrowIfReadOnly();
        var fullPath = GetFullPath(path);
        var info = GetInfo(fullPath) ?? throw new WasiException(WasiErrno.NoEntry);
        if (lastAccessTime is { } access)
        {
            info.LastAccessTimeUtc = access.UtcDateTime;
        }

        if (lastWriteTime is { } write)
        {
            info.LastWriteTimeUtc = write.UtcDateTime;
        }
    }

    /// <inheritdoc/>
    public override void CreateSymbolicLink(string path, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfReadOnly();
        var fullPath = GetFullPath(path);
        if (GetInfo(fullPath) is not null)
            throw new WasiException(WasiErrno.Exists);

        var hostTarget = OperatingSystem.IsWindows() ? target.Replace('/', '\\') : target;

        // The links are relative: an absolute target (a drive, a share, a path from the root) designates a file outside
        // of the directory for the programs of the host that follow the link
        if (Path.IsPathRooted(hostTarget) || (OperatingSystem.IsWindows() && hostTarget.Contains(':', StringComparison.Ordinal)))
            throw new WasiException(WasiErrno.NotPermitted);

        // Windows has distinct links for the files and the directories: the link is a directory link when its target is a directory
        if (OperatingSystem.IsWindows() && Directory.Exists(Path.Join(Path.GetDirectoryName(fullPath), hostTarget)))
        {
            Directory.CreateSymbolicLink(fullPath, hostTarget);
        }
        else
        {
            File.CreateSymbolicLink(fullPath, hostTarget);
        }
    }

    /// <inheritdoc/>
    public override void CreateHardLink(string path, string existingPath)
    {
        ThrowIfReadOnly();
        var fullPath = GetFullPath(path);
        var existingFullPath = GetFullPath(existingPath);
        var existing = GetInfo(existingFullPath) ?? throw new WasiException(WasiErrno.NoEntry);
        if (existing.LinkTarget is null && existing.Attributes.HasFlag(FileAttributes.Directory))
            throw new WasiException(WasiErrno.NotPermitted);

        if (GetInfo(fullPath) is not null)
            throw new WasiException(WasiErrno.Exists);

        HostFileSystemInterop.CreateHardLink(fullPath, existingFullPath);
    }
}
