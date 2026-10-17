namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded picture buffer, and the order in which its pictures are output (Rec. ITU-T H.265, clause C.5.2 "Operation of
/// the output order DPB"): the pictures that later pictures refer to, and those that wait for their turn to be output.
/// </summary>
internal sealed class HevcDecodedPictureBuffer : IDisposable
{
    private readonly List<HevcPicture> _pictures = [];
    private readonly Queue<HevcPicture> _output = new();
    private long _discardedPixels;

    /// <summary>Gets the pictures of the buffer.</summary>
    public IReadOnlyList<HevcPicture> Pictures => _pictures;

    /// <summary>Gets <c>RefPicSetStCurrBefore</c> of the current picture: an entry is <see langword="null"/> for "no reference picture".</summary>
    public List<HevcPicture?> StCurrBefore { get; } = [];

    /// <summary>Gets <c>RefPicSetStCurrAfter</c> of the current picture.</summary>
    public List<HevcPicture?> StCurrAfter { get; } = [];

    /// <summary>Gets <c>RefPicSetLtCurr</c> of the current picture.</summary>
    public List<HevcPicture?> LtCurr { get; } = [];

    /// <summary>
    /// Derives the reference picture set of the current picture and marks the pictures of the buffer (clause 8.3.2
    /// "Decoding process for reference picture set"): the pictures that the set does not have are "unused for reference",
    /// and those of its long-term subsets are "used for long-term reference".
    /// </summary>
    /// <param name="picOrderCntVal">The picture order count of the current picture.</param>
    /// <param name="header">The slice header of the first slice of the current picture.</param>
    /// <param name="sps">The active sequence parameter set.</param>
    /// <param name="isIrapWithNoRaslOutputFlag">Whether the current picture is an IRAP picture with <c>NoRaslOutputFlag</c> equal to 1: no picture is a reference picture any more.</param>
    public void ApplyReferencePictureSet(int picOrderCntVal, HevcSliceSegmentHeader header, HevcSequenceParameterSet sps, bool isIrapWithNoRaslOutputFlag)
    {
        StCurrBefore.Clear();
        StCurrAfter.Clear();
        LtCurr.Clear();
        foreach (var picture in _pictures)
        {
            picture.IsInReferencePictureSet = false;
            if (isIrapWithNoRaslOutputFlag)
            {
                picture.UsedForReference = false;
                picture.IsLongTermReference = false;
            }
        }

        // The long-term subsets (Equation 7-52 and step 1): a picture is identified by the least significant bits of its
        // picture order count, or by its whole picture order count
        var maxPicOrderCntLsb = 1 << sps.Log2MaxPicOrderCntLsb;
        for (var i = 0; i < header.NumLongTermRefPics; i++)
        {
            var pocLt = header.PocLsbLt[i];
            var hasMsb = header.DeltaPocMsbPresentFlag[i];
            if (hasMsb)
            {
                pocLt += picOrderCntVal - (header.DeltaPocMsbCycleLt[i] * maxPicOrderCntLsb) - (picOrderCntVal & (maxPicOrderCntLsb - 1));
            }

            HevcPicture? found = null;
            foreach (var picture in _pictures)
            {
                if (picture.UsedForReference && (hasMsb ? picture.PicOrderCntVal : picture.PicOrderCntVal & (maxPicOrderCntLsb - 1)) == pocLt)
                {
                    found = picture;
                    break;
                }
            }

            if (found is not null)
            {
                found.IsInReferencePictureSet = true;
            }

            if (header.UsedByCurrPicLt[i])
            {
                LtCurr.Add(found);
            }
        }

        // Step 2: the pictures of the long-term subsets are marked as "used for long-term reference"
        foreach (var picture in _pictures)
        {
            if (picture.IsInReferencePictureSet)
            {
                picture.IsLongTermReference = true;
            }
        }

        // Step 3: the short-term subsets, among the pictures that are marked as "used for short-term reference"
        var set = header.ShortTermRefPicSet;
        if (set is not null)
        {
            for (var i = 0; i < set.NumNegativePics; i++)
            {
                var found = FindShortTermReference(picOrderCntVal + set.DeltaPocS0[i]);
                if (set.UsedByCurrPicS0[i])
                {
                    StCurrBefore.Add(found);
                }
            }

            for (var i = 0; i < set.NumPositivePics; i++)
            {
                var found = FindShortTermReference(picOrderCntVal + set.DeltaPocS1[i]);
                if (set.UsedByCurrPicS1[i])
                {
                    StCurrAfter.Add(found);
                }
            }
        }

        // Step 4
        foreach (var picture in _pictures)
        {
            if (!picture.IsInReferencePictureSet)
            {
                picture.UsedForReference = false;
                picture.IsLongTermReference = false;
            }
        }
    }

    private HevcPicture? FindShortTermReference(int picOrderCntVal)
    {
        foreach (var picture in _pictures)
        {
            if (picture.UsedForReference && !picture.IsLongTermReference && picture.PicOrderCntVal == picOrderCntVal)
            {
                picture.IsInReferencePictureSet = true;
                return picture;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the number of luma samples of the pictures that were removed from the buffer without having been output, since
    /// the last call: those that <c>NoOutputOfPriorPicsFlag</c> discards.
    /// </summary>
    public long TakeDiscardedPixels()
    {
        var count = _discardedPixels;
        _discardedPixels = 0;
        return count;
    }

    /// <summary>
    /// Outputs and removes pictures before the decoding of the current picture (clause C.5.2.2 "Output and removal of
    /// pictures from the DPB").
    /// </summary>
    /// <param name="sps">The active sequence parameter set.</param>
    /// <param name="isIrapWithNoRaslOutputFlag">Whether the current picture is an IRAP picture with <c>NoRaslOutputFlag</c> equal to 1 that is not the first picture.</param>
    /// <param name="noOutputOfPriorPicsFlag"><c>NoOutputOfPriorPicsFlag</c>.</param>
    /// <exception cref="InvalidImageContentException">The buffer has no room for the current picture.</exception>
    public void BeforePicture(HevcSequenceParameterSet sps, bool isIrapWithNoRaslOutputFlag, bool noOutputOfPriorPicsFlag)
    {
        if (isIrapWithNoRaslOutputFlag)
        {
            if (!noOutputOfPriorPicsFlag)
            {
                Flush();
            }

            foreach (var picture in _pictures)
            {
                if (picture.NeededForOutput)
                {
                    _discardedPixels += (long)picture.Sps.PicWidthInLumaSamples * picture.Sps.PicHeightInLumaSamples;
                }

                picture.Release();
            }

            _pictures.Clear();
            return;
        }

        RemoveUnusedPictures();
        var highestTid = sps.HighestTid;
        while (NeedsBumping(sps) || _pictures.Count >= sps.MaxDecPicBufferingMinus1[highestTid] + 1)
        {
            if (!Bump())
            {
                // Every picture of the buffer is a reference picture: a conforming bitstream leaves room for the current one
                throw HevcErrors.Invalid("the decoded picture buffer has no room for a picture.");
            }
        }
    }

    /// <summary>
    /// Stores the current picture, which is decoded, and outputs the pictures whose turn has come (clause C.5.2.3
    /// "Additional bumping").
    /// </summary>
    public void AfterPicture(HevcPicture current, HevcSequenceParameterSet sps)
    {
        if (current.PicOutputFlag)
        {
            foreach (var picture in _pictures)
            {
                if (picture.NeededForOutput && picture.PicOrderCntVal > current.PicOrderCntVal)
                {
                    picture.PicLatencyCount++;
                }
            }
        }

        current.NeededForOutput = current.PicOutputFlag;
        current.PicLatencyCount = 0;

        // Clause 8.3.2: the current picture is marked as "used for short-term reference" after it is decoded
        current.UsedForReference = true;
        current.IsLongTermReference = false;
        current.AddReference();
        _pictures.Add(current);
        while (NeedsBumping(sps) && Bump())
        {
        }
    }

    /// <summary>Outputs every picture that waits for its turn, at the end of the sequence.</summary>
    public void Flush()
    {
        while (Bump())
        {
        }
    }

    /// <summary>Removes every picture from the buffer, at the end of a bitstream; the pictures that are ready to be output stay so.</summary>
    public void Clear()
    {
        foreach (var picture in _pictures)
        {
            picture.Release();
        }

        _pictures.Clear();
        StCurrBefore.Clear();
        StCurrAfter.Clear();
        LtCurr.Clear();
    }

    /// <summary>Gets whether a picture is ready to be output.</summary>
    public bool HasOutput => _output.Count > 0;

    /// <summary>Gets the next picture to output, which the caller releases; or <see langword="null"/> when none is ready.</summary>
    public HevcPicture? TakeOutput() => _output.TryDequeue(out var picture) ? picture : null;

    private bool NeedsBumping(HevcSequenceParameterSet sps)
    {
        var highestTid = sps.HighestTid;
        var neededForOutput = 0;
        var maxLatency = -1;
        foreach (var picture in _pictures)
        {
            if (picture.NeededForOutput)
            {
                neededForOutput++;
                maxLatency = Math.Max(maxLatency, picture.PicLatencyCount);
            }
        }

        if (neededForOutput > sps.MaxNumReorderPics[highestTid])
            return true;

        // SpsMaxLatencyPictures (Equation 7-9)
        var maxLatencyIncreasePlus1 = sps.MaxLatencyIncreasePlus1[highestTid];
        return maxLatencyIncreasePlus1 is not 0 && maxLatency >= sps.MaxNumReorderPics[highestTid] + (long)maxLatencyIncreasePlus1 - 1;
    }

    // Clause C.5.2.4 "Bumping" process
    private bool Bump()
    {
        HevcPicture? first = null;
        foreach (var picture in _pictures)
        {
            if (picture.NeededForOutput && (first is null || picture.PicOrderCntVal < first.PicOrderCntVal))
            {
                first = picture;
            }
        }

        if (first is null)
            return false;

        first.NeededForOutput = false;
        first.AddReference();
        _output.Enqueue(first);
        if (!first.UsedForReference)
        {
            _pictures.Remove(first);
            first.Release();
        }

        return true;
    }

    private void RemoveUnusedPictures()
    {
        for (var i = _pictures.Count - 1; i >= 0; i--)
        {
            var picture = _pictures[i];
            if (!picture.NeededForOutput && !picture.UsedForReference)
            {
                _pictures.RemoveAt(i);
                picture.Release();
            }
        }
    }

    public void Dispose()
    {
        while (_output.TryDequeue(out var picture))
        {
            picture.Release();
        }

        foreach (var picture in _pictures)
        {
            picture.Release();
        }

        _pictures.Clear();
    }
}
