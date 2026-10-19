namespace Meziantou.Framework;

/// <summary>Provides emoji-related query methods for Unicode characters.</summary>
/// <remarks>
/// This class provides methods to check various emoji properties defined in Unicode Technical Standard #51.
/// For more information, see: https://www.unicode.org/reports/tr51/
/// </remarks>
public static class UnicodeEmoji
{
    /// <summary>Determines whether the specified rune has the Emoji property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmoji(Rune rune) => HasProperty(rune.Value, EmojiProperties.Emoji);

    /// <summary>Determines whether the specified code point has the Emoji property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmoji(int codePoint) => HasProperty(codePoint, EmojiProperties.Emoji);

    /// <summary>Determines whether the specified rune has the Emoji_Presentation property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Presentation property; otherwise, <see langword="false"/>.</returns>
    public static bool HasEmojiPresentation(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiPresentation);

    /// <summary>Determines whether the specified code point has the Emoji_Presentation property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Presentation property; otherwise, <see langword="false"/>.</returns>
    public static bool HasEmojiPresentation(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiPresentation);

    /// <summary>Determines whether the specified rune has the Emoji_Modifier property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Modifier property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifier(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiModifier);

    /// <summary>Determines whether the specified code point has the Emoji_Modifier property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Modifier property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifier(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiModifier);

    /// <summary>Determines whether the specified rune has the Emoji_Modifier_Base property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Modifier_Base property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifierBase(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiModifierBase);

    /// <summary>Determines whether the specified code point has the Emoji_Modifier_Base property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Modifier_Base property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiModifierBase(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiModifierBase);

    /// <summary>Determines whether the specified rune has the Emoji_Component property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Emoji_Component property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiComponent(Rune rune) => HasProperty(rune.Value, EmojiProperties.EmojiComponent);

    /// <summary>Determines whether the specified code point has the Emoji_Component property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Emoji_Component property; otherwise, <see langword="false"/>.</returns>
    public static bool IsEmojiComponent(int codePoint) => HasProperty(codePoint, EmojiProperties.EmojiComponent);

    /// <summary>Determines whether the specified rune has the Extended_Pictographic property.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune has the Extended_Pictographic property; otherwise, <see langword="false"/>.</returns>
    public static bool IsExtendedPictographic(Rune rune) => HasProperty(rune.Value, EmojiProperties.ExtendedPictographic);

    /// <summary>Determines whether the specified code point has the Extended_Pictographic property.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point has the Extended_Pictographic property; otherwise, <see langword="false"/>.</returns>
    public static bool IsExtendedPictographic(int codePoint) => HasProperty(codePoint, EmojiProperties.ExtendedPictographic);

    private static bool HasProperty(int codePoint, EmojiProperties property) => (UnicodeProperties.GetEmojiProperties(codePoint) & property) != EmojiProperties.None;
}
