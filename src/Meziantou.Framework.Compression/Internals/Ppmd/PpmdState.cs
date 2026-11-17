using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>A symbol of a context: 6 bytes in the memory of the model, as in 7-Zip (Ppmd.h, CPpmd_State).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PpmdState
{
    public byte Symbol;
    public byte Freq;

    /// <summary>The offset of the child context, or of the next byte of the text area for a raw successor, or 0.</summary>
    public uint Successor;
}
