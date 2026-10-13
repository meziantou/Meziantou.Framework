namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the header of a frame written by the encoder tells about its coding: every frame is a regular frame that covers
/// the image and replaces it.
/// </summary>
internal sealed class JpegXlFrameSettings
{
    private static readonly int[] SinglePass = [0];

    /// <summary>Gets the settings of a lossless frame: Modular, one pass, no restoration filter.</summary>
    public static JpegXlFrameSettings Lossless { get; } = new() { IsModular = true };

    /// <summary>Gets a value indicating whether the frame is coded with Modular; otherwise with VarDCT.</summary>
    public bool IsModular { get; init; }

    /// <summary>Gets the flags of the frame.</summary>
    public ulong Flags { get; init; }

    /// <summary>Gets a value indicating whether the color channels of a frame of an image that is not stored in XYB are YCbCr.</summary>
    public bool IsYCbCr { get; init; }

    /// <summary>Gets the subsampling mode of each of the three channels of a YCbCr frame.</summary>
    public (int First, int Second, int Third) ChromaSubsampling { get; init; }

    /// <summary>Gets the scale of the quantization matrices of the X channel of a VarDCT frame stored in XYB.</summary>
    public int XqmScale { get; init; } = 3;

    /// <summary>Gets the scale of the quantization matrices of the B channel of a VarDCT frame stored in XYB.</summary>
    public int BqmScale { get; init; } = 2;

    /// <summary>Gets the number of bits by which the coefficients of each pass are shifted: one entry per pass, the last one 0.</summary>
    public int[] PassShifts { get; init; } = SinglePass;

    /// <summary>Gets the base-2 logarithm of each downsampling factor that a pass completes, in decreasing order.</summary>
    public int[] PassDownsampleShifts { get; init; } = [];

    /// <summary>Gets, for each entry of <see cref="PassDownsampleShifts"/>, the pass that completes it.</summary>
    public int[] PassDownsampleLastPasses { get; init; } = [];

    /// <summary>Gets a value indicating whether the decoder applies the Gabor-like filter, with its default weights.</summary>
    public bool IsGaborEnabled { get; init; }

    /// <summary>Gets the number of iterations of the edge-preserving filter the decoder applies, with its default parameters.</summary>
    public int EpfIterations { get; init; }
}
