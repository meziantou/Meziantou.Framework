using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The motion of a prediction block (Rec. ITU-T H.265, clause 8.5.3.2): the motion vectors <c>MvL0</c> and <c>MvL1</c> in
/// units of a quarter of a luma sample, the reference indices <c>RefIdxL0</c> and <c>RefIdxL1</c>, and the prediction list
/// utilization flags. The value whose fields are all zero is the one of a block that is not predicted from another picture.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct HevcMotion
{
    /// <summary>The bit of <see cref="PredFlags"/> for <c>PredFlagL0</c>.</summary>
    public const byte L0 = 1;

    /// <summary>The bit of <see cref="PredFlags"/> for <c>PredFlagL1</c>.</summary>
    public const byte L1 = 2;

    /// <summary>The value of <see cref="PredFlags"/> of a block predicted from two pictures.</summary>
    public const byte Bi = L0 | L1;

    public short Mv0X;
    public short Mv0Y;
    public short Mv1X;
    public short Mv1Y;
    public sbyte RefIdx0;
    public sbyte RefIdx1;
    public byte PredFlags;

    /// <summary>Gets whether a list is used: <c>PredFlagLX</c>.</summary>
    public readonly bool Uses(int list) => (PredFlags & (1 << list)) is not 0;

    public readonly int GetRefIdx(int list) => list is 0 ? RefIdx0 : RefIdx1;

    public readonly short GetMvX(int list) => list is 0 ? Mv0X : Mv1X;

    public readonly short GetMvY(int list) => list is 0 ? Mv0Y : Mv1Y;

    /// <summary>Sets the motion of a list, which becomes used.</summary>
    public void Set(int list, int refIdx, short mvX, short mvY)
    {
        PredFlags |= (byte)(1 << list);
        if (list is 0)
        {
            RefIdx0 = (sbyte)refIdx;
            Mv0X = mvX;
            Mv0Y = mvY;
        }
        else
        {
            RefIdx1 = (sbyte)refIdx;
            Mv1X = mvX;
            Mv1Y = mvY;
        }
    }

    /// <summary>Makes a list unused.</summary>
    public void Clear(int list)
    {
        PredFlags &= (byte)~(1 << list);
        if (list is 0)
        {
            RefIdx0 = 0;
            Mv0X = 0;
            Mv0Y = 0;
        }
        else
        {
            RefIdx1 = 0;
            Mv1X = 0;
            Mv1Y = 0;
        }
    }

    /// <summary>Gets whether two prediction blocks have the same motion vectors and the same reference indices.</summary>
    public static bool AreSame(in HevcMotion a, in HevcMotion b)
    {
        return a.PredFlags == b.PredFlags && a.RefIdx0 == b.RefIdx0 && a.RefIdx1 == b.RefIdx1 && a.Mv0X == b.Mv0X && a.Mv0Y == b.Mv0Y && a.Mv1X == b.Mv1X && a.Mv1Y == b.Mv1Y;
    }
}
