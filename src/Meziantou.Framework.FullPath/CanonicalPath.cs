using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework;

internal static class CanonicalPath
{
    public static bool TryGetCanonicalPath(string path, [NotNullWhen(true)] out string? canonicalPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsCanonicalPath.TryGetCanonicalPath(path, out canonicalPath);
        }

#if NETCOREAPP3_1_OR_GREATER
        return UnixCanonicalPath.TryGetCanonicalPath(path, out canonicalPath);
#elif NET472
        throw new PlatformNotSupportedException();
#else
#error Platform not supported
#endif
    }

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

#if NETCOREAPP3_1_OR_GREATER
    private static class UnixCanonicalPath
    {
        public static bool TryGetCanonicalPath(string path, [NotNullWhen(true)] out string? canonicalPath)
        {
            var utf8Path = Encoding.UTF8.GetBytes(path + '\0');
            var pointer = Interop.RealPath(utf8Path, IntPtr.Zero);
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

        private static class Interop
        {
            [DllImport("libc", EntryPoint = "realpath", SetLastError = true, ExactSpelling = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            private static extern IntPtr RealPathCore(byte[] path, IntPtr resolvedPath);

            [DllImport("libc", EntryPoint = "free", SetLastError = false, ExactSpelling = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            private static extern void FreeCore(IntPtr pointer);

            internal static IntPtr RealPath(byte[] path, IntPtr resolvedPath)
            {
                return RealPathCore(path, resolvedPath);
            }

            internal static void Free(IntPtr pointer)
            {
                FreeCore(pointer);
            }
        }
    }
#endif
}
