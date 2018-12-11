using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    internal static class Win32DeviceControl
    {
        internal static byte[] ControlWithInput<TStructure>(ChangeJournalSafeHandle handle, Win32ControlCode code, ref TStructure structure, int bufferlen) where TStructure : struct
        {
            uint datalen;
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
                controlResult = Win32Methods.DeviceIoControl(handle, (uint)code, structurePointer, (uint)Marshal.SizeOf(structure), bufferPointer, (uint)buffer.Length, out datalen, IntPtr.Zero);
            }
            finally
            {
                structureHandle.Free();
                bufferHandle.Free();
            }

            if (!controlResult)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (datalen < bufferlen && datalen != 0)
            {
                var tempBuffer = new byte[datalen];
                Array.Copy(buffer, 0, tempBuffer, 0, datalen);
                buffer = tempBuffer;
            }

            return buffer;
        }

        internal static void ControlWithOutput<TStructure>(ChangeJournalSafeHandle handle, Win32ControlCode code, ref TStructure structure) where TStructure : struct
        {
            bool controlResult;
            GCHandle structureHandle;
            IntPtr structurePointer;

            // get our object pointer
            structureHandle = GCHandle.Alloc(structure, GCHandleType.Pinned);
            structurePointer = structureHandle.AddrOfPinnedObject();

            try
            {
                controlResult = Win32Methods.DeviceIoControl(handle, (uint)code, IntPtr.Zero, 0, structurePointer, (uint)Marshal.SizeOf(structure), out _, IntPtr.Zero);
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
}
