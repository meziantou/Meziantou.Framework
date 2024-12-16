using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace Meziantou.Framework.Win32.Natives;

internal static class Win32DeviceControl
{
    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe Span<byte> ControlWithInput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure, int bufferLength) where TStructure : struct
    {
        uint returnedSize;
        bool controlResult;
        GCHandle bufferHandle;
        IntPtr bufferPointer;

        var buffer = bufferLength is 0 ? Array.Empty<byte>() : new byte[bufferLength];
        bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        bufferPointer = bufferHandle.AddrOfPinnedObject();
        var structurePointer = Unsafe.AsPointer(ref structure);

        try
        {
            controlResult = PInvoke.DeviceIoControl(handle, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), (void*)bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
        }
        finally
        {
            bufferHandle.Free();
        }

        if (!controlResult)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        Debug.Assert(returnedSize <= bufferLength);
        return buffer.AsSpan(0, (int)returnedSize);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe void ControlWithOutput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure) where TStructure : struct
    {
        var structurePointer = Unsafe.AsPointer(ref structure);
        uint returnedSize = 0;
        var controlResult = PInvoke.DeviceIoControl(handle, (uint)code, lpInBuffer: null, 0u, structurePointer, (uint)Marshal.SizeOf(structure), &returnedSize, lpOverlapped: null);

        if (!controlResult)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
