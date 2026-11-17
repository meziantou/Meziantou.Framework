using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>A context of the model: 12 bytes (CPpmd8_Context). NumStats is the number of symbols minus one.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Ppmd8Context
{
    public byte NumStats;
    public byte Flags;
    public ushort SummFreq;
    public uint Stats;
    public uint Suffix;
}
