using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A part of the block of a paragraph extension: a range of a line of the paragraph.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ParagraphExtensionRange(ExtensionRangeKind Kind, int Line, int Start, int Length);
