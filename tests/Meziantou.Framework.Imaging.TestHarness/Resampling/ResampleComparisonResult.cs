namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The result of comparing resized pixels with a <see cref="ReferenceResizeResult"/>.</summary>
/// <param name="Context">The context (fixture, frame, options).</param>
/// <param name="StructuralError">The size or layout mismatch, if any.</param>
/// <param name="SampleCount">The number of compared samples.</param>
/// <param name="CorrectlyRoundedCount">The number of samples equal to the exact value rounded to nearest with ties upward.</param>
/// <param name="NearTieCount">The number of samples whose exact value is within the tie window of a rounding boundary.</param>
/// <param name="LargestDeviation">The sample farthest from its exact value.</param>
/// <param name="Mismatches">The first rejected samples.</param>
public sealed record ResampleComparisonResult(string Context, string? StructuralError, long SampleCount, long CorrectlyRoundedCount, long NearTieCount, ResampleMismatch? LargestDeviation, IReadOnlyList<ResampleMismatch> Mismatches)
{
    /// <summary>Gets the number of rejected samples.</summary>
    public long ViolationCount { get; init; }

    /// <summary>Gets the diagnostic preview files written on failure.</summary>
    public IReadOnlyList<FullPath> PreviewFiles { get; init; } = [];

    /// <summary>Gets a value indicating whether every sample is accepted.</summary>
    public bool IsMatch => StructuralError is null && ViolationCount == 0;

    /// <summary>Describes the result.</summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(Context).Append(": ");
        if (StructuralError is not null)
            return sb.Append(StructuralError).ToString();

        sb.Append(CultureInfo.InvariantCulture, $"{SampleCount} samples, {CorrectlyRoundedCount} correctly rounded, {NearTieCount} near ties, {ViolationCount} rejected.");
        if (LargestDeviation is not null)
        {
            sb.AppendLine().Append("Largest deviation from the exact value: ").Append(LargestDeviation);
        }

        foreach (var mismatch in Mismatches)
        {
            sb.AppendLine().Append("  ").Append(mismatch);
        }

        foreach (var file in PreviewFiles)
        {
            sb.AppendLine().Append("  preview: ").Append(file.Value);
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
