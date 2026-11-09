namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Options of <see cref="JpegLsTestImage.Encode"/>.</summary>
internal sealed class JpegLsTestOptions
{
    /// <summary>Gets the NEAR parameter: the largest difference between a sample and its reconstruction (0: lossless).</summary>
    public int Near { get; init; }

    /// <summary>Gets the interleave mode of the components: 0 (a scan per component), 1 (lines) or 2 (samples).</summary>
    public int Interleave { get; init; }

    /// <summary>Gets the preset coding parameters written in an LSE segment (zero for a default), or <see langword="null"/> for none.</summary>
    public (int MaxValue, int T1, int T2, int T3, int Reset)? Preset { get; init; }

    /// <summary>Gets the restart interval in lines (of a component, of MCUs or of samples, depending on the interleave mode), or 0.</summary>
    public int RestartInterval { get; init; }

    /// <summary>Gets the HP color transform written in an APP8 <c>mrfx</c> segment (1, 2 or 3; 8-bit RGB samples), or 0.</summary>
    public int ColorTransform { get; init; }
}
