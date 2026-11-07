namespace Meziantou.Framework.Imaging.TestHarness.Exr;

#pragma warning disable CA1819 // Properties should not return arrays
/// <summary>A chunk of an OpenEXR file.</summary>
/// <param name="Coordinates">The y coordinate of a scan-line chunk, or the tile and level coordinates of a tile.</param>
/// <param name="Data">The stored data.</param>
public sealed record ExrChunk(IReadOnlyList<int> Coordinates, byte[] Data);
