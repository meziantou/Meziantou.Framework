using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Hash32
{
    public readonly ushort Hash1;
    public readonly ushort Hash2;

    public Hash32(uint value)
    {
        Hash1 = (ushort)value;
        var hash2 = (ushort)(value >> 16);
        Hash2 = hash2 == 0 ? (ushort)0x9E37 : hash2; // Golden ratio
    }
}
