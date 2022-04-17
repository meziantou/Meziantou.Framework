using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

[ComImport]
[Guid(IIDGuid.IFileOpenDialog)]
[CoClass(typeof(FileOpenDialogRCW))]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Used as a class")]
internal interface NativeFileOpenDialog : IFileOpenDialog
{
}
