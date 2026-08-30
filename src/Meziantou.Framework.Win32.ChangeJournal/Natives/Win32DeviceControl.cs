using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

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
            using var handleScope = new SafeHandleValue(handle);
            fixed (void* bufferPointer = buffer)
            {
                controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
            }

            if (!controlResult)
            {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == (int)WIN32_ERROR.ERROR_MORE_DATA)
                {
                    buffer = new byte[returnedSize];
                    fixed (void* bufferPointer = buffer)
                    {
                        controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
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
        var structureLength = (uint)Marshal.SizeOf<TStructure>();
        fixed (void* pStructure = &structure)
        {
            uint returnedSize = 0;
            using var handleScope = new SafeHandleValue(handle);
            var controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, (uint)code, lpInBuffer: null, 0u, pStructure, structureLength, &returnedSize, lpOverlapped: null);
            if (!controlResult)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // A driver that does not know the whole structure fills in only the prefix it does know and reports how much it
            // wrote. Ignoring that would hand back a structure whose newer fields are zero, which is indistinguishable from a
            // driver that really did report zero for them.
            if (returnedSize < structureLength)
                throw new InvalidDataException($"The device returned {returnedSize.ToString(CultureInfo.InvariantCulture)} bytes for a {structureLength.ToString(CultureInfo.InvariantCulture)}-byte structure");
        }
    }
}
