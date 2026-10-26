namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The policy of a conversion from floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) to a pixel
/// format that cannot store all of them: what to do with the samples out of its range
/// (<see cref="PixelConversionOptions.OutOfRangeSamples"/>), and the tone mapping applied before
/// (<see cref="PixelConversionOptions.ToneMapping"/>, with its peak resolved).
/// </summary>
/// <remarks>Samples that are not numbers are rejected whatever the policy. The default policy (<see cref="Strict"/>) rejects every sample out of range.</remarks>
internal sealed class FloatSampleConversion
{
    private readonly HdrToneMapStage? _toneMap;

    private FloatSampleConversion(bool clips, HdrToneMapStage? toneMap)
    {
        Clips = clips;
        _toneMap = toneMap;
    }

    /// <summary>Gets the policy that rejects every sample out of range, without tone mapping.</summary>
    public static FloatSampleConversion Strict { get; } = new(clips: false, toneMap: null);

    /// <summary>Gets the policy that clips every sample out of range, without tone mapping.</summary>
    public static FloatSampleConversion Clipping { get; } = new(clips: true, toneMap: null);

    /// <summary>Gets a value indicating whether the samples out of range are clipped (otherwise they are rejected).</summary>
    public bool Clips { get; }

    /// <summary>Gets a value indicating whether the colors are tone mapped before they are converted to an integer format.</summary>
    public bool ToneMaps => _toneMap is not null;

    /// <summary>Gets a value indicating whether the peak of the tone mapping must be derived from the pixels (no source peak luminance).</summary>
    /// <param name="options">The options.</param>
    public static bool NeedsPeak(PixelConversionOptions? options) => options?.ToneMapping is { SourcePeakLuminance: null };

    /// <summary>Creates the policy of conversion options.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="peak">
    /// The largest finite color component of the converted pixels, used as the peak of the tone mapping when the options do
    /// not give a source peak luminance (<see cref="NeedsPeak"/>); otherwise ignored.
    /// </param>
    /// <returns>The policy.</returns>
    public static FloatSampleConversion Create(PixelConversionOptions? options, double peak = 1)
    {
        var clips = options?.OutOfRangeSamples == OutOfRangeSampleHandling.Clip;
        if (options?.ToneMapping is not { } toneMapping)
            return clips ? Clipping : Strict;

        var white = toneMapping.TargetWhiteLuminance;
        var sourcePeak = toneMapping.SourcePeakLuminance is { } luminance ? luminance / white : peak;
        return new FloatSampleConversion(clips, HdrToneMapStage.CreateForWhiteUnits(toneMapping.Operator, double.IsFinite(sourcePeak) ? sourcePeak : 1, white));
    }

    /// <summary>
    /// Tone maps a color (linear light in units of the white) when the policy does. The colors whose largest component is
    /// not positive, or that have a component that is not a number, are left as they are; an infinite component becomes
    /// the white and the finite components of its color become 0.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public void ToneMap(ref double r, ref double g, ref double b)
    {
        if (_toneMap is null)
            return;

        var x = Math.Max(r, Math.Max(g, b));
        if (!(x > 0) || double.IsNaN(r) || double.IsNaN(g) || double.IsNaN(b))
            return;

        if (double.IsPositiveInfinity(x))
        {
            r = double.IsPositiveInfinity(r) ? 1 : 0;
            g = double.IsPositiveInfinity(g) ? 1 : 0;
            b = double.IsPositiveInfinity(b) ? 1 : 0;
            return;
        }

        Span<double> values = [r, g, b];
        _toneMap.Apply(values);
        (r, g, b) = (values[0], values[1], values[2]);
    }
}
