namespace Meziantou.Framework.FastEnumGenerator.FuzzEnums;

public enum FuzzLarge : ulong
{
    Min = 0,
    Mid = 1UL << 63,
    Max = ulong.MaxValue,
}
