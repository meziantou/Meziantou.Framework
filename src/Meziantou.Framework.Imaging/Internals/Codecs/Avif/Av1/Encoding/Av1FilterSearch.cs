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

    private static ReadOnlySpan<byte> FastLoopFilterLevels => [2, 4, 8, 16, 32];

    private static ReadOnlySpan<byte> CdefPrimaryStrengths => [1, 2, 3, 4, 6, 8, 10, 12, 15];

    private static ReadOnlySpan<byte> FastCdefPrimaryStrengths => [2, 4, 8, 12];

    private static ReadOnlySpan<byte> CdefSecondaryStrengths => [1, 2, 4];

    private static ReadOnlySpan<byte> FastCdefSecondaryStrengths => [2];

    private enum Stage
    {
        LoopFilter,
        CdefPrimary,
        CdefSecondary,
    }

    /// <summary>Chooses the parameters of the filters of <paramref name="frame"/> and applies them.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="postFilter">The filters of the decoder.</param>
    /// <param name="frame">The reconstructed frame, before any filter; its header receives the parameters.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="workspace">The source planes, and the planes that save the frame between candidates.</param>
    /// <param name="syntax">The syntax elements of the header to write, which receive the parameters too.</param>
    /// <param name="thoroughness">
    /// 0 to search a few values, 1 to search every value from the middle one by halving a step (the error is assumed to
    /// have one minimum), 2 to evaluate every value.
    /// </param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    public static void Run(AllocationScope scope, Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, Av1FrameHeader syntax, int thoroughness, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var search = new Search(scope, postFilter, frame, state, workspace, thoroughness >= 2, cancellationToken);
        Span<long> best = stackalloc long[3];
        Span<int> value = stackalloc int[3];

        // The loop filter: one level for both directions of the luma plane, and one for each chroma plane
        Save(frame, workspace);
        MeasureError(frame, workspace, best);
        value.Clear();
        search.Run(Stage.LoopFilter, thoroughness is 0 ? FastLoopFilterLevels : LoopFilterLevels, best, value);

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

        // The loop restoration reads rows of the frame as it is now, around the boundaries of its stripes
        if (header.UsesLr)
            postFilter.SaveRestorationBoundaries(scope, frame);

        if (!frame.Sequence.EnableCdef)
            return;

        // CDEF: one pair of strengths for the luma plane and one for the chroma planes, for the whole frame. The primary
        // strengths first, then the secondary strengths that go with them
        Save(frame, workspace);
        MeasureError(frame, workspace, best);
        best[1] += best[2];
        header.CdefBits = 0;
        header.CdefYPriStrength[0] = header.CdefUvPriStrength[0] = 0;
        header.CdefYSecStrength[0] = header.CdefUvSecStrength[0] = 0;
        value.Clear();
        search.Run(Stage.CdefPrimary, thoroughness is 0 ? FastCdefPrimaryStrengths : CdefPrimaryStrengths, best, value);
        var lumaPrimary = value[0];
        var chromaPrimary = value[1];
        header.CdefYPriStrength[0] = (byte)lumaPrimary;
        header.CdefUvPriStrength[0] = (byte)chromaPrimary;
        value.Clear();
        search.Run(Stage.CdefSecondary, thoroughness is 0 ? FastCdefSecondaryStrengths : CdefSecondaryStrengths, best, value);
        var lumaSecondary = value[0];
        var chromaSecondary = value[1];
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

    // The search of the value of a parameter. A pass of the filter gives the error of a value for every plane: the search
    // follows the error of the luma plane, and the chroma planes take the best of the values that were evaluated
    private readonly struct Search(AllocationScope scope, Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, bool exhaustive, CancellationToken cancellationToken)
    {
        // The largest number of values of a parameter
        private const int MaxValues = 16;

        /// <summary>
        /// Evaluates values of the parameter of <paramref name="stage"/>. <paramref name="best"/> holds the error of each
        /// plane (or of the luma plane then of both chroma planes, for CDEF) without the filter, and receives the error of
        /// the value of <paramref name="chosen"/>, which stays 0 when no value lowers the error.
        /// </summary>
        public void Run(Stage stage, ReadOnlySpan<byte> values, Span<long> best, Span<int> chosen)
        {
            Span<long> errors = stackalloc long[MaxValues];
            Span<bool> evaluated = stackalloc bool[MaxValues];
            if (exhaustive || values.Length <= 2)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    Evaluate(stage, values, i, errors, evaluated, best, chosen);
                }

                return;
            }

            // From the middle value, towards the neighbor with the smallest error: first by a quarter of the values, then
            // by half as many, and by one value until no neighbor is better
            var center = values.Length / 2;
            Evaluate(stage, values, center, errors, evaluated, best, chosen);
            for (var step = Math.Max(1, (values.Length + 1) / 4); step >= 1; step /= 2)
            {
                bool moved;
                do
                {
                    moved = false;
                    var low = Math.Max(0, center - step);
                    var high = Math.Min(values.Length - 1, center + step);
                    Evaluate(stage, values, low, errors, evaluated, best, chosen);
                    Evaluate(stage, values, high, errors, evaluated, best, chosen);
                    var next = errors[low] < errors[center] ? low : center;
                    if (errors[high] < errors[next])
                        next = high;

                    if (next != center)
                    {
                        center = next;
                        moved = true;
                    }
                }
                while (moved && step is 1);
            }
        }

        private void Evaluate(Stage stage, ReadOnlySpan<byte> values, int index, Span<long> errors, Span<bool> evaluated, Span<long> best, Span<int> chosen)
        {
            if (evaluated[index])
                return;

            cancellationToken.ThrowIfCancellationRequested();
            var header = frame.Header;
            var value = values[index];
            switch (stage)
            {
                case Stage.LoopFilter:
                    header.LoopFilterLevel[0] = header.LoopFilterLevel[1] = header.LoopFilterLevel[2] = header.LoopFilterLevel[3] = value;
                    break;
                case Stage.CdefPrimary:
                    header.CdefYPriStrength[0] = header.CdefUvPriStrength[0] = value;
                    break;
                default:
                    header.CdefYSecStrength[0] = header.CdefUvSecStrength[0] = value;
                    break;
            }

            Span<long> error = stackalloc long[3];
            postFilter.Apply(scope, frame, state, stage is Stage.LoopFilter ? Av1InLoopFilters.Deblock : Av1InLoopFilters.Cdef, cancellationToken);
            MeasureError(frame, workspace, error);
            Restore(frame, workspace);
            if (stage is not Stage.LoopFilter)
            {
                // The strengths of the chroma planes are the same
                error[1] += error[2];
                error[2] = long.MaxValue;
            }

            evaluated[index] = true;
            errors[index] = error[0];
            for (var group = 0; group < frame.PlaneCount; group++)
            {
                if (error[group] < best[group])
                {
                    best[group] = error[group];
                    chosen[group] = value;
                }
            }
        }
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
                sum += Av1EncoderKernels.SumOfSquaredDifferences(source.GetRow(y), reconstructed.GetRow(y));
            }

            error[plane] = sum;
        }
    }
}
