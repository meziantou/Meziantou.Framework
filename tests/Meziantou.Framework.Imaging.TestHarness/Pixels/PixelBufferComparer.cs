namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// Compares raw pixel buffers under a <see cref="ComparisonPolicy"/> and explains failures: fixture/frame context,
/// coordinates, channel, expected and actual samples (full 16-bit detail), violation count, maximum and mean error, and
/// hints for typical defects (channel swaps, flips, rotations, alpha loss, byte-swapped or 8-bit-reduced 16-bit samples).
/// </summary>
public static class PixelBufferComparer
{
    /// <summary>The default number of violating samples listed in diagnostics.</summary>
    public const int DefaultMaxReportedMismatches = 16;

    /// <summary>Compares two buffers.</summary>
    /// <param name="expected">The reference.</param>
    /// <param name="actual">The actual pixels.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="context">The context used in messages (e.g. <c>fixture 'png/a', frame 0</c>).</param>
    /// <param name="maxReportedMismatches">The maximum number of listed violating samples.</param>
    /// <returns>The result.</returns>
    public static PixelComparisonResult Compare(RawPixelBuffer expected, RawPixelBuffer actual, ComparisonPolicy policy, string? context = null, int maxReportedMismatches = DefaultMaxReportedMismatches)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(policy);
        context ??= "pixels";
        if (actual.Layout != expected.Layout)
            return Structural(expected, policy, context, $"Layout mismatch: expected {expected.Layout}, actual {actual.Layout}. Request the reference in the layout of the decoded pixels (or add that layout to the manifest).", []);

        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            var message = string.Create(CultureInfo.InvariantCulture, $"Dimension mismatch: expected {expected.Width}x{expected.Height}, actual {actual.Width}x{actual.Height}.");
            return Structural(expected, policy, context, message, FindHints(expected, actual, policy));
        }

        var result = Measure(expected, actual, policy, context, maxReportedMismatches);
        if (result.IsMatch)
            return result;

        return new PixelComparisonResult
        {
            Context = result.Context,
            Policy = result.Policy,
            Layout = result.Layout,
            Width = result.Width,
            Height = result.Height,
            SampleCount = result.SampleCount,
            DifferingSampleCount = result.DifferingSampleCount,
            ViolatingSampleCount = result.ViolatingSampleCount,
            AlphaMismatchCount = result.AlphaMismatchCount,
            MaxAbsoluteError = result.MaxAbsoluteError,
            MaxErrorSample = result.MaxErrorSample,
            MeanAbsoluteError = result.MeanAbsoluteError,
            MaxErrorPerChannel = result.MaxErrorPerChannel,
            Mismatches = result.Mismatches,
            Failures = result.Failures,
            Hints = FindHints(expected, actual, policy),
        };
    }

    /// <summary>Compares a reference with raw actual bytes, reporting truncated or oversized buffers precisely.</summary>
    /// <param name="expected">The reference.</param>
    /// <param name="actual">The actual bytes, tightly packed in the reference layout.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="context">The context used in messages.</param>
    /// <param name="maxReportedMismatches">The maximum number of listed violating samples.</param>
    /// <returns>The result.</returns>
    public static PixelComparisonResult CompareBytes(RawPixelBuffer expected, ReadOnlySpan<byte> actual, ComparisonPolicy policy, string? context = null, int maxReportedMismatches = DefaultMaxReportedMismatches)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(policy);
        context ??= "pixels";
        if (actual.Length != expected.ByteLength)
            return Structural(expected, policy, context, RawPixelBuffer.DescribeLengthMismatch(expected.Width, expected.Height, expected.Layout, actual.Length), []);

        return Compare(expected, RawPixelBuffer.Create(expected.Width, expected.Height, expected.Layout, actual), policy, context, maxReportedMismatches);
    }

    /// <summary>Gets a value indicating whether two buffers match under a policy (no diagnostics).</summary>
    /// <param name="expected">The reference.</param>
    /// <param name="actual">The actual pixels.</param>
    /// <param name="policy">The policy.</param>
    /// <returns><see langword="true"/> if the policy is satisfied.</returns>
    public static bool Matches(RawPixelBuffer expected, RawPixelBuffer actual, ComparisonPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(policy);
        return actual.Layout == expected.Layout && actual.Width == expected.Width && actual.Height == expected.Height
            && Measure(expected, actual, policy, "pixels", maxReportedMismatches: 0).IsMatch;
    }

    private static PixelComparisonResult Structural(RawPixelBuffer expected, ComparisonPolicy policy, string context, string message, IReadOnlyList<string> hints) => new()
    {
        Context = context,
        Policy = policy,
        Layout = expected.Layout,
        Width = expected.Width,
        Height = expected.Height,
        StructuralError = message,
        Hints = hints,
    };

    private static PixelComparisonResult Measure(RawPixelBuffer expected, RawPixelBuffer actual, ComparisonPolicy policy, string context, int maxReportedMismatches)
    {
        var layout = expected.Layout;
        var channels = layout.ChannelCount;
        var alpha = layout.AlphaChannel;
        var allowed = policy.IsExact ? 0 : policy.GetMaxAbsoluteError(layout);
        var perChannel = new int[channels];
        var mismatches = new List<SampleMismatch>();
        long differing = 0, violating = 0, alphaMismatches = 0, colorSamples = 0;
        double colorErrorSum = 0;
        var maxError = 0;
        SampleMismatch? maxSample = null;
        var samples = expected.SampleCount;
        for (var i = 0; i < samples; i++)
        {
            var e = expected.GetSampleAt(i);
            var a = actual.GetSampleAt(i);
            var error = Math.Abs(e - a);
            var channel = i % channels;
            var isAlpha = channel == alpha;
            if (!isAlpha)
            {
                colorSamples++;
                colorErrorSum += error;
            }

            if (error == 0)
                continue;

            differing++;
            perChannel[channel] = Math.Max(perChannel[channel], error);
            var pixel = i / channels;
            if (error > maxError)
            {
                maxError = error;
                maxSample = new SampleMismatch(pixel % expected.Width, pixel / expected.Width, channel, layout.GetChannelName(channel), e, a);
            }

            if (isAlpha)
            {
                alphaMismatches++;
            }

            if (isAlpha || error > allowed)
            {
                violating++;
                if (mismatches.Count < maxReportedMismatches)
                {
                    mismatches.Add(new SampleMismatch(pixel % expected.Width, pixel / expected.Width, channel, layout.GetChannelName(channel), e, a));
                }
            }
        }

        var mean = colorSamples == 0 ? 0 : colorErrorSum / colorSamples;
        var failures = new List<string>();
        if (policy.IsExact && differing > 0)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Exact comparison: {differing} samples differ."));
        }
        else if (!policy.IsExact)
        {
            if (alphaMismatches > 0)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{alphaMismatches} alpha samples differ; alpha is always compared exactly (tolerances never apply to alpha)."));
            }

            if (violating - alphaMismatches > 0)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{violating - alphaMismatches} color samples exceed the maximum tolerated error {allowed}."));
            }

            if (mean > policy.GetMaxMeanAbsoluteError(layout))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"Mean color error {mean:0.####} exceeds the tolerated mean {policy.GetMaxMeanAbsoluteError(layout):0.####}."));
            }
        }

        return new PixelComparisonResult
        {
            Context = context,
            Policy = policy,
            Layout = layout,
            Width = expected.Width,
            Height = expected.Height,
            SampleCount = samples,
            DifferingSampleCount = differing,
            ViolatingSampleCount = violating,
            AlphaMismatchCount = alphaMismatches,
            MaxAbsoluteError = maxError,
            MaxErrorSample = maxSample,
            MeanAbsoluteError = mean,
            MaxErrorPerChannel = perChannel,
            Mismatches = mismatches,
            Failures = failures,
        };
    }

    private static List<string> FindHints(RawPixelBuffer expected, RawPixelBuffer actual, ComparisonPolicy policy)
    {
        var hints = new List<string>();
        var candidates = new List<(string Description, Func<RawPixelBuffer> Transform)>();
        var layout = expected.Layout;
        if (actual.Width == expected.Height && actual.Height == expected.Width)
        {
            candidates.Add(("the actual pixels equal the expected pixels rotated 90 degrees clockwise (orientation applied or rows/columns exchanged)", expected.Rotate90Clockwise));
            candidates.Add(("the actual pixels equal the expected pixels rotated 90 degrees counterclockwise (orientation applied or rows/columns exchanged)", expected.Rotate90CounterClockwise));
            candidates.Add(("the actual pixels equal the transposed expected pixels (x and y exchanged)", expected.Transpose));
        }

        if (actual.Width == expected.Width && actual.Height == expected.Height)
        {
            if (layout.IsColor)
            {
                candidates.Add(("the actual pixels equal the expected pixels with channels R and B swapped (BGR(A) order)", () => expected.SwapChannels(0, 2)));
                candidates.Add(("the actual pixels equal the expected pixels with channels R and G swapped", () => expected.SwapChannels(0, 1)));
                candidates.Add(("the actual pixels equal the expected pixels with channels G and B swapped", () => expected.SwapChannels(1, 2)));
            }

            candidates.Add(("the actual pixels equal the vertically flipped expected pixels (bottom-up row order?)", expected.FlipVertical));
            candidates.Add(("the actual pixels equal the horizontally flipped expected pixels", expected.FlipHorizontal));
            candidates.Add(("the actual pixels equal the expected pixels rotated 180 degrees", expected.Rotate180));
            if (expected.Width == expected.Height)
            {
                candidates.Add(("the actual pixels equal the expected pixels rotated 90 degrees clockwise", expected.Rotate90Clockwise));
                candidates.Add(("the actual pixels equal the expected pixels rotated 90 degrees counterclockwise", expected.Rotate90CounterClockwise));
                candidates.Add(("the actual pixels equal the transposed expected pixels (x and y exchanged)", expected.Transpose));
            }

            if (layout.HasAlpha)
            {
                candidates.Add(("alpha was lost: the actual pixels equal the expected pixels with alpha forced to fully opaque", () => expected.WithChannel(layout.AlphaChannel, layout.MaxSampleValue)));
            }

            if (layout.BytesPerSample == 2)
            {
                candidates.Add(("16-bit samples appear byte-swapped (big-endian instead of little-endian)", () => Map(expected, value => ((value & 0xFF) << 8) | (value >> 8))));
                candidates.Add(("16-bit samples were reduced to 8 bits (low byte lost: value = (v >> 8) * 257)", () => Map(expected, value => (value >> 8) * 257)));
            }
        }

        foreach (var (description, transform) in candidates)
        {
            var candidate = transform();
            if (candidate.Width == actual.Width && candidate.Height == actual.Height && Measure(candidate, actual, policy, "hint", 0).IsMatch)
            {
                hints.Add(description + ".");
            }
        }

        if (hints.Count == 0 && actual.Width == expected.Width && actual.Height == expected.Height && layout.HasAlpha)
        {
            var colorOnly = Measure(expected.WithChannel(layout.AlphaChannel, 0), actual.WithChannel(layout.AlphaChannel, 0), policy, "hint", 0);
            if (colorOnly.IsMatch)
            {
                hints.Add("only alpha samples differ (premultiplication, alpha loss or a wrong transparency key?).");
            }
            else if (Measure(expected.WithTransparentColorsCleared(), actual.WithTransparentColorsCleared(), policy, "hint", 0).IsMatch)
            {
                hints.Add("only the color of fully transparent pixels differs: hidden colors are defined and compared exactly.");
            }
        }

        return hints;
    }

    private static RawPixelBuffer Map(RawPixelBuffer buffer, Func<int, int> map)
    {
        var data = new byte[buffer.ByteLength];
        for (var i = 0; i < buffer.SampleCount; i++)
        {
            RawPixelBuffer.SetSampleAt(data, buffer.Layout, i, map(buffer.GetSampleAt(i)));
        }

        return RawPixelBuffer.Create(buffer.Width, buffer.Height, buffer.Layout, data);
    }
}
