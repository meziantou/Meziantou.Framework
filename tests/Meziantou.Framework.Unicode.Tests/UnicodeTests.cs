using Meziantou.Xunit;

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
        // The count comes from the header of the binary resource, while the constant is the tally the
        // generator kept while writing it. A hardcoded number would have to be edited by hand on every
        // Unicode data refresh, and the refresh is done by a bot whose pull requests get no CI run.
        Assert.Equal(UnicodeCharacterInfos.GeneratedCharacterCount, Unicode.AllCharacters.Count);
    }

    [Fact]
    public void AllCharacters_AgreesWithLookup()
    {
        // The data resource stores runs of code points that share their properties. Enumerating
        // walks those runs while a lookup binary searches them, so the two reach an entry by
        // different paths and are worth checking against each other.
        var failures = new List<string>();
        var count = 0;
        foreach (var info in Unicode.AllCharacters)
        {
            count++;
            if (failures.Count >= 10)
                continue;

            if (!Unicode.TryGetCharacterInfo(info.Rune, out var found))
            {
                failures.Add($"U+{info.Rune.Value:X4}: enumerated but not found by lookup");
            }
            else if (found != info)
            {
                failures.Add($"U+{info.Rune.Value:X4}: lookup returned {found.Name} instead of {info.Name}");
            }
        }

        Assert.Empty(failures);
        Assert.Equal(Unicode.AllCharacters.Count, count);
    }

    [Theory]
    [InlineData(0x1D15E, "1D157 1D165")]
    [InlineData(0x2F800, "4E3D")]
    [InlineData(0x1E030, "<super> 0430")]
    [InlineData(0x00A0, "<noBreak> 0020")]
    public void GetCharacterInfo_RebuildsDecompositionMapping(int codePoint, string expected)
    {
        // Mappings are stored as code points, so the hexadecimal text is rebuilt on access.
        // Supplementary code points are the ones that do not fit the four-digit padding.
        var info = Unicode.GetCharacterInfo(new Rune(codePoint));

        Assert.NotNull(info);
        Assert.Equal(expected, info.Value.DecompositionMapping);
    }

    [Fact]
    public void GetCharacterInfo_ReturnsTheSameNameInstanceOnEveryLookup()
    {
        // Names are decoded from the resource on demand and cached, so a second lookup must not
        // pay for decoding again.
        var first = Unicode.GetCharacterInfo(new Rune(0x1F600));
        var second = Unicode.GetCharacterInfo(new Rune(0x1F600));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first.Value.Name, second.Value.Name);
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

    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
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

    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [Theory]
    [InlineData("paypal", "example")]
    [InlineData("a", "b")]
    [InlineData("", "a")]
    public void AreConfusable_DoesNotReportUnrelatedStrings(string a, string b)
    {
        Assert.False(Unicode.AreConfusable(a, b));
    }

    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [Fact]
    public void GetConfusableSkeleton_FoldsThroughTheAsciiMappings()
    {
        // U+00CF decomposes to I + U+0308, and I maps to l. Dropping the ASCII entries from the
        // table would silently break this.
        Assert.Equal("l\u0308", Unicode.GetConfusableSkeleton("\u00CF"));
    }

    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
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
    }

    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [Fact]
    public void GetConfusableSkeleton_RejectsUnpairedSurrogates()
    {
        Assert.Throws<ArgumentException>(() => Unicode.GetConfusableSkeleton("a\uD800"));
    }

    [RunIf(globalizationMode: TestGlobalizationMode.Invariant)]
    [Fact]
    public void GetConfusableSkeleton_ThrowsInInvariantGlobalization()
    {
        // string.Normalize is a no-op without ICU, so the skeleton would silently degrade to a
        // single unnormalized mapping pass. Refusing is the only safe answer for a security API.
        Assert.Throws<PlatformNotSupportedException>(() => Unicode.GetConfusableSkeleton("caf\u00E9"));
        Assert.Throws<PlatformNotSupportedException>(() => Unicode.AreConfusable("caf\u00E9", "cafe\u0301"));

        // The paths that answer before normalization is needed still work.
        Assert.Same("", Unicode.GetConfusableSkeleton(""));
        Assert.True(Unicode.AreConfusable("identical", "identical"));
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

    [Fact]
    public void UnicodeVersion_IsThePinnedVersion()
    {
        Assert.Equal(new Version(18, 0, 0), UnicodeProperties.UnicodeVersion);
    }

    [Theory]
    [InlineData(0x0041, UnicodeCategory.UppercaseLetter)]
    [InlineData(0x0061, UnicodeCategory.LowercaseLetter)]
    [InlineData(0x0030, UnicodeCategory.DecimalDigitNumber)]
    [InlineData(0x0020, UnicodeCategory.SpaceSeparator)]
    [InlineData(0x0300, UnicodeCategory.NonSpacingMark)]
    [InlineData(0x200D, UnicodeCategory.Format)]
    [InlineData(0x2028, UnicodeCategory.LineSeparator)]
    [InlineData(0xD800, UnicodeCategory.Surrogate)]
    [InlineData(0xE000, UnicodeCategory.PrivateUse)]
    [InlineData(0x0378, UnicodeCategory.OtherNotAssigned)]
    [InlineData(-1, UnicodeCategory.OtherNotAssigned)]
    [InlineData(0x110000, UnicodeCategory.OtherNotAssigned)]
    public void GetGeneralCategory_ReturnsTheCategory(int codePoint, UnicodeCategory expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetGeneralCategory(codePoint));
    }

    [Theory]
    [InlineData(0x00AD, true)]
    [InlineData(0x200B, true)]
    [InlineData(0x200D, true)]
    [InlineData(0xFE0F, true)]
    [InlineData(0xE0001, true)]
    // Unassigned, but reserved for default-ignorable characters.
    [InlineData(0xE0080, true)]
    [InlineData(0x0041, false)]
    [InlineData(0x0020, false)]
    [InlineData(-1, false)]
    public void IsDefaultIgnorable_ReturnsTheProperty(int codePoint, bool expected)
    {
        Assert.Equal(expected, UnicodeProperties.IsDefaultIgnorable(codePoint));
    }

    [Theory]
    [InlineData(0x0041, 0)]
    [InlineData(0x0300, 230)]
    [InlineData(0x0316, 220)]
    [InlineData(0x0334, 1)]
    [InlineData(0x05B0, 10)]
    [InlineData(0x094D, 9)]
    [InlineData(0x3099, 8)]
    [InlineData(0x110000, 0)]
    public void GetCanonicalCombiningClass_ReturnsTheClass(int codePoint, int expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetCanonicalCombiningClass(codePoint));
    }

    [Theory]
    [InlineData(0x0041, UnicodeBidirectionalCategory.LeftToRight)]
    [InlineData(0x05D0, UnicodeBidirectionalCategory.RightToLeft)]
    [InlineData(0x0627, UnicodeBidirectionalCategory.RightToLeftArabic)]
    [InlineData(0x0030, UnicodeBidirectionalCategory.EuropeanNumber)]
    [InlineData(0x002B, UnicodeBidirectionalCategory.EuropeanSeparator)]
    [InlineData(0x0024, UnicodeBidirectionalCategory.EuropeanTerminator)]
    [InlineData(0x0660, UnicodeBidirectionalCategory.ArabicNumber)]
    [InlineData(0x002C, UnicodeBidirectionalCategory.CommonSeparator)]
    [InlineData(0x000A, UnicodeBidirectionalCategory.ParagraphSeparator)]
    [InlineData(0x0009, UnicodeBidirectionalCategory.SegmentSeparator)]
    [InlineData(0x0020, UnicodeBidirectionalCategory.WhiteSpace)]
    [InlineData(0x0021, UnicodeBidirectionalCategory.OtherNeutral)]
    [InlineData(0x0300, UnicodeBidirectionalCategory.NonspacingMark)]
    [InlineData(0x202A, UnicodeBidirectionalCategory.LeftToRightEmbedding)]
    [InlineData(0x202D, UnicodeBidirectionalCategory.LeftToRightOverride)]
    [InlineData(0x202B, UnicodeBidirectionalCategory.RightToLeftEmbedding)]
    [InlineData(0x202E, UnicodeBidirectionalCategory.RightToLeftOverride)]
    [InlineData(0x202C, UnicodeBidirectionalCategory.PopDirectionalFormat)]
    [InlineData(0x2066, UnicodeBidirectionalCategory.LeftToRightIsolate)]
    [InlineData(0x2067, UnicodeBidirectionalCategory.RightToLeftIsolate)]
    [InlineData(0x2068, UnicodeBidirectionalCategory.FirstStrongIsolate)]
    [InlineData(0x2069, UnicodeBidirectionalCategory.PopDirectionalIsolate)]
    [InlineData(0x200B, UnicodeBidirectionalCategory.BoundaryNeutral)]
    // Noncharacters are boundary neutral.
    [InlineData(0xFFFF, UnicodeBidirectionalCategory.BoundaryNeutral)]
    [InlineData(0x0378, UnicodeBidirectionalCategory.LeftToRight)]
    [InlineData(-1, UnicodeBidirectionalCategory.LeftToRight)]
    public void GetBidiClass_ReturnsTheClass(int codePoint, UnicodeBidirectionalCategory expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetBidiClass(codePoint));
    }

    [Theory]
    // Thaana block: Arabic_Letter by default.
    [InlineData(0x07B2, UnicodeBidirectionalCategory.RightToLeftArabic)]
    // Currency Symbols block: European_Terminator by default.
    [InlineData(0x20CF, UnicodeBidirectionalCategory.EuropeanTerminator)]
    // An unallocated part of the right-to-left planes.
    [InlineData(0x10FFE, UnicodeBidirectionalCategory.RightToLeft)]
    public void GetBidiClass_UnassignedCodePointsHaveTheDefaultOfTheirRange(int codePoint, UnicodeBidirectionalCategory expected)
    {
        Assert.Equal(UnicodeCategory.OtherNotAssigned, UnicodeProperties.GetGeneralCategory(codePoint));
        Assert.False(Unicode.TryGetCharacterInfo(new Rune(codePoint), out _));
        Assert.Equal(expected, UnicodeProperties.GetBidiClass(codePoint));
    }

    [Theory]
    [InlineData(0x0028, true, 0x0029)]
    [InlineData(0x0029, true, 0x0028)]
    [InlineData(0x003C, true, 0x003E)]
    [InlineData(0x2208, true, 0x220B)]
    // The integral sign is mirrored, but no other character has the mirrored glyph.
    [InlineData(0x222B, true, -1)]
    [InlineData(0x0041, false, -1)]
    [InlineData(-1, false, -1)]
    public void BidiMirroring_ReturnsThePropertyAndTheGlyph(int codePoint, bool mirrored, int expectedGlyph)
    {
        Assert.Equal(mirrored, UnicodeProperties.IsBidiMirrored(codePoint));
        Assert.Equal(expectedGlyph >= 0, UnicodeProperties.TryGetBidiMirroringGlyph(codePoint, out var glyph));
        Assert.Equal(expectedGlyph >= 0 ? expectedGlyph : codePoint, glyph);
    }

    [Theory]
    [InlineData(0x0028, UnicodeBidiPairedBracketType.Open, 0x0029)]
    [InlineData(0x0029, UnicodeBidiPairedBracketType.Close, 0x0028)]
    [InlineData(0x005B, UnicodeBidiPairedBracketType.Open, 0x005D)]
    [InlineData(0x2329, UnicodeBidiPairedBracketType.Open, 0x232A)]
    [InlineData(0x300D, UnicodeBidiPairedBracketType.Close, 0x300C)]
    // Mirrored, but not a bracket.
    [InlineData(0x003C, UnicodeBidiPairedBracketType.None, -1)]
    [InlineData(0x0041, UnicodeBidiPairedBracketType.None, -1)]
    public void BidiPairedBracket_ReturnsTheTypeAndThePair(int codePoint, UnicodeBidiPairedBracketType type, int expectedPair)
    {
        Assert.Equal(type, UnicodeProperties.GetBidiPairedBracketType(codePoint));
        Assert.Equal(expectedPair >= 0, UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var pair));
        Assert.Equal(expectedPair >= 0 ? expectedPair : codePoint, pair);
    }

    [Theory]
    [InlineData(0x0041, UnicodeLineBreakClass.Alphabetic)]
    [InlineData(0x0020, UnicodeLineBreakClass.Space)]
    [InlineData(0x000A, UnicodeLineBreakClass.LineFeed)]
    [InlineData(0x000D, UnicodeLineBreakClass.CarriageReturn)]
    [InlineData(0x000C, UnicodeLineBreakClass.MandatoryBreak)]
    [InlineData(0x0085, UnicodeLineBreakClass.NextLine)]
    [InlineData(0x0028, UnicodeLineBreakClass.OpenPunctuation)]
    [InlineData(0x0029, UnicodeLineBreakClass.CloseParenthesis)]
    [InlineData(0x0021, UnicodeLineBreakClass.Exclamation)]
    [InlineData(0x002D, UnicodeLineBreakClass.Hyphen)]
    [InlineData(0x2010, UnicodeLineBreakClass.UnambiguousHyphen)]
    [InlineData(0x00A0, UnicodeLineBreakClass.Glue)]
    [InlineData(0x200B, UnicodeLineBreakClass.ZWSpace)]
    [InlineData(0x200D, UnicodeLineBreakClass.ZWJ)]
    [InlineData(0x2060, UnicodeLineBreakClass.WordJoiner)]
    [InlineData(0x0030, UnicodeLineBreakClass.Numeric)]
    [InlineData(0x4E00, UnicodeLineBreakClass.Ideographic)]
    [InlineData(0x3041, UnicodeLineBreakClass.ConditionalJapaneseStarter)]
    [InlineData(0x0E01, UnicodeLineBreakClass.ComplexContext)]
    [InlineData(0x1F1E6, UnicodeLineBreakClass.RegionalIndicator)]
    [InlineData(0x1F600, UnicodeLineBreakClass.Ideographic)]
    [InlineData(0x261D, UnicodeLineBreakClass.EBase)]
    [InlineData(0x1F3FB, UnicodeLineBreakClass.EModifier)]
    [InlineData(0x05D0, UnicodeLineBreakClass.HebrewLetter)]
    [InlineData(0xAC00, UnicodeLineBreakClass.H2)]
    [InlineData(0xAC01, UnicodeLineBreakClass.H3)]
    [InlineData(0x1100, UnicodeLineBreakClass.JL)]
    [InlineData(0x1160, UnicodeLineBreakClass.JV)]
    [InlineData(0x11A8, UnicodeLineBreakClass.JT)]
    [InlineData(0x2014, UnicodeLineBreakClass.BreakBoth)]
    [InlineData(0x00B4, UnicodeLineBreakClass.BreakBefore)]
    [InlineData(0x002F, UnicodeLineBreakClass.BreakSymbols)]
    [InlineData(0x002C, UnicodeLineBreakClass.InfixNumeric)]
    [InlineData(0x0025, UnicodeLineBreakClass.PostfixNumeric)]
    [InlineData(0x0024, UnicodeLineBreakClass.PrefixNumeric)]
    [InlineData(0x0022, UnicodeLineBreakClass.Quotation)]
    [InlineData(0x0300, UnicodeLineBreakClass.CombiningMark)]
    [InlineData(0x2024, UnicodeLineBreakClass.Inseparable)]
    [InlineData(0xFFFC, UnicodeLineBreakClass.ContingentBreak)]
    [InlineData(0x00A7, UnicodeLineBreakClass.Ambiguous)]
    [InlineData(0xD800, UnicodeLineBreakClass.Surrogate)]
    [InlineData(0x1B05, UnicodeLineBreakClass.Aksara)]
    [InlineData(0x11003, UnicodeLineBreakClass.AksaraPrebase)]
    [InlineData(0x1B50, UnicodeLineBreakClass.AksaraStart)]
    [InlineData(0x1B44, UnicodeLineBreakClass.Virama)]
    [InlineData(0x1BF2, UnicodeLineBreakClass.ViramaFinal)]
    [InlineData(0x0378, UnicodeLineBreakClass.Unknown)]
    // Unassigned code points of the Currency Symbols block and of the ideographic areas have a default.
    [InlineData(0x20CF, UnicodeLineBreakClass.PrefixNumeric)]
    [InlineData(0x3FFFD, UnicodeLineBreakClass.Ideographic)]
    [InlineData(-1, UnicodeLineBreakClass.Unknown)]
    public void GetLineBreakClass_ReturnsTheClass(int codePoint, UnicodeLineBreakClass expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetLineBreakClass(codePoint));
    }

    [Theory]
    [InlineData(0x0041, UnicodeEastAsianWidth.Narrow)]
    [InlineData(0x00A1, UnicodeEastAsianWidth.Ambiguous)]
    [InlineData(0xFF21, UnicodeEastAsianWidth.Fullwidth)]
    [InlineData(0xFF61, UnicodeEastAsianWidth.Halfwidth)]
    [InlineData(0x20A9, UnicodeEastAsianWidth.Halfwidth)]
    [InlineData(0x4E00, UnicodeEastAsianWidth.Wide)]
    [InlineData(0x0378, UnicodeEastAsianWidth.Neutral)]
    // Unassigned code points of the ideographic areas are wide.
    [InlineData(0x3FFFD, UnicodeEastAsianWidth.Wide)]
    public void GetEastAsianWidth_ReturnsTheWidth(int codePoint, UnicodeEastAsianWidth expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetEastAsianWidth(codePoint));
    }

    [Theory]
    [InlineData(0x0041, UnicodeGraphemeClusterBreak.Other, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x000D, UnicodeGraphemeClusterBreak.CR, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x000A, UnicodeGraphemeClusterBreak.LF, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x0000, UnicodeGraphemeClusterBreak.Control, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x0300, UnicodeGraphemeClusterBreak.Extend, UnicodeIndicConjunctBreak.Extend)]
    [InlineData(0x200D, UnicodeGraphemeClusterBreak.ZWJ, UnicodeIndicConjunctBreak.Extend)]
    [InlineData(0x1F1E6, UnicodeGraphemeClusterBreak.RegionalIndicator, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x0600, UnicodeGraphemeClusterBreak.Prepend, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x0903, UnicodeGraphemeClusterBreak.SpacingMark, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x1100, UnicodeGraphemeClusterBreak.L, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x1160, UnicodeGraphemeClusterBreak.V, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x11A8, UnicodeGraphemeClusterBreak.T, UnicodeIndicConjunctBreak.None)]
    [InlineData(0xAC00, UnicodeGraphemeClusterBreak.LV, UnicodeIndicConjunctBreak.None)]
    [InlineData(0xAC01, UnicodeGraphemeClusterBreak.LVT, UnicodeIndicConjunctBreak.None)]
    [InlineData(0x094D, UnicodeGraphemeClusterBreak.Extend, UnicodeIndicConjunctBreak.Linker)]
    [InlineData(0x0915, UnicodeGraphemeClusterBreak.Other, UnicodeIndicConjunctBreak.Consonant)]
    public void GetGraphemeClusterBreak_ReturnsTheClasses(int codePoint, UnicodeGraphemeClusterBreak expected, UnicodeIndicConjunctBreak expectedConjunct)
    {
        Assert.Equal(expected, UnicodeProperties.GetGraphemeClusterBreak(codePoint));
        Assert.Equal(expectedConjunct, UnicodeProperties.GetIndicConjunctBreak(codePoint));
    }

    [Theory]
    [InlineData(0x0041, UnicodeWordBreak.ALetter)]
    [InlineData(0x0030, UnicodeWordBreak.Numeric)]
    [InlineData(0x0027, UnicodeWordBreak.SingleQuote)]
    [InlineData(0x0022, UnicodeWordBreak.DoubleQuote)]
    [InlineData(0x003A, UnicodeWordBreak.MidLetter)]
    [InlineData(0x002E, UnicodeWordBreak.MidNumLet)]
    [InlineData(0x002C, UnicodeWordBreak.MidNum)]
    [InlineData(0x005F, UnicodeWordBreak.ExtendNumLet)]
    [InlineData(0x30A1, UnicodeWordBreak.Katakana)]
    [InlineData(0x05D0, UnicodeWordBreak.HebrewLetter)]
    [InlineData(0x000D, UnicodeWordBreak.CR)]
    [InlineData(0x000A, UnicodeWordBreak.LF)]
    [InlineData(0x000B, UnicodeWordBreak.Newline)]
    [InlineData(0x0020, UnicodeWordBreak.WSegSpace)]
    [InlineData(0x00AD, UnicodeWordBreak.Format)]
    [InlineData(0x0300, UnicodeWordBreak.Extend)]
    [InlineData(0x200D, UnicodeWordBreak.ZWJ)]
    [InlineData(0x1F1E6, UnicodeWordBreak.RegionalIndicator)]
    [InlineData(0x4E00, UnicodeWordBreak.Other)]
    public void GetWordBreak_ReturnsTheClass(int codePoint, UnicodeWordBreak expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetWordBreak(codePoint));
    }

    [Theory]
    [InlineData(0x0628, UnicodeJoiningType.DualJoining, UnicodeJoiningGroup.Beh)]
    [InlineData(0x0627, UnicodeJoiningType.RightJoining, UnicodeJoiningGroup.Alef)]
    [InlineData(0x0644, UnicodeJoiningType.DualJoining, UnicodeJoiningGroup.Lam)]
    [InlineData(0x0647, UnicodeJoiningType.DualJoining, UnicodeJoiningGroup.Heh)]
    [InlineData(0x0710, UnicodeJoiningType.RightJoining, UnicodeJoiningGroup.Alaph)]
    [InlineData(0x0640, UnicodeJoiningType.JoinCausing, UnicodeJoiningGroup.NoJoiningGroup)]
    [InlineData(0x200D, UnicodeJoiningType.JoinCausing, UnicodeJoiningGroup.NoJoiningGroup)]
    [InlineData(0x064B, UnicodeJoiningType.Transparent, UnicodeJoiningGroup.NoJoiningGroup)]
    [InlineData(0xA872, UnicodeJoiningType.LeftJoining, UnicodeJoiningGroup.NoJoiningGroup)]
    [InlineData(0x0041, UnicodeJoiningType.NonJoining, UnicodeJoiningGroup.NoJoiningGroup)]
    public void GetJoiningType_ReturnsTheTypeAndTheGroup(int codePoint, UnicodeJoiningType expectedType, UnicodeJoiningGroup expectedGroup)
    {
        Assert.Equal(expectedType, UnicodeProperties.GetJoiningType(codePoint));
        Assert.Equal(expectedGroup, UnicodeProperties.GetJoiningGroup(codePoint));
    }

    [Theory]
    [InlineData(0x0915, UnicodeIndicSyllabicCategory.Consonant, UnicodeIndicPositionalCategory.NotApplicable)]
    [InlineData(0x094D, UnicodeIndicSyllabicCategory.Virama, UnicodeIndicPositionalCategory.Bottom)]
    [InlineData(0x0902, UnicodeIndicSyllabicCategory.Bindu, UnicodeIndicPositionalCategory.Top)]
    [InlineData(0x093F, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.Left)]
    [InlineData(0x0940, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.Right)]
    [InlineData(0x0941, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.Bottom)]
    [InlineData(0x0947, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.Top)]
    [InlineData(0x09CB, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.LeftAndRight)]
    [InlineData(0x0E40, UnicodeIndicSyllabicCategory.VowelDependent, UnicodeIndicPositionalCategory.VisualOrderLeft)]
    [InlineData(0x0905, UnicodeIndicSyllabicCategory.VowelIndependent, UnicodeIndicPositionalCategory.NotApplicable)]
    [InlineData(0x0966, UnicodeIndicSyllabicCategory.Number, UnicodeIndicPositionalCategory.NotApplicable)]
    [InlineData(0x093C, UnicodeIndicSyllabicCategory.Nukta, UnicodeIndicPositionalCategory.Bottom)]
    [InlineData(0x200D, UnicodeIndicSyllabicCategory.Joiner, UnicodeIndicPositionalCategory.NotApplicable)]
    [InlineData(0x200C, UnicodeIndicSyllabicCategory.NonJoiner, UnicodeIndicPositionalCategory.NotApplicable)]
    [InlineData(0x0041, UnicodeIndicSyllabicCategory.Other, UnicodeIndicPositionalCategory.NotApplicable)]
    public void GetIndicCategories_ReturnsTheCategories(int codePoint, UnicodeIndicSyllabicCategory expectedSyllabic, UnicodeIndicPositionalCategory expectedPositional)
    {
        Assert.Equal(expectedSyllabic, UnicodeProperties.GetIndicSyllabicCategory(codePoint));
        Assert.Equal(expectedPositional, UnicodeProperties.GetIndicPositionalCategory(codePoint));
    }

    [Theory]
    [InlineData(0x0041, 0x0061, 0x0041)]
    [InlineData(0x0061, 0x0061, 0x0041)]
    [InlineData(0x00E9, 0x00E9, 0x00C9)]
    [InlineData(0x0130, 0x0069, 0x0130)]
    [InlineData(0x0131, 0x0131, 0x0049)]
    // A titlecase letter has both mappings.
    [InlineData(0x01C5, 0x01C6, 0x01C4)]
    [InlineData(0x10400, 0x10428, 0x10400)]
    // The uppercase of the sharp s is two letters: it has no simple mapping.
    [InlineData(0x00DF, 0x00DF, 0x00DF)]
    [InlineData(0x0031, 0x0031, 0x0031)]
    [InlineData(-1, -1, -1)]
    [InlineData(0x110000, 0x110000, 0x110000)]
    public void SimpleCaseMappings_ReturnTheMappedCodePoint(int codePoint, int expectedLower, int expectedUpper)
    {
        Assert.Equal(expectedLower, UnicodeProperties.ToSimpleLowercase(codePoint));
        Assert.Equal(expectedUpper, UnicodeProperties.ToSimpleUppercase(codePoint));
    }

    [Fact]
    public void Properties_AgreeWithTheCharacterInfoOfEveryCharacter()
    {
        // UnicodeCharacterInfo is decoded from UnicodeData.txt; the tables of UnicodeProperties come from the derived
        // property files, through another reader.
        var count = 0;
        foreach (var info in Unicode.AllCharacters)
        {
            var codePoint = info.Rune.Value;
            if (UnicodeProperties.GetGeneralCategory(codePoint) != info.Category
                || UnicodeProperties.GetCanonicalCombiningClass(codePoint) != info.CanonicalCombiningClass
                || UnicodeProperties.GetBidiClass(codePoint) != info.BidiCategory
                || UnicodeProperties.IsBidiMirrored(codePoint) != info.IsMirrored
                || UnicodeProperties.ToSimpleLowercase(codePoint) != (info.SimpleLowercaseMapping?.Value ?? codePoint)
                || UnicodeProperties.ToSimpleUppercase(codePoint) != (info.SimpleUppercaseMapping?.Value ?? codePoint)
                || UnicodeEmoji.IsEmoji(info.Rune) != info.IsEmoji
                || UnicodeEmoji.HasEmojiPresentation(info.Rune) != info.HasEmojiPresentation
                || UnicodeEmoji.IsEmojiModifier(info.Rune) != info.IsEmojiModifier
                || UnicodeEmoji.IsEmojiModifierBase(info.Rune) != info.IsEmojiModifierBase
                || UnicodeEmoji.IsEmojiComponent(info.Rune) != info.IsEmojiComponent
                || UnicodeEmoji.IsExtendedPictographic(info.Rune) != info.IsExtendedPictographic)
            {
                Assert.Fail($"The properties of U+{codePoint:X4} disagree with its character information");
            }

            count++;
        }

        Assert.Equal(UnicodeCharacterInfos.GeneratedCharacterCount, count);
    }

    [Fact]
    public void Properties_UnassignedCodePointsHaveNoCategoryAndNoMapping()
    {
        var count = 0;
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF || Unicode.TryGetCharacterInfo(new Rune(codePoint), out _))
                continue;

            if (UnicodeProperties.GetGeneralCategory(codePoint) != UnicodeCategory.OtherNotAssigned
                || UnicodeProperties.GetCanonicalCombiningClass(codePoint) != 0
                || UnicodeProperties.ToSimpleLowercase(codePoint) != codePoint
                || UnicodeProperties.ToSimpleUppercase(codePoint) != codePoint)
            {
                Assert.Fail($"U+{codePoint:X4} is not assigned but has properties");
            }

            count++;
        }

        Assert.Equal(0x110000 - 0x800 - UnicodeCharacterInfos.GeneratedCharacterCount, count);
    }

    [Theory]
    [InlineData(0x0041, new[] { UnicodeScript.Latin })]
    [InlineData(0x30FC, new[] { UnicodeScript.Hiragana, UnicodeScript.Katakana })]
    [InlineData(0x1CD0, new[] { UnicodeScript.Bengali, UnicodeScript.Devanagari, UnicodeScript.Grantha, UnicodeScript.Kannada })]
    // No explicit value: the script itself, even when it is Unknown.
    [InlineData(0x0378, new[] { UnicodeScript.Unknown })]
    [InlineData(-1, new[] { UnicodeScript.Unknown })]
    public void GetScriptExtensions_ReturnsTheScripts(int codePoint, UnicodeScript[] expected)
    {
        Span<UnicodeScript> buffer = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
        var count = UnicodeScripts.GetScriptExtensions(codePoint, buffer);

        Assert.Equal(expected.OrderBy(script => (int)script).ToArray(), buffer[..count].ToArray());
        foreach (var script in expected)
        {
            Assert.True(UnicodeScripts.HasScriptExtension(codePoint, script));
        }

        Assert.False(UnicodeScripts.HasScriptExtension(codePoint, UnicodeScript.Cherokee));
    }

    [Fact]
    public void GetScriptExtensions_MaxScriptExtensionCountIsEnoughForEveryCodePoint()
    {
        var buffer = new UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
        var max = 0;
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            max = Math.Max(max, UnicodeScripts.GetScriptExtensions(codePoint, buffer));
        }

        Assert.Equal(UnicodeScripts.MaxScriptExtensionCount, max);
    }

    [Fact]
    public void GetScriptExtensions_ThrowsWhenTheDestinationIsTooSmall()
    {
        Assert.Throws<ArgumentException>("destination", () => UnicodeScripts.GetScriptExtensions(0x0041, []));
        Assert.Throws<ArgumentException>("destination", () => UnicodeScripts.GetScriptExtensions(0x30FC, new UnicodeScript[1]));
    }

    [Theory]
    [InlineData(UnicodeScript.Latin, "Latn")]
    [InlineData(UnicodeScript.Han, "Hani")]
    [InlineData(UnicodeScript.Arabic, "Arab")]
    [InlineData(UnicodeScript.Common, "Zyyy")]
    [InlineData(UnicodeScript.Inherited, "Zinh")]
    [InlineData(UnicodeScript.Unknown, "Zzzz")]
    [InlineData((UnicodeScript)60000, "Zzzz")]
    public void GetIso15924Code_ReturnsTheCode(UnicodeScript script, string expected)
    {
        Assert.Equal(expected, UnicodeScripts.GetIso15924Code(script));
    }

    [Fact]
    public void GetIso15924Code_EveryScriptHasADistinctCode()
    {
        var codes = Enum.GetValues<UnicodeScript>().Select(UnicodeScripts.GetIso15924Code).ToArray();

        Assert.HasCount(codes.Length, codes.Distinct(StringComparer.Ordinal).ToArray());
        foreach (var code in codes)
        {
            if (code.Length != 4 || !code.All(char.IsAsciiLetter))
            {
                Assert.Fail($"'{code}' is not an ISO 15924 code");
            }
        }
    }

    [Fact]
    public void PropertyEnumerations_KeepTheirPublishedValues()
    {
        // The values are compiled into the assemblies that use them: they must never change. A new version of the
        // Unicode Standard only appends.
        Assert.Equal(0, (int)UnicodeScript.Unknown);
        Assert.Equal(26, (int)UnicodeScript.Common);
        Assert.Equal(59, (int)UnicodeScript.Inherited);
        Assert.Equal(0, (int)UnicodeLineBreakClass.Unknown);
        Assert.Equal(9, (int)UnicodeLineBreakClass.MandatoryBreak);
        Assert.Equal(0, (int)UnicodeGraphemeClusterBreak.Other);
        Assert.Equal(0, (int)UnicodeIndicConjunctBreak.None);
        Assert.Equal(0, (int)UnicodeWordBreak.Other);
        Assert.Equal(0, (int)UnicodeEastAsianWidth.Neutral);
        Assert.Equal(0, (int)UnicodeBidiPairedBracketType.None);
        Assert.Equal(0, (int)UnicodeJoiningType.NonJoining);
        Assert.Equal(0, (int)UnicodeJoiningGroup.NoJoiningGroup);
        Assert.Equal(0, (int)UnicodeIndicSyllabicCategory.Other);
        Assert.Equal(0, (int)UnicodeIndicPositionalCategory.NotApplicable);
    }
}
