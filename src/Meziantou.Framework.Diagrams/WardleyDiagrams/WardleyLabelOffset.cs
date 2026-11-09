using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams;

/// <summary>The position of the label of a component of a Wardley map, relative to the component, in pixels (Mermaid <c>label [x, y]</c>).</summary>
/// <param name="X">The horizontal offset: a negative value moves the label left.</param>
/// <param name="Y">The vertical offset: a negative value moves the label up.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct WardleyLabelOffset(int X, int Y);
