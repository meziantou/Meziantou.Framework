using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>An sRGB color, components from 0 to 1.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RgbColor(double R, double G, double B);
