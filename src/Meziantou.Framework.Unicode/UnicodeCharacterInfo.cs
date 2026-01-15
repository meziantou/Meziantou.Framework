namespace Meziantou.Framework;

/// <summary>Represents Unicode character information.</summary>
public readonly struct UnicodeCharacterInfo
{
    private readonly sbyte _decimalDigitValue;
    private readonly sbyte _digitValue;

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
        int simpleTitlecaseMapping)
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
}
