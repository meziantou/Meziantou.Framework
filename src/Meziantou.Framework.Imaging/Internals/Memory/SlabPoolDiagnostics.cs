using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A snapshot of the state of a <see cref="SlabPool"/>, for internal tests and diagnostics only.</summary>
/// <param name="RetainedBuffers">The number of buffers currently retained for reuse.</param>
/// <param name="RetainedBytes">The sum of the capacities of the retained buffers.</param>
/// <param name="ReusedRentals">The number of rentals served from a retained buffer.</param>
/// <param name="NewPooledAllocations">The number of rentals that allocated a new buffer of a pooled size class.</param>
/// <param name="UnpooledAllocations">The number of rentals larger than the largest size class (allocated exactly, never retained).</param>
/// <param name="RetainedReturns">The number of returned buffers that were retained.</param>
/// <param name="DroppedReturns">The number of returned buffers left to the garbage collector because a retention bound was reached or the buffer is not poolable.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SlabPoolDiagnostics(
    int RetainedBuffers,
    long RetainedBytes,
    long ReusedRentals,
    long NewPooledAllocations,
    long UnpooledAllocations,
    long RetainedReturns,
    long DroppedReturns);
