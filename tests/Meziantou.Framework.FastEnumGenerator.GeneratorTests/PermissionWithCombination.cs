namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

[Flags]
public enum PermissionWithCombination
{
    None = 0,
    A = 1,
    B = 2,
    AandB = 3,
    C = 4,
}
