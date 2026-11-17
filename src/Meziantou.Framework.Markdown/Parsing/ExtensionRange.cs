using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A part of the construct of an inline extension, relative to its trigger character.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExtensionRange(ExtensionRangeKind Kind, int Start, int Length);
