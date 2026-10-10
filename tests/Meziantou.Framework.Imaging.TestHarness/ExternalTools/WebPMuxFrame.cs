namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>One frame line of <c>webpmux -info</c>.</summary>
/// <param name="Width">The frame width.</param>
/// <param name="Height">The frame height.</param>
/// <param name="HasAlpha">Whether webpmux reports alpha for the frame.</param>
/// <param name="X">The x offset.</param>
/// <param name="Y">The y offset.</param>
/// <param name="DurationMilliseconds">The duration.</param>
/// <param name="Dispose">The disposal method (<c>none</c> or <c>background</c>).</param>
/// <param name="Blend">Whether the frame is alpha-blended.</param>
/// <param name="Compression">The compression (<c>lossless</c> or <c>lossy</c>).</param>
public sealed record WebPMuxFrame(int Width, int Height, bool HasAlpha, int X, int Y, int DurationMilliseconds, string Dispose, bool Blend, string Compression);
