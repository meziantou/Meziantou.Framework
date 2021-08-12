namespace Meziantou.Framework.Win32.Natives;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_UI_RESTRICTIONS
{
    public JobObjectUILimit UIRestrictionsClass;
}
