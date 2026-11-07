using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One progression of a POC marker segment (T.800, A.6.6): the packets of a range of resolution levels, components and layers, in a progression order.</summary>
/// <param name="ResolutionStart">The first resolution level (RSpoc).</param>
/// <param name="ComponentStart">The first component (CSpoc).</param>
/// <param name="LayerEnd">The layer after the last one (LYEpoc).</param>
/// <param name="ResolutionEnd">The resolution level after the last one (REpoc).</param>
/// <param name="ComponentEnd">The component after the last one (CEpoc).</param>
/// <param name="Progression">The progression order (Ppoc).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000ProgressionChange(int ResolutionStart, int ComponentStart, int LayerEnd, int ResolutionEnd, int ComponentEnd, int Progression);
