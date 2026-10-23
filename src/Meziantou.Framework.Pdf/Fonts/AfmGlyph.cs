using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>A glyph of an AFM file: its code in the encoding of the font (-1 when it has none) and its advance width in 1/1000 em.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AfmGlyph(string Name, int Code, double Width);
