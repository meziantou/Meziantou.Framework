namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Chooses the parameters of the in-loop filters of a reconstructed frame: the levels of the loop filter (AV1
/// specification, section 7.14) and the strengths of the constrained directional enhancement filter (section 7.15). The
/// specification defines what the filters do for given parameters; the encoder picks the parameters that bring the frame
/// closest to the frame it encodes.
/// </summary>
/// <remarks>
/// A candidate is evaluated by filtering the frame with the filters of the decoder and measuring the squared error of each
/// plane against the source. The planes are filtered independently of one another (the luma levels do not change the
/// chroma planes, and the other way round), so one filtering pass gives the error of a value for every plane, and each
/// plane takes the value that suits it best. On return the frame is filtered with the chosen parameters, which are also
/// in the header that is written.
/// </remarks>
internal static class Av1FilterSearch
{
    private static ReadOnlySpan<byte> LoopFilterLevels => [1, 2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 32, 40, 48, 63];

    private static ReadOnlySpan<byte> CdefPrimaryStrengths => [1, 2, 3, 4, 6, 8, 10, 12, 15];

    private static ReadOnlySpan<byte> CdefSecondaryStrengths => [1, 2, 4];

    /// <summary>Chooses the parameters of the filters of <paramref name="frame"/> and applies them.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="postFilter">The filters of the decoder.</param>
    /// <param name="frame">The reconstructed frame, before any filter; its header receives the parameters.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="workspace">The source planes, and the planes that save the frame between candidates.</param>
    /// <param name="syntax">The syntax elements of the header to write, which receive the parameters too.</param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    public static void Run(AllocationScope scope, Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, Av1FrameHeader syntax, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var planeCount = frame.PlaneCount;
        Span<long> best = stackalloc long[3];
        Span<long> error = stackalloc long[3];
        Span<int> value = stackalloc int[3];

        // The loop filter: one level for both directions of the luma plane, and one for each chroma plane
        Save(frame, workspace);
        MeasureError(frame, workspace, best);
        value.Clear();
        foreach (var level in LoopFilterLevels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            header.LoopFilterLevel[0] = header.LoopFilterLevel[1] = header.LoopFilterLevel[2] = header.LoopFilterLevel[3] = level;
            postFilter.Apply(scope, frame, state, Av1InLoopFilters.Deblock, cancellationToken);
            MeasureError(frame, workspace, error);
            Restore(frame, workspace);
            for (var plane = 0; plane < planeCount; plane++)
            {
                if (error[plane] < best[plane])
                {
                    best[plane] = error[plane];
                    value[plane] = level;
                }
            }
        }

        // The chroma levels are only coded when the luma plane is filtered
        if (value[0] is 0)
            value.Clear();

        header.LoopFilterLevel[0] = header.LoopFilterLevel[1] = (byte)value[0];
        header.LoopFilterLevel[2] = (byte)value[1];
        header.LoopFilterLevel[3] = (byte)value[2];
        for (var i = 0; i < 4; i++)
        {
            syntax.LoopFilterLevel[i] = header.LoopFilterLevel[i];
        }

        postFilter.Apply(scope, frame, state, Av1InLoopFilters.Deblock, cancellationToken);
        if (!frame.Sequence.EnableCdef)
            return;

        // CDEF: one pair of strengths for the luma plane and one for the chroma planes, for the whole frame. The primary
        // strengths first, then the secondary strengths that go with them
        Save(frame, workspace);
        MeasureError(frame, workspace, best);
        best[1] += best[2];
        var lumaPrimary = 0;
        var chromaPrimary = 0;
        header.CdefBits = 0;
        header.CdefYSecStrength[0] = header.CdefUvSecStrength[0] = 0;
        foreach (var strength in CdefPrimaryStrengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            header.CdefYPriStrength[0] = header.CdefUvPriStrength[0] = strength;
            postFilter.Apply(scope, frame, state, Av1InLoopFilters.Cdef, cancellationToken);
            MeasureError(frame, workspace, error);
            Restore(frame, workspace);
            if (error[0] < best[0])
            {
                best[0] = error[0];
                lumaPrimary = strength;
            }

            if (planeCount > 1 && error[1] + error[2] < best[1])
            {
                best[1] = error[1] + error[2];
                chromaPrimary = strength;
            }
        }

        var lumaSecondary = 0;
        var chromaSecondary = 0;
        header.CdefYPriStrength[0] = (byte)lumaPrimary;
        header.CdefUvPriStrength[0] = (byte)chromaPrimary;
        foreach (var strength in CdefSecondaryStrengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            header.CdefYSecStrength[0] = header.CdefUvSecStrength[0] = strength;
            postFilter.Apply(scope, frame, state, Av1InLoopFilters.Cdef, cancellationToken);
            MeasureError(frame, workspace, error);
            Restore(frame, workspace);
            if (error[0] < best[0])
            {
                best[0] = error[0];
                lumaSecondary = strength;
            }

            if (planeCount > 1 && error[1] + error[2] < best[1])
            {
                best[1] = error[1] + error[2];
                chromaSecondary = strength;
            }
        }

        header.CdefYSecStrength[0] = (byte)lumaSecondary;
        header.CdefUvSecStrength[0] = (byte)chromaSecondary;
        syntax.CdefDamping = header.CdefDamping;
        syntax.CdefBits = 0;
        syntax.CdefYPriStrength[0] = header.CdefYPriStrength[0];
        syntax.CdefYSecStrength[0] = header.CdefYSecStrength[0];
        syntax.CdefUvPriStrength[0] = header.CdefUvPriStrength[0];
        syntax.CdefUvSecStrength[0] = header.CdefUvSecStrength[0];
        if (lumaPrimary is not 0 || lumaSecondary is not 0 || chromaPrimary is not 0 || chromaSecondary is not 0)
            postFilter.Apply(scope, frame, state, Av1InLoopFilters.Cdef, cancellationToken);
    }

    private static void Save(Av1FrameBuffer frame, Av1EncoderWorkspace workspace)
    {
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            frame.GetPlane(plane).Samples.CopyTo(workspace.GetSavedPlane(plane).Samples);
        }
    }

    private static void Restore(Av1FrameBuffer frame, Av1EncoderWorkspace workspace)
    {
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            workspace.GetSavedPlane(plane).Samples.CopyTo(frame.GetPlane(plane).Samples);
        }
    }

    // The squared error of the samples of each plane that are part of the frame
    private static void MeasureError(Av1FrameBuffer frame, Av1EncoderWorkspace workspace, Span<long> error)
    {
        error.Clear();
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var reconstructed = frame.GetPlane(plane);
            var source = workspace.GetSource(plane);
            long sum = 0;
            for (var y = 0; y < reconstructed.Height; y++)
            {
                var actual = reconstructed.GetRow(y);
                var expected = source.GetRow(y);
                for (var x = 0; x < actual.Length; x++)
                {
                    var difference = expected[x] - actual[x];
                    sum += difference * difference;
                }
            }

            error[plane] = sum;
        }
    }
}
