namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

[Flags]
public enum Permission
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}
