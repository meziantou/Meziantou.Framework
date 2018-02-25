using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Dialogs.Natives
{
    [ComImport]
    [Guid(IIDGuid.IFileOpenDialog)]
    [CoClass(typeof(FileOpenDialogRCW))]
    internal interface NativeFileOpenDialog : IFileOpenDialog
    { }
}
