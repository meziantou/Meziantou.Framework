namespace Meziantou.Framework;

/// <summary>Represents an emoji property of strings: a set of emoji sequences defined by UTS #51.</summary>
/// <remarks>The <c>RGI_Emoji</c> property is the union of all of them.</remarks>
/// <seealso href="https://www.unicode.org/reports/tr51/#Emoji_Sets" />
public enum UnicodeEmojiSequenceProperty
{
    /// <summary>The <c>Basic_Emoji</c> set: the emoji that are a single code point, possibly followed by the emoji presentation selector (U+FE0F).</summary>
    BasicEmoji = 0,

    /// <summary>The <c>Emoji_Keycap_Sequence</c> set: a digit, <c>#</c> or <c>*</c>, the emoji presentation selector and U+20E3.</summary>
    EmojiKeycapSequence = 1,

    /// <summary>The <c>RGI_Emoji_Flag_Sequence</c> set: the pairs of regional indicators recommended for general interchange.</summary>
    RgiEmojiFlagSequence = 2,

    /// <summary>The <c>RGI_Emoji_Modifier_Sequence</c> set: an emoji modifier base followed by a skin tone modifier.</summary>
    RgiEmojiModifierSequence = 3,

    /// <summary>The <c>RGI_Emoji_Tag_Sequence</c> set: the subdivision flags recommended for general interchange.</summary>
    RgiEmojiTagSequence = 4,

    /// <summary>The <c>RGI_Emoji_ZWJ_Sequence</c> set: the zero width joiner sequences recommended for general interchange.</summary>
    RgiEmojiZwjSequence = 5,
}
