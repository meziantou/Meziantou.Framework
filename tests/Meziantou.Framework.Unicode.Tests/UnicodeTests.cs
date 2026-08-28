namespace Meziantou.Framework.Tests;

public sealed class UnicodeTests
{
    [Fact]
    public void ReplaceConfusablesCharacters_ReplacesMappedCharacters()
    {
        var input = "\u0410\u0430\u03B1";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Equal("Aaa", output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_NoChange()
    {
        var input = "Hello";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Same(input, output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_CharOverload()
    {
        var output = Unicode.ReplaceConfusablesCharacters('\u0410');

        Assert.Equal("A", output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_CharOverload_IgnoresSurrogates()
    {
        var output = Unicode.ReplaceConfusablesCharacters('\uD800');

        Assert.Equal("\uD800", output);
    }

    [Fact]
    public void IsConfusableCharacter_ReturnsExpectedValue()
    {
        Assert.True(Unicode.IsConfusableCharacter(new Rune('\u0410')));
        Assert.False(Unicode.IsConfusableCharacter(new Rune('A')));
    }

    [Fact]
    public void GetCharacterInfo_ReturnsExpectedMetadata()
    {
        var info = Unicode.GetCharacterInfo(new Rune('A'));

        Assert.NotNull(info);
        Assert.Equal(new Rune('A'), info.Value.Rune);
        Assert.Equal("LATIN CAPITAL LETTER A", info.Value.Name);
        Assert.Equal(UnicodeCategory.UppercaseLetter, info.Value.Category);
        Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.Value.BidiCategory);
        Assert.Equal("Basic Latin", info.Value.Block.Name);

        var digitInfo = Unicode.GetCharacterInfo(new Rune('0'));

        Assert.NotNull(digitInfo);
        Assert.Equal(0, digitInfo.Value.DecimalDigitValue);
        Assert.Equal(0, digitInfo.Value.DigitValue);
    }

    [Fact]
    public void GetCharacterInfo_ReturnsExpectedMetadata_ForOneHalf()
    {
        var info = Unicode.GetCharacterInfo(new Rune(0x00BD));

        // 00BD;VULGAR FRACTION ONE HALF;No;0;ON;<fraction> 0031 2044 0032;;;1/2;N;FRACTION ONE HALF;;;;
        Assert.NotNull(info);
        Assert.Equal(new Rune(0x00BD), info.Value.Rune);
        Assert.Equal("VULGAR FRACTION ONE HALF", info.Value.Name);
        Assert.Equal(UnicodeCategory.OtherNumber, info.Value.Category);
        Assert.Equal(UnicodeBidirectionalCategory.OtherNeutral, info.Value.BidiCategory);
        Assert.Equal(0, info.Value.CanonicalCombiningClass);
        Assert.Equal("<fraction> 0031 2044 0032", info.Value.DecompositionMapping);
        Assert.Null(info.Value.DecimalDigitValue);
        Assert.Null(info.Value.DigitValue);
        Assert.Equal("1/2", info.Value.NumericValue);
        Assert.False(info.Value.IsMirrored);
        Assert.Equal("FRACTION ONE HALF", info.Value.Unicode1Name);
        Assert.Null(info.Value.IsoComment);
        Assert.Null(info.Value.SimpleUppercaseMapping);
        Assert.Null(info.Value.SimpleLowercaseMapping);
        Assert.Null(info.Value.SimpleTitlecaseMapping);
    }

    [Fact]
    public void GetCharacterInfo_CharOverload_HandlesSurrogates()
    {
        var info = Unicode.GetCharacterInfo('A');

        Assert.NotNull(info);
        Assert.Equal(new Rune('A'), info.Value.Rune);

        var surrogateInfo = Unicode.GetCharacterInfo('\uD800');

        Assert.Null(surrogateInfo);
    }

    [Fact]
    public void TryGetCharacterInfo_CharOverload_HandlesSurrogates()
    {
        Assert.True(Unicode.TryGetCharacterInfo('A', out var info));
        Assert.Equal(new Rune('A'), info.Rune);

        Assert.False(Unicode.TryGetCharacterInfo('\uD800', out var surrogateInfo));
        Assert.Equal(default, surrogateInfo);
    }

    [Fact]
    public void UnicodeCharacterInfo_EqualityIsStructural()
    {
        Assert.True(Unicode.TryGetCharacterInfo(new Rune('A'), out var a));
        Assert.True(Unicode.TryGetCharacterInfo(new Rune('A'), out var b));
        Assert.True(Unicode.TryGetCharacterInfo(new Rune('B'), out var c));

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.NotEqual<object>("not a character", a);
    }

    [Fact]
    public void UnicodeCharacterInfo_DefaultIsNotEqualToTheEntryForNull()
    {
        Assert.True(Unicode.TryGetCharacterInfo(new Rune(0), out var nul));

        Assert.Equal(new Rune(0), nul.Rune);
        Assert.Equal(default, default(UnicodeCharacterInfo).Rune);
        Assert.NotEqual(default, nul);
    }

    [Fact]
    public void UnicodeCharacterInfo_HashCodeDistinguishesCharacters()
    {
        var seen = new Dictionary<int, Rune>();
        var collisions = new List<string>();
        for (var codePoint = 0; codePoint < 1000; codePoint++)
        {
            if (!Unicode.TryGetCharacterInfo(new Rune(codePoint), out var info))
                continue;

            var hash = info.GetHashCode();
            if (seen.TryGetValue(hash, out var existing))
            {
                collisions.Add($"U+{existing.Value:X4} collides with U+{info.Rune.Value:X4}");
            }
            else
            {
                seen[hash] = info.Rune;
            }
        }

        Assert.Empty(collisions);
    }

    [Fact]
    public void UnicodeCharacterInfo_DeduplicatesInAHashSet()
    {
        Assert.True(Unicode.TryGetCharacterInfo(new Rune('A'), out var a));
        Assert.True(Unicode.TryGetCharacterInfo(new Rune('A'), out var duplicate));

        var set = new HashSet<UnicodeCharacterInfo> { a, duplicate };

        Assert.Single(set);
    }

    [Fact]
    public void IsEmoji_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmoji(new Rune(0x1F600)));
        Assert.True(UnicodeEmoji.IsEmoji(0x1F600));
        Assert.False(UnicodeEmoji.IsEmoji(new Rune('A')));
    }

    [Fact]
    public void HasEmojiPresentation_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.HasEmojiPresentation(new Rune(0x1F600)));
        Assert.False(UnicodeEmoji.HasEmojiPresentation(new Rune('A')));
    }

    [Fact]
    public void IsEmojiModifier_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiModifier(new Rune(0x1F3FB)));
        Assert.False(UnicodeEmoji.IsEmojiModifier(new Rune('A')));
    }

    [Fact]
    public void IsEmojiModifierBase_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiModifierBase(new Rune(0x1F44B)));
        Assert.False(UnicodeEmoji.IsEmojiModifierBase(new Rune('A')));
    }

    [Fact]
    public void IsEmojiComponent_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiComponent(new Rune(0x0023)));
        Assert.False(UnicodeEmoji.IsEmojiComponent(new Rune('A')));
    }

    [Fact]
    public void IsExtendedPictographic_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsExtendedPictographic(new Rune(0x1F600)));
        Assert.False(UnicodeEmoji.IsExtendedPictographic(new Rune('A')));
    }

    [Fact]
    public void GetCharacterInfo_IncludesEmojiProperties()
    {
        var info = Unicode.GetCharacterInfo(new Rune(0x1F600));

        Assert.NotNull(info);
        Assert.True(info.Value.IsEmoji);
        Assert.True(info.Value.HasEmojiPresentation);
        Assert.True(info.Value.IsExtendedPictographic);
        Assert.False(info.Value.IsEmojiModifier);
        Assert.False(info.Value.IsEmojiModifierBase);
    }

    [Fact]
    public void AllCharacters_MatchesGeneratedEntryCount()
    {
        Assert.Equal(297334, Unicode.AllCharacters.Count);
    }

    [Theory]
    [InlineData(0x4E00, "<CJK Ideograph>")]
    [InlineData(0xAC00, "<Hangul Syllable>")]
    [InlineData(0xE000, "<Private Use>")]
    [InlineData(0x20000, "<CJK Ideograph Extension B>")]
    [InlineData(0xF0000, "<Plane 15 Private Use>")]
    public void GetCharacterInfo_ResolvesRangeExpandedCodePoints(int codePoint, string expectedName)
    {
        var info = Unicode.GetCharacterInfo(new Rune(codePoint));

        Assert.NotNull(info);
        Assert.Equal(expectedName, info.Value.Name);
    }

    [Fact]
    public void AllCharacters_HaveConsistentBlockAndName()
    {
        var failures = new List<string>();
        foreach (var info in Unicode.AllCharacters)
        {
            if (failures.Count >= 10)
                break;

            if (!ReferenceEquals(UnicodeBlocks.GetBlock(info.Rune), info.Block))
            {
                failures.Add($"U+{info.Rune.Value:X4}: block {info.Block.Name} != {UnicodeBlocks.GetBlock(info.Rune).Name}");
            }
            else if (!info.Block.Contains(info.Rune))
            {
                failures.Add($"U+{info.Rune.Value:X4}: not contained in block {info.Block.Name}");
            }
            else if (string.IsNullOrEmpty(info.Name))
            {
                failures.Add($"U+{info.Rune.Value:X4}: empty name");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void UnicodeRange_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnicodeRange(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnicodeRange(0x110000, 0x110000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnicodeRange(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnicodeRange(0, 0x110000));
        Assert.Throws<ArgumentException>(() => new UnicodeRange(10, 5));
    }

    [Fact]
    public void UnicodeRange_LengthAndContainsAreInclusive()
    {
        var range = new UnicodeRange(0x40, 0x4F);

        Assert.Equal(16, range.Length);
        Assert.Equal(1, new UnicodeRange(0x41, 0x41).Length);
        Assert.True(range.Contains(0x40));
        Assert.True(range.Contains(0x4F));
        Assert.True(range.Contains(new Rune(0x45)));
        Assert.False(range.Contains(0x3F));
        Assert.False(range.Contains(0x50));
    }

    [Fact]
    public void UnicodeRange_EqualityIsStructural()
    {
        var a = new UnicodeRange(0, 0x7F);
        var b = new UnicodeRange(0, 0x7F);
        var c = new UnicodeRange(0, 0x80);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.Equal<object>(a, b);
        Assert.NotEqual<object>("not a range", a);
    }

    [Fact]
    public void UnicodeRange_ToStringUsesCodePointNotation()
    {
        Assert.Equal("U+0000..U+007F", new UnicodeRange(0, 0x7F).ToString());
        Assert.Equal("U+10000..U+10FFFF", new UnicodeRange(0x10000, 0x10FFFF).ToString());
    }

    [Fact]
    public void UnicodeBlocks_GetBlockMatchesBlockRange()
    {
        Assert.Same(UnicodeBlocks.BasicLatin, UnicodeBlocks.GetBlock(0x41));
        Assert.Same(UnicodeBlocks.BasicLatin, UnicodeBlocks.GetBlock(new Rune('A')));
        Assert.Same(UnicodeBlocks.Latin1Supplement, UnicodeBlocks.GetBlock(0x80));
        Assert.Same(UnicodeBlocks.Unknown, UnicodeBlocks.GetBlock(-1));
        Assert.Same(UnicodeBlocks.Unknown, UnicodeBlocks.GetBlock(0x110000));
        Assert.Same(UnicodeBlocks.Unknown, UnicodeBlocks.GetBlock(int.MinValue));
    }

    [Fact]
    public void UnicodeBlock_ExposesNameRangeAndEquality()
    {
        var block = UnicodeBlocks.BasicLatin;

        Assert.Equal("Basic Latin", block.Name);
        Assert.Equal(new UnicodeRange(0, 0x7F), block.Range);
        Assert.True(block.Contains(0x41));
        Assert.True(block.Contains(new Rune('A')));
        Assert.False(block.Contains(0x80));
        Assert.Equal("Basic Latin (U+0000..U+007F)", block.ToString());
        Assert.Equal(block, UnicodeBlocks.BasicLatin);
        Assert.NotEqual(block, UnicodeBlocks.Latin1Supplement);
        Assert.False(block.Equals(null));
        Assert.Equal(block.GetHashCode(), UnicodeBlocks.BasicLatin.GetHashCode());
    }

    [Theory]
    // Composed vs decomposed forms of the same text.
    [InlineData("caf\u00E9", "cafe\u0301")]
    // Latin vs Cyrillic look-alikes.
    [InlineData("paypal", "\u0440\u0430\u0443\u0440\u0430l")]
    // A precomposed letter whose decomposition is confusable.
    [InlineData("\u00CF", "I\u0308")]
    // Latin diaeresis vs Cyrillic diaeresis.
    [InlineData("Zo\u00EB", "Zo\u0451")]
    public void AreConfusable_DetectsConfusableStrings(string a, string b)
    {
        Assert.True(Unicode.AreConfusable(a, b));
        Assert.Equal(Unicode.GetConfusableSkeleton(a), Unicode.GetConfusableSkeleton(b));
    }

    [Theory]
    [InlineData("paypal", "example")]
    [InlineData("a", "b")]
    [InlineData("", "a")]
    public void AreConfusable_DoesNotReportUnrelatedStrings(string a, string b)
    {
        Assert.False(Unicode.AreConfusable(a, b));
    }

    [Fact]
    public void GetConfusableSkeleton_FoldsThroughTheAsciiMappings()
    {
        // U+00CF decomposes to I + U+0308, and I maps to l. Dropping the ASCII entries from the
        // table would silently break this.
        Assert.Equal("l\u0308", Unicode.GetConfusableSkeleton("\u00CF"));
    }

    [Fact]
    public void GetConfusableSkeleton_IsIdempotent()
    {
        var skeleton = Unicode.GetConfusableSkeleton("\u0440\u0430\u0443\u0440\u0430l");

        Assert.Equal(skeleton, Unicode.GetConfusableSkeleton(skeleton));
    }

    [Fact]
    public void GetConfusableSkeleton_HandlesEmptyAndIdenticalInput()
    {
        Assert.Same("", Unicode.GetConfusableSkeleton(""));
        Assert.True(Unicode.AreConfusable("identical", "identical"));
    }

    [Fact]
    public void GetConfusableSkeleton_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Unicode.GetConfusableSkeleton(null!));
        Assert.Throws<ArgumentNullException>(() => Unicode.AreConfusable(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => Unicode.AreConfusable("a", null!));
        Assert.Throws<ArgumentException>(() => Unicode.GetConfusableSkeleton("a\uD800"));
    }
}
