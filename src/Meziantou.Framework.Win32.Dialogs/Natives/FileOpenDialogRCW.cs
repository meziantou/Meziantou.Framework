#nullable disable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [ComImport]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid(CLSIDGuid.FileOpenDialog)]
    [SuppressMessage("Design", "MA0053:Make class sealed", Justification = "This class cannot be sealed (ComImport)")]
    internal class FileOpenDialogRCW
    {
    }
}
