namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The intra sample prediction of a transform block from its neighbouring samples (Rec. ITU-T H.265, clause 8.4.4.2 "Intra
/// sample prediction").
/// </summary>
/// <remarks>
/// The <c>nTbS * 4 + 1</c> neighbouring samples <c>p[ x ][ y ]</c> are one list, in the order of the substitution process:
/// the left column from the bottom to the top, <c>p[ -1 ][ nTbS * 2 - 1 ]</c> to <c>p[ -1 ][ 0 ]</c>, the corner
/// <c>p[ -1 ][ -1 ]</c>, then the row above from the left to the right, <c>p[ 0 ][ -1 ]</c> to
/// <c>p[ nTbS * 2 - 1 ][ -1 ]</c>.
/// </remarks>
internal static class HevcIntraPredictor
{
    /// <summary>The largest number of neighbouring samples of a block: those of a block of 32 samples.</summary>
    public const int MaxNeighbourCount = (32 * 4) + 1;

    /// <summary>
    /// Substitutes the samples that are not available for intra prediction (clause 8.4.4.2.2 "Reference sample
    /// substitution process for intra sample prediction").
    /// </summary>
    /// <param name="p">The neighbouring samples.</param>
    /// <param name="available">Whether each sample is marked as available for intra prediction.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <returns><see langword="true"/> when a sample was substituted.</returns>
    public static bool Substitute(Span<ushort> p, ReadOnlySpan<bool> available, int bitDepth)
    {
        var first = available.IndexOf(true);
        if (first < 0)
        {
            p.Fill((ushort)(1 << (bitDepth - 1)));
            return true;
        }

        var substituted = first > 0 || available.Contains(false);
        if (!substituted)
            return false;

        // 1. The first sample of the search order takes the value of the first available one
        if (first > 0)
        {
            p[0] = p[first];
        }

        // 2. and 3. A sample that is not available takes the value of the one before it in the search order
        for (var i = 1; i < p.Length; i++)
        {
            if (!available[i])
            {
                p[i] = p[i - 1];
            }
        }

        return true;
    }

    /// <summary>
    /// Gets <c>filterFlag</c> of the filtering process of neighbouring samples (clause 8.4.4.2.3): whether the samples are
    /// filtered for a prediction mode and a block size.
    /// </summary>
    public static bool GetFilterFlag(int predModeIntra, int nTbS)
    {
        if (predModeIntra is HevcConstants.IntraDc || nTbS is 4)
            return false;

        // Table 8-4 - Specification of intraHorVerDistThres[ nTbS ]
        var intraHorVerDistThres = nTbS switch
        {
            8 => 7,
            16 => 1,
            _ => 0,
        };
        var minDistVerHor = Math.Min(Math.Abs(predModeIntra - 26), Math.Abs(predModeIntra - 10));
        return minDistVerHor > intraHorVerDistThres;
    }

    /// <summary>
    /// Filters the neighbouring samples (clause 8.4.4.2.3 "Filtering process of neighbouring samples"), when
    /// <see cref="GetFilterFlag"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="p">The neighbouring samples.</param>
    /// <param name="filtered">The filtered samples <c>pF</c>.</param>
    /// <param name="nTbS">The size of the transform block.</param>
    /// <param name="strongIntraSmoothing">Whether the bi-linear interpolation is considered: <c>strong_intra_smoothing_enabled_flag</c> is 1 and <c>cIdx</c> is 0.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <returns><see langword="true"/> when <c>biIntFlag</c> is 1.</returns>
    public static bool Filter(ReadOnlySpan<ushort> p, Span<ushort> filtered, int nTbS, bool strongIntraSmoothing, int bitDepth)
    {
        var corner = nTbS * 2;
        var last = nTbS * 4;
        if (strongIntraSmoothing && nTbS is 32)
        {
            // p[ -1 ][ nTbS * 2 - 1 ] is the first sample of the list, p[ nTbS * 2 - 1 ][ -1 ] the last one
            var threshold = 1 << (bitDepth - 5);
            if (Math.Abs(p[corner] + p[last] - (2 * p[corner + nTbS])) < threshold
                && Math.Abs(p[corner] + p[0] - (2 * p[corner - nTbS])) < threshold)
            {
                // Equations 8-36 to 8-40
                filtered[corner] = p[corner];
                filtered[0] = p[0];
                filtered[last] = p[last];
                for (var i = 0; i <= 62; i++)
                {
                    filtered[corner - 1 - i] = (ushort)((((63 - i) * p[corner]) + ((i + 1) * p[0]) + 32) >> 6);
                    filtered[corner + 1 + i] = (ushort)((((63 - i) * p[corner]) + ((i + 1) * p[last]) + 32) >> 6);
                }

                return true;
            }
        }

        // Equations 8-41 to 8-45: the neighbours of a sample of the list are its neighbours in the picture
        filtered[0] = p[0];
        filtered[last] = p[last];
        for (var i = 1; i < last; i++)
        {
            filtered[i] = (ushort)((p[i - 1] + (2 * p[i]) + p[i + 1] + 2) >> 2);
        }

        return false;
    }

    /// <summary>Predicts a block with the mode <c>INTRA_PLANAR</c> (clause 8.4.4.2.4).</summary>
    public static void PredictPlanar(ReadOnlySpan<ushort> p, Span<ushort> destination, int stride, int nTbS)
    {
        var corner = nTbS * 2;
        var shift = int.Log2(nTbS) + 1;
        int topRight = p[corner + 1 + nTbS];
        int bottomLeft = p[corner - 1 - nTbS];
        for (var y = 0; y < nTbS; y++)
        {
            var row = destination.Slice(y * stride, nTbS);
            int left = p[corner - 1 - y];
            for (var x = 0; x < nTbS; x++)
            {
                // Equation 8-46
                row[x] = (ushort)((((nTbS - 1 - x) * left) + ((x + 1) * topRight) + ((nTbS - 1 - y) * p[corner + 1 + x]) + ((y + 1) * bottomLeft) + nTbS) >> shift);
            }
        }
    }

    /// <summary>Predicts a block with the mode <c>INTRA_DC</c> (clause 8.4.4.2.5).</summary>
    /// <param name="p">The neighbouring samples.</param>
    /// <param name="destination">The samples of the plane, from the first sample of the block.</param>
    /// <param name="stride">The number of samples of a row of the plane.</param>
    /// <param name="nTbS">The size of the transform block.</param>
    /// <param name="edgeFilter">Whether the first row and the first column are filtered: <c>cIdx</c> is 0 and <c>nTbS</c> is less than 32.</param>
    public static void PredictDc(ReadOnlySpan<ushort> p, Span<ushort> destination, int stride, int nTbS, bool edgeFilter)
    {
        var corner = nTbS * 2;

        // Equation 8-47
        var sum = nTbS;
        for (var i = 0; i < nTbS; i++)
        {
            sum += p[corner + 1 + i] + p[corner - 1 - i];
        }

        var dcVal = sum >> (int.Log2(nTbS) + 1);
        for (var y = 0; y < nTbS; y++)
        {
            destination.Slice(y * stride, nTbS).Fill((ushort)dcVal);
        }

        if (!edgeFilter)
            return;

        // Equations 8-48 to 8-50
        destination[0] = (ushort)((p[corner - 1] + (2 * dcVal) + p[corner + 1] + 2) >> 2);
        for (var x = 1; x < nTbS; x++)
        {
            destination[x] = (ushort)((p[corner + 1 + x] + (3 * dcVal) + 2) >> 2);
        }

        for (var y = 1; y < nTbS; y++)
        {
            destination[y * stride] = (ushort)((p[corner - 1 - y] + (3 * dcVal) + 2) >> 2);
        }
    }

    /// <summary>
    /// Predicts a block with a mode in the range <c>INTRA_ANGULAR2</c> to <c>INTRA_ANGULAR34</c> (clause 8.4.4.2.6).
    /// </summary>
    /// <param name="p">The neighbouring samples.</param>
    /// <param name="destination">The samples of the plane, from the first sample of the block.</param>
    /// <param name="stride">The number of samples of a row of the plane.</param>
    /// <param name="nTbS">The size of the transform block.</param>
    /// <param name="predModeIntra">The prediction mode.</param>
    /// <param name="edgeFilter">Whether the first column of the vertical mode and the first row of the horizontal mode are filtered: <c>cIdx</c> is 0, <c>nTbS</c> is less than 32 and <c>disableIntraBoundaryFilter</c> is 0.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    public static void PredictAngular(ReadOnlySpan<ushort> p, Span<ushort> destination, int stride, int nTbS, int predModeIntra, bool edgeFilter, int bitDepth)
    {
        var corner = nTbS * 2;
        int intraPredAngle = HevcIntraTables.IntraPredAngle[predModeIntra];
        var vertical = predModeIntra >= 18;

        // ref[ x ] for x in the range -nTbS to 2 * nTbS: the element nTbS + x of the buffer. The main reference is the
        // row above for the modes 18 to 34 and the left column for the other ones; the list of the neighbouring samples
        // has the left column backwards.
        // One more element is never read: it ends the rows of nTbS + 1 values that the interpolation takes.
        Span<ushort> buffer = stackalloc ushort[(3 * 32) + 2];
        var reference = buffer[..((3 * nTbS) + 2)];
        var mainStep = vertical ? 1 : -1;
        for (var x = 0; x <= nTbS; x++)
        {
            // Equations 8-53 and 8-61
            reference[nTbS + x] = p[corner + (mainStep * x)];
        }

        if (intraPredAngle < 0)
        {
            var lastIndex = (nTbS * intraPredAngle) >> 5;
            if (lastIndex < -1)
            {
                // Equations 8-54 and 8-62: the samples of the other side, projected on the main reference
                int invAngle = HevcIntraTables.InvAngle[predModeIntra - 11];
                for (var x = lastIndex; x <= -1; x++)
                {
                    reference[nTbS + x] = p[corner - (mainStep * (((x * invAngle) + 128) >> 8))];
                }
            }
        }
        else
        {
            for (var x = nTbS + 1; x <= 2 * nTbS; x++)
            {
                // Equations 8-55 and 8-63
                reference[nTbS + x] = p[corner + (mainStep * x)];
            }
        }

        var maxValue = (1 << bitDepth) - 1;
        if (vertical)
        {
            for (var y = 0; y < nTbS; y++)
            {
                // Equations 8-56 to 8-59
                var iIdx = ((y + 1) * intraPredAngle) >> 5;
                var iFact = ((y + 1) * intraPredAngle) & 31;
                var row = destination.Slice(y * stride, nTbS);
                var source = reference.Slice(nTbS + iIdx + 1, nTbS + 1);
                if (iFact is not 0)
                {
                    for (var x = 0; x < nTbS; x++)
                    {
                        row[x] = (ushort)((((32 - iFact) * source[x]) + (iFact * source[x + 1]) + 16) >> 5);
                    }
                }
                else
                {
                    source[..nTbS].CopyTo(row);
                }
            }

            if (predModeIntra is HevcConstants.IntraAngular26 && edgeFilter)
            {
                // Equation 8-60
                for (var y = 0; y < nTbS; y++)
                {
                    destination[y * stride] = (ushort)Math.Clamp(p[corner + 1] + ((p[corner - 1 - y] - p[corner]) >> 1), 0, maxValue);
                }
            }
        }
        else
        {
            for (var x = 0; x < nTbS; x++)
            {
                // Equations 8-64 to 8-67
                var iIdx = ((x + 1) * intraPredAngle) >> 5;
                var iFact = ((x + 1) * intraPredAngle) & 31;
                var source = reference.Slice(nTbS + iIdx + 1, nTbS + 1);
                if (iFact is not 0)
                {
                    for (var y = 0; y < nTbS; y++)
                    {
                        destination[(y * stride) + x] = (ushort)((((32 - iFact) * source[y]) + (iFact * source[y + 1]) + 16) >> 5);
                    }
                }
                else
                {
                    for (var y = 0; y < nTbS; y++)
                    {
                        destination[(y * stride) + x] = source[y];
                    }
                }
            }

            if (predModeIntra is HevcConstants.IntraAngular10 && edgeFilter)
            {
                // Equation 8-68
                for (var x = 0; x < nTbS; x++)
                {
                    destination[x] = (ushort)Math.Clamp(p[corner - 1] + ((p[corner + 1 + x] - p[corner]) >> 1), 0, maxValue);
                }
            }
        }
    }
}
