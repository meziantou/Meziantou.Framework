namespace Meziantou.Framework.Imaging.TestHarness.Dng;

#pragma warning disable CA1819 // Properties should not return arrays

/// <summary>The rendering of <see cref="ReferenceDng"/>: the RGBA floats of the default crop, and stage 2 (the linear values of the active area).</summary>
/// <param name="Width">The width of the default crop.</param>
/// <param name="Height">The height of the default crop.</param>
/// <param name="Rgba">The pixels: linear BT.709 RGB and an alpha of 1.</param>
/// <param name="ActiveWidth">The width of the active area.</param>
/// <param name="ActiveHeight">The height of the active area.</param>
/// <param name="SamplesPerPixel">The samples of a pixel of stage 2.</param>
/// <param name="Stage2">The linear values of the active area.</param>
public sealed record DngRendering(int Width, int Height, float[] Rgba, int ActiveWidth, int ActiveHeight, int SamplesPerPixel, float[] Stage2);
