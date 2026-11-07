using System.Runtime.InteropServices.ComTypes;

namespace Meziantou.Framework.Win32;

internal static class FileTimeExtensions
{
    public static long ToFileTime(this FILETIME fileTime)
    {
        return ((long)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
    }

    public static DateTime ToDateTime(this FILETIME fileTime)
    {
        return DateTime.FromFileTime(fileTime.ToFileTime());
    }
}
