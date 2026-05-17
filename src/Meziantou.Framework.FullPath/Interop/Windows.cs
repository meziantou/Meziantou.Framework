#pragma warning disable MA0048
#pragma warning disable IDE1006 // Naming Styles
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework
{
    internal static partial class Interop
    {
        internal static partial class Errors
        {
            internal const int ERROR_SUCCESS = 0x0;
            internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
            internal const int ERROR_MORE_DATA = 0xEA;
        }

        internal static partial class Kernel32
        {
            [SupportedOSPlatform("windows5.1.2600")]
            internal static SafeFileHandle CreateFile(
                string lpFileName,
                int dwDesiredAccess,
                FileShare dwShareMode,
                FileMode dwCreationDisposition,
                int dwFlagsAndAttributes)
            {
                lpFileName = PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);

                return PInvoke.CreateFile(
                    lpFileName,
                    (uint)dwDesiredAccess,
                    (FILE_SHARE_MODE)(uint)dwShareMode,
                    lpSecurityAttributes: null,
                    (FILE_CREATION_DISPOSITION)(uint)dwCreationDisposition,
                    (FILE_FLAGS_AND_ATTRIBUTES)(uint)dwFlagsAndAttributes,
                    hTemplateFile: null);
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct FILE_TIME
            {
                internal uint dwLowDateTime;
                internal uint dwHighDateTime;

                internal FILE_TIME(long fileTime)
                {
                    dwLowDateTime = (uint)fileTime;
                    dwHighDateTime = (uint)(fileTime >> 32);
                }

                internal readonly long ToTicks() => ((long)dwHighDateTime << 32) + dwLowDateTime;
                internal readonly DateTime ToDateTimeUtc() => DateTime.FromFileTimeUtc(ToTicks());
                internal readonly DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromFileTime(ToTicks());
            }

            internal static partial class FileOperations
            {
                internal const int OPEN_EXISTING = 3;
                internal const int COPY_FILE_FAIL_IF_EXISTS = 0x00000001;

                internal const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
                internal const int FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
                internal const int FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
                internal const int FILE_FLAG_OVERLAPPED = 0x40000000;

                internal const int FILE_LIST_DIRECTORY = 0x0001;
            }

            [SupportedOSPlatform("windows5.1.2600")]
            internal static unsafe FindCloseSafeHandle FindFirstFile(string fileName, ref WIN32_FIND_DATA data)
            {
                fileName = PathInternal.EnsureExtendedPrefixIfNeeded(fileName);

                // use FindExInfoBasic since we don't care about short name and it has better perf
                fixed (WIN32_FIND_DATA* dataPtr = &data)
                {
                    return PInvoke.FindFirstFileEx(fileName, FINDEX_INFO_LEVELS.FindExInfoBasic, dataPtr, FINDEX_SEARCH_OPS.FindExSearchNameMatch, (FIND_FIRST_EX_FLAGS)0u);
                }
            }

            internal const uint FSCTL_GET_REPARSE_POINT = 0x000900a8;
            internal const uint SYMLINK_FLAG_RELATIVE = 1;

            [StructLayout(LayoutKind.Sequential)]
            internal struct REPARSE_DATA_BUFFER_SYMLINK
            {
                public uint ReparseTag;
                public ushort ReparseDataLength;
                public ushort Reserved;

                // SymbolicLinkReparseBuffer members, we only care about this part of union
                public ushort SubstituteNameOffset;
                public ushort SubstituteNameLength;
                public ushort PrintNameOffset;
                public ushort PrintNameLength;
                public uint Flags;
            }

            [SupportedOSPlatform("windows5.1.2600")]
            internal static unsafe bool DeviceIoControl(
                SafeFileHandle fileHandle,
                uint ioControlCode,
                [In] byte[]? inBuffer,
                uint cbInBuffer,
                [Out] byte[] outBuffer,
                uint cbOutBuffer,
                out uint cbBytesReturned,
                IntPtr overlapped)
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(overlapped, IntPtr.Zero);

                return PInvoke.DeviceIoControl(
                    fileHandle,
                    ioControlCode,
                    inBuffer is null ? default : inBuffer.AsSpan(0, checked((int)cbInBuffer)),
                    outBuffer.AsSpan(0, checked((int)cbOutBuffer)),
                    out cbBytesReturned,
                    lpOverlapped: null);
            }

            [SupportedOSPlatform("windows6.0.6000")]
            internal static uint GetFinalPathNameByHandle(
                SafeFileHandle hFile,
                [Out] char[] lpszFilePath,
                uint cchFilePath,
                uint dwFlags)
            {
                return PInvoke.GetFinalPathNameByHandle(hFile, lpszFilePath.AsSpan(0, checked((int)cchFilePath)), (GETFINALPATHNAMEBYHANDLE_FLAGS)dwFlags);
            }

            internal const int MAX_PATH = 260;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal unsafe struct WIN32_FIND_DATA
            {
                internal uint dwFileAttributes;
                internal FILE_TIME ftCreationTime;
                internal FILE_TIME ftLastAccessTime;
                internal FILE_TIME ftLastWriteTime;
                internal uint nFileSizeHigh;
                internal uint nFileSizeLow;
                internal uint dwReserved0;
                internal uint dwReserved1;
                private fixed char _cFileName[MAX_PATH];
                private fixed char _cAlternateFileName[14];
            }
        }
    }
}
namespace System.IO
{
    internal static partial class PathInternal
    {
        // All paths in Win32 ultimately end up becoming a path to a File object in the Windows object manager. Passed in paths get mapped through
        // DosDevice symbolic links in the object tree to actual File objects under \Devices. To illustrate, this is what happens with a typical
        // path "Foo" passed as a filename to any Win32 API:
        //
        //  1. "Foo" is recognized as a relative path and is appended to the current directory (say, "C:\" in our example)
        //  2. "C:\Foo" is prepended with the DosDevice namespace "\??\"
        //  3. CreateFile tries to create an object handle to the requested file "\??\C:\Foo"
        //  4. The Object Manager recognizes the DosDevices prefix and looks
        //      a. First in the current session DosDevices ("\Sessions\1\DosDevices\" for example, mapped network drives go here)
        //      b. If not found in the session, it looks in the Global DosDevices ("\GLOBAL??\")
        //  5. "C:" is found in DosDevices (in our case "\GLOBAL??\C:", which is a symbolic link to "\Device\HarddiskVolume6")
        //  6. The full path is now "\Device\HarddiskVolume6\Foo", "\Device\HarddiskVolume6" is a File object and parsing is handed off
        //      to the registered parsing method for Files
        //  7. The registered open method for File objects is invoked to create the file handle which is then returned
        //
        // There are multiple ways to directly specify a DosDevices path. The final format of "\??\" is one way. It can also be specified
        // as "\\.\" (the most commonly documented way) and "\\?\". If the question mark syntax is used the path will skip normalization
        // (essentially GetFullPathName()) and path length checks.

        // Windows Kernel-Mode Object Manager
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff565763.aspx
        // https://channel9.msdn.com/Shows/Going+Deep/Windows-NT-Object-Manager
        //
        // Introduction to MS-DOS Device Names
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff548088.aspx
        //
        // Local and Global MS-DOS Device Names
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff554302.aspx

        internal const string ExtendedDevicePathPrefix = @"\\?\";
        internal const string UncPathPrefix = @"\\";
        internal const string UncDevicePrefixToInsert = @"?\UNC\";
        internal const string UncExtendedPathPrefix = @"\\?\UNC\";
        internal const string DevicePathPrefix = @"\\.\";

        internal const int MaxShortPath = 260;

        // \\?\, \\.\, \??\
        internal const int DevicePrefixLength = 4;

        /// <summary>Returns true if the given character is a valid drive letter</summary>
        internal static bool IsValidDriveChar(char value)
        {
            return value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
        }

        private static bool EndsWithPeriodOrSpace(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var c = path[^1];
            return c is ' ' or '.';
        }

        /// <summary>
        /// Adds the extended path prefix (\\?\) if not already a device path, IF the path is not relative,
        /// AND the path is more than 259 characters. (> MAX_PATH + null). This will also insert the extended
        /// prefix if the path ends with a period or a space. Trailing periods and spaces are normally eaten
        /// away from paths during normalization, but if we see such a path at this point it should be
        /// normalized and has retained the final characters. (Typically from one of the *Info classes)
        /// </summary>
        [return: NotNullIfNotNull(nameof(path))]
        internal static string? EnsureExtendedPrefixIfNeeded(string? path)
        {
            if (path is not null && (path.Length >= MaxShortPath || EndsWithPeriodOrSpace(path)))
            {
                return EnsureExtendedPrefix(path);
            }
            else
            {
                return path;
            }
        }

        /// <summary>Adds the extended path prefix (\\?\) if not relative or already a device path.</summary>
        internal static string EnsureExtendedPrefix(string path)
        {
            // Putting the extended prefix on the path changes the processing of the path. It won't get normalized, which
            // means adding to relative paths will prevent them from getting the appropriate current directory inserted.

            // If it already has some variant of a device path (\??\, \\?\, \\.\, //./, etc.) we don't need to change it
            // as it is either correct or we will be changing the behavior. When/if Windows supports long paths implicitly
            // in the future we wouldn't want normalization to come back and break existing code.

            // In any case, all internal usages should be hitting normalize path (Path.GetFullPath) before they hit this
            // shimming method. (Or making a change that doesn't impact normalization, such as adding a filename to a
            // normalized base path.)
            if (IsPartiallyQualified(path) || IsDevice(path))
                return path;

            // Given \\server\share in longpath becomes \\?\UNC\server\share
            if (path.StartsWith(UncPathPrefix, StringComparison.OrdinalIgnoreCase))
                return path.Insert(2, UncDevicePrefixToInsert);

            return ExtendedDevicePathPrefix + path;
        }

        /// <summary>Returns true if the path uses any of the DOS device path syntaxes. ("\\.\", "\\?\", or "\??\")</summary>
        internal static bool IsDevice(string path)
        {
            // If the path begins with any two separators is will be recognized and normalized and prepped with
            // "\??\" for internal usage correctly. "\??\" is recognized and handled, "/??/" is not.
            return IsExtended(path)
                ||
                (
                    path.Length >= DevicePrefixLength
                    && IsDirectorySeparator(path[0])
                    && IsDirectorySeparator(path[1])
                    && (path[2] == '.' || path[2] == '?')
                    && IsDirectorySeparator(path[3])
                );
        }

        /// <summary>
        /// Returns true if the path uses the canonical form of extended syntax ("\\?\" or "\??\"). If the
        /// path matches exactly (cannot use alternate directory separators) Windows will skip normalization
        /// and path length checks.
        /// </summary>
        internal static bool IsExtended(string path)
        {
            // While paths like "//?/C:/" will work, they're treated the same as "\\.\" paths.
            // Skipping of normalization will *only* occur if back slashes ('\') are used.
            return path.Length >= DevicePrefixLength
                && path[0] == '\\'
                && (path[1] == '\\' || path[1] == '?')
                && path[2] == '?'
                && path[3] == '\\';
        }

        /// <summary>
        /// Returns true if the path specified is relative to the current drive or working directory.
        /// Returns false if the path is fixed to a specific drive or UNC path.  This method does no
        /// validation of the path (URIs will be returned as relative as a result).
        /// </summary>
        /// <remarks>
        /// Handles paths that use the alternate directory separator.  It is a frequent mistake to
        /// assume that rooted paths (Path.IsPathRooted) are not relative.  This isn't the case.
        /// "C:a" is drive relative- meaning that it will be resolved against the current directory
        /// for C: (rooted, but relative). "C:\a" is rooted and not relative (the current directory
        /// will not be used to modify the path).
        /// </remarks>
        internal static bool IsPartiallyQualified(string path)
        {
            if (path.Length < 2)
            {
                // It isn't fixed, it must be relative.  There is no way to specify a fixed
                // path with one character (or less).
                return true;
            }

            if (IsDirectorySeparator(path[0]))
            {
                // There is no valid way to specify a relative path with two initial slashes or
                // \? as ? isn't valid for drive relative paths and \??\ is equivalent to \\?\
                return !(path[1] == '?' || IsDirectorySeparator(path[1]));
            }

            // The only way to specify a fixed path that doesn't begin with two slashes
            // is the drive, colon, slash format- i.e. C:\
            return !((path.Length >= 3)
                && (path[1] == Path.VolumeSeparatorChar)
                && IsDirectorySeparator(path[2])
                // To match old behavior we'll check the drive character for validity as the path is technically
                // not qualified if you don't have a valid drive. "=:\" is the "=" file's default data stream.
                && IsValidDriveChar(path[0]));
        }

        /// <summary>True if the given character is a directory separator.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDirectorySeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
