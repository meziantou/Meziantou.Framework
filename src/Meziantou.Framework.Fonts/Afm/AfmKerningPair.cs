using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A kerning pair of an AFM file (the <c>KP</c>, <c>KPX</c>, <c>KPY</c> and <c>KPH</c> keys): the vector by which the
/// second character moves relative to the first one.
/// </summary>
/// <remarks>
/// <c>KP</c>, <c>KPX</c> and <c>KPY</c> name the characters, and <c>KPH</c> gives their codes. The other identifier of
/// each character is filled from <see cref="AfmFontMetrics.CharacterMetrics"/> when the file has the character, and is
/// <see langword="null"/> otherwise.
/// </remarks>
/// <param name="First">The name of the first character.</param>
/// <param name="Second">The name of the second character.</param>
/// <param name="FirstCode">The code of the first character.</param>
/// <param name="SecondCode">The code of the second character.</param>
/// <param name="X">The x component of the kerning vector, in units of 1/1000 of the point size.</param>
/// <param name="Y">The y component of the kerning vector, in units of 1/1000 of the point size.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmKerningPair(string? First, string? Second, int? FirstCode, int? SecondCode, double X, double Y);
