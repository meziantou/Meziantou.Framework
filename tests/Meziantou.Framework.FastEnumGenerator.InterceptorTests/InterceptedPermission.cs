namespace Meziantou.Framework.FastEnumGenerator.InterceptorTests;

[Flags]
public enum InterceptedPermission
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}
