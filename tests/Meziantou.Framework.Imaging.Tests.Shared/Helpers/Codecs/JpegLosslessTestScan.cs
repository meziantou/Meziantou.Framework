namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>A scan of a lossless frame written by <see cref="JpegLosslessTestImage.Encode"/> (ITU-T T.81 B.2.3 scan parameters).</summary>
/// <param name="Components">The component indices of the scan.</param>
/// <param name="Predictor">The predictor the scan selects (<c>Ss</c>), 1 to 7; written as given, even when it is not valid.</param>
/// <param name="PointTransform">The point transform of the scan (<c>Al</c>).</param>
internal sealed record JpegLosslessTestScan(int[] Components, int Predictor, int PointTransform = 0);
