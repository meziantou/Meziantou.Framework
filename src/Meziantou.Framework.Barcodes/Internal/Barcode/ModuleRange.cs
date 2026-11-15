using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>A range of modules, from <paramref name="Start"/> (inclusive) to <paramref name="End"/> (exclusive).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ModuleRange(int Start, int End)
{
    public int Length => End - Start;

    public bool Contains(int module) => module >= Start && module < End;
}
