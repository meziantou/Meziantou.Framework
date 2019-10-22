﻿using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32
{
    internal static class Amsi
    {
        internal static bool AmsiResultIsMalware(AmsiResult result)
        {
            return result >= AmsiResult.AMSI_RESULT_DETECTED;
        }

        [DllImport("Amsi.dll", EntryPoint = "AmsiInitialize", CallingConvention = CallingConvention.StdCall)]
        internal static extern int AmsiInitialize([MarshalAs(UnmanagedType.LPWStr)]string appName, out AmsiContextSafeHandle amsiContext);

        [DllImport("Amsi.dll", EntryPoint = "AmsiUninitialize", CallingConvention = CallingConvention.StdCall)]
        internal static extern void AmsiUninitialize(IntPtr amsiContext);

        [DllImport("Amsi.dll", EntryPoint = "AmsiOpenSession", CallingConvention = CallingConvention.StdCall)]
        internal static extern int AmsiOpenSession(AmsiContextSafeHandle amsiContext, out AmsiSessionSafeHandle session);

        [DllImport("Amsi.dll", EntryPoint = "AmsiCloseSession", CallingConvention = CallingConvention.StdCall)]
        internal static extern void AmsiCloseSession(AmsiContextSafeHandle amsiContext, IntPtr session);

        [DllImport("Amsi.dll", EntryPoint = "AmsiScanString", CallingConvention = CallingConvention.StdCall)]
        internal static extern int AmsiScanString(AmsiContextSafeHandle amsiContext, [In, MarshalAs(UnmanagedType.LPWStr)]string payload, [In, MarshalAs(UnmanagedType.LPWStr)]string contentName, AmsiSessionSafeHandle session, out AmsiResult result);

        [DllImport("Amsi.dll", EntryPoint = "AmsiScanBuffer", CallingConvention = CallingConvention.StdCall)]
        internal static extern int AmsiScanBuffer(AmsiContextSafeHandle amsiContext, byte[] buffer, uint length, string contentName, AmsiSessionSafeHandle session, out AmsiResult result);
    }
}
