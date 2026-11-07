using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Where the samples of one channel of the image come from.</summary>
/// <param name="Component">The component of the codestream.</param>
/// <param name="PaletteColumn">The column of the palette the samples of the component index, or -1 when the samples are those of the component.</param>
/// <param name="Precision">The number of bits of the samples of the channel.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000Channel(int Component, int PaletteColumn, int Precision);
