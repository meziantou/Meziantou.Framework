using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// How decoded samples are compared with a reference. Structural properties (dimensions, layout, buffer length, frame
/// count, poster presence, timing) are always compared exactly, and alpha samples are always compared exactly, whatever
/// the policy: a tolerance can never mask missing frames, alpha loss, truncated buffers or swapped dimensions.
/// </summary>
/// <remarks>
/// <para><b>Exact</b>: every sample must be identical (lossless decoding, 16-bit low bits and hidden colors of transparent
/// pixels included).</para>
/// <para><b>Tolerance</b>: for lossy codecs compared with an independent decoding of the same encoded input. Every color
/// sample error must be at most <see cref="MaxAbsoluteError"/> and the mean color error at most
/// <see cref="MaxMeanAbsoluteError"/> (both on the 8-bit scale, multiplied by 257 for 16-bit layouts). Values are capped by
/// <see cref="AbsoluteErrorCeiling"/> and <see cref="MeanAbsoluteErrorCeiling"/>, a justification is mandatory, and the
/// manifest validator verifies, for each reference, that channel swaps and flips of the reference are rejected.</para>
/// </remarks>
public sealed class ComparisonPolicy
{
    /// <summary>The hard ceiling of <see cref="MaxAbsoluteError"/> (8-bit scale).</summary>
    public const int AbsoluteErrorCeiling = 12;

    /// <summary>The hard ceiling of <see cref="MaxMeanAbsoluteError"/> (8-bit scale).</summary>
    public const double MeanAbsoluteErrorCeiling = 2.0;

    /// <summary>The minimum length of a tolerance justification.</summary>
    public const int MinimumJustificationLength = 40;

    private ComparisonPolicy(bool isExact, int maxAbsoluteError, double maxMeanAbsoluteError, string? justification)
    {
        IsExact = isExact;
        MaxAbsoluteError = maxAbsoluteError;
        MaxMeanAbsoluteError = maxMeanAbsoluteError;
        Justification = justification;
    }

    /// <summary>Gets the exact policy.</summary>
    public static ComparisonPolicy Exact { get; } = new(isExact: true, 0, 0, justification: null);

    /// <summary>Gets a value indicating whether every sample must be identical.</summary>
    public bool IsExact { get; }

    /// <summary>Gets the maximum absolute error of a color sample (8-bit scale).</summary>
    public int MaxAbsoluteError { get; }

    /// <summary>Gets the maximum mean absolute error over color samples (8-bit scale).</summary>
    public double MaxMeanAbsoluteError { get; }

    /// <summary>Gets the justification of a tolerance policy.</summary>
    public string? Justification { get; }

    /// <summary>Creates a tolerance policy.</summary>
    /// <param name="maxAbsoluteError">The maximum absolute color sample error (8-bit scale), 1 to <see cref="AbsoluteErrorCeiling"/>.</param>
    /// <param name="maxMeanAbsoluteError">The maximum mean absolute color error (8-bit scale), 0 to <see cref="MeanAbsoluteErrorCeiling"/>.</param>
    /// <param name="justification">The justification (measured, attributable differences).</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentException">The values exceed the ceilings or the justification is missing.</exception>
    public static ComparisonPolicy Tolerance(int maxAbsoluteError, double maxMeanAbsoluteError, string justification)
    {
        var errors = Validate(new ComparisonPolicyDefinition { Mode = "tolerance", MaxAbsoluteError = maxAbsoluteError, MaxMeanAbsoluteError = maxMeanAbsoluteError, Justification = justification });
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(maxAbsoluteError));

        return new ComparisonPolicy(isExact: false, maxAbsoluteError, maxMeanAbsoluteError, justification);
    }

    /// <summary>Creates a policy from its manifest definition.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentException">The definition is invalid.</exception>
    public static ComparisonPolicy FromDefinition(ComparisonPolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = Validate(definition);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(definition));

        return definition.Mode == "exact" ? Exact : new ComparisonPolicy(isExact: false, definition.MaxAbsoluteError!.Value, definition.MaxMeanAbsoluteError!.Value, definition.Justification);
    }

    /// <summary>Validates a manifest policy definition.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The errors; empty when valid.</returns>
    public static IReadOnlyList<string> Validate(ComparisonPolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        switch (definition.Mode)
        {
            case "exact":
                if (definition.MaxAbsoluteError is not null || definition.MaxMeanAbsoluteError is not null || definition.Justification is not null)
                {
                    errors.Add("An exact policy must not declare tolerances or a justification.");
                }

                break;

            case "tolerance":
                if (definition.MaxAbsoluteError is not { } max || max < 1 || max > AbsoluteErrorCeiling)
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"A tolerance policy requires 1 <= maxAbsoluteError <= {AbsoluteErrorCeiling} (8-bit scale); larger values could mask gross errors."));
                }

                if (definition.MaxMeanAbsoluteError is not { } mean || mean < 0 || mean > MeanAbsoluteErrorCeiling || double.IsNaN(mean))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"A tolerance policy requires 0 <= maxMeanAbsoluteError <= {MeanAbsoluteErrorCeiling} (8-bit scale)."));
                }

                if (string.IsNullOrWhiteSpace(definition.Justification) || definition.Justification.Trim().Length < MinimumJustificationLength)
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"A tolerance policy requires a justification of at least {MinimumJustificationLength} characters describing the measured, attributable differences."));
                }

                break;

            default:
                errors.Add($"Unknown comparison mode '{definition.Mode}' (allowed: exact, tolerance).");
                break;
        }

        return errors;
    }

    /// <summary>
    /// Gets the loosest tolerance justified by a measured disagreement between independent decoders of the same input:
    /// measured max + 1 (at least 2) and 1.5 x measured mean + 0.1 (at least 0.5), rounded up to 0.01. The corpus
    /// generator derives its policies with the same formula.
    /// </summary>
    /// <param name="measuredMaxAbsoluteError">The measured maximum absolute error (8-bit scale).</param>
    /// <param name="measuredMeanAbsoluteError">The measured mean absolute color error (8-bit scale).</param>
    /// <returns>The maximum justified tolerance.</returns>
    public static (int MaxAbsoluteError, double MaxMeanAbsoluteError) GetMaximumJustifiedTolerance(int measuredMaxAbsoluteError, double measuredMeanAbsoluteError)
        => (Math.Max(2, measuredMaxAbsoluteError + 1), Math.Ceiling(Math.Max(0.5, (measuredMeanAbsoluteError * 1.5) + 0.1) * 100) / 100);

    /// <summary>Gets the maximum tolerated absolute color error in the sample scale of a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The scaled maximum.</returns>
    public int GetMaxAbsoluteError(RawPixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return MaxAbsoluteError * layout.ScaleFrom8Bit;
    }

    /// <summary>Gets the maximum tolerated mean absolute color error in the sample scale of a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The scaled maximum.</returns>
    public double GetMaxMeanAbsoluteError(RawPixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return MaxMeanAbsoluteError * layout.ScaleFrom8Bit;
    }

    /// <inheritdoc/>
    public override string ToString()
        => IsExact ? "exact" : string.Create(CultureInfo.InvariantCulture, $"tolerance (color max {MaxAbsoluteError}, color mean {MaxMeanAbsoluteError}, 8-bit scale; alpha exact)");
}
