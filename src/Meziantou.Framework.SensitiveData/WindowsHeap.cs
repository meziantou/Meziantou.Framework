using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Meziantou.Framework;

[SupportedOSPlatform("windows")]
internal static partial class WindowsHeap
{
    private const uint CRYPTPROTECTMEMORY_BLOCK_SIZE = 16;
    private const uint CRYPTPROTECTMEMORY_SAME_PROCESS = 0x00;

    private static readonly HANDLE Heap = GetOrCreateHeap();

    public static unsafe IntPtr Allocate(nuint length)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException();

        return (IntPtr)PInvoke.HeapAlloc(Heap, (HEAP_FLAGS)0, length);
    }

    public static unsafe bool Free(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException();

        return PInvoke.HeapFree(Heap, (HEAP_FLAGS)0, (void*)handle);
    }

    public static nuint GetAlignedSize(nuint size)
    {
        if (size == 0)
            return 0;

        return (size + CRYPTPROTECTMEMORY_BLOCK_SIZE - 1) / CRYPTPROTECTMEMORY_BLOCK_SIZE * CRYPTPROTECTMEMORY_BLOCK_SIZE;
    }

    public static unsafe void ProtectMemory(IntPtr pData, nuint cbData)
    {
        if (cbData == 0)
            return;

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            throw new PlatformNotSupportedException();

        if (!PInvoke.CryptProtectMemory((void*)pData, (uint)cbData, CRYPTPROTECTMEMORY_SAME_PROCESS))
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
    }

    public static unsafe void UnprotectMemory(IntPtr pData, nuint cbData)
    {
        if (cbData == 0)
            return;

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            throw new PlatformNotSupportedException();

        if (!PInvoke.CryptUnprotectMemory((void*)pData, (uint)cbData, CRYPTPROTECTMEMORY_SAME_PROCESS))
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
    }

    private static HANDLE GetOrCreateHeap()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException();

        // In 64-bit processes where the virtual address space is larger,
        // we use our own private heap to store the shrouded data. This
        // somewhat isolates the shrouded data's storage space from that of
        // the rest of the application, providing limited mitigation of a
        // use-after-free elsewhere in the application accidentally pointing
        // to an address used by a shrouded buffer instance.

        var hHeap = (IntPtr.Size == 8) ? PInvoke.HeapCreate((HEAP_FLAGS)0, 0, 0) : PInvoke.GetProcessHeap();
        if (hHeap == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            Environment.FailFast("Couldn't get heap information.");
        }

        return hHeap;
    }
}
