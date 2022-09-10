using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace Meziantou.Framework.Win32.Natives;

internal static class Win32DeviceControl
{
    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe byte[] ControlWithInput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure, int bufferlen) where TStructure : struct
    {
        uint returnedSize;
        bool controlResult;
        GCHandle structureHandle;
        GCHandle bufferHandle;
        IntPtr structurePointer;
        IntPtr bufferPointer;

        var buffer = new byte[bufferlen];
        bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        structureHandle = GCHandle.Alloc(structure, GCHandleType.Pinned);
        bufferPointer = bufferHandle.AddrOfPinnedObject();
        structurePointer = structureHandle.AddrOfPinnedObject();

        try
        {
            controlResult = PInvoke.DeviceIoControl(handle, (uint)code, (void*)structurePointer, (uint)Marshal.SizeOf(structure), (void*)bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
        }
        finally
        {
            structureHandle.Free();
            bufferHandle.Free();
        }

        if (!controlResult)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (returnedSize < bufferlen && returnedSize != 0)
        {
            var tempBuffer = new byte[returnedSize];
            Array.Copy(buffer, 0, tempBuffer, 0, returnedSize);
            buffer = tempBuffer;
        }

        return buffer;
    }

    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe void ControlWithOutput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure) where TStructure : struct
    {
        bool controlResult;
        GCHandle structureHandle;
        nint structurePointer;

        // get our object pointer
        structureHandle = GCHandle.Alloc(structure, GCHandleType.Pinned);
        structurePointer = structureHandle.AddrOfPinnedObject();

        try
        {
            uint returnedSize = 0;
            controlResult = PInvoke.DeviceIoControl(handle, (uint)code, lpInBuffer: null, 0u, (void*)structurePointer, (uint)Marshal.SizeOf(structure), &returnedSize, lpOverlapped: null);
        }
        finally
        {
            // always release GH handle
            structureHandle.Free();
        }

        if (!controlResult)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        structure = Marshal.PtrToStructure<TStructure>(structurePointer);
    }
}
