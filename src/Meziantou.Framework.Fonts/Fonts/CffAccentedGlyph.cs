using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The components of a glyph made of two others by the deprecated form of the <c>endchar</c> operator of a CFF charstring
/// (Technical Note #5177, appendix C): a base character and an accent, given by their codes in the standard encoding.
/// </summary>
/// <param name="AccentX">The horizontal offset of the origin of the accent from the origin of the base character.</param>
/// <param name="AccentY">The vertical offset of the origin of the accent from the origin of the base character.</param>
/// <param name="BaseCode">The code of the base character in the standard encoding.</param>
/// <param name="AccentCode">The code of the accent in the standard encoding.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CffAccentedGlyph(double AccentX, double AccentY, int BaseCode, int AccentCode);
