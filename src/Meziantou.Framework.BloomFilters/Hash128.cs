using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Hash128
{
    public readonly ulong Hash1;
    public readonly ulong Hash2;

    public Hash128(ulong low, ulong high)
    {
        Hash1 = low;
        Hash2 = high == 0 ? 0x9E3779B97F4A7C15UL : high; // Golden ratio
    }

    public Hash128(UInt128 value)
        : this((ulong)value, (ulong)(value >> 64))
    {
    }
}
