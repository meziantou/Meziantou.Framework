using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework;

internal static partial class CanonicalPath
{
    public static bool TryGetCanonicalPath(string path, [NotNullWhen(true)] out string? canonicalPath)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
        {
            return WindowsCanonicalPath.TryGetCanonicalPath(path, out canonicalPath);
        }

        return UnixCanonicalPath.TryGetCanonicalPath(path, out canonicalPath);
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private static class WindowsCanonicalPath
    {
        public static bool TryGetCanonicalPath(string path, [NotNullWhen(true)] out string? canonicalPath)
        {
            using var handle = Interop.Kernel32.CreateFile(
                path,
                dwDesiredAccess: 0,
                dwShareMode: FileShare.ReadWrite | FileShare.Delete,
                dwCreationDisposition: FileMode.Open,
                dwFlagsAndAttributes: Interop.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS);
            if (handle.IsInvalid)
            {
                canonicalPath = null;
                return false;
            }

            var bufferSize = Interop.Kernel32.MAX_PATH;
            while (true)
            {
                var buffer = new char[bufferSize];
                var result = Interop.Kernel32.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, dwFlags: 0);
                if (result == 0)
                {
                    canonicalPath = null;
                    return false;
                }

                if (result >= buffer.Length)
                {
                    bufferSize = checked((int)result + 1);
                    continue;
                }

                canonicalPath = NormalizePath(new string(buffer, 0, (int)result));
                return true;
            }
        }

        private static string NormalizePath(string path)
        {
            if (path.StartsWith(PathInternal.UncExtendedPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(PathInternal.UncPathPrefix, path.AsSpan(PathInternal.UncExtendedPathPrefix.Length));
            }

            if (PathInternal.IsExtended(path))
            {
                return path[PathInternal.DevicePrefixLength..];
            }

            return path;
        }
    }

    private static partial class UnixCanonicalPath
    {
        public static bool TryGetCanonicalPath(string path, [NotNullWhen(true)] out string? canonicalPath)
        {
            var pointer = Interop.RealPath(path, IntPtr.Zero);
            if (pointer == IntPtr.Zero)
            {
                canonicalPath = null;
                return false;
            }

            try
            {
                canonicalPath = Marshal.PtrToStringUTF8(pointer);
                return canonicalPath is not null;
            }
            finally
            {
                Interop.Free(pointer);
            }
        }

        private static partial class Interop
        {
            [LibraryImport("libc", EntryPoint = "realpath", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial IntPtr RealPath(string path, IntPtr resolvedPath);

            [LibraryImport("libc", EntryPoint = "free", SetLastError = false)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static partial void Free(IntPtr pointer);
        }
    }
}
