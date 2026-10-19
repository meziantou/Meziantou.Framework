namespace Meziantou.Framework;

/// <summary>Represents Unicode character information.</summary>
public readonly struct UnicodeCharacterInfo : IEquatable<UnicodeCharacterInfo>
{
    // The index of the property record, 0 for a default instance. Every field but the rune is read from the tables
    // when it is asked for, which keeps the struct small and a lookup cheap.
    private readonly int _propertyIndex;

    // The name decoded by the enumerator of Unicode.AllCharacters, which reads the names in order; null otherwise.
    private readonly string? _name;

    internal UnicodeCharacterInfo(Rune rune, int propertyIndex, string? name)
    {
        Rune = rune;
        _propertyIndex = propertyIndex;
        _name = name;
    }

    /// <summary>Gets the Unicode scalar value.</summary>
    public Rune Rune { get; }

    /// <summary>Gets the character name.</summary>
    public string Name => _name ?? (IsDefault ? null! : UnicodeCharacterInfos.GetName(Rune.Value));

    /// <summary>Gets the Unicode general category.</summary>
    public UnicodeCategory Category => (UnicodeCategory)GetField(UnicodeDataFormat.CategoryField);

    /// <summary>Gets the Unicode bidirectional category.</summary>
    public UnicodeBidirectionalCategory BidiCategory => (UnicodeBidirectionalCategory)GetField(UnicodeDataFormat.BidiCategoryField);

    /// <summary>Gets the Unicode block.</summary>
    public UnicodeBlock Block => IsDefault ? null! : UnicodeBlocks.GetBlock(Rune.Value);

    /// <summary>Gets the canonical combining class.</summary>
    public byte CanonicalCombiningClass => GetField(UnicodeDataFormat.CanonicalCombiningClassField);

    /// <summary>Gets the decomposition mapping.</summary>
    public string? DecompositionMapping => IsDefault ? null : UnicodeCharacterInfos.GetDecompositionMapping(Rune.Value);

    /// <summary>Gets the decimal digit value.</summary>
    public int? DecimalDigitValue => GetDigit(UnicodeDataFormat.DecimalDigitValueField);

    /// <summary>Gets the digit value.</summary>
    public int? DigitValue => GetDigit(UnicodeDataFormat.DigitValueField);

    /// <summary>Gets the numeric value.</summary>
    public string? NumericValue => IsDefault ? null : UnicodeCharacterInfos.GetNumericValue(Rune.Value);

    /// <summary>Gets a value indicating whether the character is mirrored.</summary>
    public bool IsMirrored => (GetField(UnicodeDataFormat.FlagsField) & UnicodeDataFormat.MirroredFlag) != 0;

    /// <summary>Gets the Unicode 1.0 name.</summary>
    public string? Unicode1Name => IsDefault ? null : UnicodeCharacterInfos.GetUnicode1Name(Rune.Value);

    /// <summary>Gets the ISO comment.</summary>
    public string? IsoComment => IsDefault ? null : UnicodeCharacterInfos.GetIsoComment(Rune.Value);

    /// <summary>Gets the simple uppercase mapping.</summary>
    public Rune? SimpleUppercaseMapping => UnicodeCharacterInfos.GetCaseMapping(Rune.Value, _propertyIndex, UnicodeDataFormat.HasUppercaseFlag, UnicodeDataFormat.UppercaseDeltaField);

    /// <summary>Gets the simple lowercase mapping.</summary>
    public Rune? SimpleLowercaseMapping => UnicodeCharacterInfos.GetCaseMapping(Rune.Value, _propertyIndex, UnicodeDataFormat.HasLowercaseFlag, UnicodeDataFormat.LowercaseDeltaField);

    /// <summary>Gets the simple titlecase mapping.</summary>
    public Rune? SimpleTitlecaseMapping => UnicodeCharacterInfos.GetCaseMapping(Rune.Value, _propertyIndex, UnicodeDataFormat.HasTitlecaseFlag, UnicodeDataFormat.TitlecaseDeltaField);

    /// <summary>Gets a value indicating whether the character has the Emoji property.</summary>
    public bool IsEmoji => HasFlag(EmojiProperties.Emoji);

    /// <summary>Gets a value indicating whether the character has the Emoji_Presentation property.</summary>
    public bool HasEmojiPresentation => HasFlag(EmojiProperties.EmojiPresentation);

    /// <summary>Gets a value indicating whether the character has the Emoji_Modifier property.</summary>
    public bool IsEmojiModifier => HasFlag(EmojiProperties.EmojiModifier);

    /// <summary>Gets a value indicating whether the character has the Emoji_Modifier_Base property.</summary>
    public bool IsEmojiModifierBase => HasFlag(EmojiProperties.EmojiModifierBase);

    /// <summary>Gets a value indicating whether the character has the Emoji_Component property.</summary>
    public bool IsEmojiComponent => HasFlag(EmojiProperties.EmojiComponent);

    /// <summary>Gets a value indicating whether the character has the Extended_Pictographic property.</summary>
    public bool IsExtendedPictographic => HasFlag(EmojiProperties.ExtendedPictographic);

    /// <summary>Determines whether this instance and another describe the same Unicode character.</summary>
    /// <param name="other">The other character information.</param>
    /// <returns><see langword="true"/> when both describe the same character; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Instances come from a fixed table keyed by <see cref="Rune"/>, so the scalar value identifies the
    /// entry. The index of the property record is compared as well so that a <see langword="default"/> instance,
    /// which carries the scalar value U+0000 and no properties, is not equal to the real entry for U+0000.
    /// </remarks>
    public bool Equals(UnicodeCharacterInfo other) => Rune == other.Rune && _propertyIndex == other._propertyIndex;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UnicodeCharacterInfo other && Equals(other);

    public override int GetHashCode() => Rune.GetHashCode();

    public static bool operator ==(UnicodeCharacterInfo left, UnicodeCharacterInfo right) => left.Equals(right);

    public static bool operator !=(UnicodeCharacterInfo left, UnicodeCharacterInfo right) => !left.Equals(right);

    private bool IsDefault => _propertyIndex == 0;

    private bool HasFlag(EmojiProperties flag) => ((EmojiProperties)GetField(UnicodeDataFormat.EmojiPropertiesField) & flag) != EmojiProperties.None;

    // Record 0 is all zeros, so a default instance reads the values it always had without a special case.
    private byte GetField(int field) => UnicodeCharacterInfos.GetPropertyField(_propertyIndex, field);

    private int? GetDigit(int field)
    {
        var value = (sbyte)GetField(field);
        return value >= 0 ? value : null;
    }
}
