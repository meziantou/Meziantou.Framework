using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A part of a composite character of an AFM file (the <c>PCC</c> key).</summary>
/// <param name="Name">The name of the character drawn.</param>
/// <param name="DeltaX">The x displacement of the character from the origin of the composite.</param>
/// <param name="DeltaY">The y displacement of the character from the origin of the composite.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmCompositePart(string Name, double DeltaX, double DeltaY);
