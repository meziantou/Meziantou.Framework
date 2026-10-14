using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the SIZ marker segment says of one component (Table A.11): its samples and its sub-sampling on the reference grid.</summary>
/// <param name="Precision">The number of bits of a sample, 1 to 38.</param>
/// <param name="IsSigned">Whether the samples are signed.</param>
/// <param name="XSubsampling">The horizontal separation of the samples on the reference grid (XRsiz), 1 to 255.</param>
/// <param name="YSubsampling">The vertical separation of the samples on the reference grid (YRsiz), 1 to 255.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000ComponentSize(int Precision, bool IsSigned, int XSubsampling, int YSubsampling);
