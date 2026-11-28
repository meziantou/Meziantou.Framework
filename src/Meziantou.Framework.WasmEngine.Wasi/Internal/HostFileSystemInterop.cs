using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>The identity of a file of the disk: its serial number and its number of hard links.</summary>
/// <param name="Inode">The serial number of the file, distinct for each file of the volumes of the process.</param>
/// <param name="LinkCount">The number of hard links to the file.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HostFileIdentity(ulong Inode, ulong LinkCount);

/// <summary>
/// The operations of the file system that .NET does not provide: the identity of a file (to tell the hard links of a
/// file), and the creation of a hard link.
/// </summary>
internal static class HostFileSystemInterop
{
    private const ulong Mix = 0x9E3779B97F4A7C15;

    /// <summary>Returns the identity of a file, of a directory or of a symbolic link (not of its target), or null when it is not available.</summary>
    /// <param name="fullPath">The full path of the file.</param>
    public static HostFileIdentity? GetIdentity(string fullPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsIdentity(fullPath);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
                return GetLinuxIdentity(fullPath);

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst())
                return GetDarwinIdentity(fullPath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The C library does not provide the function (an older version): the caller uses a hash of the path
        }

        return null;
    }

    /// <summary>Creates a hard link to a file, or to a symbolic link (not to its target).</summary>
    /// <param name="fullPath">The full path of the link, which does not exist.</param>
    /// <param name="existingFullPath">The full path of the file.</param>
    /// <exception cref="WasiException">The link cannot be created.</exception>
    /// <remarks>
    /// The link never follows a symbolic link: a module that can create a symbolic link to a file outside of its directory
    /// must not be able to create a hard link to the target. <c>link</c> follows the links on macOS, <c>linkat</c> without
    /// flags never does, and <c>CreateHardLinkW</c> links the symbolic link itself.
    /// </remarks>
    public static void CreateHardLink(string fullPath, string existingFullPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                CreateWindowsHardLink(fullPath, existingFullPath);
                return;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD())
            {
                CreateUnixHardLink(fullPath, existingFullPath);
                return;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new WasiException(WasiErrno.NotSupported);
        }

        throw new WasiException(WasiErrno.NotSupported);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Windows
    // ------------------------------------------------------------------------------------------------------------

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7; // READ | WRITE | DELETE
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000; // opens the directories
    private const uint FileFlagOpenReparsePoint = 0x00200000; // opens the symbolic link, not its target

    private static HostFileIdentity? GetWindowsIdentity(string fullPath)
    {
        nint handle;
        unsafe
        {
            fixed (char* path = fullPath)
            {
                handle = WindowsInterop.CreateFile(path, FileReadAttributes, FileShareAll, 0, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, 0);
            }
        }

        if (handle == -1)
            return null;

        using var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(handle, ownsHandle: true);

        // BY_HANDLE_FILE_INFORMATION: the volume serial number at 28, the number of links at 40, the file index at 44 (high) and 48 (low)
        Span<byte> information = stackalloc byte[52];
        int result;
        unsafe
        {
            fixed (byte* pointer = information)
            {
                result = WindowsInterop.GetFileInformationByHandle(safeHandle.DangerousGetHandle(), pointer);
            }
        }

        if (result == 0)
            return null;

        var volume = BinaryPrimitives.ReadUInt32LittleEndian(information[28..]);
        var links = BinaryPrimitives.ReadUInt32LittleEndian(information[40..]);
        var index = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(information[44..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(information[48..]);
        return CreateIdentity(index, volume, links);
    }

    private static void CreateWindowsHardLink(string fullPath, string existingFullPath)
    {
        int result;
        unsafe
        {
            fixed (char* path = fullPath)
            fixed (char* existing = existingFullPath)
            {
                result = WindowsInterop.CreateHardLink(path, existing, 0);
            }
        }

        if (result == 0)
            throw new WasiException(WasiErrors.FromWin32Error(Marshal.GetLastPInvokeError()));
    }

    [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "The code generated by the LibraryImport generator of .NET 10 does not compile under the updated memory safety rules")]
    private static class WindowsInterop
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe extern nint CreateFile(char* fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe extern int GetFileInformationByHandle(nint file, byte* information);

        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe extern int CreateHardLink(char* fileName, char* existingFileName, nint securityAttributes);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Unix
    // ------------------------------------------------------------------------------------------------------------

    private const int LinuxAtFdCwd = -100;
    private const int DarwinAtFdCwd = -2;
    private const int LinuxAtSymlinkNoFollow = 0x100;
    private const uint StatxNlink = 0x4;
    private const uint StatxIno = 0x100;
    private const uint StatxBasicStats = 0x7FF;

    private static byte[] ToNullTerminatedUtf8(string path)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(path) + 1];
        Encoding.UTF8.GetBytes(path, bytes);
        return bytes;
    }

    private static HostFileIdentity? GetLinuxIdentity(string fullPath)
    {
        // statx has the same layout on every architecture (lstat does not): stx_mask at 0, stx_nlink at 16, stx_ino at
        // 32, stx_dev_major at 136 and stx_dev_minor at 140
        var path = ToNullTerminatedUtf8(fullPath);
        Span<byte> buffer = stackalloc byte[256];
        int result;
        unsafe
        {
            fixed (byte* pathPointer = path)
            fixed (byte* bufferPointer = buffer)
            {
                result = UnixInterop.Statx(LinuxAtFdCwd, pathPointer, LinuxAtSymlinkNoFollow, StatxBasicStats, bufferPointer);
            }
        }

        var mask = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (result != 0 || (mask & (StatxIno | StatxNlink)) != (StatxIno | StatxNlink))
            return null;

        var links = BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]);
        var inode = BinaryPrimitives.ReadUInt64LittleEndian(buffer[32..]);
        var device = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(buffer[136..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(buffer[140..]);
        return CreateIdentity(inode, device, links);
    }

    private static HostFileIdentity? GetDarwinIdentity(string fullPath)
    {
        // struct stat with 64-bit inodes: st_dev (int32) at 0, st_nlink (uint16) at 6, st_ino (uint64) at 8; 144 bytes
        var path = ToNullTerminatedUtf8(fullPath);
        Span<byte> buffer = stackalloc byte[256];
        int result;
        unsafe
        {
            fixed (byte* pathPointer = path)
            fixed (byte* bufferPointer = buffer)
            {
                // The x64 C library keeps lstat for the 32-bit inodes: the 64-bit version is lstat$INODE64
                result = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? UnixInterop.DarwinLStatInode64(pathPointer, bufferPointer) : UnixInterop.DarwinLStat(pathPointer, bufferPointer);
            }
        }

        if (result != 0)
            return null;

        var device = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var links = BinaryPrimitives.ReadUInt16LittleEndian(buffer[6..]);
        var inode = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]);
        return CreateIdentity(inode, device, links);
    }

    private static void CreateUnixHardLink(string fullPath, string existingFullPath)
    {
        var path = ToNullTerminatedUtf8(fullPath);
        var existing = ToNullTerminatedUtf8(existingFullPath);
        var currentDirectory = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || OperatingSystem.IsFreeBSD() ? LinuxAtFdCwd : DarwinAtFdCwd;
        int result;
        unsafe
        {
            fixed (byte* pathPointer = path)
            fixed (byte* existingPointer = existing)
            {
                result = UnixInterop.LinkAt(currentDirectory, existingPointer, currentDirectory, pathPointer, flags: 0);
            }
        }

        if (result != 0)
        {
            throw new WasiException(WasiErrors.FromErrno(Marshal.GetLastPInvokeError()));
        }
    }

    [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "The code generated by the LibraryImport generator of .NET 10 does not compile under the updated memory safety rules")]
    private static class UnixInterop
    {
        [DllImport("libc", EntryPoint = "statx", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static unsafe extern int Statx(int directory, byte* path, int flags, uint mask, byte* buffer);

        [DllImport("libc", EntryPoint = "lstat", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static unsafe extern int DarwinLStat(byte* path, byte* buffer);

        [DllImport("libc", EntryPoint = "lstat$INODE64", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static unsafe extern int DarwinLStatInode64(byte* path, byte* buffer);

        [DllImport("libc", EntryPoint = "linkat", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static unsafe extern int LinkAt(int existingDirectory, byte* existingPath, int directory, byte* path, int flags);
    }

    /// <summary>Combines the serial number of a file with its volume: the files of distinct volumes have distinct serial numbers.</summary>
    private static HostFileIdentity CreateIdentity(ulong index, ulong volume, ulong links)
    {
        var inode = index ^ (volume * Mix);
        return new HostFileIdentity(inode == 0 ? 1 : inode, links);
    }
}
