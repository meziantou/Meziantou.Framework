namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>The pixels of an image printed by <c>oiiotool --dumpdata</c> (<see cref="OpenImageIOTool.DumpAsync"/>).</summary>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Channels">The number of samples of a pixel.</param>
/// <param name="SampleType">The type of a sample as OpenImageIO names it: <c>uint1</c>, <c>uint8</c>, <c>uint16</c>, <c>float</c>, ...</param>
/// <param name="Samples">The samples of the pixels, the rows from the top: integers as they are stored, or floating-point values.</param>
public sealed record OpenImageIOPixels(int Width, int Height, int Channels, string SampleType, IReadOnlyList<double> Samples);
