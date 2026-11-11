using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>A part of a text that one face draws (<see cref="SceneFontCollection.GetRuns"/>).</summary>
/// <param name="Start">The index of the first character of the run in the text.</param>
/// <param name="Length">The number of characters of the run.</param>
/// <param name="Face">The face that draws the run.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneFontRun(int Start, int Length, SceneFontFace Face);
