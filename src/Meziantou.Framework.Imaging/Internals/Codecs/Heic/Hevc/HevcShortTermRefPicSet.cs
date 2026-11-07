namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A candidate short-term reference picture set (Rec. ITU-T H.265, clauses 7.3.7 "Short-term reference picture set syntax"
/// and 7.4.8 "Short-term reference picture set semantics"): the pictures before and after the current picture in output
/// order, as differences of picture order count.
/// </summary>
internal sealed class HevcShortTermRefPicSet
{
    private readonly int[] _deltaPocS0;
    private readonly int[] _deltaPocS1;
    private readonly bool[] _usedByCurrPicS0;
    private readonly bool[] _usedByCurrPicS1;

    private HevcShortTermRefPicSet(int[] deltaPocS0, bool[] usedByCurrPicS0, int[] deltaPocS1, bool[] usedByCurrPicS1)
    {
        _deltaPocS0 = deltaPocS0;
        _usedByCurrPicS0 = usedByCurrPicS0;
        _deltaPocS1 = deltaPocS1;
        _usedByCurrPicS1 = usedByCurrPicS1;
    }

    /// <summary>Gets <c>NumNegativePics[ stRpsIdx ]</c>.</summary>
    public int NumNegativePics => _deltaPocS0.Length;

    /// <summary>Gets <c>NumPositivePics[ stRpsIdx ]</c>.</summary>
    public int NumPositivePics => _deltaPocS1.Length;

    /// <summary>Gets <c>NumDeltaPocs[ stRpsIdx ]</c> (Equation 7-71).</summary>
    public int NumDeltaPocs => _deltaPocS0.Length + _deltaPocS1.Length;

    /// <summary>Gets <c>DeltaPocS0[ stRpsIdx ][ i ]</c>.</summary>
    public ReadOnlySpan<int> DeltaPocS0 => _deltaPocS0;

    /// <summary>Gets <c>DeltaPocS1[ stRpsIdx ][ i ]</c>.</summary>
    public ReadOnlySpan<int> DeltaPocS1 => _deltaPocS1;

    /// <summary>Gets <c>UsedByCurrPicS0[ stRpsIdx ][ i ]</c>.</summary>
    public ReadOnlySpan<bool> UsedByCurrPicS0 => _usedByCurrPicS0;

    /// <summary>Gets <c>UsedByCurrPicS1[ stRpsIdx ][ i ]</c>.</summary>
    public ReadOnlySpan<bool> UsedByCurrPicS1 => _usedByCurrPicS1;

    /// <summary>Gets the number of pictures of the set that the current picture may refer to.</summary>
    public int NumUsedByCurrPic => _usedByCurrPicS0.AsSpan().Count(true) + _usedByCurrPicS1.AsSpan().Count(true);

    /// <summary>Reads the syntax structure <c>st_ref_pic_set( stRpsIdx )</c>.</summary>
    /// <param name="reader">The reader, at the first bit of the structure.</param>
    /// <param name="stRpsIdx">The index of the set: <paramref name="numShortTermRefPicSets"/> for the set of a slice header.</param>
    /// <param name="numShortTermRefPicSets"><c>num_short_term_ref_pic_sets</c> of the sequence parameter set.</param>
    /// <param name="candidates">The sets of the sequence parameter set that precede the set.</param>
    /// <param name="maxDecPicBufferingMinus1"><c>sps_max_dec_pic_buffering_minus1</c> of the highest sub-layer.</param>
    /// <exception cref="InvalidImageContentException">A value is out of range, or the data are truncated.</exception>
    public static HevcShortTermRefPicSet Parse(ref HevcBitReader reader, int stRpsIdx, int numShortTermRefPicSets, ReadOnlySpan<HevcShortTermRefPicSet> candidates, int maxDecPicBufferingMinus1)
    {
        Span<int> deltaPocS0 = stackalloc int[HevcConstants.MaxRefPicSetPictures];
        Span<int> deltaPocS1 = stackalloc int[HevcConstants.MaxRefPicSetPictures];
        Span<bool> usedByCurrPicS0 = stackalloc bool[HevcConstants.MaxRefPicSetPictures];
        Span<bool> usedByCurrPicS1 = stackalloc bool[HevcConstants.MaxRefPicSetPictures];
        int numNegativePics;
        int numPositivePics;
        var interRefPicSetPredictionFlag = stRpsIdx is not 0 && reader.ReadFlag();
        if (interRefPicSetPredictionFlag)
        {
            var deltaIdxMinus1 = stRpsIdx == numShortTermRefPicSets ? reader.ReadUE(stRpsIdx - 1, "delta_idx_minus1") : 0;

            // Equations 7-59 and 7-60
            var reference = candidates[stRpsIdx - (deltaIdxMinus1 + 1)];
            var deltaRpsSign = reader.ReadFlag();
            var absDeltaRpsMinus1 = reader.ReadUE(0x7FFF, "abs_delta_rps_minus1");
            var deltaRps = (deltaRpsSign ? -1 : 1) * (absDeltaRpsMinus1 + 1);
            Span<bool> usedByCurrPicFlag = stackalloc bool[HevcConstants.MaxRefPicSetPictures + 1];
            Span<bool> useDeltaFlag = stackalloc bool[HevcConstants.MaxRefPicSetPictures + 1];
            for (var j = 0; j <= reference.NumDeltaPocs; j++)
            {
                usedByCurrPicFlag[j] = reader.ReadFlag();
                useDeltaFlag[j] = usedByCurrPicFlag[j] || reader.ReadFlag();
            }

            // Equation 7-61
            var i = 0;
            for (var j = reference.NumPositivePics - 1; j >= 0; j--)
            {
                var dPoc = reference._deltaPocS1[j] + deltaRps;
                if (dPoc < 0 && useDeltaFlag[reference.NumNegativePics + j])
                {
                    deltaPocS0[i] = dPoc;
                    usedByCurrPicS0[i++] = usedByCurrPicFlag[reference.NumNegativePics + j];
                }
            }

            if (deltaRps < 0 && useDeltaFlag[reference.NumDeltaPocs])
            {
                deltaPocS0[i] = deltaRps;
                usedByCurrPicS0[i++] = usedByCurrPicFlag[reference.NumDeltaPocs];
            }

            for (var j = 0; j < reference.NumNegativePics; j++)
            {
                var dPoc = reference._deltaPocS0[j] + deltaRps;
                if (dPoc < 0 && useDeltaFlag[j])
                {
                    deltaPocS0[i] = dPoc;
                    usedByCurrPicS0[i++] = usedByCurrPicFlag[j];
                }
            }

            numNegativePics = i;

            // Equation 7-62
            i = 0;
            for (var j = reference.NumNegativePics - 1; j >= 0; j--)
            {
                var dPoc = reference._deltaPocS0[j] + deltaRps;
                if (dPoc > 0 && useDeltaFlag[j])
                {
                    deltaPocS1[i] = dPoc;
                    usedByCurrPicS1[i++] = usedByCurrPicFlag[j];
                }
            }

            if (deltaRps > 0 && useDeltaFlag[reference.NumDeltaPocs])
            {
                deltaPocS1[i] = deltaRps;
                usedByCurrPicS1[i++] = usedByCurrPicFlag[reference.NumDeltaPocs];
            }

            for (var j = 0; j < reference.NumPositivePics; j++)
            {
                var dPoc = reference._deltaPocS1[j] + deltaRps;
                if (dPoc > 0 && useDeltaFlag[reference.NumNegativePics + j])
                {
                    deltaPocS1[i] = dPoc;
                    usedByCurrPicS1[i++] = usedByCurrPicFlag[reference.NumNegativePics + j];
                }
            }

            numPositivePics = i;

            // The source set has at most 16 pictures, so a predicted set has at most 17: one more than a set can have
            if (numNegativePics + numPositivePics > maxDecPicBufferingMinus1)
                throw HevcErrors.Invalid("a short-term reference picture set has more pictures than the decoded picture buffer.");
        }
        else
        {
            numNegativePics = reader.ReadUE(maxDecPicBufferingMinus1, "num_negative_pics");
            numPositivePics = reader.ReadUE(maxDecPicBufferingMinus1 - numNegativePics, "num_positive_pics");

            // Equations 7-63 to 7-70
            var poc = 0;
            for (var i = 0; i < numNegativePics; i++)
            {
                poc -= reader.ReadUE(0x7FFF, "delta_poc_s0_minus1") + 1;
                deltaPocS0[i] = poc;
                usedByCurrPicS0[i] = reader.ReadFlag();
            }

            poc = 0;
            for (var i = 0; i < numPositivePics; i++)
            {
                poc += reader.ReadUE(0x7FFF, "delta_poc_s1_minus1") + 1;
                deltaPocS1[i] = poc;
                usedByCurrPicS1[i] = reader.ReadFlag();
            }
        }

        return new HevcShortTermRefPicSet(
            deltaPocS0[..numNegativePics].ToArray(),
            usedByCurrPicS0[..numNegativePics].ToArray(),
            deltaPocS1[..numPositivePics].ToArray(),
            usedByCurrPicS1[..numPositivePics].ToArray());
    }
}
