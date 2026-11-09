using System.Runtime.InteropServices;

namespace Meziantou.Framework.Mermaid.Internals.VennDiagrams;

/// <summary>A token: its kind, its text as the lexer of Mermaid returns it, and its position in the text being parsed.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct VennToken(VennTokenKind Kind, string Value, int Offset);
