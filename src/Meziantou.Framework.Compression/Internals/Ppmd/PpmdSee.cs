using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The secondary escape estimation of a context class (Ppmd.h, CPpmd_See).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PpmdSee
{
    public ushort Summ;
    public byte Shift;
    public byte Count;

    public const int PeriodBits = 7;

    /// <summary>Returns the escape frequency and decreases the sum (Ppmd7_MakeEscFreq, Ppmd8_MakeEscFreq).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetMean()
    {
        var r = (uint)(Summ >> Shift);
        Summ = (ushort)(Summ - r);
        return r + (r == 0 ? 1u : 0u);
    }

    /// <summary>Ppmd_See_Update.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update()
    {
        if (Shift < PeriodBits && --Count == 0)
        {
            Summ = (ushort)(Summ << 1);
            Count = (byte)(3 << Shift++);
        }
    }
}
