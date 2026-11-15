using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A line of a leaf block: its content, from <paramref name="Start"/> to <paramref name="End"/>, and its line ending, up to <paramref name="EolEnd"/>.</summary>
/// <param name="VirtualSpaces">The columns of a tab before the content that a container did not take.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Line(int Start, int End, int EolEnd, int VirtualSpaces);
