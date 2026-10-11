using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Consecutive glyphs of a layout that have the same font, size and paint.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphRun(int Font, double Size, int Brush, int Start, int Count);
