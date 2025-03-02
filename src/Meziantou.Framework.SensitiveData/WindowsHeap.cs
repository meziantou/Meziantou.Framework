using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework;

[SupportedOSPlatform("windows")]
internal static partial class WindowsHeap
{
    private static readonly IntPtr Heap = GetOrCreateHeap();

    public static IntPtr Allocate(nuint length) => Interop.HeapAlloc(Heap, 0, length);
    public static bool Free(IntPtr handle) => Interop.HeapFree(Heap, 0, handle);

    private static IntPtr GetOrCreateHeap()
    {
        // In 64-bit processes where the virtual address space is larger,
        // we use our own private heap to store the shrouded data. This
        // somewhat isolates the shrouded data's storage space from that of
        // the rest of the application, providing limited mitigation of a
        // use-after-free elsewhere in the application accidentally pointing
        // to an address used by a shrouded buffer instance.

        IntPtr hHeap = (IntPtr.Size == 8) ? Interop.HeapCreate(0, 0, 0) : Interop.GetProcessHeap();
        if (hHeap == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            Environment.FailFast("Couldn't get heap information.");
        }

        return hHeap;
    }

    private static partial class Interop
    {
        private const string KERNEL32_LIB = "kernel32.dll";

        [LibraryImport(KERNEL32_LIB, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr GetProcessHeap();

        [LibraryImport(KERNEL32_LIB, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr HeapCreate(uint flOptions, nuint dwInitialSize, nuint dwMaximumSize);

        [LibraryImport(KERNEL32_LIB, SetLastError = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr HeapAlloc(IntPtr hHeap, uint dwFlags, nuint dwBytes);

        [LibraryImport(KERNEL32_LIB, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool HeapFree(IntPtr hHeap, uint dwFlags, IntPtr lpMem);
    }
}
