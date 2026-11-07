using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A vector of an AFM file, in units of 1/1000 of the point size.</summary>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmVector(double X, double Y);
