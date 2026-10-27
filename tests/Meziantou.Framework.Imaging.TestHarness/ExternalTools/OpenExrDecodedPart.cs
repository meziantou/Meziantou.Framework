namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A part of an OpenEXR file decoded by <c>exr-to-raw</c>.</summary>
/// <param name="Compression">The compression the header declares, as OpenEXR names it.</param>
/// <param name="Width">The width of the data window.</param>
/// <param name="Height">The height of the data window.</param>
/// <param name="Channels">The samples of every channel of the data window, row by row, by channel name.</param>
public sealed record OpenExrDecodedPart(string Compression, int Width, int Height, IReadOnlyDictionary<string, float[]> Channels);
