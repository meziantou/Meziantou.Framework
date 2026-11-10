using System.Runtime.InteropServices;

namespace Meziantou.Framework.Mermaid.Internals.WardleyDiagrams;

/// <summary>A token of a Wardley map: its kind and its range in the text.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WardleyToken(WardleyTokenKind Kind, int Start, int End)
{
    public int Length => End - Start;
}
