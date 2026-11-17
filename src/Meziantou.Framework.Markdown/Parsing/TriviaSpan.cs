using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>The kind of the trivia of a range of the text, when it is not only whitespace and line endings.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TriviaSpan(int Start, int End, SyntaxKind Kind);
