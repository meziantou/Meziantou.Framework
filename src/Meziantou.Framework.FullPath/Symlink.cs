using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Wdk.Storage.FileSystem;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework;

internal static partial class Symlink
{
    public static bool IsSymbolicLink(string path)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            return WindowsSymlink.IsSymbolicLink(path);
        }
        else
        {
            return UnixSymlink.IsSymbolicLink(path);
        }
    }

    public static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            return WindowsSymlink.TryGetSymLinkTarget(path, out target);
        }
        else
        {
            return UnixSymlink.TryGetSymLinkTarget(path, out target);
        }
    }

    /// <summary>Resolves a relative symbolic link target against the directory that contains the link.</summary>
    private static string ResolveRelativeTarget(string linkDirectory, string linkTarget)
    {
        var combined = Path.Combine(linkDirectory, linkTarget);

        // Without a ".." segment the combined path opens the same file as the link does, whatever the directory is made
        // of, so the extra work is only needed for the targets that can walk out of it
        if (!ContainsParentDirectorySegment(linkTarget))
            return combined;

        // Normalizing "dir/../name" lexically cancels "dir" even when it is a symbolic link, which makes the result name
        // a different file than the one the link opens. Canonicalizing the directory of the combined path resolves those
        // links first, and covers a link the target itself walks through before the ".." segment.
        var directory = Path.GetDirectoryName(combined);
        if (!string.IsNullOrEmpty(directory) && CanonicalPath.TryGetCanonicalPath(directory, out var canonicalDirectory))
            return Path.Combine(canonicalDirectory, Path.GetFileName(combined));

        // A dangling or unreadable component cannot be canonicalized, so the lexical result is the best answer available
        return combined;
    }

    private static bool ContainsParentDirectorySegment(string path)
    {
        var remaining = path.AsSpan();
        while (!remaining.IsEmpty)
        {
            var separatorIndex = remaining.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segment = separatorIndex < 0 ? remaining : remaining[..separatorIndex];
            if (segment is "..")
                return true;

            if (separatorIndex < 0)
                break;

            remaining = remaining[(separatorIndex + 1)..];
        }

        return false;
    }

    private static partial class UnixSymlink
    {
        internal static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
        {
            if (TryReadLink(path, out var linkTarget))
            {
                var root = Path.GetDirectoryName(path);
                target = root is null ? linkTarget : ResolveRelativeTarget(root, linkTarget);

                return true;
            }

            target = null;
            return false;
        }

        internal static bool IsSymbolicLink(string path)
        {
            return TryReadLink(path, out _);
        }

        private static bool TryReadLink(string path, [NotNullWhen(true)] out string? target)
        {
            var bufferSize = 256;
            while (true)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    var bytesRead = Interop.ReadLink(path, buffer, (nuint)buffer.Length);
                    if (bytesRead < 0)
                    {
                        target = null;
                        return false;
                    }

                    if (bytesRead >= buffer.Length)
                    {
                        bufferSize = checked(buffer.Length * 2);
                        continue;
                    }

                    target = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);
                    return true;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        private static partial class Interop
        {
            [LibraryImport("libc", EntryPoint = "readlink", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial nint ReadLink(string path, byte[] buffer, nuint bufferSize);
        }
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static class WindowsSymlink
    {
        // CsWin32 only generates these as members of WIN32_ERROR, and that enum carries a few thousand values into the
        // assembly for the three that are needed here
        private const int ERROR_SUCCESS = 0x0;
        private const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        private const int ERROR_MORE_DATA = 0xEA;

        /// <summary>
        /// Byte offset of <c>PathBuffer</c> in <see cref="REPARSE_DATA_BUFFER"/>. The name offsets a symbolic link
        /// reparse point reports are relative to it. It is not the size of the structure: that also counts the first
        /// element of the variable length path buffer and the padding the element needs.
        /// </summary>
        private static readonly int ReparseDataHeaderLength = GetReparseDataHeaderLength();

        private static int GetReparseDataHeaderLength()
        {
            var buffer = default(REPARSE_DATA_BUFFER);
            return (int)Unsafe.ByteOffset(
                ref Unsafe.As<REPARSE_DATA_BUFFER, byte>(ref buffer),
                ref Unsafe.As<char, byte>(ref buffer.SymbolicLinkReparseBuffer.PathBuffer.e0));
        }

        public static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
        {
            target = null;
            if (IsSymbolicLink(path))
            {
                // Follow link so long as we are still finding symlinks
                target = GetSingleSymbolicLinkTarget(path);
            }

            return target is not null;
        }

        internal static unsafe bool IsSymbolicLink(string path)
        {
            var findData = default(WIN32_FIND_DATAW);

            // FindExInfoBasic skips the short name, which this code does not read and which costs time to compute
            using var handle = PInvoke.FindFirstFileEx(
                PathInternal.EnsureExtendedPrefixIfNeeded(path),
                FINDEX_INFO_LEVELS.FindExInfoBasic,
                &findData,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                dwAdditionalFlags: default);
            if (!handle.IsInvalid)
            {
                // dwReserved0 holds the reparse tag, so it must be compared for equality.
                // A bitwise test would also match other reparse points such as junctions (IO_REPARSE_TAG_MOUNT_POINT).
                return ((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.ReparsePoint) &&
                    findData.dwReserved0 == PInvoke.IO_REPARSE_TAG_SYMLINK;
            }

            return false;
        }

        // Adapted from dotnet/runtime's reparse point reader, licensed to the .NET Foundation under one or more
        // agreements. The .NET Foundation licenses this file to you under the MIT license.
        // Source: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs
        internal static unsafe string? GetSingleSymbolicLinkTarget(string path)
        {
            // https://docs.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-get-reparse-point
            using var handle = PInvoke.CreateFile(
                PathInternal.EnsureExtendedPrefixIfNeeded(path),
                dwDesiredAccess: 0,                                                   // No file access required, this avoids file in use
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE |
                    FILE_SHARE_MODE.FILE_SHARE_DELETE,                                // Share all access
                lpSecurityAttributes: null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OPEN_REPARSE_POINT |              // Open the reparse point, not its target
                    FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,             // Permit opening of directories
                hTemplateFile: null);

            // The link can be deleted, renamed, or have its access denied between the IsSymbolicLink probe and this
            // open. Report that as "no target" so the caller returns false, instead of letting DeviceIoControl fail
            // with ERROR_INVALID_HANDLE and throw a Win32Exception out of a Try method.
            if (handle.IsInvalid)
                return null;

            var bufferSize = ReparseDataHeaderLength + (int)PInvoke.MAX_PATH;

            while (true)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    var result = PInvoke.DeviceIoControl(handle, PInvoke.FSCTL_GET_REPARSE_POINT, lpInBuffer: default, buffer, out var bytesRead, lpOverlapped: null) ?
                        0 : Marshal.GetLastWin32Error();

                    if (result is not ERROR_SUCCESS and not ERROR_INSUFFICIENT_BUFFER and not ERROR_MORE_DATA)
                    {
                        throw new Win32Exception(result);
                    }

                    ReadOnlySpan<byte> validBuffer = buffer.AsSpan()[..(int)bytesRead];

                    if (validBuffer.Length < ReparseDataHeaderLength)
                    {
                        if (result == ERROR_SUCCESS)
                        {
                            // didn't read enough for header
                            throw new InvalidDataException("FSCTL_GET_REPARSE_POINT did not return sufficient data");
                        }

                        // can't read header, guess at buffer length
                        bufferSize = checked(buffer.Length + (int)PInvoke.MAX_PATH);
                        continue;
                    }

                    // The rented array is always at least as long as the fixed part of the structure, so the header can be
                    // read in place. Only the fields before PathBuffer are touched until the payload length is validated.
                    ref var header = ref Unsafe.As<byte, REPARSE_DATA_BUFFER>(ref MemoryMarshal.GetArrayDataReference(buffer)).SymbolicLinkReparseBuffer;

                    // we only care about SubstituteName.
                    // Per https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/b41f1cbf-10df-4a47-98d4-1c52a833d913 print name is only valid for displaying to the user
                    bufferSize = ReparseDataHeaderLength + header.SubstituteNameOffset + header.SubstituteNameLength;
                    // bufferSize = ReparseDataHeaderLength + Math.Max(header.SubstituteNameOffset + header.SubstituteNameLength, header.PrintNameOffset + header.PrintNameLength);

                    if (bytesRead >= bufferSize)
                    {
                        // got entire payload with valid header.
                        var target = Encoding.Unicode.GetString(validBuffer.Slice(ReparseDataHeaderLength + header.SubstituteNameOffset, header.SubstituteNameLength));
                        if ((header.Flags & Windows.Wdk.PInvoke.SYMLINK_FLAG_RELATIVE) != 0)
                        {
                            // The device prefix is taken off first, so the relative target is combined with an ordinary path
                            var isExtended = PathInternal.IsExtended(path);
                            var linkPath = isExtended ? path[PathInternal.DevicePrefixLength..] : path;
                            var rootPath = Path.GetDirectoryName(linkPath);
                            var resolved = rootPath is null
                                ? Path.GetFullPath(target)
                                : Path.GetFullPath(ResolveRelativeTarget(rootPath, target));

                            // Canonicalizing can turn a mapped drive into a UNC path, which the extended prefix cannot be
                            // put back in front of, so it is only restored for a path that still accepts it
                            target = isExtended && !PathInternal.IsDevice(resolved) && !resolved.StartsWith(PathInternal.UncPathPrefix, StringComparison.Ordinal)
                                ? string.Concat(path.AsSpan(0, PathInternal.DevicePrefixLength), resolved)
                                : resolved;
                        }

                        return target;
                    }

                    // The next iteration rents a buffer of at least bufferSize. If that does not exceed the buffer we
                    // just used, ArrayPool returns the same bucket size and the call repeats identically, so the loop
                    // would spin forever on a reparse buffer whose header declares a length it does not deliver.
                    if (bufferSize <= buffer.Length)
                    {
                        throw new InvalidDataException($"FSCTL_GET_REPARSE_POINT did not return sufficient data ({bufferSize.ToString(CultureInfo.InvariantCulture)}) when provided buffer ({buffer.Length}).");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}
