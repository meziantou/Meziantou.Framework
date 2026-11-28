namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>An open file descriptor of a module.</summary>
internal abstract class WasiFd(ulong rightsBase, ulong rightsInheriting)
{
    public ulong RightsBase { get; set; } = rightsBase;

    public ulong RightsInheriting { get; set; } = rightsInheriting;

    public ushort Flags { get; set; }

    public abstract WasiFileType FileType { get; }

    public bool IsAppend => (Flags & WasiRights.FdFlagAppend) != 0;

    public void Require(ulong rights)
    {
        if ((RightsBase & rights) != rights)
            throw new WasiException(WasiErrno.NotCapable);
    }

    public virtual void Close()
    {
    }
}

/// <summary>A standard stream: the input (0), the output (1) or the error (2). The streams of the host are not closed.</summary>
internal sealed class WasiStreamFd(Stream? stream, bool isInput) : WasiFd(isInput ? WasiRights.StandardInput : WasiRights.StandardOutput, 0)
{
    public Stream? Stream { get; } = stream;

    public bool IsInput { get; } = isInput;

    public override WasiFileType FileType => WasiFileType.CharacterDevice;
}

/// <summary>A regular file of a file system.</summary>
internal sealed class WasiFileFd(WasiFileSystem fileSystem, string path, Stream stream, ulong rightsBase, ulong rightsInheriting) : WasiFd(rightsBase, rightsInheriting)
{
    public WasiFileSystem FileSystem { get; } = fileSystem;

    /// <summary>The path of the file when it was opened: the module can move or delete the file while it is open.</summary>
    public string Path { get; } = path;

    public Stream Stream { get; } = stream;

    /// <summary>The serial number of the file when it was opened, or 0 when the file system does not give one.</summary>
    public ulong Inode { get; } = fileSystem.GetStat(path)?.Inode ?? 0;

    public override WasiFileType FileType => WasiFileType.RegularFile;

    /// <summary>Returns the entry of the file at its path, or null when the module moved or deleted the file: the path then designates another file, or none.</summary>
    public WasiFileStat? GetEntry() => FileSystem.GetStat(Path) is { } stat && (Inode == 0 || stat.Inode == Inode) ? stat : null;

    public override void Close() => Stream.Dispose();
}

/// <summary>A directory of a file system: a preopened directory, or a directory opened by the module.</summary>
internal sealed class WasiDirectoryFd(WasiFileSystem fileSystem, string path, string? preopenName, ulong rightsBase, ulong rightsInheriting) : WasiFd(rightsBase, rightsInheriting)
{
    public WasiFileSystem FileSystem { get; } = fileSystem;

    public string Path { get; } = path;

    /// <summary>The path of a preopened directory for the module (<c>fd_prestat_dir_name</c>), or null.</summary>
    public string? PreopenName { get; } = preopenName;

    public override WasiFileType FileType => WasiFileType.Directory;
}
