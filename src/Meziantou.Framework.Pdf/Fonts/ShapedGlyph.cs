using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>A glyph of a shaped text: its identifier, the adjustment of its position before it and its offset above the baseline, in thousandths of the font size.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapedGlyph(int Glyph, double Adjustment, double Rise);
