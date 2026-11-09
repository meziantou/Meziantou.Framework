using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A run of text in one style.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextRun(string Text, bool Bold, bool Italic);
