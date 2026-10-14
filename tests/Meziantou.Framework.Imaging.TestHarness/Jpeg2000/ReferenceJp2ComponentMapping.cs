namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>An entry of the component mapping box of a JP2 file (<c>cmap</c>): how a channel is made from a component.</summary>
/// <param name="Component">The index of the component.</param>
/// <param name="IsPaletteMapping">Whether the samples of the component are indexes in a column of the palette.</param>
/// <param name="PaletteColumn">The column of the palette.</param>
public sealed record ReferenceJp2ComponentMapping(int Component, bool IsPaletteMapping, int PaletteColumn);
