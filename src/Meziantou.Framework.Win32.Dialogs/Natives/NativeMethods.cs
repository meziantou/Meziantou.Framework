using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

internal static class NativeMethods
{
#pragma warning disable IDE1006 // Naming Styles
    internal const int S_OK = 0x00000000;
    internal const int ERROR_CANCELLED = unchecked((int)0x800704C7);
    internal const int FILE_NOT_FOUND = unchecked((int)0x80070002);
#pragma warning restore IDE1006 // Naming Styles

    [DllImport("user32")]
    internal static extern IntPtr GetActiveWindow();

    [DllImport("shell32")]
    internal static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);
}
