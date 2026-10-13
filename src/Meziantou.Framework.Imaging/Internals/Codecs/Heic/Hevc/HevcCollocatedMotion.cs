using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The motion that a picture keeps for the temporal motion vector prediction of the pictures that follow it (Rec. ITU-T
/// H.265, clause 8.5.3.2.9 "Derivation process for collocated motion vectors"): the motion of the prediction block that
/// covers the top-left sample of a 16x16 block of luma samples, with its reference pictures told by their picture order
/// counts rather than by their indices, which are those of the slice of the block.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct HevcCollocatedMotion
{
    /// <summary>The position in <see cref="Flags"/> of the bits that tell that the reference picture of a list was marked as "used for long-term reference".</summary>
    public const int LongTermShift = 2;

    public int RefPoc0;
    public int RefPoc1;
    public short Mv0X;
    public short Mv0Y;
    public short Mv1X;
    public short Mv1Y;

    /// <summary>The prediction list utilization flags (<see cref="HevcMotion.L0"/>, <see cref="HevcMotion.L1"/>), and the long-term flags of the two reference pictures.</summary>
    public byte Flags;
}
