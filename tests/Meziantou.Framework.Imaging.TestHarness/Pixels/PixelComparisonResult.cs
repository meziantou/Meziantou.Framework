namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>The outcome of a pixel comparison, with the diagnostics needed to understand a failure.</summary>
public sealed class PixelComparisonResult
{
    internal PixelComparisonResult()
    {
    }

    /// <summary>Gets a value indicating whether the actual pixels satisfy the policy.</summary>
    public bool IsMatch => StructuralError is null && Failures.Count == 0;

    /// <summary>Gets the context (fixture, frame...) used in messages.</summary>
    public required string Context { get; init; }

    /// <summary>Gets the policy.</summary>
    public required ComparisonPolicy Policy { get; init; }

    /// <summary>Gets the expected layout.</summary>
    public required RawPixelLayout Layout { get; init; }

    /// <summary>Gets the expected width.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the expected height.</summary>
    public required int Height { get; init; }

    /// <summary>Gets a structural mismatch (dimensions, layout, buffer length); pixels are not compared when set.</summary>
    public string? StructuralError { get; init; }

    /// <summary>Gets the number of compared samples.</summary>
    public long SampleCount { get; init; }

    /// <summary>Gets the number of samples that differ at all.</summary>
    public long DifferingSampleCount { get; init; }

    /// <summary>Gets the number of samples that violate the policy (any difference for exact policies and alpha samples).</summary>
    public long ViolatingSampleCount { get; init; }

    /// <summary>Gets the number of differing alpha samples (always a violation).</summary>
    public long AlphaMismatchCount { get; init; }

    /// <summary>Gets the maximum absolute error over all samples.</summary>
    public int MaxAbsoluteError { get; init; }

    /// <summary>Gets the location of the first sample with the maximum error.</summary>
    public SampleMismatch? MaxErrorSample { get; init; }

    /// <summary>Gets the mean absolute error over color samples (every sample for layouts without alpha).</summary>
    public double MeanAbsoluteError { get; init; }

    /// <summary>Gets the maximum absolute error of each channel.</summary>
    public IReadOnlyList<int> MaxErrorPerChannel { get; init; } = [];

    /// <summary>Gets the first violating samples (bounded list).</summary>
    public IReadOnlyList<SampleMismatch> Mismatches { get; init; } = [];

    /// <summary>Gets the reasons of the failure.</summary>
    public IReadOnlyList<string> Failures { get; init; } = [];

    /// <summary>Gets likely explanations (channel swap, flip, rotation, alpha loss, byte swap...).</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Formats a complete diagnostic message.</summary>
    /// <returns>The message.</returns>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Context} ({Width}x{Height} {Layout}, policy: {Policy}): ");
        if (IsMatch)
        {
            sb.Append(CultureInfo.InvariantCulture, $"match ({DifferingSampleCount} of {SampleCount} samples differ, max error {MaxAbsoluteError}, mean color error {MeanAbsoluteError:0.####}).");
            return sb.ToString();
        }

        if (StructuralError is not null)
        {
            sb.Append(StructuralError);
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"{ViolatingSampleCount} of {SampleCount} samples violate the policy ({DifferingSampleCount} differ, {AlphaMismatchCount} alpha); max error {MaxAbsoluteError}");
            if (MaxErrorSample is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" at ({MaxErrorSample.X},{MaxErrorSample.Y}) channel {MaxErrorSample.ChannelName}");
            }

            sb.Append(CultureInfo.InvariantCulture, $"; mean color error {MeanAbsoluteError:0.####}.");
            sb.AppendLine();
            sb.Append("Per-channel max error:");
            for (var c = 0; c < MaxErrorPerChannel.Count; c++)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {Layout.GetChannelName(c)}={MaxErrorPerChannel[c]}");
            }

            foreach (var failure in Failures)
            {
                sb.AppendLine().Append("- ").Append(failure);
            }

            if (Mismatches.Count > 0)
            {
                sb.AppendLine().Append(CultureInfo.InvariantCulture, $"First {Mismatches.Count} violating samples:");
                foreach (var mismatch in Mismatches)
                {
                    sb.AppendLine().Append("  ").Append(mismatch.Format(Layout));
                }
            }
        }

        foreach (var hint in Hints)
        {
            sb.AppendLine().Append("Hint: ").Append(hint);
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
