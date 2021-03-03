using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework
{
    internal static class Symlink
    {
        public static bool IsSymbolicLink(string path)
        {
            if (IsWindows())
            {
                return WindowsSymlink.IsSymbolicLink(path);
            }
            else
            {
#if NETCOREAPP3_1 || NET5_0
                return UnixSymlink.IsSymbolicLink(path);
#elif NET472
                throw new PlatformNotSupportedException();
#else
#error Platform not supported
#endif
            }
        }

        public static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
        {
            if (IsWindows())
            {
                return WindowsSymlink.TryGetSymLinkTarget(path, out target);
            }
            else
            {
#if NETCOREAPP3_1 || NET5_0
                return UnixSymlink.TryGetSymLinkTarget(path, out target);
#elif NET472
                throw new PlatformNotSupportedException();
#else
#error Platform not supported
#endif
            }
        }

        private static bool IsWindows()
        {
#if NET5_0
            return OperatingSystem.IsWindows();
#elif NETCOREAPP3_1 || NETSTANDARD2_0
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#elif NET472
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
#error Platform notsupported
#endif
        }

#if NETCOREAPP3_1 || NET5_0
        private static class UnixSymlink
        {
            internal static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
            {
                var symbolicLinkInfo = new Mono.Unix.UnixSymbolicLinkInfo(path);
                if (symbolicLinkInfo.IsSymbolicLink)
                {
                    var root = Path.GetDirectoryName(path);
                    if (root == null)
                    {
                        target = symbolicLinkInfo.ContentsPath;
                    }
                    else
                    {
                        target = Path.Combine(root, symbolicLinkInfo.ContentsPath);
                    }
                    return true;
                }

                target = null;
                return false;
            }

            internal static bool IsSymbolicLink(string path)
            {
                var symbolicLinkInfo = new Mono.Unix.UnixSymbolicLinkInfo(path);
                return symbolicLinkInfo.IsSymbolicLink;
            }
        }
#endif

        private static class WindowsSymlink
        {
            public static bool TryGetSymLinkTarget(string path, [NotNullWhen(true)] out string? target)
            {
                target = null;
                if (IsSymbolicLink(path))
                {
                    // Follow link so long as we are still finding symlinks
                    target = GetSingleSymbolicLinkTarget(path);
                }

                return target != null;
            }

            internal static bool IsSymbolicLink(string path)
            {
                var findData = new Interop.Kernel32.WIN32_FIND_DATA();
                using (var handle = Interop.Kernel32.FindFirstFile(path, ref findData))
                {
                    if (!handle.IsInvalid)
                    {
                        return ((FileAttributes)findData.dwFileAttributes & FileAttributes.ReparsePoint) != 0 &&
                            (findData.dwReserved0 & 0xA000000C) != 0;  // IO_REPARSE_TAG_SYMLINK
                    }
                }

                return false;
            }

            internal static string GetSingleSymbolicLinkTarget(string path)
            {
                using var handle =
                    Interop.Kernel32.CreateFile(path,
                    0,                                                             // No file access required, this avoids file in use
                    FileShare.ReadWrite | FileShare.Delete,                        // Share all access
                    FileMode.Open,
                    Interop.Kernel32.FileOperations.FILE_FLAG_OPEN_REPARSE_POINT | // Open the reparse point, not its target
                    Interop.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS);   // Permit opening of directories
                                                                                   // https://docs.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-get-reparse-point

                var sizeHeader = Marshal.SizeOf<Interop.Kernel32.REPARSE_DATA_BUFFER_SYMLINK>();
                uint bytesRead = 0;
                ReadOnlySpan<byte> validBuffer;
                var bufferSize = sizeHeader + Interop.Kernel32.MAX_PATH;

                while (true)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    try
                    {
                        var result = Interop.Kernel32.DeviceIoControl(handle, Interop.Kernel32.FSCTL_GET_REPARSE_POINT, inBuffer: null, cbInBuffer: 0, buffer, (uint)buffer.Length, out bytesRead, overlapped: IntPtr.Zero) ?
                            0 : Marshal.GetLastWin32Error();

                        if (result != Interop.Errors.ERROR_SUCCESS && result != Interop.Errors.ERROR_INSUFFICIENT_BUFFER && result != Interop.Errors.ERROR_MORE_DATA)
                        {
                            throw new Win32Exception(result);
                        }

                        validBuffer = buffer.AsSpan().Slice(0, (int)bytesRead);

                        if (!MemoryMarshal.TryRead<Interop.Kernel32.REPARSE_DATA_BUFFER_SYMLINK>(validBuffer, out var header))
                        {
                            if (result == Interop.Errors.ERROR_SUCCESS)
                            {
                                // didn't read enough for header
                                throw new InvalidDataException("FSCTL_GET_REPARSE_POINT did not return sufficient data");
                            }

                            // can't read header, guess at buffer length
                            buffer = new byte[buffer.Length + Interop.Kernel32.MAX_PATH];
                            continue;
                        }

                        // we only care about SubstituteName.
                        // Per https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/b41f1cbf-10df-4a47-98d4-1c52a833d913 print name is only valid for displaying to the user
                        bufferSize = sizeHeader + header.SubstituteNameOffset + header.SubstituteNameLength;
                        // bufferSize = sizeHeader + Math.Max(header.SubstituteNameOffset + header.SubstituteNameLength, header.PrintNameOffset + header.PrintNameLength);

                        if (bytesRead >= bufferSize)
                        {
                            // got entire payload with valid header.
#if NETSTANDARD2_0 || NET472
                            var target = Encoding.Unicode.GetString(validBuffer.Slice(sizeHeader + header.SubstituteNameOffset, header.SubstituteNameLength).ToArray());
#elif NETCOREAPP3_1 || NET5_0
                            var target = Encoding.Unicode.GetString(validBuffer.Slice(sizeHeader + header.SubstituteNameOffset, header.SubstituteNameLength));
#else
#error Platform not supported
#endif
                            if ((header.Flags & Interop.Kernel32.SYMLINK_FLAG_RELATIVE) != 0)
                            {
                                if (PathInternal.IsExtended(path))
                                {
                                    var rootPath = Path.GetDirectoryName(path[4..]);
                                    if (rootPath != null)
                                    {
                                        target = path.Substring(0, 4) + Path.GetFullPath(Path.Combine(rootPath, target));
                                    }
                                    else
                                    {
                                        target = path.Substring(0, 4) + Path.GetFullPath(target);
                                    }
                                }
                                else
                                {
                                    var rootPath = Path.GetDirectoryName(path);
                                    if (rootPath != null)
                                    {
                                        target = Path.GetFullPath(Path.Combine(rootPath, target));
                                    }
                                    else
                                    {
                                        target = Path.GetFullPath(target);
                                    }
                                }
                            }

                            return target;
                        }

                        if (bufferSize < buffer.Length)
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
}
