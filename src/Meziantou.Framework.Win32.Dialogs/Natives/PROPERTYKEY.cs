using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Dialogs.Natives
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        internal Guid fmtid;
        internal uint pid;
    }
}
