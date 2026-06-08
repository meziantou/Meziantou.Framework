using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Hash64
{
    public readonly uint Hash1;
    public readonly uint Hash2;

    public Hash64(ulong value)
    {
        Hash1 = (uint)value;
        var hash2 = (uint)(value >> 32);
        Hash2 = hash2 == 0 ? 0x9E3779B9U : hash2; // Golden ratio
    }
}
