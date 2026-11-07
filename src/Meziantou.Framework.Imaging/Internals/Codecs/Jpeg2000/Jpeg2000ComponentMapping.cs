using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One channel of a <c>cmap</c> box (T.800, I.5.3.5): a component, used as it is or through a column of the palette.</summary>
/// <param name="Component">The component (CMP).</param>
/// <param name="UsesPalette">Whether the samples of the component are indices in the palette (MTYP is 1).</param>
/// <param name="PaletteColumn">The column of the palette (PCOL).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000ComponentMapping(int Component, bool UsesPalette, int PaletteColumn);
