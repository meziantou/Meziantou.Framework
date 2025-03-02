namespace Meziantou.Framework.Win32;

[Flags]
public enum JobObjectSecurityLimit
{
    NoAdmin = 0x00000001,
    RestrictedToken = 0x00000002,
}
