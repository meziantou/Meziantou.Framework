using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A snapshot of an <see cref="AllocationScope"/>, for internal tests and diagnostics only (there is no public diagnostics API).</summary>
/// <param name="Limit">The live-byte limit (<see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>).</param>
/// <param name="LiveBytes">The bytes currently charged, at actual rented capacity, including unconsumed reservations.</param>
/// <param name="ReservedBytes">The part of <paramref name="LiveBytes"/> that is reserved but not yet rented.</param>
/// <param name="PeakLiveBytes">The highest value reached by <paramref name="LiveBytes"/>.</param>
/// <param name="LiveAllocations">The number of live buffers and charges.</param>
/// <param name="TotalAllocations">The number of buffers and charges created since the scope was created.</param>
/// <param name="RejectedRequests">The number of charges rejected because they would exceed the limit.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AllocationScopeDiagnostics(
    long Limit,
    long LiveBytes,
    long ReservedBytes,
    long PeakLiveBytes,
    int LiveAllocations,
    long TotalAllocations,
    long RejectedRequests);
