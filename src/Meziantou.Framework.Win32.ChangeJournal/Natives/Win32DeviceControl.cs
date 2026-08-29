using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Meziantou.Framework.Win32.Natives;

internal static class Win32DeviceControl
{
    /// <summary>
    ///     Size to grow to when the caller asked for no output buffer at all but the control code turns out to want one, so that
    ///     doubling has something to work from.
    /// </summary>
    private const int MinimumOutputBufferLength = 1024;

    /// <summary>
    ///     Upper bound on the output buffer, so a driver that keeps reporting ERROR_MORE_DATA cannot grow it without limit.
    /// </summary>
    private const int MaximumOutputBufferLength = 1024 * 1024;

    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe Span<byte> ControlWithInput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure, int initialBufferLength) where TStructure : unmanaged
    {
        var structureLength = (uint)Marshal.SizeOf<TStructure>();
        byte[] buffer = initialBufferLength is 0 ? [] : new byte[initialBufferLength];

        fixed (void* structurePointer = &structure)
        {
            using var handleScope = new SafeHandleValue(handle);
            while (true)
            {
                uint returnedSize;
                bool controlResult;
                fixed (void* bufferPointer = buffer)
                {
                    controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, (uint)code, structurePointer, structureLength, bufferPointer, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
                }

                if (controlResult)
                    return buffer.AsSpan(0, (int)returnedSize);

                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode is not (int)WIN32_ERROR.ERROR_MORE_DATA || buffer.Length >= MaximumOutputBufferLength)
                    throw new Win32Exception(errorCode);

                // ERROR_MORE_DATA reports the number of bytes written, not the number required, so it is never larger than the
                // buffer that just proved too small. Grow geometrically instead of trusting returnedSize to be large enough.
                buffer = new byte[Math.Min(Math.Max(buffer.Length * 2, MinimumOutputBufferLength), MaximumOutputBufferLength)];
            }
        }
    }

    [SupportedOSPlatform("windows5.1.2600")]
    internal static unsafe void ControlWithOutput<TStructure>(SafeFileHandle handle, Win32ControlCode code, ref TStructure structure) where TStructure : unmanaged
    {
        fixed (void* pStructure = &structure)
        {
            uint returnedSize = 0;
            using var handleScope = new SafeHandleValue(handle);
            var controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, (uint)code, lpInBuffer: null, 0u, pStructure, (uint)Marshal.SizeOf<TStructure>(), &returnedSize, lpOverlapped: null);
            if (!controlResult)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
