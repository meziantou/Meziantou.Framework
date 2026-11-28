namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>The rights of a file descriptor (<c>rights</c>), and the flags of the functions of WASI Preview 1.</summary>
internal static class WasiRights
{
    public const ulong FdDatasync = 1UL << 0;
    public const ulong FdRead = 1UL << 1;
    public const ulong FdSeek = 1UL << 2;
    public const ulong FdFdstatSetFlags = 1UL << 3;
    public const ulong FdSync = 1UL << 4;
    public const ulong FdTell = 1UL << 5;
    public const ulong FdWrite = 1UL << 6;
    public const ulong FdAdvise = 1UL << 7;
    public const ulong FdAllocate = 1UL << 8;
    public const ulong PathCreateDirectory = 1UL << 9;
    public const ulong PathCreateFile = 1UL << 10;
    public const ulong PathLinkSource = 1UL << 11;
    public const ulong PathLinkTarget = 1UL << 12;
    public const ulong PathOpen = 1UL << 13;
    public const ulong FdReaddir = 1UL << 14;
    public const ulong PathReadlink = 1UL << 15;
    public const ulong PathRenameSource = 1UL << 16;
    public const ulong PathRenameTarget = 1UL << 17;
    public const ulong PathFilestatGet = 1UL << 18;
    public const ulong PathFilestatSetSize = 1UL << 19;
    public const ulong PathFilestatSetTimes = 1UL << 20;
    public const ulong FdFilestatGet = 1UL << 21;
    public const ulong FdFilestatSetSize = 1UL << 22;
    public const ulong FdFilestatSetTimes = 1UL << 23;
    public const ulong PathSymlink = 1UL << 24;
    public const ulong PathRemoveDirectory = 1UL << 25;
    public const ulong PathUnlinkFile = 1UL << 26;
    public const ulong PollFdReadwrite = 1UL << 27;
    public const ulong SockShutdown = 1UL << 28;
    public const ulong SockAccept = 1UL << 29;

    /// <summary>The rights of a regular file.</summary>
    public const ulong RegularFile = FdDatasync | FdRead | FdSeek | FdFdstatSetFlags | FdSync | FdTell | FdWrite | FdAdvise | FdAllocate | FdFilestatGet | FdFilestatSetSize | FdFilestatSetTimes | PollFdReadwrite;

    /// <summary>The rights of a directory.</summary>
    public const ulong Directory = FdFdstatSetFlags | FdSync | FdDatasync | PathCreateDirectory | PathCreateFile | PathLinkSource | PathLinkTarget | PathOpen | FdReaddir | PathReadlink |
        PathRenameSource | PathRenameTarget | PathFilestatGet | PathFilestatSetSize | PathFilestatSetTimes | FdFilestatGet | FdFilestatSetTimes | PathSymlink | PathRemoveDirectory | PathUnlinkFile;

    /// <summary>The rights that a directory gives to the files and the directories opened from it.</summary>
    public const ulong DirectoryInheriting = RegularFile | Directory;

    /// <summary>The rights of a standard stream: a terminal is not seekable.</summary>
    public const ulong StandardInput = FdRead | FdFdstatSetFlags | FdFilestatGet | PollFdReadwrite;

    public const ulong StandardOutput = FdWrite | FdFdstatSetFlags | FdFilestatGet | PollFdReadwrite;

    // fdflags
    public const ushort FdFlagAppend = 1;
    public const ushort FdFlagDsync = 2;
    public const ushort FdFlagNonblock = 4;
    public const ushort FdFlagRsync = 8;
    public const ushort FdFlagSync = 16;
    public const ushort FdFlagsAll = FdFlagAppend | FdFlagDsync | FdFlagNonblock | FdFlagRsync | FdFlagSync;

    // oflags
    public const ushort OpenCreate = 1;
    public const ushort OpenDirectory = 2;
    public const ushort OpenExclusive = 4;
    public const ushort OpenTruncate = 8;

    // lookupflags
    public const uint LookupSymlinkFollow = 1;

    // fstflags
    public const ushort SetAccessTime = 1;
    public const ushort SetAccessTimeNow = 2;
    public const ushort SetWriteTime = 4;
    public const ushort SetWriteTimeNow = 8;
}
