using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A ligature of an AFM file (the <c>L</c> key of a character): the character followed by <paramref name="Successor"/> may be replaced by <paramref name="Ligature"/>.</summary>
/// <param name="Successor">The name of the character that follows.</param>
/// <param name="Ligature">The name of the character that replaces the two.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmLigature(string Successor, string Ligature);
