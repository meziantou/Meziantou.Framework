namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>A scan of a <see cref="ReferenceJpeg"/> file (ITU-T T.81 section B.2.3).</summary>
/// <param name="Components">The components of the scan, in scan order.</param>
/// <param name="SpectralStart">Ss: the first coefficient (zig-zag index) of the scan; 0 for a baseline scan.</param>
/// <param name="SpectralEnd">Se: the last coefficient (zig-zag index) of the scan; 63 for a baseline scan.</param>
/// <param name="SuccessiveHigh">Ah: 0 for the first scan of the coefficients, else the Al of their previous scan.</param>
/// <param name="SuccessiveLow">Al: the bit position the scan stops at.</param>
public sealed record ReferenceJpegScan(IReadOnlyList<ReferenceJpegScanComponent> Components, int SpectralStart, int SpectralEnd, int SuccessiveHigh, int SuccessiveLow);
