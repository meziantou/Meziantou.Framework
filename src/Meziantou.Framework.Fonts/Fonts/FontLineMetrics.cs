using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>The vertical metrics of a line of horizontal text, in font units, as given by <see cref="FontFace.GetLineMetrics"/>.</summary>
/// <param name="Ascender">The top of the line above the baseline.</param>
/// <param name="Descender">The bottom of the line, negative below the baseline.</param>
/// <param name="LineGap">The space added between two lines.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct FontLineMetrics(int Ascender, int Descender, int LineGap);
