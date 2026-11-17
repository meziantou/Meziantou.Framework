using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>A context of the model: 12 bytes (CPpmd7_Context). A context with one symbol stores it at offset 2 (OneState).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Ppmd7Context
{
    public ushort NumStats;
    public ushort SummFreq;
    public uint Stats;
    public uint Suffix;
}
