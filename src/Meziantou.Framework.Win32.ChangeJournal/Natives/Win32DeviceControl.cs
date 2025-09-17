using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace Meziantou.Framework.Win32.Natives;

internal static class Win32DeviceControl
{
    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe Span<byte> ControlWithInput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure, int initialBufferLength) where TStructure : unmanaged
    {
        uint returnedSize;
        bool controlResult;

        var buffer = initialBufferLength is 0 ? Array.Empty<byte>() : new byte[initialBufferLength];
        fixed (void* structurePointer = &structure)
        {
            fixed (void* bufferPointer = buffer)
            {
                controlResult = PInvoke.DeviceIoControl(handle, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
            }

            if (!controlResult)
            {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == (int)Windows.Win32.Foundation.WIN32_ERROR.ERROR_MORE_DATA)
                {
                    buffer = new byte[returnedSize];
                    fixed (void* bufferPointer = buffer)
                    {
                        controlResult = PInvoke.DeviceIoControl(handle, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
                    }

                    if (!controlResult)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else
                {
                    throw new Win32Exception(errorCode);
                }
            }
        }

        return buffer.AsSpan(0, (int)returnedSize);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe void ControlWithOutput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure) where TStructure : unmanaged
    {
        fixed (void* pStructure = &structure)
        {
            uint returnedSize = 0;
            var controlResult = PInvoke.DeviceIoControl(handle, (uint)code, lpInBuffer: null, 0u, pStructure, (uint)Marshal.SizeOf<TStructure>(), &returnedSize, lpOverlapped: null);
            if (!controlResult)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
