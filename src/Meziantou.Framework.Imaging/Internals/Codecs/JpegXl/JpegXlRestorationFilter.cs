namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <c>RestorationFilter</c> bundle of a frame header (ISO/IEC 18181-1, section C.2.7): the parameters of the Gabor-like
/// filter and of the edge-preserving filter applied to the decoded frame.
/// </summary>
internal sealed class JpegXlRestorationFilter
{
    private static readonly float[] DefaultGaborWeights =
    [
        1.1f * 0.104699568f, 1.1f * 0.055680538f,
        1.1f * 0.104699568f, 1.1f * 0.055680538f,
        1.1f * 0.104699568f, 1.1f * 0.055680538f,
    ];

    private static readonly float[] DefaultSharpnessLut = [0f, 1f / 7, 2f / 7, 3f / 7, 4f / 7, 5f / 7, 6f / 7, 1f];

    private static readonly float[] DefaultChannelScale = [40.0f, 5.0f, 3.5f];

    private JpegXlRestorationFilter()
    {
    }

    /// <summary>Gets the filter of a header that only sets <c>all_default</c>.</summary>
    public static JpegXlRestorationFilter Default { get; } = new();

    /// <summary>Gets a value indicating whether the Gabor-like filter is applied.</summary>
    public bool IsGaborEnabled { get; private init; } = true;

    /// <summary>Gets the weights of the Gabor-like filter: for each of the three channels, the weight of the four adjacent samples then of the four diagonal ones.</summary>
    public float[] GaborWeights { get; private init; } = DefaultGaborWeights;

    /// <summary>Gets the number of iterations of the edge-preserving filter (0 to 3).</summary>
    public int EpfIterations { get; private init; } = 2;

    /// <summary>Gets the eight multipliers of the filter strength, indexed by the sharpness of a block.</summary>
    public float[] EpfSharpnessLut { get; private init; } = DefaultSharpnessLut;

    /// <summary>Gets the weight of each of the three channels in the distance between samples.</summary>
    public float[] EpfChannelScale { get; private init; } = DefaultChannelScale;

    /// <summary>Gets the multiplier of the quantization step in the filter strength.</summary>
    public float EpfQuantMultiplier { get; private init; } = 0.46f;

    /// <summary>Gets the scale of the filter strength of the first step.</summary>
    public float EpfPass0SigmaScale { get; private init; } = 0.9f;

    /// <summary>Gets the scale of the filter strength of the last step.</summary>
    public float EpfPass2SigmaScale { get; private init; } = 6.5f;

    /// <summary>Gets the multiplier of the distance for the samples on the border of a block.</summary>
    public float EpfBorderSadMultiplier { get; private init; } = 2f / 3;

    /// <summary>Gets the filter strength of a frame coded with the Modular mode.</summary>
    public float EpfSigmaForModular { get; private init; } = 1.0f;

    public static JpegXlRestorationFilter Read(JpegXlBitReader reader, bool isModular)
    {
        if (reader.ReadBool())
            return Default;

        var gaborEnabled = reader.ReadBool();
        var gaborWeights = DefaultGaborWeights;
        if (gaborEnabled && reader.ReadBool())
        {
            gaborWeights = new float[6];
            for (var i = 0; i < gaborWeights.Length; i++)
            {
                gaborWeights[i] = reader.ReadF16();
            }

            for (var channel = 0; channel < 3; channel++)
            {
                if (MathF.Abs(1.0f + ((gaborWeights[channel * 2] + gaborWeights[(channel * 2) + 1]) * 4)) < 1e-8f)
                    throw JpegXlFormat.Invalid("the weights of the Gabor-like filter cannot be normalized.");
            }
        }

        var epfIterations = (int)reader.ReadBits(2);
        var sharpnessLut = DefaultSharpnessLut;
        var channelScale = DefaultChannelScale;
        var quantMultiplier = 0.46f;
        var pass0SigmaScale = 0.9f;
        var pass2SigmaScale = 6.5f;
        var borderSadMultiplier = 2f / 3;
        var sigmaForModular = 1.0f;
        if (epfIterations > 0)
        {
            if (!isModular && reader.ReadBool())
            {
                sharpnessLut = new float[8];
                for (var i = 0; i < sharpnessLut.Length; i++)
                {
                    sharpnessLut[i] = reader.ReadF16();
                }
            }

            if (reader.ReadBool())
            {
                channelScale = [reader.ReadF16(), reader.ReadF16(), reader.ReadF16()];

                // Two fields that no longer have a meaning
                _ = reader.ReadBits(32);
            }

            if (reader.ReadBool())
            {
                if (!isModular)
                {
                    quantMultiplier = reader.ReadF16();
                }

                pass0SigmaScale = reader.ReadF16();
                pass2SigmaScale = reader.ReadF16();
                borderSadMultiplier = reader.ReadF16();
            }

            if (isModular)
            {
                sigmaForModular = reader.ReadF16();
                if (sigmaForModular < 1e-8f)
                    throw JpegXlFormat.Invalid("the strength of the edge-preserving filter is too small.");
            }
        }

        JpegXlImageHeader.SkipExtensions(reader);
        return new JpegXlRestorationFilter
        {
            IsGaborEnabled = gaborEnabled,
            GaborWeights = gaborWeights,
            EpfIterations = epfIterations,
            EpfSharpnessLut = sharpnessLut,
            EpfChannelScale = channelScale,
            EpfQuantMultiplier = quantMultiplier,
            EpfPass0SigmaScale = pass0SigmaScale,
            EpfPass2SigmaScale = pass2SigmaScale,
            EpfBorderSadMultiplier = borderSadMultiplier,
            EpfSigmaForModular = sigmaForModular,
        };
    }
}
