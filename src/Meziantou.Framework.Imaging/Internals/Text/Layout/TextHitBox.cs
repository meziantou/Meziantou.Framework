using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a cluster of a line takes of the line, for the positions of the text: its characters, and the space from where
/// the pen is before its first glyph to where it is after its last one.
/// </summary>
/// <param name="Start">The index in the text of the first character of the cluster.</param>
/// <param name="Length">The number of UTF-16 code units of the cluster.</param>
/// <param name="Left">The left of the cluster, from the left of the layout.</param>
/// <param name="Right">The right of the cluster.</param>
/// <param name="IsRightToLeft">Whether the text of the cluster goes from its right to its left.</param>
/// <param name="CharacterCount">The number of characters (grapheme clusters) the cluster holds: more than one for a ligature, which shares its width between them.</param>
/// <param name="FirstBoundary">For a cluster of several characters, the index of the end of its first character in the list of the boundaries: one per character, from the start of the cluster.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextHitBox(int Start, int Length, double Left, double Right, bool IsRightToLeft, int CharacterCount, int FirstBoundary);
