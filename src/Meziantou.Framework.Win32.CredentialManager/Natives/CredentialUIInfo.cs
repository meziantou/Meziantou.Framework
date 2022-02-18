using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct CredentialUIInfo
{
    public int CbSize;
    public IntPtr HwndParent;
    public string? PszMessageText;
    public string? PszCaptionText;
    public IntPtr HbmBanner;
}
