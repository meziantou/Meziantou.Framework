namespace Meziantou.Framework.Imaging.InteropTests.JpegXlConformance;

/// <summary>The largest errors a test case of the conformance corpus accepts for one frame, on samples where 1 is the largest value.</summary>
/// <param name="Peak">The largest absolute error of a sample.</param>
/// <param name="Rms">The largest root mean square error of a channel.</param>
internal sealed record JpegXlConformanceTolerance(double Peak, double Rms);
