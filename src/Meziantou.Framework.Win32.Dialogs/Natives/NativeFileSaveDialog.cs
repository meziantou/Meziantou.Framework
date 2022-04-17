using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

[ComImport]
[Guid(IIDGuid.IFileSaveDialog)]
[CoClass(typeof(FileSaveDialogRCW))]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Used as a class")]
internal interface NativeFileSaveDialog : IFileSaveDialog
{
}
