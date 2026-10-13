namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The reference picture lists <c>RefPicList0</c> and <c>RefPicList1</c> of a slice (Rec. ITU-T H.265, clause 8.3.4
/// "Decoding process for reference picture lists construction"). The pictures are held by the decoded picture buffer for
/// as long as the picture of the slice is being decoded.
/// </summary>
internal sealed class HevcReferencePictureLists
{
    /// <summary>The greatest number of entries of a list: <c>num_ref_idx_l0_active_minus1</c> is at most 14.</summary>
    public const int MaxEntries = 16;

    private readonly HevcPicture?[] _pictures = new HevcPicture?[2 * MaxEntries];
    private readonly int[] _poc = new int[2 * MaxEntries];
    private readonly bool[] _isLongTerm = new bool[2 * MaxEntries];
    private readonly int[] _count = new int[2];

    /// <summary>
    /// Gets <c>NoBackwardPredFlag</c> (clause 8.5.3.2.9): whether no reference picture of the slice follows the picture of the
    /// slice in output order.
    /// </summary>
    public bool NoBackwardPredFlag { get; private set; }

    /// <summary>Gets or sets the picture that the temporal motion vector prediction of the slice reads: <c>ColPic</c>; or <see langword="null"/> when the slice has none.</summary>
    public HevcPicture? CollocatedPicture { get; set; }

    /// <summary>Gets the number of entries of a list: <c>num_ref_idx_lX_active_minus1 + 1</c>.</summary>
    public int GetCount(int list) => _count[list];

    public HevcPicture GetPicture(int list, int refIdx) => _pictures[(list * MaxEntries) + refIdx]!;

    /// <summary>Gets the picture order count of a reference picture, which identifies it among the pictures of the decoded picture buffer.</summary>
    public int GetPoc(int list, int refIdx) => _poc[(list * MaxEntries) + refIdx];

    /// <summary>Gets whether a reference picture is marked as "used for long-term reference" for the picture of the slice.</summary>
    public bool IsLongTerm(int list, int refIdx) => _isLongTerm[(list * MaxEntries) + refIdx];

    public void Add(int list, HevcPicture picture, bool isLongTerm)
    {
        var index = (list * MaxEntries) + _count[list]++;
        _pictures[index] = picture;
        _poc[index] = picture.PicOrderCntVal;
        _isLongTerm[index] = isLongTerm;
    }

    /// <summary>Derives the values that depend on every entry, once the lists are constructed.</summary>
    public void Complete(int picOrderCntVal)
    {
        var noBackwardPred = true;
        for (var list = 0; list < 2; list++)
        {
            for (var i = 0; i < _count[list]; i++)
            {
                noBackwardPred &= _poc[(list * MaxEntries) + i] <= picOrderCntVal;
            }
        }

        NoBackwardPredFlag = noBackwardPred;
    }
}
