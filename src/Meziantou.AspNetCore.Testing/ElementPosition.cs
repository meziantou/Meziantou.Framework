using System.Runtime.InteropServices;

namespace Meziantou.AspNetCore.Testing;

/// <summary>A position in the border box of an element, in CSS pixels from its top-left corner.</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ElementPosition(double X, double Y);
