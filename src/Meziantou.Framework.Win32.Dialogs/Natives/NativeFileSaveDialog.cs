using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

[ComImport]
[Guid(IIDGuid.IFileSaveDialog)]
[CoClass(typeof(FileSaveDialogRCW))]
internal interface NativeFileSaveDialog : IFileSaveDialog
{
}
