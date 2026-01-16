namespace Meziantou.Framework;

/// <summary>Represents Unicode character information.</summary>
public readonly struct UnicodeCharacterInfo
{
    private readonly sbyte _decimalDigitValue;
    private readonly sbyte _digitValue;
    private readonly byte _emojiProperties;

    internal UnicodeCharacterInfo(
        Rune rune,
        string name,
        UnicodeCategory category,
        UnicodeBidirectionalCategory bidiCategory,
        UnicodeBlock block,
        byte canonicalCombiningClass,
        string? decompositionMapping,
        sbyte decimalDigitValue,
        sbyte digitValue,
        string? numericValue,
        bool mirrored,
        string? unicode1Name,
        string? isoComment,
        int simpleUppercaseMapping,
        int simpleLowercaseMapping,
        int simpleTitlecaseMapping,
        byte emojiProperties)
    {
        Rune = rune;
        Name = name;
        Category = category;
        BidiCategory = bidiCategory;
        Block = block;
        CanonicalCombiningClass = canonicalCombiningClass;
        DecompositionMapping = decompositionMapping;
        _decimalDigitValue = decimalDigitValue;
        _digitValue = digitValue;
        NumericValue = numericValue;
        IsMirrored = mirrored;
        Unicode1Name = unicode1Name;
        IsoComment = isoComment;
        SimpleUppercaseMapping = TryCreateRune(simpleUppercaseMapping);
        SimpleLowercaseMapping = TryCreateRune(simpleLowercaseMapping);
        SimpleTitlecaseMapping = TryCreateRune(simpleTitlecaseMapping);
        _emojiProperties = emojiProperties;

        static Rune? TryCreateRune(int value)
        {
            if (Rune.TryCreate(value, out var result))
                return result;

            return null;
        }
    }

    /// <summary>Gets the Unicode scalar value.</summary>
    public Rune Rune { get; }

    /// <summary>Gets the character name.</summary>
    public string Name { get; }

    /// <summary>Gets the Unicode general category.</summary>
    public UnicodeCategory Category { get; }

    /// <summary>Gets the Unicode bidirectional category.</summary>
    public UnicodeBidirectionalCategory BidiCategory { get; }

    /// <summary>Gets the Unicode block.</summary>
    public UnicodeBlock Block { get; }

    /// <summary>Gets the canonical combining class.</summary>
    public byte CanonicalCombiningClass { get; }

    /// <summary>Gets the decomposition mapping.</summary>
    public string? DecompositionMapping { get; }

    /// <summary>Gets the decimal digit value.</summary>
    public int? DecimalDigitValue => _decimalDigitValue >= 0 ? _decimalDigitValue : null;

    /// <summary>Gets the digit value.</summary>
    public int? DigitValue => _digitValue >= 0 ? _digitValue : null;

    /// <summary>Gets the numeric value.</summary>
    public string? NumericValue { get; }

    /// <summary>Gets a value indicating whether the character is mirrored.</summary>
    public bool IsMirrored { get; }

    /// <summary>Gets the Unicode 1.0 name.</summary>
    public string? Unicode1Name { get; }

    /// <summary>Gets the ISO comment.</summary>
    public string? IsoComment { get; }

    /// <summary>Gets the simple uppercase mapping.</summary>
    public Rune? SimpleUppercaseMapping { get; }

    /// <summary>Gets the simple lowercase mapping.</summary>
    public Rune? SimpleLowercaseMapping { get; }

    /// <summary>Gets the simple titlecase mapping.</summary>
    public Rune? SimpleTitlecaseMapping { get; }

    /// <summary>Gets a value indicating whether the character has the Emoji property.</summary>
    public bool IsEmoji => (_emojiProperties & 0x01) != 0;

    /// <summary>Gets a value indicating whether the character has the Emoji_Presentation property.</summary>
    public bool HasEmojiPresentation => (_emojiProperties & 0x02) != 0;

    /// <summary>Gets a value indicating whether the character has the Emoji_Modifier property.</summary>
    public bool IsEmojiModifier => (_emojiProperties & 0x04) != 0;

    /// <summary>Gets a value indicating whether the character has the Emoji_Modifier_Base property.</summary>
    public bool IsEmojiModifierBase => (_emojiProperties & 0x08) != 0;

    /// <summary>Gets a value indicating whether the character has the Emoji_Component property.</summary>
    public bool IsEmojiComponent => (_emojiProperties & 0x10) != 0;

    /// <summary>Gets a value indicating whether the character has the Extended_Pictographic property.</summary>
    public bool IsExtendedPictographic => (_emojiProperties & 0x20) != 0;
}
