namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A scan of a JPEG file.</summary>
/// <param name="SpectralStart">The first coefficient of the scan, in zig-zag order.</param>
/// <param name="SpectralEnd">The last coefficient of the scan.</param>
/// <param name="SuccessiveHigh">The position of the bit the previous scan of these coefficients ended at.</param>
/// <param name="SuccessiveLow">The position of the lowest bit the scan codes.</param>
internal sealed record JpegBitstreamScan(int SpectralStart, int SpectralEnd, int SuccessiveHigh, int SuccessiveLow)
{
    /// <summary>Gets the components of the scan.</summary>
    public List<JpegBitstreamScanComponent> Components { get; } = [];

    /// <summary>Gets the blocks, in the order of the scan, before which a run of end-of-band symbols is written although it could grow.</summary>
    public List<int> ResetPoints { get; } = [];

    /// <summary>Gets the blocks, in the order of the scan, that end with runs of 16 zeros that an encoder does not need to write, and their number.</summary>
    public List<(int Block, int Count)> ExtraZeroRuns { get; } = [];
}
