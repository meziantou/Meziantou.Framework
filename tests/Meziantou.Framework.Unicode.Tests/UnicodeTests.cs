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
    public void ReplaceConfusablesCharacters_ExpandsMultiCharacterReplacements()
    {
        Assert.Equal("c\u0338", Unicode.ReplaceConfusablesCharacters("\u00A2"));
    }

    [Fact]
    public void ReplaceConfusablesCharacters_HandlesAstralCodePoints()
    {
        Assert.Equal("xAy", Unicode.ReplaceConfusablesCharacters("x\U0001D400y"));
    }

    [Fact]
    public void ReplaceConfusablesCharacters_PreservesTheScannedPrefix()
    {
        Assert.Equal("abca", Unicode.ReplaceConfusablesCharacters("abc\u0430"));
    }

    [Fact]
    public void ReplaceConfusablesCharacters_HandlesEmptyAndLoneSurrogates()
    {
        Assert.Same("", Unicode.ReplaceConfusablesCharacters(""));
        Assert.Same("\uD800", Unicode.ReplaceConfusablesCharacters("\uD800"));
        Assert.Equal("a\uD800", Unicode.ReplaceConfusablesCharacters("a\uD800"));
        Assert.Equal("A\uD800", Unicode.ReplaceConfusablesCharacters("\u0410\uD800"));
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
    [InlineData(0x0041, UnicodeScript.Latin)]
    [InlineData(0x0030, UnicodeScript.Common)]
    [InlineData(0x0410, UnicodeScript.Cyrillic)]
    [InlineData(0x03B1, UnicodeScript.Greek)]
    [InlineData(0x0301, UnicodeScript.Inherited)]
    [InlineData(0x4E00, UnicodeScript.Han)]
    [InlineData(0x3042, UnicodeScript.Hiragana)]
    [InlineData(0x30A2, UnicodeScript.Katakana)]
    [InlineData(0x1F600, UnicodeScript.Common)]
    // Unassigned, private use, and the top of the code space have no script.
    [InlineData(0x0378, UnicodeScript.Unknown)]
    [InlineData(0xE000, UnicodeScript.Unknown)]
    [InlineData(0x10FFFF, UnicodeScript.Unknown)]
    public void GetScript_ReturnsExpectedScript(int codePoint, UnicodeScript expected)
    {
        Assert.Equal(expected, UnicodeScripts.GetScript(codePoint));
        Assert.Equal(expected, UnicodeScripts.GetScript(new Rune(codePoint)));
    }

    [Fact]
    public void GetScript_ReturnsUnknownOutsideTheCodeSpace()
    {
        Assert.Equal(UnicodeScript.Unknown, UnicodeScripts.GetScript(-1));
        Assert.Equal(UnicodeScript.Unknown, UnicodeScripts.GetScript(int.MinValue));
        Assert.Equal(UnicodeScript.Unknown, UnicodeScripts.GetScript(0x110000));
        Assert.Equal(UnicodeScript.Unknown, UnicodeScripts.GetScript(int.MaxValue));
    }

    [Fact]
    public void UnicodeScript_DefaultIsUnknown()
    {
        Assert.Equal(UnicodeScript.Unknown, default);
    }

    [Theory]
    // Each pair is the last code point of a script range and the first of the next one, so the
    // binary search is exercised exactly where an off-by-one would show up.
    [InlineData(0x005A, UnicodeScript.Latin, 0x005B, UnicodeScript.Common)]
    [InlineData(0x007A, UnicodeScript.Latin, 0x007B, UnicodeScript.Common)]
    [InlineData(0x00AA, UnicodeScript.Latin, 0x00AB, UnicodeScript.Common)]
    [InlineData(0x00D6, UnicodeScript.Latin, 0x00D7, UnicodeScript.Common)]
    [InlineData(0x00F6, UnicodeScript.Latin, 0x00F7, UnicodeScript.Common)]
    [InlineData(0x0377, UnicodeScript.Greek, 0x0378, UnicodeScript.Unknown)]
    public void GetScript_IsCorrectAtRangeBoundaries(int last, UnicodeScript lastScript, int next, UnicodeScript nextScript)
    {
        Assert.Equal(lastScript, UnicodeScripts.GetScript(last));
        Assert.Equal(nextScript, UnicodeScripts.GetScript(next));
    }

    [Fact]
    public void GetScript_NeverThrowsAcrossTheWholeCodeSpace()
    {
        // The binary search decodes offsets from a byte blob, so walk every scalar value once to
        // prove no input drives it out of the table.
        var scripts = new HashSet<UnicodeScript>();
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
                continue;

            scripts.Add(UnicodeScripts.GetScript(codePoint));
        }

        Assert.Contains(UnicodeScript.Latin, scripts);
        Assert.Contains(UnicodeScript.Han, scripts);
        Assert.Contains(UnicodeScript.Unknown, scripts);
    }


    [Theory]
    [InlineData("")]
    [InlineData("paypal")]
    [InlineData("user123")]
    [InlineData("123 !?")]
    // Whole words in one script.
    [InlineData("\u041F\u0440\u0438\u0432\u0435\u0442")]
    [InlineData("\u0395\u03BB\u03BB\u03AC\u03B4\u03B1")]
    // Japanese and Korean legitimately combine scripts; the UTS #39 augmented sets cover them.
    [InlineData("\u65E5\u672C\u8A9E\u3067\u3059")]
    [InlineData("\u65E5\u672C\u30AB\u30BF\u30AB\u30CA")]
    [InlineData("\uD55C\uAD6D\uC5B4\u6F22\u5B57")]
    [InlineData("\u6F22\u5B57\u3105\u3106")]
    // A prolonged sound mark after katakana.
    [InlineData("\u30A2\u30FC")]
    public void IsSingleScript_AcceptsSingleScriptText(string value)
    {
        Assert.True(Unicode.IsSingleScript(value));
        Assert.False(Unicode.IsMixedScript(value));
    }

    [Theory]
    // Cyrillic look-alikes with a Latin "l" - the classic homograph.
    [InlineData("\u0440\u0430\u0443\u0440\u0430l")]
    // Latin with a Greek beta.
    [InlineData("a\u03B2c")]
    // Hiragana and Hangul share no augmented script.
    [InlineData("\u3053\u3093\u306B\u3061\u306F\uD55C\uAD6D")]
    public void IsSingleScript_RejectsMixedScriptText(string value)
    {
        Assert.False(Unicode.IsSingleScript(value));
        Assert.True(Unicode.IsMixedScript(value));
    }

    [Theory]
    // U+30FC and U+3006 both have Script=Common, which on its own would make them match any
    // script. Their Script_Extensions are {Hiragana, Katakana} and {Han}, so pairing either with
    // Latin is mixed. These cases only pass when Script_Extensions is consulted.
    [InlineData("a\u30FC")]
    [InlineData("a\u3006")]
    public void IsSingleScript_UsesScriptExtensionsNotJustScript(string value)
    {
        Assert.Equal(UnicodeScript.Common, UnicodeScripts.GetScript(value[1]));
        Assert.False(Unicode.IsSingleScript(value));
    }

    [Fact]
    public void IsSingleScript_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Unicode.IsSingleScript(null!));
        Assert.Throws<ArgumentNullException>(() => Unicode.IsMixedScript(null!));
    }

    [Fact]
    public void IsSingleScript_TreatsLoneSurrogatesAsScriptNeutral()
    {
        // EnumerateRunes substitutes U+FFFD, whose script is Common, so an ill-formed string is
        // not reported as mixed on that basis alone.
        Assert.True(Unicode.IsSingleScript("a\uD800b"));
        Assert.False(Unicode.IsSingleScript("a\uD800\u0431"));
    }
}
