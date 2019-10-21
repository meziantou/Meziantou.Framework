#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }
}
