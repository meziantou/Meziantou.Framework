using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [ComImport()]
    [Guid(IIDGuid.IModalWindow)]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IModalWindow
    {
        [PreserveSig]
        int Show([In] IntPtr parent);
    }
}
