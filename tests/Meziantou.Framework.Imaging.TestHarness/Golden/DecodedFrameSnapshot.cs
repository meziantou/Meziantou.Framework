using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>One decoded displayed frame: its full-canvas pixels and exact duration.</summary>
/// <param name="Pixels">The pixels.</param>
/// <param name="Duration">The exact duration.</param>
public sealed record DecodedFrameSnapshot(RawPixelBuffer Pixels, RationalDuration Duration);
