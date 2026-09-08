using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>The properties a run of consecutive code points shares in the Unicode data resource.</summary>
/// <remarks>
/// Case mappings are stored relative to the code point rather than absolute: that is what lets a
/// whole alphabet collapse into one tuple, since every letter of it maps by the same distance.
/// A missing mapping is recorded in <see cref="Flags"/> rather than as a sentinel delta.
/// <para>
/// This is a record so that the generator can deduplicate tuples by value. This file is compiled
/// into the generator as well; see <see cref="UnicodeDataFormat"/> for the layout.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct UnicodeCharacterProperties(
    UnicodeCategory Category,
    UnicodeBidirectionalCategory BidiCategory,
    byte CanonicalCombiningClass,
    sbyte DecimalDigitValue,
    sbyte DigitValue,
    byte EmojiProperties,
    byte Flags,
    int SimpleUppercaseDelta,
    int SimpleLowercaseDelta,
    int SimpleTitlecaseDelta)
{
    /// <summary>Gets a simple case mapping, or <c>-1</c> when the character does not have one.</summary>
    /// <param name="codePoint">The code point the mapping is relative to.</param>
    /// <param name="flag">The flag that tells whether the mapping exists.</param>
    /// <returns>The mapped code point, or <c>-1</c>.</returns>
    public int GetMapping(int codePoint, byte flag)
    {
        if ((Flags & flag) == 0)
            return -1;

        var delta = flag switch
        {
            UnicodeDataFormat.HasUppercaseFlag => SimpleUppercaseDelta,
            UnicodeDataFormat.HasLowercaseFlag => SimpleLowercaseDelta,
            _ => SimpleTitlecaseDelta,
        };

        return codePoint + delta;
    }
}
