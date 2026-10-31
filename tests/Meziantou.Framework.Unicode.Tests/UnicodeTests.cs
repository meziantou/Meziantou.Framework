namespace Meziantou.Framework.Tests;

public sealed class UnicodeTests
{
    [Fact]
    public void ReadmeExample_ReplaceConfusablesCharacters()
    {
        #region Unicode_ReplaceConfusablesCharacters
        var input = "\u0440\u0430\u0443\u0440\u0430l"; // "раураl": Cyrillic letters that look like Latin ones
        var normalized = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Equal("paypal", normalized);
        #endregion
    }

    [Fact]
    public void ReadmeExample_AreConfusable()
    {
        #region Unicode_AreConfusable
        Assert.True(Unicode.AreConfusable("paypal", "\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic look-alikes
        Assert.True(Unicode.AreConfusable("caf\u00E9", "cafe\u0301")); // composed vs decomposed
        Assert.False(Unicode.AreConfusable("paypal", "example"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_GetConfusableSkeleton()
    {
        #region Unicode_GetConfusableSkeleton
        var userName = "\u0440\u0430\u0443\u0440\u0430l";
        var skeleton = Unicode.GetConfusableSkeleton(userName);

        Assert.Equal(Unicode.GetConfusableSkeleton("paypal"), skeleton);
        #endregion
    }

    [Fact]
    public void ReadmeExample_IsMixedScript()
    {
        #region Unicode_IsMixedScript
        Assert.False(Unicode.IsMixedScript("paypal")); // all Latin
        Assert.True(Unicode.IsMixedScript("\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic letters with a Latin "l"
        Assert.False(Unicode.IsMixedScript("日本語です")); // Japanese legitimately mixes Han and Hiragana
        Assert.False(Unicode.IsMixedScript("user123")); // digits match any script
        #endregion
    }

    [Fact]
    public void ReadmeExample_Scripts()
    {
        #region Unicode_Scripts
        Assert.Equal(UnicodeScript.Latin, UnicodeScripts.GetScript(new Rune('A')));
        Assert.Equal(UnicodeScript.Cyrillic, UnicodeScripts.GetScript(new Rune(0x0410)));
        Assert.Equal("Latn", UnicodeScripts.GetIso15924Code(UnicodeScript.Latin));

        // Script_Extensions: the scripts a shared character is used with
        Span<UnicodeScript> scripts = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
        var count = UnicodeScripts.GetScriptExtensions(0x30FC, scripts);
        Assert.Equal<UnicodeScript>([UnicodeScript.Hiragana, UnicodeScript.Katakana], scripts[..count]);
        Assert.True(UnicodeScripts.HasScriptExtension(0x30FC, UnicodeScript.Katakana));

        // The names of PropertyValueAliases.txt
        Assert.True(UnicodeScripts.TryParseScript("Grek", out var script));
        Assert.Equal(UnicodeScript.Greek, script);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Properties()
    {
        #region Unicode_Properties
        Assert.Equal(UnicodeCategory.UppercaseLetter, UnicodeProperties.GetGeneralCategory('A'));
        Assert.Equal(230, UnicodeProperties.GetCanonicalCombiningClass(0x0301));
        Assert.Equal(UnicodeBidirectionalCategory.RightToLeft, UnicodeProperties.GetBidiClass(0x05D0));
        Assert.True(UnicodeProperties.TryGetBidiMirroringGlyph('(', out var mirrored));
        Assert.Equal(')', mirrored);
        Assert.Equal(UnicodeLineBreakClass.Glue, UnicodeProperties.GetLineBreakClass(0x00A0));
        Assert.Equal(UnicodeGraphemeClusterBreak.ZWJ, UnicodeProperties.GetGraphemeClusterBreak(0x200D));
        Assert.Equal(UnicodeWordBreak.ExtendNumLet, UnicodeProperties.GetWordBreak('_'));
        Assert.Equal(UnicodeEastAsianWidth.Wide, UnicodeProperties.GetEastAsianWidth(0x4E00));
        Assert.Equal(UnicodeVerticalOrientation.TransformedRotated, UnicodeProperties.GetVerticalOrientation(0x300C));
        Assert.Equal(UnicodeJoiningType.DualJoining, UnicodeProperties.GetJoiningType(0x0628));
        Assert.Equal(UnicodeIndicSyllabicCategory.Virama, UnicodeProperties.GetIndicSyllabicCategory(0x094D));
        Assert.True(UnicodeProperties.IsDefaultIgnorable(0x00AD));
        Assert.True(UnicodeProperties.IsIdStart('a')); // UAX #31
        Assert.True(UnicodeProperties.IsIdContinue('0'));
        Assert.Equal('a', UnicodeProperties.ToSimpleLowercase('A'));
        Assert.Equal(0x01C5, UnicodeProperties.ToSimpleTitlecase(0x01C6)); // 'ǆ' to 'ǅ'
        Assert.Equal('k', UnicodeProperties.ToSimpleCaseFold(0x212A)); // the Kelvin sign
        Assert.True(UnicodeProperties.HasBinaryProperty(0x00E9, UnicodeBinaryProperty.Alphabetic));
        Assert.Equal([new UnicodeRange('0', '9'), new UnicodeRange('A', 'F'), new UnicodeRange('a', 'f')], UnicodeProperties.GetBinaryPropertyRanges(UnicodeBinaryProperty.ASCIIHexDigit));
        #endregion
    }

    [Fact]
    public void ReadmeExample_CaseMappings()
    {
        #region Unicode_CaseMappings
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];

        var length = UnicodeProperties.GetUppercaseMapping('ß', mapping);
        Assert.Equal<int>(['S', 'S'], mapping[..length]);

        length = UnicodeProperties.GetTitlecaseMapping(0xFB01, mapping); // the "ﬁ" ligature
        Assert.Equal<int>(['F', 'i'], mapping[..length]);

        length = UnicodeProperties.GetLowercaseMapping(0x0130, mapping); // 'İ'
        Assert.Equal<int>(['i', 0x0307], mapping[..length]);
        #endregion
    }

    [Fact]
    public void ReadmeExample_CaseMappingOfText()
    {
        #region Unicode_CaseMappingOfText
        // The mappings that do not depend on the language, with the final sigma of Greek
        Assert.Equal("STRASSE", UnicodeCaseMapping.ToUpper("Stra\u00DFe"));
        Assert.Equal("\u03BF\u03B4\u03CC\u03C2", UnicodeCaseMapping.ToLower("\u039F\u0394\u038C\u03A3"));

        // Turkish and Azerbaijani: the dotless and the dotted I
        Assert.Equal("\u0131i", UnicodeCaseMapping.ToLower("I\u0130", "tr"));
        Assert.Equal("\u0130I", UnicodeCaseMapping.ToUpper("i\u0131", "tr-TR"));

        // Lithuanian: the i keeps its dot under an accent
        Assert.Equal("i\u0307\u0300", UnicodeCaseMapping.ToLower("\u00CC", "lt"));
        #endregion
    }

    [Theory]
    // Turkish and Azerbaijani (SpecialCasing.txt)
    [InlineData("I", "tr", "\u0131")]
    [InlineData("\u0130", "tr", "i")]
    [InlineData("I\u0307", "tr", "i")]
    [InlineData("I\u0316\u0307", "tr", "i\u0316")] // a mark below does not separate the I from the dot above
    [InlineData("I\u0301\u0307", "tr", "\u0131\u0301\u0307")] // a mark above does
    [InlineData("a\u0307", "tr", "a\u0307")]
    [InlineData("ISTANBUL \u0130ZM\u0130R", "tr", "\u0131stanbul izmir")]
    [InlineData("I\u0130", "az", "\u0131i")]
    [InlineData("I\u0130", "TR-tr", "\u0131i")]
    [InlineData("I\u0130", "tur", "\u0131i")]
    [InlineData("I\u0130", "az_Latn_AZ", "\u0131i")]
    [InlineData("I\u0130", null, "ii\u0307")]
    [InlineData("I\u0130", "en", "ii\u0307")]
    [InlineData("I\u0130", "", "ii\u0307")]
    [InlineData("I\u0130", "trx", "ii\u0307")]
    // Lithuanian (SpecialCasing.txt)
    [InlineData("\u00CC\u00CD\u0128", "lt", "i\u0307\u0300i\u0307\u0301i\u0307\u0303")]
    [InlineData("I\u0300", "lt", "i\u0307\u0300")]
    [InlineData("J\u0301", "lt", "j\u0307\u0301")]
    [InlineData("\u012E\u0301", "lt", "\u012F\u0307\u0301")]
    [InlineData("I\u0328\u0301", "lt", "i\u0307\u0328\u0301")] // the ogonek (230 is not its class) does not separate them
    [InlineData("IJ\u012E", "lt", "ij\u012F")]
    [InlineData("\u00CC", "lit-LT", "i\u0307\u0300")]
    [InlineData("\u00CC", null, "\u00EC")]
    [InlineData("I\u0300", null, "i\u0300")]
    // The final sigma, in every language (The Unicode Standard, section 3.13)
    [InlineData("\u039F\u0394\u039F\u03A3", null, "\u03BF\u03B4\u03BF\u03C2")]
    [InlineData("\u039F\u0394\u039F\u03A3", "tr", "\u03BF\u03B4\u03BF\u03C2")]
    [InlineData("\u03A3", null, "\u03C3")]
    [InlineData("\u03A3\u0391", null, "\u03C3\u03B1")]
    [InlineData("\u0391\u03A3\u0391", null, "\u03B1\u03C3\u03B1")]
    [InlineData("\u0391\u03A3 \u0391\u03A3", null, "\u03B1\u03C2 \u03B1\u03C2")]
    [InlineData("\u0391\u03A3.", null, "\u03B1\u03C2.")] // the full stop is case-ignorable
    [InlineData("\u0391.\u03A3", null, "\u03B1.\u03C2")]
    [InlineData("\u0391\u03A3\u0301", null, "\u03B1\u03C2\u0301")]
    [InlineData("\u0391\u03A3\u0301\u0391", null, "\u03B1\u03C3\u0301\u03B1")]
    [InlineData("\u0391\u03A3\u03A3", null, "\u03B1\u03C3\u03C2")]
    [InlineData("1\u03A3", null, "1\u03C3")]
    [InlineData("\U00010400\u03A3", null, "\U00010428\u03C2")] // a cased letter outside the BMP
    [InlineData(" \u02B0\u03A3", null, " \u02B0\u03C3")] // a modifier letter is cased and case-ignorable: it is ignored, as in ICU
    public void ToLower_AppliesTheConditionalMappings(string value, string? language, string expected)
    {
        Assert.Equal(expected, UnicodeCaseMapping.ToLower(value, language));
    }

    [Theory]
    [InlineData("i", "tr", "\u0130")]
    [InlineData("\u0131", "tr", "I")]
    [InlineData("istanbul izmir", "tr", "\u0130STANBUL \u0130ZM\u0130R")]
    [InlineData("i", "az", "\u0130")]
    [InlineData("i", null, "I")]
    [InlineData("i", "lt", "I")]
    // Lithuanian: the dot above after a soft-dotted letter is removed
    [InlineData("i\u0307", "lt", "I")]
    [InlineData("i\u0307\u0300", "lt", "I\u0300")]
    [InlineData("j\u0307", "lt", "J")]
    [InlineData("\u012F\u0307", "lt", "\u012E")]
    [InlineData("i\u0328\u0307", "lt", "I\u0328")]
    [InlineData("i\u0300\u0307", "lt", "I\u0300\u0307")] // a mark above separates them
    [InlineData("a\u0307", "lt", "A\u0307")]
    [InlineData("\u0307", "lt", "\u0307")]
    [InlineData("i\u0307", null, "I\u0307")]
    [InlineData("i\u0307", "tr", "\u0130\u0307")]
    // The full mappings
    [InlineData("Stra\u00DFe", null, "STRASSE")]
    [InlineData("\uFB01", null, "FI")]
    [InlineData("\u03C2", null, "\u03A3")]
    [InlineData("\U00010428", null, "\U00010400")]
    public void ToUpper_AppliesTheConditionalMappings(string value, string? language, string expected)
    {
        Assert.Equal(expected, UnicodeCaseMapping.ToUpper(value, language));
    }

    [Fact]
    public void CaseMapping_OfOneCodePoint_IsTheFullMapping()
    {
        // Without context and without language, the conversion of one code point is its full case mapping.
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                var surrogate = ((char)codePoint).ToString();
                Assert.Same(surrogate, UnicodeCaseMapping.ToUpper(surrogate));
                Assert.Same(surrogate, UnicodeCaseMapping.ToLower(surrogate));
                continue;
            }

            var text = char.ConvertFromUtf32(codePoint);
            var length = UnicodeProperties.GetUppercaseMapping(codePoint, mapping);
            Assert.Equal(ToString(mapping[..length]), UnicodeCaseMapping.ToUpper(text));

            length = UnicodeProperties.GetLowercaseMapping(codePoint, mapping);
            Assert.Equal(ToString(mapping[..length]), UnicodeCaseMapping.ToLower(text));
        }
    }

    [Fact]
    public void CaseMapping_ReturnsTheSameInstanceWhenNothingChanges()
    {
        var text = new string(['a', 'b', 'c', ' ', '1', '\u00E9', (char)0xD800]);
        Assert.Same(text, UnicodeCaseMapping.ToLower(text));
        Assert.Same(text, UnicodeCaseMapping.ToLower(text, "tr"));

        text = new string(['A', 'B', 'C', ' ', '1', '\u00C9', (char)0xDC00]);
        Assert.Same(text, UnicodeCaseMapping.ToUpper(text));
        Assert.Same(text, UnicodeCaseMapping.ToUpper(text, "lt"));

        Assert.Same(string.Empty, UnicodeCaseMapping.ToUpper(string.Empty));
        Assert.Equal("ABC\u00C9" + (char)0xD800, UnicodeCaseMapping.ToUpper("abc\u00E9" + (char)0xD800));
    }

    [Fact]
    public void CaseMapping_ThrowsForANullText()
    {
        Assert.Throws<ArgumentNullException>("value", () => UnicodeCaseMapping.ToUpper(null!));
        Assert.Throws<ArgumentNullException>("value", () => UnicodeCaseMapping.ToLower(null!, "tr"));
    }

    [Theory]
    [InlineData('A', true, false)]
    [InlineData('a', true, false)]
    [InlineData(0x01C5, true, false)] // a titlecase letter
    [InlineData(0x2160, true, false)] // Other_Uppercase
    [InlineData(0x02B0, true, true)] // a modifier letter with Other_Lowercase
    [InlineData(0x0301, false, true)]
    [InlineData(0x00AD, false, true)]
    [InlineData('\'', false, true)]
    [InlineData('.', false, true)]
    [InlineData(':', false, true)]
    [InlineData('1', false, false)]
    [InlineData(' ', false, false)]
    [InlineData(0x05D0, false, false)]
    [InlineData(0x4E00, false, false)]
    [InlineData(-1, false, false)]
    [InlineData(0x110000, false, false)]
    public void IsCased_IsCaseIgnorable_ReturnTheProperties(int codePoint, bool cased, bool caseIgnorable)
    {
        Assert.Equal(cased, UnicodeProperties.IsCased(codePoint));
        Assert.Equal(caseIgnorable, UnicodeProperties.IsCaseIgnorable(codePoint));
    }

    [Theory]
    [InlineData('i', true)]
    [InlineData('j', true)]
    [InlineData(0x012F, true)]
    [InlineData(0x0456, true)]
    [InlineData(0x1D422, true)]
    [InlineData(0x1E068, true)]
    [InlineData('I', false)]
    [InlineData('k', false)]
    [InlineData(0x0131, false)]
    [InlineData(-1, false)]
    [InlineData(0x110000, false)]
    public void IsSoftDotted_ReturnsTheProperty(int codePoint, bool expected)
    {
        Assert.Equal(expected, UnicodeProperties.IsSoftDotted(codePoint));
    }

    [Fact]
    public void EmojiProperties_AreNotChangedByTheCaseFlags()
    {
        // The case flags share their byte with the emoji properties.
        Assert.True(UnicodeEmoji.IsEmoji(0x1F600));
        Assert.False(UnicodeEmoji.IsEmoji('a'));
        Assert.Equal(EmojiProperties.None, UnicodeProperties.GetEmojiProperties(0x02B0));
    }

    [Fact]
    public void ReadmeExample_Segmentation()
    {
        #region Unicode_Segmentation
        // Extended grapheme clusters: what a reader perceives as one character
        var text = "e\u0301\U0001F1EB\U0001F1F7"; // "e" and a combining acute accent, then the French flag
        var clusters = new List<string>();
        foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
        {
            clusters.Add(text[cluster]);
        }

        Assert.Equal(["e\u0301", "\U0001F1EB\U0001F1F7"], clusters);

        // Word boundaries: words, numbers, runs of spaces, punctuation marks
        text = "Hello, world!";
        var segments = new List<string>();
        foreach (Range segment in Unicode.EnumerateWordBoundaries(text))
        {
            segments.Add(text[segment]);
        }

        Assert.Equal(["Hello", ",", " ", "world", "!"], segments);

        // The kind of each segment: whether it is a word (isWordLike in Intl.Segmenter)
        text = "I have 2 cats.";
        var words = new List<string>();
        var enumerator = Unicode.EnumerateWordBoundaries(text);
        while (enumerator.MoveNext())
        {
            if (enumerator.CurrentKind is not UnicodeWordKind.None) // Number, Letter, Kana or Ideographic
            {
                words.Add(text[enumerator.Current]);
            }
        }

        Assert.Equal(["I", "have", "2", "cats"], words);

        // Sentences: the spaces after the terminal punctuation belong to the sentence
        text = "It costs $3.50, i.e. not much. Really? Yes!";
        var sentences = new List<string>();
        foreach (Range sentence in Unicode.EnumerateSentenceBoundaries(text))
        {
            sentences.Add(text[sentence]);
        }

        Assert.Equal(["It costs $3.50, i.e. not much. ", "Really? ", "Yes!"], sentences);

        // Line break opportunities
        text = "A long line\nof text";
        var lines = new List<string>();
        var lineStart = 0;
        foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text))
        {
            // lineBreak.Index: the index of the first character of the next line
            // lineBreak.IsMandatory: after a line feed, and at the end of the text
            lines.Add(text[lineStart..lineBreak.Index] + (lineBreak.IsMandatory ? " (mandatory)" : ""));
            lineStart = lineBreak.Index;
        }

        Assert.Equal(["A ", "long ", "line\n (mandatory)", "of ", "text (mandatory)"], lines);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ScriptRuns()
    {
        #region Unicode_ScriptRuns
        var text = "gamma (\u03B3) is";
        var runs = new List<string>();
        foreach (UnicodeScriptRun run in Unicode.EnumerateScriptRuns(text))
        {
            runs.Add($"{text.Substring(run.Index, run.Length)}: {run.Script}");
        }

        Assert.Equal(["gamma (: Latin", "\u03B3: Greek", ") is: Latin"], runs);
        #endregion
    }

    [Fact]
    public void ReadmeExample_LineBreakDictionary()
    {
        #region Unicode_LineBreakDictionary
        // The word list, as plain text: one word per line.
        // Use LineBreakDictionary.Load("thaidict.txt") to read it from a file.
        var dictionary = LineBreakDictionary.Parse("ภาษา\nไทย");

        // The line break opportunities of a text: those of UAX #14, and those before the words of the dictionary
        var breaks = new List<int>();
        foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks("Hello ภาษาไทย!", dictionary))
        {
            breaks.Add(lineBreak.Index);
        }

        Assert.Equal([6, 10, 14], breaks); // after the space, before "ไทย", the end of the text

        // The words of a run of text of the script of the dictionary
        Span<int> positions = stackalloc int[16];
        var count = dictionary.FindBreaks("ภาษาไทย", positions);
        Assert.Equal<int>([4], positions[..count]);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Normalization()
    {
        #region Unicode_Normalization
        Assert.Equal("caf\u00E9", UnicodeNormalization.Normalize("cafe\u0301")); // NFC
        Assert.Equal("cafe\u0301", UnicodeNormalization.Normalize("caf\u00E9", NormalizationForm.FormD));
        Assert.True(UnicodeNormalization.IsNormalized("caf\u00E9"));

        // Span-based
        Span<char> destination = stackalloc char[16];
        Assert.True(UnicodeNormalization.TryNormalize("cafe\u0301", destination, out var written));
        Assert.Equal("caf\u00E9", destination[..written]);

        Assert.True(UnicodeNormalization.TryComposeCanonical('e', 0x0301, out var composite));
        Assert.Equal('\u00E9', composite);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Bidi()
    {
        #region Unicode_Bidi
        var paragraph = "car \u05D0\u05D1\u05D2!";
        Span<byte> levels = stackalloc byte[paragraph.Length];
        var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, UnicodeBidiDirection.Auto, levels);

        // For each line, once the paragraph is broken into lines:
        UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
        Span<int> visualToLogical = stackalloc int[paragraph.Length];
        UnicodeBidi.GetVisualOrder(levels, visualToLogical);

        Assert.Equal<int>([0, 1, 2, 3, 6, 5, 4, 7], visualToLogical);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Idna()
    {
        #region Unicode_Idna
        Assert.True(UnicodeIdna.TryToAscii("B\u00FCcher.example", out var ascii));
        Assert.Equal("xn--bcher-kva.example", ascii);

        Assert.Equal("b\u00FCcher.example", UnicodeIdna.ToUnicode("xn--bcher-kva.example", out var hasErrors));
        Assert.False(hasErrors);

        // The flags of UTS #46: the defaults enable every check, with nontransitional processing
        var options = new UnicodeIdnaOptions { UseStd3AsciiRules = false, VerifyDnsLength = false };
        Assert.True(UnicodeIdna.TryToAscii("exa_mple.com", options, out ascii));
        Assert.Equal("exa_mple.com", ascii);

        Assert.True(UnicodeIdna.TryEncodePunycode("b\u00FCcher", out var encoded));
        Assert.Equal("bcher-kva", encoded);
        Assert.True(UnicodeIdna.TryDecodePunycode("bcher-kva", out var decoded));
        Assert.Equal("b\u00FCcher", decoded);
        #endregion
    }

    [Fact]
    public void ReadmeExample_GetCharacterInfo()
    {
        #region Unicode_GetCharacterInfo
        var info = Unicode.GetCharacterInfo(new Rune('A'));

        Assert.NotNull(info);
        Assert.Equal("LATIN CAPITAL LETTER A", info.Value.Name);
        Assert.Equal(UnicodeCategory.UppercaseLetter, info.Value.Category);
        Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.Value.BidiCategory);
        Assert.Equal("Basic Latin", info.Value.Block.Name);
        #endregion
    }

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region Unicode_GettingStarted
        var text = "e\u0301\U0001F44D\U0001F3FD"; // "e" and a combining acute accent, then a thumbs up with a skin tone
        Assert.HasCount(6, text); // 6 UTF-16 code units, not characters

        // What a reader sees as characters: the grapheme clusters
        var characters = new List<string>();
        foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
        {
            characters.Add(text[cluster]);
        }

        Assert.Equal(["e\u0301", "\U0001F44D\U0001F3FD"], characters);

        // The composed form of the text, the same on every platform
        Assert.Equal("\u00E9\U0001F44D\U0001F3FD", UnicodeNormalization.Normalize(text));
        #endregion
    }

    [Fact]
    public void Documentation_BlocksAndEmoji()
    {
        #region Unicode_BlocksAndEmoji
        Assert.True(UnicodeBlocks.BasicLatin.Contains('A'));
        Assert.Equal(new UnicodeRange(0x0000, 0x007F), UnicodeBlocks.BasicLatin.Range);

        Assert.True(UnicodeEmoji.IsEmoji(0x1F600));
        Assert.True(UnicodeEmoji.HasEmojiPresentation(0x1F600));
        Assert.False(UnicodeEmoji.HasEmojiPresentation(0x2764)); // the heart is shown as text unless followed by U+FE0F

        // The emoji that are sequences of code points, such as flags
        var flags = UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiFlagSequence);
        Assert.Contains("\U0001F1EB\U0001F1F7", flags);
        #endregion
    }

    [Fact]
    public void Documentation_TruncateText()
    {
        #region Unicode_TruncateText
        static string Truncate(string text, int maxLength)
        {
            // Keep whole grapheme clusters: an accent or an emoji is never cut in half
            var length = 0;
            foreach (Range cluster in Unicode.EnumerateGraphemeClusters(text))
            {
                var end = cluster.End.GetOffset(text.Length);
                if (end > maxLength)
                    break;

                length = end;
            }

            return text[..length];
        }

        Assert.Equal("Caf", Truncate("Cafe\u0301 au lait", 4)); // not "Cafe" without its accent
        Assert.Equal("Go ", Truncate("Go \U0001F1EB\U0001F1F7", 5)); // not half of the flag
        #endregion
    }

    [Fact]
    public void Documentation_WrapText()
    {
        #region Unicode_WrapText
        static List<string> Wrap(string text, int maxLength)
        {
            var lines = new List<string>();
            var lineStart = 0;
            var previousBreak = 0;
            foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text))
            {
                // The line would be too long: break at the previous opportunity
                if (lineBreak.Index - lineStart > maxLength && previousBreak > lineStart)
                {
                    lines.Add(text[lineStart..previousBreak].TrimEnd());
                    lineStart = previousBreak;
                }

                if (lineBreak.IsMandatory)
                {
                    lines.Add(text[lineStart..lineBreak.Index].TrimEnd());
                    lineStart = lineBreak.Index;
                }

                previousBreak = lineBreak.Index;
            }

            return lines;
        }

        Assert.Equal(["The quick brown", "fox jumps over", "the lazy dog."], Wrap("The quick brown fox jumps over the lazy dog.", 16));
        Assert.Equal(["See https://", "www.meziantou.net/", "blog"], Wrap("See https://www.meziantou.net/blog", 16));
        #endregion
    }

    [Fact]
    public void Documentation_LineBreakTailorings()
    {
        #region Unicode_LineBreakTailorings
        static List<string> Split(string text, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
        {
            var segments = new List<string>();
            var start = 0;
            foreach (UnicodeLineBreak lineBreak in Unicode.EnumerateLineBreaks(text, strictness, wordBreak))
            {
                segments.Add(text[start..lineBreak.Index]);
                start = lineBreak.Index;
            }

            return segments;
        }

        // Ideographs: a line can break between any two of them, unless KeepAll keeps the words together
        var japanese = "\u65E5\u672C\u8A9E";
        Assert.Equal(["\u65E5", "\u672C", "\u8A9E"], Split(japanese));
        Assert.Equal(["\u65E5\u672C\u8A9E"], Split(japanese, wordBreak: UnicodeWordBreakBehavior.KeepAll));

        // BreakAll: a line can break between two letters, as in a table cell that must not overflow
        Assert.Equal(["a", "b", "c"], Split("abc", wordBreak: UnicodeWordBreakBehavior.BreakAll));

        // A small kana starts no line with the strict rules; Normal allows it
        var katakana = "\u30C1\u30E3";
        Assert.Equal(["\u30C1\u30E3"], Split(katakana));
        Assert.Equal(["\u30C1", "\u30E3"], Split(katakana, UnicodeLineBreakStrictness.Normal));
        #endregion
    }

    [Fact]
    public void Documentation_CompatibilityNormalization()
    {
        #region Unicode_CompatibilityNormalization
        // NFKC also replaces the characters that differ only by their presentation
        Assert.Equal("fi", UnicodeNormalization.Normalize("\uFB01", NormalizationForm.FormKC)); // the "fi" ligature
        Assert.Equal("x2", UnicodeNormalization.Normalize("x\u00B2", NormalizationForm.FormKC)); // a superscript two
        Assert.Equal("ABC", UnicodeNormalization.Normalize("\uFF21\uFF22\uFF23", NormalizationForm.FormKC)); // full-width letters

        // NFC keeps them
        Assert.Equal("\uFB01", UnicodeNormalization.Normalize("\uFB01"));
        #endregion
    }

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
    public void ReplaceConfusablesCharacters_RuneOverload_ReusesTheReplacement()
    {
        var replacement = Unicode.ReplaceConfusablesCharacters(new Rune(0x00BD));
        Assert.Equal("l/2", replacement);
        Assert.Same(replacement, Unicode.ReplaceConfusablesCharacters(new Rune(0x00BD)));
        Assert.Equal("A", Unicode.ReplaceConfusablesCharacters('A'));
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
    public void UnicodeCharacterInfo_DefaultHasNoProperties()
    {
        // The fields are read from the tables on demand; a default instance must not read the entry for U+0000.
        var info = default(UnicodeCharacterInfo);

        Assert.Null(info.Name);
        Assert.Equal(UnicodeCategory.UppercaseLetter, info.Category);
        Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.BidiCategory);
        Assert.Null(info.Block);
        Assert.Equal(0, info.CanonicalCombiningClass);
        Assert.Null(info.DecompositionMapping);
        Assert.Equal(0, info.DecimalDigitValue);
        Assert.Equal(0, info.DigitValue);
        Assert.Null(info.NumericValue);
        Assert.False(info.IsMirrored);
        Assert.Null(info.Unicode1Name);
        Assert.Null(info.IsoComment);
        Assert.Null(info.SimpleUppercaseMapping);
        Assert.Null(info.SimpleLowercaseMapping);
        Assert.Null(info.SimpleTitlecaseMapping);
        Assert.False(info.IsEmoji);
        Assert.False(info.IsExtendedPictographic);
    }

    [Fact]
    public void TryGetCharacterInfo_DoesNotAllocate()
    {
        Assert.True(Unicode.TryGetCharacterInfo(new Rune(0x1F600), out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var codePoint = 0; codePoint < 0x3000; codePoint += 7)
        {
            if (Unicode.TryGetCharacterInfo(new Rune(codePoint), out var info))
            {
                sum += (int)info.Category + info.CanonicalCombiningClass + (info.IsMirrored ? 1 : 0) + (info.SimpleUppercaseMapping?.Value ?? 0);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.NotEqual(0, sum);
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
        Assert.True(UnicodeEmoji.HasEmojiPresentation(0x1F600));
        Assert.False(UnicodeEmoji.HasEmojiPresentation('A'));
        Assert.False(UnicodeEmoji.HasEmojiPresentation(0xD800));
        Assert.False(UnicodeEmoji.HasEmojiPresentation(-1));
    }

    [Fact]
    public void IsEmojiModifier_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiModifier(new Rune(0x1F3FB)));
        Assert.False(UnicodeEmoji.IsEmojiModifier(new Rune('A')));
        Assert.True(UnicodeEmoji.IsEmojiModifier(0x1F3FB));
        Assert.False(UnicodeEmoji.IsEmojiModifier('A'));
        Assert.False(UnicodeEmoji.IsEmojiModifier(0xD800));
        Assert.False(UnicodeEmoji.IsEmojiModifier(-1));
    }

    [Fact]
    public void IsEmojiModifierBase_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiModifierBase(new Rune(0x1F44B)));
        Assert.False(UnicodeEmoji.IsEmojiModifierBase(new Rune('A')));
        Assert.True(UnicodeEmoji.IsEmojiModifierBase(0x1F44B));
        Assert.False(UnicodeEmoji.IsEmojiModifierBase('A'));
        Assert.False(UnicodeEmoji.IsEmojiModifierBase(0xD800));
        Assert.False(UnicodeEmoji.IsEmojiModifierBase(-1));
    }

    [Fact]
    public void IsEmojiComponent_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsEmojiComponent(new Rune(0x0023)));
        Assert.False(UnicodeEmoji.IsEmojiComponent(new Rune('A')));
        Assert.True(UnicodeEmoji.IsEmojiComponent(0x0023));
        Assert.False(UnicodeEmoji.IsEmojiComponent('A'));
        Assert.False(UnicodeEmoji.IsEmojiComponent(0xD800));
        Assert.False(UnicodeEmoji.IsEmojiComponent(-1));
    }

    [Fact]
    public void IsExtendedPictographic_ReturnsExpectedValue()
    {
        Assert.True(UnicodeEmoji.IsExtendedPictographic(new Rune(0x1F600)));
        Assert.False(UnicodeEmoji.IsExtendedPictographic(new Rune('A')));
        Assert.True(UnicodeEmoji.IsExtendedPictographic(0x1F600));
        Assert.False(UnicodeEmoji.IsExtendedPictographic('A'));
        Assert.False(UnicodeEmoji.IsExtendedPictographic(0xD800));
        Assert.False(UnicodeEmoji.IsExtendedPictographic(-1));
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
                failures.Add($"U+{info.Rune.Value:X4}: lookup returned a different entry");
            }
            else if (!string.Equals(found.Name, info.Name, StringComparison.Ordinal))
            {
                // The enumerator decodes the names in order rather than looking each one up.
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
    }

    [Fact]
    public void GetConfusableSkeleton_RejectsUnpairedSurrogates()
    {
        Assert.Throws<ArgumentException>("value", () => Unicode.GetConfusableSkeleton("a\uD800"));
        Assert.Throws<ArgumentException>("value", () => Unicode.GetConfusableSkeleton("\uDC00a"));
        Assert.Equal("\U0001F600", Unicode.GetConfusableSkeleton("\U0001F600"));
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
    [InlineData(0x0041, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x0020, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x05D0, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x1820, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x2014, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x4E00, UnicodeVerticalOrientation.Upright)]
    [InlineData(0x3042, UnicodeVerticalOrientation.Upright)]
    [InlineData(0xAC00, UnicodeVerticalOrientation.Upright)]
    [InlineData(0xFF10, UnicodeVerticalOrientation.Upright)]
    [InlineData(0x3000, UnicodeVerticalOrientation.Upright)]
    [InlineData(0x00A9, UnicodeVerticalOrientation.Upright)]
    [InlineData(0x1F600, UnicodeVerticalOrientation.Upright)]
    // Punctuation that has another glyph in a vertical line, which stays upright or, without that glyph, is rotated.
    [InlineData(0x3001, UnicodeVerticalOrientation.TransformedUpright)]
    [InlineData(0x3041, UnicodeVerticalOrientation.TransformedUpright)]
    [InlineData(0x300C, UnicodeVerticalOrientation.TransformedRotated)]
    [InlineData(0x30FC, UnicodeVerticalOrientation.TransformedRotated)]
    [InlineData(0xFF08, UnicodeVerticalOrientation.TransformedRotated)]
    // Unassigned code points are rotated, except in the areas of the ideographs and of the symbols, where they are upright.
    [InlineData(0x0378, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x3FFFD, UnicodeVerticalOrientation.Upright)]
    [InlineData(-1, UnicodeVerticalOrientation.Rotated)]
    [InlineData(0x110000, UnicodeVerticalOrientation.Rotated)]
    public void GetVerticalOrientation_ReturnsTheOrientation(int codePoint, UnicodeVerticalOrientation expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetVerticalOrientation(codePoint));
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
    [InlineData(0x0061, UnicodeSentenceBreak.Lower)]
    [InlineData(0x0041, UnicodeSentenceBreak.Upper)]
    [InlineData(0x05D0, UnicodeSentenceBreak.OLetter)]
    [InlineData(0x0030, UnicodeSentenceBreak.Numeric)]
    [InlineData(0x002E, UnicodeSentenceBreak.ATerm)]
    [InlineData(0x003F, UnicodeSentenceBreak.STerm)]
    [InlineData(0x3002, UnicodeSentenceBreak.STerm)]
    [InlineData(0x0029, UnicodeSentenceBreak.Close)]
    [InlineData(0x002C, UnicodeSentenceBreak.SContinue)]
    [InlineData(0x0020, UnicodeSentenceBreak.Sp)]
    [InlineData(0x2029, UnicodeSentenceBreak.Sep)]
    [InlineData(0x000D, UnicodeSentenceBreak.CR)]
    [InlineData(0x000A, UnicodeSentenceBreak.LF)]
    [InlineData(0x0301, UnicodeSentenceBreak.Extend)]
    [InlineData(0x00AD, UnicodeSentenceBreak.Format)]
    [InlineData(0x0024, UnicodeSentenceBreak.Other)]
    [InlineData(-1, UnicodeSentenceBreak.Other)]
    [InlineData(0x110000, UnicodeSentenceBreak.Other)]
    public void GetSentenceBreak_ReturnsTheClass(int codePoint, UnicodeSentenceBreak expected)
    {
        Assert.Equal(expected, UnicodeProperties.GetSentenceBreak(codePoint));
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
    public void AsciiCharacters_HaveThePropertiesOfTheAsciiFastPaths()
    {
        // The ASCII fast paths of the segmentation, the script runs, IsSingleScript and IDNA rely on these values.
        var buffer = new UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
        for (var codePoint = 0; codePoint < 0x80; codePoint++)
        {
            var c = (char)codePoint;
            var expectedScript = char.IsAsciiLetter(c) ? UnicodeScript.Latin : UnicodeScript.Common;
            Assert.Equal(1, UnicodeScripts.GetScriptExtensions(codePoint, buffer));
            Assert.Equal(expectedScript, buffer[0]);

            if (char.IsAsciiLetter(c))
            {
                Assert.Equal(UnicodeLineBreakClass.Alphabetic, UnicodeProperties.GetLineBreakClass(codePoint));
                Assert.Equal(UnicodeWordBreak.ALetter, UnicodeProperties.GetWordBreak(codePoint));
                Assert.Equal(UnicodeEastAsianWidth.Narrow, UnicodeProperties.GetEastAsianWidth(codePoint));
            }
            else if (char.IsAsciiDigit(c))
            {
                Assert.Equal(UnicodeLineBreakClass.Numeric, UnicodeProperties.GetLineBreakClass(codePoint));
                Assert.Equal(UnicodeWordBreak.Numeric, UnicodeProperties.GetWordBreak(codePoint));
                Assert.Equal(UnicodeEastAsianWidth.Narrow, UnicodeProperties.GetEastAsianWidth(codePoint));
            }

            if (c is >= ' ' and <= '~')
            {
                Assert.False(UnicodeProperties.GetLineBreakClass(codePoint) is UnicodeLineBreakClass.CombiningMark or UnicodeLineBreakClass.ZWJ);
            }
        }

        Assert.Equal(UnicodeWordBreak.ExtendNumLet, UnicodeProperties.GetWordBreak('_'));
    }

    [Fact]
    public void CodePointsBelowU0300_NeverExtendAGraphemeCluster()
    {
        // GetGraphemeClusterLength breaks between two of them without looking them up.
        for (var codePoint = 0; codePoint < 0x300; codePoint++)
        {
            Assert.True(UnicodeProperties.GetGraphemeClusterBreak(codePoint) is UnicodeGraphemeClusterBreak.Other or UnicodeGraphemeClusterBreak.Control
                or UnicodeGraphemeClusterBreak.CR or UnicodeGraphemeClusterBreak.LF);
            Assert.Equal(UnicodeIndicConjunctBreak.None, UnicodeProperties.GetIndicConjunctBreak(codePoint));
        }
    }

    [Fact]
    public void CodePointsBelowU0590_AreNotRightToLeft()
    {
        // MayRequireBidi, and the shortcut of ResolveLevels for left-to-right text, skip them.
        for (var codePoint = 0; codePoint < 0x590; codePoint++)
        {
            Assert.False(UnicodeProperties.GetBidiClass(codePoint) is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic
                or UnicodeBidirectionalCategory.ArabicNumber or UnicodeBidirectionalCategory.LeftToRightEmbedding or UnicodeBidirectionalCategory.LeftToRightOverride
                or UnicodeBidirectionalCategory.RightToLeftEmbedding or UnicodeBidirectionalCategory.RightToLeftOverride
                or UnicodeBidirectionalCategory.LeftToRightIsolate or UnicodeBidirectionalCategory.RightToLeftIsolate or UnicodeBidirectionalCategory.FirstStrongIsolate);
        }
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
        Assert.Equal(0, (int)UnicodeSentenceBreak.Other);
        Assert.Equal(0, (int)UnicodeEastAsianWidth.Neutral);
        Assert.Equal(0, (int)UnicodeBidiPairedBracketType.None);
        Assert.Equal(0, (int)UnicodeJoiningType.NonJoining);
        Assert.Equal(0, (int)UnicodeJoiningGroup.NoJoiningGroup);
        Assert.Equal(0, (int)UnicodeIndicSyllabicCategory.Other);
        Assert.Equal(0, (int)UnicodeIndicPositionalCategory.NotApplicable);
    }

    [Theory]
    [InlineData("", new int[0])]
    [InlineData("abc", new[] { 1, 1, 1 })]
    // A base letter and its combining mark.
    [InlineData("e\u0301a", new[] { 2, 1 })]
    [InlineData("\r\n\n", new[] { 2, 1 })]
    // Two flags: each is a pair of regional indicators.
    [InlineData("\U0001F1EB\U0001F1F7\U0001F1E9\U0001F1EA", new[] { 4, 4 })]
    // An emoji with a skin tone, then a family joined by zero width joiners.
    [InlineData("\U0001F44B\U0001F3FD\U0001F468\u200D\U0001F469\u200D\U0001F467", new[] { 4, 8 })]
    // A Hangul syllable written with conjoining jamos.
    [InlineData("\u1100\u1161\u11A8\uAC00", new[] { 3, 1 })]
    // A Devanagari conjunct: consonant, virama, consonant.
    [InlineData("\u0915\u094D\u0937\u093F", new[] { 4 })]
    // The last ASCII letter takes the mark that follows it.
    [InlineData("ab\u0301", new[] { 1, 2 })]
    [InlineData("a\r\n", new[] { 1, 2 })]
    [InlineData("\u00E9\u0301x", new[] { 2, 1 })]
    public void EnumerateGraphemeClusters_ReturnsTheClusters(string text, int[] expectedLengths)
    {
        var lengths = new List<int>();
        var position = 0;
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            Assert.Equal(position, range.Start.Value);
            lengths.Add(range.End.Value - range.Start.Value);
            position = range.End.Value;
        }

        Assert.Equal(expectedLengths, lengths.ToArray());
        Assert.Equal(text.Length, position);
        Assert.Equal(expectedLengths.Length == 0 ? 0 : expectedLengths[0], Unicode.GetGraphemeClusterLength(text));
    }

    [Fact]
    public void Segmentation_TreatsAnUnpairedSurrogateAsACharacter()
    {
        // Built in code: test data that goes through UTF-8 cannot hold an unpaired surrogate.
        var text = "a" + (char)0xD800 + "e" + (char)0x0301 + (char)0xDC00 + " b";

        var clusters = new List<int>();
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            clusters.Add(range.End.Value);
        }

        Assert.Equal(new[] { 1, 2, 4, 5, 6, 7 }, clusters.ToArray());

        var lineBreaks = new List<int>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text))
        {
            lineBreaks.Add(lineBreak.Index);
        }

        Assert.Equal(new[] { 6, 7 }, lineBreaks.ToArray());

        var runs = 0;
        foreach (var _ in Unicode.EnumerateScriptRuns(text))
        {
            runs++;
        }

        Assert.True(runs >= 1);
        Assert.Equal(text.Length, UnicodeBidi.GetWorkspaceLength(text.Length) / 3);
        Assert.Equal(0, UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, new byte[text.Length]));

        // Normalization copies it and composes around it.
        var composed = UnicodeNormalization.Normalize(text, NormalizationForm.FormC);
        Assert.Equal("a" + (char)0xD800 + (char)0x00E9 + (char)0xDC00 + " b", composed);
        Assert.Same(composed, UnicodeNormalization.Normalize(composed, NormalizationForm.FormC));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("Hello, world!", new[] { "Hello", ",", " ", "world", "!" })]
    [InlineData("can't stop", new[] { "can't", " ", "stop" })]
    [InlineData("3.14 or 1,000", new[] { "3.14", " ", "or", " ", "1,000" })]
    [InlineData("example.com.", new[] { "example.com", "." })]
    [InlineData("snake_case42", new[] { "snake_case42" })]
    [InlineData("a  b", new[] { "a", "  ", "b" })]
    // Ideographs are not grouped into words: there is no dictionary.
    [InlineData("\u65E5\u672C", new[] { "\u65E5", "\u672C" })]
    [InlineData("a\r\nb", new[] { "a", "\r\n", "b" })]
    // A mark after an ASCII word is ignored, and the word goes on after it.
    [InlineData("hello\u0301world", new[] { "hello\u0301world" })]
    [InlineData("abc.def", new[] { "abc.def" })]
    [InlineData("a1_b-c", new[] { "a1_b", "-", "c" })]
    [InlineData("x\u05D0y", new[] { "x\u05D0y" })]
    [InlineData("\u00E9t\u00E9", new[] { "\u00E9t\u00E9" })]
    public void EnumerateWordBoundaries_ReturnsTheSegments(string text, string[] expected)
    {
        var segments = new List<string>();
        foreach (var range in Unicode.EnumerateWordBoundaries(text))
        {
            segments.Add(text[range]);
        }

        Assert.Equal(expected, segments.ToArray());
    }

    [Theory]
    [InlineData("Hello, world!", new[] { "Hello", ",", " ", "world", "!" }, new[] { UnicodeWordKind.Letter, UnicodeWordKind.None, UnicodeWordKind.None, UnicodeWordKind.Letter, UnicodeWordKind.None })]
    [InlineData("3.14 or 1,000", new[] { "3.14", " ", "or", " ", "1,000" }, new[] { UnicodeWordKind.Number, UnicodeWordKind.None, UnicodeWordKind.Letter, UnicodeWordKind.None, UnicodeWordKind.Number })]
    // The kind is the one of the last character.
    [InlineData("a1 1a can't", new[] { "a1", " ", "1a", " ", "can't" }, new[] { UnicodeWordKind.Number, UnicodeWordKind.None, UnicodeWordKind.Letter, UnicodeWordKind.None, UnicodeWordKind.Letter })]
    [InlineData("snake_case42", new[] { "snake_case42" }, new[] { UnicodeWordKind.Number })]
    // A low line takes the kind of the character before it, and is not a word on its own.
    [InlineData("a_ 1_ _ __", new[] { "a_", " ", "1_", " ", "_", " ", "__" }, new[] { UnicodeWordKind.Letter, UnicodeWordKind.None, UnicodeWordKind.Number, UnicodeWordKind.None, UnicodeWordKind.None, UnicodeWordKind.None, UnicodeWordKind.Letter })]
    [InlineData("\u30AB_", new[] { "\u30AB_" }, new[] { UnicodeWordKind.Kana })]
    // Hebrew letters, and a Hebrew letter with a single quote (WB7a).
    [InlineData("\u05D0\u05D1 \u05D2'", new[] { "\u05D0\u05D1", " ", "\u05D2'" }, new[] { UnicodeWordKind.Letter, UnicodeWordKind.None, UnicodeWordKind.Letter })]
    // Kana, ideographs and Hangul.
    [InlineData("\u30AB\u30BF\u30AB\u30CA", new[] { "\u30AB\u30BF\u30AB\u30CA" }, new[] { UnicodeWordKind.Kana })]
    [InlineData("\u3053\u65E5\u3005", new[] { "\u3053", "\u65E5", "\u3005" }, new[] { UnicodeWordKind.Kana, UnicodeWordKind.Ideographic, UnicodeWordKind.Ideographic })]
    [InlineData("\uD55C\uAD6D", new[] { "\uD55C\uAD6D" }, new[] { UnicodeWordKind.Letter })]
    // A script written without spaces between words is a letter word, even without a dictionary.
    [InlineData("\u0E20\u0E32", new[] { "\u0E20", "\u0E32" }, new[] { UnicodeWordKind.Letter, UnicodeWordKind.Letter })]
    // The trailing marks are ignored (WB4), and a mark on its own is not a word.
    [InlineData("e\u0301 \u0301", new[] { "e\u0301", " \u0301" }, new[] { UnicodeWordKind.Letter, UnicodeWordKind.None })]
    [InlineData("\u0301e", new[] { "\u0301", "e" }, new[] { UnicodeWordKind.None, UnicodeWordKind.Letter })]
    [InlineData("\U0001F600\U0001F1EB\U0001F1F7\r\n\u20AC", new[] { "\U0001F600", "\U0001F1EB\U0001F1F7", "\r\n", "\u20AC" }, new[] { UnicodeWordKind.None, UnicodeWordKind.None, UnicodeWordKind.None, UnicodeWordKind.None })]
    public void EnumerateWordBoundaries_ReturnsTheKindOfEachSegment(string text, string[] expectedSegments, UnicodeWordKind[] expectedKinds)
    {
        var segments = new List<string>();
        var kinds = new List<UnicodeWordKind>();
        var enumerator = Unicode.EnumerateWordBoundaries(text);
        Assert.Equal(UnicodeWordKind.None, enumerator.CurrentKind);
        while (enumerator.MoveNext())
        {
            segments.Add(text[enumerator.Current]);
            kinds.Add(enumerator.CurrentKind);
        }

        Assert.Equal(expectedSegments, segments.ToArray());
        Assert.Equal(expectedKinds, kinds.ToArray());
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("Hello.", new[] { "Hello." })]
    [InlineData("Hello. World", new[] { "Hello. ", "World" })]
    [InlineData("Hello.  World", new[] { "Hello.  ", "World" })]
    // The closing punctuation and the spaces after the terminal punctuation end the sentence (SB9, SB10).
    [InlineData("(He left.) Then", new[] { "(He left.) ", "Then" })]
    // A full stop followed by a digit (SB6), by an uppercase letter (SB7) or by a lowercase word (SB8). The other
    // terminal punctuation marks end the sentence before a lowercase word.
    [InlineData("Pi is 3.14 today.", new[] { "Pi is 3.14 today." })]
    [InlineData("The U.S.A. Is big.", new[] { "The U.S.A. ", "Is big." })]
    [InlineData("See e.g. the list.", new[] { "See e.g. the list." })]
    [InlineData("Mr. Smith", new[] { "Mr. ", "Smith" })]
    [InlineData("\"Stop!\" he said", new[] { "\"Stop!\" ", "he said" })]
    [InlineData("Ready?! Go.", new[] { "Ready?! ", "Go." })]
    // A comma after a terminal punctuation continues the sentence (SB8a).
    [InlineData("\"Why?\", she asked.", new[] { "\"Why?\", she asked." })]
    // A paragraph separator ends a sentence (SB4), and belongs to it.
    [InlineData("One\nTwo\r\nThree\u2029Four", new[] { "One\n", "Two\r\n", "Three\u2029", "Four" })]
    [InlineData("End.\nNext", new[] { "End.\n", "Next" })]
    [InlineData("End. \r\nNext", new[] { "End. \r\n", "Next" })]
    // Extend and Format characters are ignored (SB5).
    [InlineData("e\u0301. Then", new[] { "e\u0301. ", "Then" })]
    [InlineData(".\u0301 Then", new[] { ".\u0301 ", "Then" })]
    [InlineData("\u3053\u308C\u3002\u305D\u308C\u3002", new[] { "\u3053\u308C\u3002", "\u305D\u308C\u3002" })]
    public void EnumerateSentenceBoundaries_ReturnsTheSentences(string text, string[] expected)
    {
        var sentences = new List<string>();
        foreach (var range in Unicode.EnumerateSentenceBoundaries(text))
        {
            sentences.Add(text[range]);
        }

        Assert.Equal(expected, sentences.ToArray());
    }

    [Fact]
    public void EnumerateSentenceBoundaries_IsLinearAfterAFullStop()
    {
        // SB8 looks ahead for a lowercase letter after "ATerm Close* Sp*": the lookahead must not be repeated at each
        // position of a long sequence of spaces or of characters it skips.
        var text = "a." + new string(' ', 100_000) + new string('1', 100_000) + "A";
        var sentences = new List<Range>();
        foreach (var range in Unicode.EnumerateSentenceBoundaries(text))
        {
            sentences.Add(range);
        }

        Assert.Equal([0..100_002, 100_002..text.Length], sentences);
    }

    [Fact]
    public void EnumerateSentenceBoundaries_KeepsUnpairedSurrogates()
    {
        var text = "a" + (char)0xD800 + ". " + (char)0xDC00 + "B";
        var sentences = new List<string>();
        foreach (var range in Unicode.EnumerateSentenceBoundaries(text))
        {
            sentences.Add(text[range]);
        }

        Assert.Equal(["a" + (char)0xD800 + ". ", (char)0xDC00 + "B"], sentences);
    }

    [Theory]
    [InlineData("", new int[0], new int[0])]
    [InlineData("a b", new[] { 2, 3 }, new[] { 3 })]
    [InlineData("a\nb", new[] { 2, 3 }, new[] { 2, 3 })]
    [InlineData("a\r\nb", new[] { 3, 4 }, new[] { 3, 4 })]
    // Paragraph separator and next line are hard line breaks too.
    [InlineData("a\u2029b\u0085c", new[] { 2, 4, 5 }, new[] { 2, 4, 5 })]
    // A soft hyphen is an opportunity after it.
    [InlineData("co\u00ADop", new[] { 3, 5 }, new[] { 5 })]
    // No break at a no-break space, nor before the closing punctuation.
    [InlineData("a\u00A0b (c)", new[] { 4, 7 }, new[] { 7 })]
    [InlineData("\u65E5\u672C\u8A9E", new[] { 1, 2, 3 }, new[] { 3 })]
    // A zero width space is an opportunity that takes no room.
    [InlineData("ab\u200Bcd", new[] { 3, 5 }, new[] { 5 })]
    // The last letter of an ASCII word takes the mark that follows it.
    [InlineData("abc\u0301 def", new[] { 5, 8 }, new[] { 8 })]
    [InlineData("word123 abc", new[] { 8, 11 }, new[] { 11 })]
    [InlineData("abc(def) gh", new[] { 9, 11 }, new[] { 11 })]
    [InlineData("abc\u65E5def", new[] { 3, 4, 7 }, new[] { 7 })]
    public void EnumerateLineBreaks_ReturnsTheBreaks(string text, int[] expectedIndexes, int[] expectedMandatory)
    {
        var indexes = new List<int>();
        var mandatory = new List<int>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text))
        {
            indexes.Add(lineBreak.Index);
            if (lineBreak.IsMandatory)
            {
                mandatory.Add(lineBreak.Index);
            }
        }

        Assert.Equal(expectedIndexes, indexes.ToArray());
        Assert.Equal(expectedMandatory, mandatory.ToArray());
    }

    [Theory]
    // A small kana does not start a line with the strict rules; it can with the normal ones.
    [InlineData("\u3042\u3041", UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.Normal, new[] { 2 })]
    [InlineData("\u3042\u3041", UnicodeLineBreakStrictness.Normal, UnicodeWordBreakBehavior.Normal, new[] { 1, 2 })]
    [InlineData("ab 12", UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.BreakAll, new[] { 1, 3, 4, 5 })]
    [InlineData("\u65E5\u672C a", UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.KeepAll, new[] { 3, 4 })]
    // Every grapheme cluster boundary, including before a space and inside a word.
    [InlineData("ab e\u0301", UnicodeLineBreakStrictness.Anywhere, UnicodeWordBreakBehavior.Normal, new[] { 1, 2, 3, 5 })]
    [InlineData("a\r\nb", UnicodeLineBreakStrictness.Anywhere, UnicodeWordBreakBehavior.Normal, new[] { 1, 3, 4 })]
    public void EnumerateLineBreaks_AppliesTheTailoring(string text, UnicodeLineBreakStrictness strictness, UnicodeWordBreakBehavior wordBreak, int[] expectedIndexes)
    {
        var indexes = new List<int>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, strictness, wordBreak))
        {
            indexes.Add(lineBreak.Index);
            Assert.Equal(lineBreak.Index == text.Length || text[lineBreak.Index - 1] == '\n', lineBreak.IsMandatory);
        }

        Assert.Equal(expectedIndexes, indexes.ToArray());
    }

    [Fact]
    public void EnumerateLineBreaks_ValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("strictness", () => { _ = Unicode.EnumerateLineBreaks("a", (UnicodeLineBreakStrictness)3); });
        Assert.Throws<ArgumentOutOfRangeException>("strictness", () => { _ = Unicode.EnumerateLineBreaks("a", (UnicodeLineBreakStrictness)(-1)); });
        Assert.Throws<ArgumentOutOfRangeException>("wordBreak", () => { _ = Unicode.EnumerateLineBreaks("a", wordBreak: (UnicodeWordBreakBehavior)3); });
    }

    // Thai: phasa (language), thai, sawatdi (hello), khrap (a polite particle), chan (I), kin (to eat), khao (rice),
    // ta (eye), tak (to dry), klom (round), lom (wind), nam (water), di (good).
    private const string ThaiPhasa = "\u0E20\u0E32\u0E29\u0E32";
    private const string ThaiThai = "\u0E44\u0E17\u0E22";
    private const string ThaiSawatdi = "\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35";
    private const string ThaiKhrap = "\u0E04\u0E23\u0E31\u0E1A";
    private const string ThaiChan = "\u0E09\u0E31\u0E19";
    private const string ThaiKin = "\u0E01\u0E34\u0E19";
    private const string ThaiKhao = "\u0E02\u0E49\u0E32\u0E27";
    private const string ThaiTa = "\u0E15\u0E32";
    private const string ThaiTak = "\u0E15\u0E32\u0E01";
    private const string ThaiKlom = "\u0E01\u0E25\u0E21";
    private const string ThaiLom = "\u0E25\u0E21";
    private const string ThaiNam = "\u0E19\u0E49\u0E33";
    private const string ThaiDi = "\u0E14\u0E35";

    // Lao: phasa (language), lao, sabai (well), di (good).
    private const string LaoPhasa = "\u0E9E\u0EB2\u0EAA\u0EB2";
    private const string LaoLao = "\u0EA5\u0EB2\u0EA7";
    private const string LaoSabai = "\u0EAA\u0EB0\u0E9A\u0EB2\u0E8D";
    private const string LaoDi = "\u0E94\u0EB5";

    // Khmer: pheasa (language), khmer.
    private const string KhmerPheasa = "\u1797\u17B6\u179F\u17B6";
    private const string KhmerKhmer = "\u1781\u17D2\u1798\u17C2\u179A";

    // Myanmar: myanma, sa (writing), kyezu (thanks), tin, pa, te (the three other words of "thank you").
    private const string MyanmarMyanma = "\u1019\u103C\u1014\u103A\u1019\u102C";
    private const string MyanmarSa = "\u1005\u102C";
    private const string MyanmarKyezu = "\u1000\u103B\u1031\u1038\u1007\u1030\u1038";
    private const string MyanmarTin = "\u1010\u1004\u103A";
    private const string MyanmarPa = "\u1015\u102B";
    private const string MyanmarTe = "\u1010\u101A\u103A";

    [Theory]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, ThaiPhasa + ThaiThai, new[] { 4 })]
    [InlineData(ThaiSawatdi + "\n" + ThaiKhrap, ThaiSawatdi + ThaiKhrap, new[] { 6 })]
    [InlineData(ThaiChan + "\n" + ThaiKin + "\n" + ThaiKhao, ThaiChan + ThaiKin + ThaiKhao, new[] { 3, 6 })]
    [InlineData(LaoPhasa + "\n" + LaoLao, LaoPhasa + LaoLao, new[] { 4 })]
    [InlineData(LaoSabai + "\n" + LaoDi, LaoSabai + LaoDi, new[] { 5 })]
    [InlineData(LaoSabai + "\n" + LaoDi + "\n" + LaoPhasa + "\n" + LaoLao, LaoSabai + LaoDi + LaoPhasa + LaoLao, new[] { 5, 7, 11 })]
    [InlineData(KhmerPheasa + "\n" + KhmerKhmer, KhmerPheasa + KhmerKhmer, new[] { 4 })]
    [InlineData(KhmerPheasa + "\n" + KhmerKhmer, KhmerKhmer + KhmerPheasa + KhmerKhmer, new[] { 5, 9 })]
    [InlineData(MyanmarMyanma + "\n" + MyanmarSa, MyanmarMyanma + MyanmarSa, new[] { 6 })]
    [InlineData(MyanmarKyezu + "\n" + MyanmarTin + "\n" + MyanmarPa + "\n" + MyanmarTe, MyanmarKyezu + MyanmarTin + MyanmarPa + MyanmarTe, new[] { 7, 10, 12 })]
    // A text that is one word, that has no word, or that is empty has no break.
    [InlineData(ThaiPhasa + "\n" + ThaiThai, ThaiPhasa, new int[0])]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, ThaiKhrap, new int[0])]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, "", new int[0])]
    // The characters that are in no word stay with what precedes them: an unknown suffix, an unknown word between
    // two words, and the repetition mark U+0E46. An unknown prefix is followed by a break.
    [InlineData(ThaiPhasa + "\n" + ThaiThai, ThaiPhasa + ThaiThai + "\u0E01\u0E02", new[] { 4 })]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, ThaiPhasa + "\u0E01\u0E02" + ThaiThai, new[] { 6 })]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, "\u0E01\u0E02" + ThaiPhasa + ThaiThai, new[] { 2, 6 })]
    [InlineData(ThaiPhasa + "\n" + ThaiThai, "\u0E01\u0E02\u0E04", new int[0])]
    [InlineData(ThaiDi + "\n" + ThaiKhrap, ThaiDi + "\u0E46" + ThaiKhrap, new[] { 3 })]
    // Two segmentations in two words: the longer first word wins ("tak lom", not "ta klom").
    [InlineData(ThaiTa + "\n" + ThaiTak + "\n" + ThaiKlom + "\n" + ThaiLom, ThaiTa + ThaiKlom, new[] { 3 })]
    // The words are code units: a position counts both code units of a character outside the BMP (Ahom).
    [InlineData("\U00011700\U00011701\n\U00011700", "\U00011700\U00011701\U00011700\U00011700", new[] { 4, 6 })]
    // The text is segmented whatever its script.
    [InlineData("in\nput\ninput", "inputinput", new[] { 5 })]
    public void FindBreaks_ReturnsTheStartOfEachWord(string words, string text, int[] expected)
    {
        var dictionary = LineBreakDictionary.Parse(words);

        Assert.Equal(expected, FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected, FindBreaksExhaustively(text, [.. words.Split('\n')]));
    }

    [Theory]
    // The fewest uncovered characters first: the longest match "abc" would leave "d" uncovered.
    [InlineData("abc ab cd", "abcd", new[] { 2 })]
    [InlineData("abc ab", "abcd", new int[0])]
    // Then the fewest words.
    [InlineData("a b c d ab cd", "abcd", new[] { 2 })]
    [InlineData("a b c d ab cd abcd", "abcd", new int[0])]
    [InlineData("a b c d", "abcd", new[] { 1, 2, 3 })]
    [InlineData("ab a bcde c de", "abcde", new[] { 1 })]
    // Then the longer word at the first difference.
    [InlineData("ab c a bc", "abc", new[] { 2 })]
    [InlineData("a bc abc d ab cd", "abcd", new[] { 3 })]
    // A word is preferred to an uncovered character: "ab" then "c" uncovered, not "a" uncovered then "bc".
    [InlineData("ab bc", "abc", new int[0])]
    [InlineData("ab bc", "abcbc", new[] { 3 })]
    [InlineData("ab ba bac", "ababac", new[] { 3 })]
    [InlineData("ab ba bac", "ababab", new[] { 2, 4 })]
    // The end of the text decides its start: the first word is "da" so that "bac" covers the end.
    [InlineData("d da ab ba bac", "dabab", new[] { 1, 3 })]
    [InlineData("d da ab ba bac", "dabac", new[] { 2 })]
    [InlineData("d da ab ba bac", "dababababab", new[] { 1, 3, 5, 7, 9 })]
    [InlineData("d da ab ba bac", "dababababac", new[] { 2, 4, 6, 8 })]
    // A word can be in the text more than once, and overlap another one.
    [InlineData("aa", "aaaaa", new[] { 2 })]
    [InlineData("aa aaa", "aaaaa", new[] { 3 })]
    [InlineData("aa aaa", "aaaaaaa", new[] { 3, 5 })]
    public void FindBreaks_ChoosesTheSegmentation(string words, string text, int[] expected)
    {
        words = ToThaiLetters(words).Replace(' ', '\n');
        text = ToThaiLetters(text);
        var dictionary = LineBreakDictionary.Parse(words);

        Assert.Equal(expected, FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected, FindBreaksExhaustively(text, [.. words.Split('\n')]));
    }

    [Theory]
    // A word cannot end before a combining mark: the first letter carries U+0E31.
    [InlineData("\u0E01\n\u0E31\u0E01", "\u0E01\u0E31\u0E01", new[] { 2 })]
    [InlineData("\u0E01", "\u0E01\u0E31\u0E01\u0E01", new[] { 2, 3 })]
    // U+0E33 is a letter for the line breaking rules, and it is in the grapheme cluster of the letter before it.
    [InlineData("\u0E19\u0E49\n\u0E33\u0E15\u0E32", ThaiNam + ThaiTa, new int[0])]
    [InlineData("\u0E19\n\u0E33\n" + ThaiTa, ThaiNam + ThaiTa, new[] { 3 })]
    [InlineData(ThaiNam + "\n" + ThaiTa, ThaiNam + ThaiTa, new[] { 3 })]
    // A Khmer consonant, the sign coeng and the next consonant are one grapheme cluster.
    [InlineData(KhmerPheasa + "\n\u1781\n\u1798\u17C2\u179A", KhmerPheasa + KhmerKhmer, new int[0])]
    [InlineData(KhmerPheasa + "\n\u1781\u17D2\u1798\u17C2\n\u179A", KhmerPheasa + KhmerKhmer, new[] { 4, 8 })]
    // U+102C is a combining mark that starts a grapheme cluster: no word ends before it.
    [InlineData("\u1019\n\u102C\u1019", "\u1019\u102C\u1019", new[] { 2 })]
    [InlineData("\u1019\n\u1019\u102C", "\u1019\u102C\u1019\u1019\u102C", new[] { 2, 3 })]
    // The uncovered characters are counted in UTF-16 code units: the last letter and its mark are two.
    [InlineData("\u0E01\u0E02\n\u0E02\u0E04\u0E31", "\u0E01\u0E02\u0E04\u0E31", new[] { 1 })]
    // A combining mark of another script, and a zero width joiner.
    [InlineData("a\nb", "aa\u0301b", new[] { 3 })]
    [InlineData("a\nb", "aa\u200Db", new[] { 3 })]
    public void FindBreaks_DoesNotBreakInsideAGraphemeClusterNorBeforeAMark(string words, string text, int[] expected)
    {
        var dictionary = LineBreakDictionary.Parse(words);

        Assert.Equal(expected, FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected, FindBreaksExhaustively(text, [.. words.Split('\n')]));
    }

    [Fact]
    public void FindBreaks_SegmentsALongTextPieceByPiece()
    {
        var dictionary = LineBreakDictionary.Parse(ThaiPhasa + "\n" + ThaiThai);

        // 3,500 code units: the pieces are joined without losing or adding a break.
        var text = string.Concat(Enumerable.Repeat(ThaiPhasa + ThaiThai, 500));
        var expected = new List<int>();
        for (var i = 0; i < 500; i++)
        {
            if (i > 0)
            {
                expected.Add(i * 7);
            }

            expected.Add((i * 7) + 4);
        }

        Assert.Equal(expected.ToArray(), FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected.Append(text.Length).ToArray(), GetLineBreakIndexes(text, dictionary));

        // A text of 1,024 code units is segmented as a whole, and its end decides its start: "e", "da", 508 times
        // "ba", "bac", "d" and "d" leave no character uncovered, when "e", "d" and "ab" would leave "ac".
        dictionary = LineBreakDictionary.Parse(ToThaiLetters("e d da ab ba bac").Replace(' ', '\n'));
        text = ToThaiLetters("ed" + string.Concat(Enumerable.Repeat("ab", 509)) + "acdd");
        expected.Clear();
        for (var i = 1; i <= 1019; i += 2)
        {
            expected.Add(i);
        }

        expected.Add(1022);
        expected.Add(1023);
        Assert.HasCount(1024, text);
        Assert.Equal(expected.ToArray(), FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected.Append(text.Length).ToArray(), GetLineBreakIndexes(text, dictionary));

        // A longer text is not. Its first 1,024 code units are "e", "d" and 511 times "ab": the words that start in
        // the first 512 code units are kept. The text that follows them is short enough to be segmented as a whole:
        // 344 times "ab", "a" that is uncovered, "bac" and "d".
        text = ToThaiLetters("ed" + string.Concat(Enumerable.Repeat("ab", 600)) + "acd");
        expected.Clear();
        expected.Add(1);
        for (var i = 2; i <= 1198; i += 2)
        {
            expected.Add(i);
        }

        expected.Add(1201);
        expected.Add(1204);
        Assert.HasCount(1205, text);
        Assert.Equal(expected.ToArray(), FindDictionaryBreaks(dictionary, text));
        Assert.Equal(expected.Append(text.Length).ToArray(), GetLineBreakIndexes(text, dictionary));
    }

    [Fact]
    public void FindBreaks_LeavesAGraphemeClusterLongerThanAPieceUncovered()
    {
        var dictionary = LineBreakDictionary.Parse(ThaiPhasa + "\n" + ThaiThai + "\n\u0E01");
        var cluster = "\u0E01" + new string('\u0E31', 1500);

        var text = ThaiPhasa + cluster + ThaiThai;
        Assert.Equal(new[] { 1505 }, FindDictionaryBreaks(dictionary, text));
        Assert.Equal(new[] { 1505, 1508 }, GetLineBreakIndexes(text, dictionary));

        text = cluster + ThaiPhasa + ThaiThai;
        Assert.Equal(new[] { 1501, 1505 }, FindDictionaryBreaks(dictionary, text));
        Assert.Equal(new[] { 1501, 1505, 1508 }, GetLineBreakIndexes(text, dictionary));

        // The cluster fits in a piece, but not in the first one.
        text = string.Concat(Enumerable.Repeat(ThaiPhasa + ThaiThai, 100)) + "\u0E01" + new string('\u0E31', 900) + ThaiThai;
        var breaks = FindDictionaryBreaks(dictionary, text);
        Assert.HasCount(200, breaks);
        Assert.Equal(new[] { 693, 697, 1601 }, breaks[^3..]);
        Assert.Equal(breaks.Append(text.Length).ToArray(), GetLineBreakIndexes(text, dictionary));

        // The sign coeng joins the Khmer consonants around it: 541 of them are one grapheme cluster, and the word
        // that is the end of the cluster is not found in it.
        text = string.Concat(Enumerable.Repeat("\u1780\u17D2", 540)) + "\u1780";
        dictionary = LineBreakDictionary.Parse(text.AsSpan(1024));
        Assert.Equal(text.Length, Unicode.GetGraphemeClusterLength(text));
        Assert.Equal(new[] { text.Length }, FindDictionaryBreaks(dictionary, text + text[1024..]));
        Assert.Empty(FindDictionaryBreaks(dictionary, text));
        Assert.Equal(new[] { text.Length }, GetLineBreakIndexes(text, dictionary));
    }

    [Fact]
    public void FindBreaks_AgreesWithAnExhaustiveSearch()
    {
        // Letters, a nonspacing mark, a letter that extends a grapheme cluster (U+0E33), a Myanmar consonant, the
        // sign that stacks it under another (U+1039), and a combining mark that starts a grapheme cluster (U+102C).
        string[] alphabet = ["\u0E01", "\u0E02", "\u0E04", "\u0E31", "\u0E33", "\u1019", "\u1039", "\u102C"];
        var state = 12345u;
        var count = 0;
        for (var iteration = 0; iteration < 4000; iteration++)
        {
            var text = RandomText(ref state, alphabet, 1 + NextRandom(ref state, 10));
            var words = new HashSet<string>(StringComparer.Ordinal);
            var wordCount = NextRandom(ref state, 9);
            for (var i = 0; i < wordCount; i++)
            {
                if (NextRandom(ref state, 2) == 0)
                {
                    words.Add(RandomText(ref state, alphabet, 1 + NextRandom(ref state, 4)));
                }
                else
                {
                    var start = NextRandom(ref state, text.Length);
                    words.Add(text.Substring(start, 1 + NextRandom(ref state, Math.Min(5, text.Length - start))));
                }
            }

            var dictionary = LineBreakDictionary.Parse(string.Join('\n', words));
            var expected = FindBreaksExhaustively(text, words);
            var actual = FindDictionaryBreaks(dictionary, text);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail($"'{Escape(text)}' with [{string.Join(' ', words.Select(Escape))}]: expected {string.Join(' ', expected)}, actual {string.Join(' ', actual)}");
            }

            count += expected.Length;
        }

        Assert.True(count > 1500, $"Only {count} breaks were compared");
    }

    [Fact]
    public void FindBreaks_AgreesWithTheRuleAppliedPieceByPiece()
    {
        // Texts longer than a piece, with words short enough to be found everywhere. A space ends a run for the
        // enumerator, and is a character like another for FindBreaks.
        string[] alphabet = ["\u0E01", "\u0E02", "\u0E04", "\u0E01", "\u0E02", "\u0E31", "\u0E33", "\u1019", "\u1039", "\u102C"];
        var state = 777u;
        var count = 0;
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var builder = new StringBuilder();
            var length = 1000 + NextRandom(ref state, 3000);
            var spaces = NextRandom(ref state, 3);
            for (var i = 0; i < length; i++)
            {
                builder.Append(spaces != 0 && NextRandom(ref state, 1500) == 0 ? " " : alphabet[NextRandom(ref state, alphabet.Length)]);
            }

            var text = builder.ToString();
            var words = new HashSet<string>(StringComparer.Ordinal);
            var wordCount = 4 + NextRandom(ref state, 30);
            for (var i = 0; i < wordCount; i++)
            {
                words.Add(RandomText(ref state, alphabet, 1 + NextRandom(ref state, 4)));
            }

            var dictionary = LineBreakDictionary.Parse(string.Join('\n', words));
            var expected = FindBreaksPieceByPiece(text, words);
            var actual = FindDictionaryBreaks(dictionary, text);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail($"Iteration {iteration}: expected {string.Join(' ', expected.Except(actual).Take(10))}, unexpected {string.Join(' ', actual.Except(expected).Take(10))}");
            }

            // The breaks of the enumerator are those of the annex and those of each run.
            var expectedLineBreaks = new SortedSet<int>(GetLineBreakIndexes(text, dictionary: null));
            foreach (var (start, end) in GetComplexContextRuns(text))
            {
                expectedLineBreaks.UnionWith(FindBreaksPieceByPiece(text[start..end], words).Select(index => start + index));
            }

            Assert.Equal(expectedLineBreaks.ToArray(), GetLineBreakIndexes(text, dictionary));
            Assert.Equal(expectedLineBreaks.ToArray(), GetLineBreakIndexes(text, dictionary, UnicodeLineBreakStrictness.Normal, UnicodeWordBreakBehavior.KeepAll));
            count += actual.Length;
        }

        Assert.True(count > 10000, $"Only {count} breaks were compared");
    }

    [Fact]
    public void FindBreaks_ThrowsWhenTheDestinationIsTooSmall()
    {
        var dictionary = LineBreakDictionary.Parse(ThaiChan + "\n" + ThaiKin + "\n" + ThaiKhao);
        var text = ThaiChan + ThaiKin + ThaiKhao;

        var positions = new int[2];
        Assert.Equal(2, dictionary.FindBreaks(text, positions));
        Assert.Equal(new[] { 3, 6 }, positions);

        Assert.Throws<ArgumentException>("breakPositions", () => { dictionary.FindBreaks(text, new int[1]); });
        Assert.Throws<ArgumentException>("breakPositions", () => { dictionary.FindBreaks(text, []); });
        Assert.Equal(0, dictionary.FindBreaks(ThaiChan, []));
        Assert.Equal(0, LineBreakDictionary.Parse("").FindBreaks(text, []));
    }

    [Fact]
    public void FindBreaks_DoesNotAllocate()
    {
        var dictionary = LineBreakDictionary.Parse(ThaiPhasa + "\n" + ThaiThai + "\n" + ThaiSawatdi + "\n" + ThaiKhrap);
        var shortText = ThaiSawatdi + ThaiKhrap + ThaiPhasa + ThaiThai;
        var longText = string.Concat(Enumerable.Repeat(shortText, 200));
        var mixedText = "Hello " + shortText + ", " + longText + " (" + shortText + ")";
        var positions = new int[longText.Length];
        dictionary.FindBreaks(shortText, positions);
        dictionary.FindBreaks(longText, positions);
        var count = 0;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(mixedText, dictionary))
        {
            count += lineBreak.Index;
        }

        // Tiered compilation running concurrently can occasionally attribute a few bytes to this thread: best of several rounds
        var allocated = long.MaxValue;
        for (var round = 0; round < 5 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++)
            {
                dictionary.FindBreaks(shortText, positions);
                dictionary.FindBreaks(longText, positions);
                foreach (var lineBreak in Unicode.EnumerateLineBreaks(mixedText, dictionary))
                {
                    count += lineBreak.Index;
                }
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
        Assert.True(count > 0);
    }

    [Theory]
    [InlineData(ThaiPhasa + ThaiThai, new[] { 4, 7 }, new[] { 7 })]
    // The rules of the annex decide the start and the end of a run: spaces, letters of another script, digits and punctuation.
    [InlineData("Hello " + ThaiSawatdi + ThaiKhrap + " world", new[] { 6, 12, 17, 22 }, new[] { 6, 17, 22 })]
    [InlineData("Hello " + ThaiPhasa + ThaiThai + "!", new[] { 6, 10, 14 }, new[] { 6, 14 })]
    [InlineData("abc" + ThaiPhasa + ThaiThai + "abc", new[] { 7, 13 }, new[] { 13 })]
    [InlineData(ThaiPhasa + "123" + ThaiThai, new[] { 10 }, new[] { 10 })]
    [InlineData(ThaiPhasa + "\u0E51" + ThaiThai, new[] { 8 }, new[] { 8 })]
    [InlineData("(" + ThaiPhasa + ThaiThai + ")", new[] { 5, 9 }, new[] { 9 })]
    [InlineData(ThaiPhasa + ThaiThai + ", " + ThaiThai + ThaiPhasa, new[] { 4, 9, 12, 16 }, new[] { 9, 16 })]
    [InlineData(ThaiPhasa + ThaiThai + "\n" + ThaiPhasa + ThaiThai, new[] { 4, 8, 12, 15 }, new[] { 8, 15 })]
    [InlineData(ThaiPhasa + "-" + ThaiThai + ThaiPhasa, new[] { 5, 8, 12 }, new[] { 5, 12 })]
    // A zero width space still gives a break, and ends a run: "phasa" is not found across it.
    [InlineData(ThaiPhasa + ThaiThai + "\u200B" + ThaiSawatdi + ThaiKhrap, new[] { 4, 8, 14, 18 }, new[] { 8, 18 })]
    [InlineData("\u0E20\u0E32\u200B\u0E29\u0E32" + ThaiThai, new[] { 3, 5, 8 }, new[] { 3, 8 })]
    // A word joiner and a no-break space end a run too, and the start of a run is never a break of the dictionary.
    [InlineData(ThaiPhasa + "\u2060" + ThaiThai + ThaiPhasa, new[] { 8, 12 }, new[] { 12 })]
    [InlineData(ThaiPhasa + "\u00A0" + ThaiThai + ThaiPhasa, new[] { 8, 12 }, new[] { 12 })]
    // The characters that are in no word stay with what precedes them.
    [InlineData(ThaiPhasa + ThaiThai + "\u0E01\u0E02 " + ThaiDi + "\u0E46" + ThaiKhrap, new[] { 4, 10, 13, 17 }, new[] { 10, 17 })]
    [InlineData("\u0E01\u0E02\u0E04 " + ThaiNam, new[] { 4, 7 }, new[] { 4, 7 })]
    // A combining mark of another script ends a run: the letter after it is the start of the next run.
    [InlineData(ThaiPhasa + "\u0301" + ThaiThai + ThaiPhasa, new[] { 8, 12 }, new[] { 12 })]
    // A Complex_Context mark that follows a character of another class belongs to that character: the run starts at the first letter.
    [InlineData("a\u0E31" + ThaiPhasa + ThaiThai, new[] { 6, 9 }, new[] { 9 })]
    [InlineData("(\u0E31" + ThaiPhasa + ThaiThai, new[] { 6, 9 }, new[] { 9 })]
    [InlineData(" \u0E31" + ThaiPhasa + ThaiThai, new[] { 1, 6, 9 }, new[] { 1, 9 })]
    [InlineData("\u0E31" + ThaiPhasa + ThaiThai, new[] { 5, 8 }, new[] { 8 })]
    // The other scripts.
    [InlineData(LaoSabai + LaoDi + " " + LaoPhasa + LaoLao, new[] { 5, 8, 12, 15 }, new[] { 8, 15 })]
    [InlineData(KhmerPheasa + KhmerKhmer + "\u17D4", new[] { 4, 10 }, new[] { 10 })]
    [InlineData(MyanmarKyezu + MyanmarTin + MyanmarPa + MyanmarTe + "\u104B" + MyanmarMyanma + MyanmarSa, new[] { 7, 10, 12, 16, 22, 24 }, new[] { 16, 24 })]
    public void EnumerateLineBreaks_WithADictionary_BreaksBeforeTheWords(string text, int[] expectedIndexes, int[] expectedWithoutDictionary)
    {
        var dictionary = LineBreakDictionary.Parse(string.Join('\n', ThaiPhasa, ThaiThai, ThaiSawatdi, ThaiKhrap, ThaiDi, ThaiNam, LaoPhasa, LaoLao, LaoSabai, LaoDi, KhmerPheasa, KhmerKhmer, MyanmarMyanma, MyanmarSa, MyanmarKyezu, MyanmarTin, MyanmarPa, MyanmarTe));

        Assert.Equal(expectedWithoutDictionary, GetLineBreakIndexes(text, dictionary: null));
        Assert.Equal(expectedIndexes, GetLineBreakIndexes(text, dictionary));

        // A break of the dictionary is not mandatory.
        var mandatory = new List<int>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text))
        {
            if (lineBreak.IsMandatory)
            {
                mandatory.Add(lineBreak.Index);
            }
        }

        var mandatoryWithDictionary = new List<int>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, dictionary))
        {
            if (lineBreak.IsMandatory)
            {
                mandatoryWithDictionary.Add(lineBreak.Index);
            }
        }

        Assert.Equal(mandatory.ToArray(), mandatoryWithDictionary.ToArray());
    }

    [Theory]
    [InlineData(ThaiPhasa + ThaiThai, UnicodeLineBreakStrictness.Normal, UnicodeWordBreakBehavior.Normal, new[] { 4, 7 }, new[] { 7 })]
    // The breaks of the dictionary are between words: they are kept when a line does not break inside a word.
    [InlineData(ThaiPhasa + ThaiThai, UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.KeepAll, new[] { 4, 7 }, new[] { 7 })]
    [InlineData("\u65E5\u672C" + ThaiPhasa + ThaiThai + "\u65E5\u672C", UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.KeepAll, new[] { 6, 11 }, new[] { 11 })]
    [InlineData("\u65E5\u672C" + ThaiPhasa + ThaiThai + "\u65E5\u672C", UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.Normal, new[] { 1, 2, 6, 9, 10, 11 }, new[] { 1, 2, 9, 10, 11 })]
    // A line already breaks between any two letters, or any two grapheme clusters: the dictionary changes nothing.
    [InlineData(ThaiPhasa + ThaiThai, UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.BreakAll, new[] { 1, 2, 3, 4, 5, 6, 7 }, new[] { 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData(ThaiSawatdi + ThaiKhrap, UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior.BreakAll, new[] { 1, 3, 4, 6, 7, 9, 10 }, new[] { 1, 3, 4, 6, 7, 9, 10 })]
    [InlineData(ThaiSawatdi + ThaiKhrap, UnicodeLineBreakStrictness.Anywhere, UnicodeWordBreakBehavior.Normal, new[] { 1, 3, 4, 6, 7, 9, 10 }, new[] { 1, 3, 4, 6, 7, 9, 10 })]
    [InlineData(ThaiNam + ThaiTa, UnicodeLineBreakStrictness.Anywhere, UnicodeWordBreakBehavior.KeepAll, new[] { 3, 4, 5 }, new[] { 3, 4, 5 })]
    public void EnumerateLineBreaks_WithADictionary_CombinesWithTheTailoring(string text, UnicodeLineBreakStrictness strictness, UnicodeWordBreakBehavior wordBreak, int[] expectedIndexes, int[] expectedWithoutDictionary)
    {
        var dictionary = LineBreakDictionary.Parse(string.Join('\n', ThaiPhasa, ThaiThai, ThaiSawatdi, ThaiKhrap, ThaiNam, ThaiTa));

        Assert.Equal(expectedWithoutDictionary, GetLineBreakIndexes(text, dictionary: null, strictness, wordBreak));
        Assert.Equal(expectedIndexes, GetLineBreakIndexes(text, dictionary, strictness, wordBreak));
    }

    [Fact]
    public void EnumerateLineBreaks_WithADictionary_AddsTheBreaksOfEachRun()
    {
        // The alphabet of FindBreaks_AgreesWithAnExhaustiveSearch, and characters of the other line breaking classes.
        string[] alphabet = ["\u0E01", "\u0E02", "\u0E04", "\u0E31", "\u0E33", "\u1019", "\u1039", "\u102C", "\u0E01", "\u0E02", "a", " ", "1", ",", "(", ")", "-", "\n", "\u200B", "\u200D", "\u0301", "\u00A0", "\u65E5", "\u0E46"];
        var empty = LineBreakDictionary.Parse("# no word\n");
        var state = 2024u;
        var count = 0;
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            // Two characters out of three are among the first eight of the alphabet, so that the runs are long enough to have several words.
            var builder = new StringBuilder();
            var length = 1 + NextRandom(ref state, 20);
            for (var i = 0; i < length; i++)
            {
                builder.Append(alphabet[NextRandom(ref state, NextRandom(ref state, 3) == 0 ? alphabet.Length : 8)]);
            }

            var text = builder.ToString();
            var words = new HashSet<string>(StringComparer.Ordinal);
            var wordCount = NextRandom(ref state, 9);
            for (var i = 0; i < wordCount; i++)
            {
                var start = NextRandom(ref state, text.Length);
                var word = text.Substring(start, 1 + NextRandom(ref state, Math.Min(4, text.Length - start)));
                if (NextRandom(ref state, 2) == 0)
                {
                    word = RandomText(ref state, alphabet.AsSpan(0, 8), 1 + NextRandom(ref state, 3));
                }

                if (!word.AsSpan().ContainsAny("\n#") && word.AsSpan().Trim().Length == word.Length)
                {
                    words.Add(word);
                }
            }

            var dictionary = LineBreakDictionary.Parse(string.Join('\n', words));
            Assert.Equal(words.Count, dictionary.WordCount);

            // The breaks of the dictionary: those of each run, found by the exhaustive search.
            var dictionaryBreaks = new List<int>();
            foreach (var (start, end) in GetComplexContextRuns(text))
            {
                var run = text[start..end];
                var expected = FindBreaksExhaustively(run, words);
                Assert.Equal(expected, FindDictionaryBreaks(dictionary, run));
                dictionaryBreaks.AddRange(expected.Select(index => start + index));
            }

            var clusterBoundaries = new HashSet<int>();
            foreach (var cluster in Unicode.EnumerateGraphemeClusters(text))
            {
                clusterBoundaries.Add(cluster.End.Value);
            }

            foreach (var strictness in Enum.GetValues<UnicodeLineBreakStrictness>())
            {
                foreach (var wordBreak in Enum.GetValues<UnicodeWordBreakBehavior>())
                {
                    var withoutDictionary = new List<UnicodeLineBreak>();
                    foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, strictness, wordBreak))
                    {
                        withoutDictionary.Add(lineBreak);
                    }

                    // No dictionary and a dictionary without a word change nothing.
                    Assert.Equal(withoutDictionary, GetLineBreaks(text, dictionary: null, strictness, wordBreak));
                    Assert.Equal(withoutDictionary, GetLineBreaks(text, empty, strictness, wordBreak));

                    var actual = GetLineBreaks(text, dictionary, strictness, wordBreak);
                    var expected = new SortedDictionary<int, bool>();
                    foreach (var lineBreak in withoutDictionary)
                    {
                        expected.Add(lineBreak.Index, lineBreak.IsMandatory);
                    }

                    if (strictness is not UnicodeLineBreakStrictness.Anywhere && wordBreak is not UnicodeWordBreakBehavior.BreakAll)
                    {
                        foreach (var index in dictionaryBreaks)
                        {
                            expected.TryAdd(index, value: false);
                        }

                        count += dictionaryBreaks.Count;
                    }

                    if (!expected.Select(item => (item.Key, item.Value)).SequenceEqual(actual.Select(item => (item.Index, item.IsMandatory))))
                    {
                        Assert.Fail($"'{Escape(text)}' with [{string.Join(' ', words.Select(Escape))}], {strictness}, {wordBreak}: expected {string.Join(' ', expected.Keys)}, actual {string.Join(' ', actual.Select(item => item.Index))}");
                    }

                    // The breaks are in order and after the start of the text, and the last one is the end of the
                    // text. A break of the dictionary is at a grapheme cluster boundary of the text; a break of the
                    // annex is not always: a line breaks between a space and the spacing mark that follows it.
                    var previous = 0;
                    foreach (var lineBreak in actual)
                    {
                        Assert.True(lineBreak.Index > previous);
                        if (!withoutDictionary.Contains(lineBreak))
                        {
                            Assert.Contains(lineBreak.Index, clusterBoundaries);
                        }

                        previous = lineBreak.Index;
                    }

                    Assert.Equal(text.Length, previous);
                    Assert.True(actual[^1].IsMandatory);
                }
            }
        }

        Assert.True(count > 2000, $"Only {count} breaks of a dictionary were compared");
    }

    [Fact]
    public void EnumerateLineBreaks_WithADictionary_ValidatesArguments()
    {
        var dictionary = LineBreakDictionary.Parse("a");
        Assert.Throws<ArgumentOutOfRangeException>("strictness", () => { _ = Unicode.EnumerateLineBreaks("a", dictionary, (UnicodeLineBreakStrictness)3); });
        Assert.Throws<ArgumentOutOfRangeException>("strictness", () => { _ = Unicode.EnumerateLineBreaks("a", dictionary: null, (UnicodeLineBreakStrictness)(-1)); });
        Assert.Throws<ArgumentOutOfRangeException>("wordBreak", () => { _ = Unicode.EnumerateLineBreaks("a", dictionary, wordBreak: (UnicodeWordBreakBehavior)3); });
        Assert.Empty(GetLineBreakIndexes("", dictionary));
    }

    [Fact]
    public void LineBreakDictionary_Parse_ReadsOneWordPerLine()
    {
        // A byte order mark, comments, blank lines, the three line endings, columns after a tab, and white space around a word.
        var words = "\uFEFF# A comment\r\n\r\nabc\t12\tabd\r\n  def  \nghi # A comment\n\n\tjkl\n#mno\npqr\rstu\n abc \nv w\n" + ThaiPhasa + "\t3\ne\u0301";
        var dictionary = LineBreakDictionary.Parse(words);

        Assert.Equal(8, dictionary.WordCount);
        foreach (var word in new[] { "abc", "def", "ghi", "pqr", "stu", "v w", ThaiPhasa, "e\u0301" })
        {
            Assert.True(dictionary.Contains(word), word);
        }

        // A word is kept as written: a prefix of a word, another case and another normalization form are not in the dictionary.
        foreach (var word in new[] { "", "ab", "abcd", "ABC", "12", "abd", "jkl", "mno", "#mno", "A", "comment", "v", "w", "\u00E9", "e", "\uFEFF", ThaiThai })
        {
            Assert.False(dictionary.Contains(word), word);
        }

        Assert.Equal(0, LineBreakDictionary.Parse("").WordCount);
        Assert.Equal(0, LineBreakDictionary.Parse("\uFEFF").WordCount);
        Assert.Equal(0, LineBreakDictionary.Parse("# A comment\n\n \t a\r\n").WordCount);

        // Only the first character of the text can be a byte order mark.
        Assert.True(LineBreakDictionary.Parse("\uFEFF\uFEFFa\n\uFEFFb").Contains("\uFEFFa"));
        Assert.True(LineBreakDictionary.Parse("\uFEFF\uFEFFa\n\uFEFFb").Contains("\uFEFFb"));
    }

    [Fact]
    public void LineBreakDictionary_Contains_AgreesWithTheWordList()
    {
        // Thai consonants, without a third of them: the first levels of the trie have a table indexed by letter, with
        // holes in it. With letters that are far apart, they have none.
        string[] thai = [.. Enumerable.Range(0x0E01, 46).Where(codePoint => codePoint % 3 != 0).Select(codePoint => ((char)codePoint).ToString())];
        string[] others = ["\u0E00", "\u0E01", "\u0E04", "\u0E2E", "b"];
        var state = 2024u;
        foreach (var alphabet in new[] { thai, [.. thai, "a", "\U00020000"] })
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < 2000; i++)
            {
                words.Add(RandomText(ref state, alphabet, 1 + NextRandom(ref state, 5)));
            }

            var dictionary = LineBreakDictionary.Parse(string.Join('\n', words));
            Assert.Equal(words.Count, dictionary.WordCount);

            string[] probeAlphabet = [.. alphabet, .. others];
            var found = 0;
            for (var i = 0; i < 20000; i++)
            {
                var text = RandomText(ref state, probeAlphabet, NextRandom(ref state, 4));
                var expected = words.Contains(text);
                if (dictionary.Contains(text) != expected)
                {
                    Assert.Fail($"'{Escape(text)}': expected {expected}");
                }

                found += expected ? 1 : 0;
            }

            Assert.True(found > 1000, $"Only {found} words were found");
        }
    }

    [Fact]
    public void LineBreakDictionary_Parse_AppliesTheLimits()
    {
        var threeWords = new LineBreakDictionaryOptions { MaxWordCount = 3 };
        Assert.Equal(3, LineBreakDictionary.Parse("a\nb\n\n# c\nc", threeWords).WordCount);
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("a\nb\nc\nd", threeWords));

        // A word that is in the list twice counts twice.
        Assert.Equal(2, LineBreakDictionary.Parse("a\nb\na", threeWords).WordCount);
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("a\nb\na\nb", threeWords));

        // The length of a word is a number of UTF-16 code units, without the white space around the word.
        var fourCodeUnits = new LineBreakDictionaryOptions { MaxWordLength = 4 };
        Assert.True(LineBreakDictionary.Parse("  abcd  \t12345", fourCodeUnits).Contains("abcd"));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("abcde", fourCodeUnits));
        Assert.True(LineBreakDictionary.Parse("\U00011700\U00011701", fourCodeUnits).Contains("\U00011700\U00011701"));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("a\U00011700\U00011701", fourCodeUnits));
        Assert.Equal(1, LineBreakDictionary.Parse(new string('a', 128)).WordCount);
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse(new string('a', 129)));
        Assert.Equal(1, LineBreakDictionary.Parse(new string('a', 255), new LineBreakDictionaryOptions { MaxWordLength = 255 }).WordCount);

        // The length of the input is a number of UTF-16 code units, or of bytes.
        var tenUnits = new LineBreakDictionaryOptions { MaxInputLength = 10 };
        Assert.Equal(2, LineBreakDictionary.Parse("abcd\nefghi", tenUnits).WordCount);
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("abcd\nefghij", tenUnits));
        Assert.Equal(1, LineBreakDictionary.Parse(ThaiPhasa + ThaiSawatdi, tenUnits).WordCount);
        Assert.Equal(2, LineBreakDictionary.Parse("abcd\nefghi"u8, tenUnits).WordCount);
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("abcd\nefghij"u8, tenUnits));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse(Encoding.UTF8.GetBytes(ThaiPhasa), tenUnits));

        using var accepted = new MemoryStream("abcd\nefghi"u8.ToArray());
        Assert.Equal(2, LineBreakDictionary.Load(accepted, tenUnits).WordCount);
        using var rejected = new MemoryStream("abcd\nefghij"u8.ToArray());
        Assert.Throws<FormatException>(() => LineBreakDictionary.Load(rejected, tenUnits));
    }

    [Fact]
    public void LineBreakDictionary_Parse_RejectsInvalidText()
    {
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse([(byte)'a', 0xFF, (byte)'b']));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse([(byte)'a', 0xC3]));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse([0xED, 0xA0, 0x80]));

        var highSurrogate = "a" + (char)0xD805;
        var lowSurrogate = (char)0xDF00 + "a";
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse(highSurrogate));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse(highSurrogate + "a"));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse(lowSurrogate));
        Assert.Throws<FormatException>(() => LineBreakDictionary.Parse("a\n" + lowSurrogate + highSurrogate));

        // A comment and the columns after a tab are not read.
        Assert.Equal(2, LineBreakDictionary.Parse("a # " + highSurrogate + "\nb\t" + lowSurrogate).WordCount);
    }

    [Fact]
    public async Task LineBreakDictionary_Load_ReadsUtf8FilesAndStreams()
    {
        byte[] words = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Thai\r\n" + ThaiPhasa + "\t10\r\n" + ThaiThai + "\r\n")];
        var directory = Path.Combine(Path.GetTempPath(), "line-break-dictionary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "words.txt");
            await File.WriteAllBytesAsync(path, words, TestContext.Current.CancellationToken);

            Check(LineBreakDictionary.Load(path));
            Check(await LineBreakDictionary.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

            using var stream = new MemoryStream(words);
            Check(LineBreakDictionary.Load(stream));

            // The stream is left open.
            stream.Position = 0;
            Check(await LineBreakDictionary.LoadAsync(stream, cancellationToken: TestContext.Current.CancellationToken));
            Check(LineBreakDictionary.Parse(words));

            var oneWord = new LineBreakDictionaryOptions { MaxWordCount = 1 };
            stream.Position = 0;
            Assert.Throws<FormatException>(() => LineBreakDictionary.Load(path, oneWord));
            Assert.Throws<FormatException>(() => LineBreakDictionary.Load(stream, oneWord));
            await Assert.ThrowsAsync<FormatException>(() => LineBreakDictionary.LoadAsync(path, oneWord, TestContext.Current.CancellationToken));
            stream.Position = 0;
            await Assert.ThrowsAsync<FormatException>(() => LineBreakDictionary.LoadAsync(stream, oneWord, TestContext.Current.CancellationToken));

            var tooShort = new LineBreakDictionaryOptions { MaxInputLength = words.Length - 1 };
            var longEnough = new LineBreakDictionaryOptions { MaxInputLength = words.Length };
            stream.Position = 0;
            Check(await LineBreakDictionary.LoadAsync(stream, longEnough, TestContext.Current.CancellationToken));
            Check(LineBreakDictionary.Load(path, longEnough));
            stream.Position = 0;
            await Assert.ThrowsAsync<FormatException>(() => LineBreakDictionary.LoadAsync(stream, tooShort, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<FormatException>(() => LineBreakDictionary.LoadAsync(path, tooShort, TestContext.Current.CancellationToken));
            Assert.Throws<FormatException>(() => LineBreakDictionary.Load(path, tooShort));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static void Check(LineBreakDictionary dictionary)
        {
            Assert.Equal(2, dictionary.WordCount);
            Assert.Equal(new[] { 4 }, FindDictionaryBreaks(dictionary, ThaiPhasa + ThaiThai));
        }
    }

    [Fact]
    public async Task LineBreakDictionary_Load_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>("path", () => LineBreakDictionary.Load((string)null!));
        Assert.Throws<ArgumentNullException>("stream", () => LineBreakDictionary.Load((Stream)null!));
        await Assert.ThrowsAsync<ArgumentNullException>("path", () => LineBreakDictionary.LoadAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>("stream", () => LineBreakDictionary.LoadAsync((Stream)null!, cancellationToken: TestContext.Current.CancellationToken));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var stream = new MemoryStream("a\nb"u8.ToArray());
        var isCanceled = false;
        try
        {
            await LineBreakDictionary.LoadAsync(stream, cancellationToken: canceled.Token);
        }
        catch (OperationCanceledException)
        {
            isCanceled = true;
        }

        Assert.True(isCanceled);
    }

    [Fact]
    public void LineBreakDictionaryOptions_ValidateTheirValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineBreakDictionaryOptions { MaxWordCount = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineBreakDictionaryOptions { MaxWordLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineBreakDictionaryOptions { MaxWordLength = 256 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineBreakDictionaryOptions { MaxInputLength = 0 });

        var options = LineBreakDictionaryOptions.Default;
        Assert.Equal(500_000, options.MaxWordCount);
        Assert.Equal(128, options.MaxWordLength);
        Assert.Equal(8 * 1024 * 1024, options.MaxInputLength);
    }

    private static int[] FindDictionaryBreaks(LineBreakDictionary dictionary, string text)
    {
        var positions = new int[text.Length];
        var count = dictionary.FindBreaks(text, positions);
        return positions[..count];
    }

    private static List<UnicodeLineBreak> GetLineBreaks(string text, LineBreakDictionary? dictionary, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
    {
        var result = new List<UnicodeLineBreak>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, dictionary, strictness, wordBreak))
        {
            result.Add(lineBreak);
        }

        return result;
    }

    private static int[] GetLineBreakIndexes(string text, LineBreakDictionary? dictionary, UnicodeLineBreakStrictness strictness = UnicodeLineBreakStrictness.Strict, UnicodeWordBreakBehavior wordBreak = UnicodeWordBreakBehavior.Normal)
    {
        return [.. GetLineBreaks(text, dictionary, strictness, wordBreak).Select(lineBreak => lineBreak.Index)];
    }

    /// <summary>Replaces the letters a to z with Thai consonants: each of them is a grapheme cluster of the class Complex_Context.</summary>
    private static string ToThaiLetters(string text)
    {
        return string.Create(text.Length, text, (span, value) =>
        {
            for (var i = 0; i < value.Length; i++)
            {
                span[i] = char.IsAsciiLetterLower(value[i]) ? (char)(0x0E01 + value[i] - 'a') : value[i];
            }
        });
    }

    /// <summary>Determines whether rule LB9 attaches a character to the character before it: the classes CM and ZWJ, and the marks of the class SA (LB1).</summary>
    private static bool IsLineBreakCombiningMark(int codePoint)
    {
        return UnicodeProperties.GetLineBreakClass(codePoint) switch
        {
            UnicodeLineBreakClass.CombiningMark or UnicodeLineBreakClass.ZWJ => true,
            UnicodeLineBreakClass.ComplexContext => UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark,
            _ => false,
        };
    }

    /// <summary>Applies the documented rule of <see cref="LineBreakDictionary"/> by trying every segmentation of a short text.</summary>
    private static int[] FindBreaksExhaustively(string text, HashSet<string> words)
    {
        var boundaries = new List<int>();
        foreach (var cluster in Unicode.EnumerateGraphemeClusters(text))
        {
            var start = cluster.Start.Value;
            if (start == 0 || !IsLineBreakCombiningMark(char.ConvertToUtf32(text, start)))
            {
                boundaries.Add(start);
            }
        }

        boundaries.Add(text.Length);

        // A segmentation is the length of the word that starts at each index of the text, or 0. The uncovered
        // code units, then the words, are counted, and at the first index where two segmentations differ the
        // longer word wins.
        var wordLengths = new int[text.Length];
        var segmentations = new List<(int Uncovered, int WordCount, int[] WordLengths)>();
        Search(boundary: 0, uncovered: 0, wordCount: 0);
        var best = segmentations[0];
        foreach (var segmentation in segmentations)
        {
            var isPreferred = segmentation.Uncovered != best.Uncovered ? segmentation.Uncovered < best.Uncovered
                : segmentation.WordCount != best.WordCount ? segmentation.WordCount < best.WordCount
                : segmentation.WordLengths.AsSpan().SequenceCompareTo(best.WordLengths) > 0;
            if (isPreferred)
            {
                best = segmentation;
            }
        }

        var result = new List<int>();
        for (var i = 1; i < text.Length; i++)
        {
            if (best.WordLengths[i] != 0)
            {
                result.Add(i);
            }
        }

        return [.. result];

        void Search(int boundary, int uncovered, int wordCount)
        {
            if (boundary == boundaries.Count - 1)
            {
                segmentations.Add((uncovered, wordCount, [.. wordLengths]));
                return;
            }

            var start = boundaries[boundary];
            Search(boundary + 1, uncovered + boundaries[boundary + 1] - start, wordCount);
            for (var end = boundary + 1; end < boundaries.Count; end++)
            {
                if (words.Contains(text[start..boundaries[end]]))
                {
                    wordLengths[start] = boundaries[end] - start;
                    Search(end, uncovered, wordCount + 1);
                    wordLengths[start] = 0;
                }
            }
        }
    }

    /// <summary>Applies the documented rule of <see cref="LineBreakDictionary"/> to a text of any length: the best segmentation of each piece, found by dynamic programming over the boundaries of the piece.</summary>
    private static int[] FindBreaksPieceByPiece(string text, HashSet<string> words)
    {
        const int PieceLength = 1024;
        const int KeptLength = 512;
        var maxWordLength = words.Max(word => word.Length);
        var result = new List<int>();
        var pieceStart = 0;
        while (pieceStart < text.Length)
        {
            // The boundaries of the piece: the grapheme cluster boundaries of the rest of the text that are not before a combining mark.
            var rest = text[pieceStart..];
            var boundaries = new List<int> { 0 };
            var isLastPiece = true;
            var firstClusterLength = 0;
            while (boundaries[^1] < rest.Length)
            {
                var next = boundaries[^1] + Unicode.GetGraphemeClusterLength(rest.AsSpan(boundaries[^1]));
                while (next < rest.Length && IsLineBreakCombiningMark(char.ConvertToUtf32(rest, next)))
                {
                    next += Unicode.GetGraphemeClusterLength(rest.AsSpan(next));
                }

                if (boundaries.Count == 1)
                {
                    firstClusterLength = next;
                }

                if (next > PieceLength)
                {
                    isLastPiece = false;
                    break;
                }

                boundaries.Add(next);
            }

            if (boundaries.Count == 1)
            {
                // A cluster longer than a piece is uncovered.
                pieceStart += firstClusterLength;
                continue;
            }

            // The best segmentation from each boundary to the end of the piece.
            var last = boundaries.Count - 1;
            var costs = new (int Uncovered, int WordCount)[boundaries.Count];
            var choices = new int[boundaries.Count];
            for (var i = last - 1; i >= 0; i--)
            {
                costs[i] = (costs[i + 1].Uncovered + boundaries[i + 1] - boundaries[i], costs[i + 1].WordCount);
                choices[i] = -1;
                for (var j = i + 1; j <= last && boundaries[j] - boundaries[i] <= maxWordLength; j++)
                {
                    // A word is preferred to an uncovered cluster of the same cost, and a longer word to a shorter one.
                    var cost = (costs[j].Uncovered, WordCount: costs[j].WordCount + 1);
                    if (words.Contains(rest[boundaries[i]..boundaries[j]]) && cost.CompareTo(costs[i]) <= 0)
                    {
                        costs[i] = cost;
                        choices[i] = j;
                    }
                }
            }

            var boundary = 0;
            while (boundary < last && (isLastPiece || boundaries[boundary] < KeptLength))
            {
                if (choices[boundary] < 0)
                {
                    boundary++;
                }
                else
                {
                    if (pieceStart + boundaries[boundary] > 0)
                    {
                        result.Add(pieceStart + boundaries[boundary]);
                    }

                    boundary = choices[boundary];
                }
            }

            pieceStart += boundaries[boundary];
        }

        return [.. result];
    }

    /// <summary>Finds the runs of Complex_Context characters as the documentation of the enumerator specifies them: from their first letter.</summary>
    private static List<(int Start, int End)> GetComplexContextRuns(string text)
    {
        var runs = new List<(int Start, int End)>();
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var isComplexContext = index < text.Length && UnicodeProperties.GetLineBreakClass(text[index]) is UnicodeLineBreakClass.ComplexContext;
            if (isComplexContext)
            {
                if (start < 0 && !IsLineBreakCombiningMark(text[index]))
                {
                    start = index;
                }
            }
            else
            {
                if (start >= 0)
                {
                    runs.Add((start, index));
                }

                start = -1;
            }
        }

        return runs;
    }

    private static int NextRandom(ref uint state, int exclusiveMaximum)
    {
        state = (state * 1664525) + 1013904223;
        return (int)((state >> 8) % (uint)exclusiveMaximum);
    }

    private static string RandomText(ref uint state, ReadOnlySpan<string> alphabet, int length)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            builder.Append(alphabet[NextRandom(ref state, alphabet.Length)]);
        }

        return builder.ToString();
    }

    private static string Escape(string text) => string.Concat(text.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));

    [Theory]
    [InlineData("caf\u00E9", NormalizationForm.FormD, "cafe\u0301")]
    [InlineData("cafe\u0301", NormalizationForm.FormC, "caf\u00E9")]
    // The marks are put in canonical order: below (220) before above (230).
    [InlineData("a\u0301\u0323", NormalizationForm.FormD, "a\u0323\u0301")]
    [InlineData("a\u0301\u0323", NormalizationForm.FormC, "\u1EA1\u0301")]
    // A Hangul syllable and its jamos.
    [InlineData("\uD55C", NormalizationForm.FormD, "\u1112\u1161\u11AB")]
    [InlineData("\u1112\u1161\u11AB", NormalizationForm.FormC, "\uD55C")]
    // A ligature and a superscript only change in the compatibility forms.
    [InlineData("\uFB01\u00B2", NormalizationForm.FormC, "\uFB01\u00B2")]
    [InlineData("\uFB01\u00B2", NormalizationForm.FormKC, "fi2")]
    [InlineData("\uFB01\u00B2", NormalizationForm.FormKD, "fi2")]
    // The ohm sign is a singleton: it is replaced in every form.
    [InlineData("\u2126", NormalizationForm.FormC, "\u03A9")]
    // A composition exclusion stays decomposed.
    [InlineData("\u0958", NormalizationForm.FormC, "\u0915\u093C")]
    [InlineData("", NormalizationForm.FormC, "")]
    // The text before the first character that changes is copied: the starter before it still composes with it.
    [InlineData("abcdefe\u0301", NormalizationForm.FormC, "abcdef\u00E9")]
    [InlineData("abc\u0301\u0323", NormalizationForm.FormD, "abc\u0323\u0301")]
    [InlineData("abc\u0301\u0323", NormalizationForm.FormC, "ab\u0107\u0323")]
    [InlineData("abc\uFB01", NormalizationForm.FormKC, "abcfi")]
    [InlineData("\u00E9\u00E9e\u0301", NormalizationForm.FormD, "e\u0301e\u0301e\u0301")]
    [InlineData("\u00E9x\u0301\u00E9\uFB01", NormalizationForm.FormKC, "\u00E9x\u0301\u00E9fi")]
    [InlineData("\u1100\u1161", NormalizationForm.FormC, "\uAC00")]
    [InlineData("\uAC00\u11A8", NormalizationForm.FormC, "\uAC01")]
    public void Normalize_ReturnsTheNormalizedText(string text, NormalizationForm form, string expected)
    {
        var actual = UnicodeNormalization.Normalize(text, form);

        Assert.Equal(expected, actual);
        Assert.Equal(string.Equals(text, expected, StringComparison.Ordinal), UnicodeNormalization.IsNormalized(text, form));
        Assert.Equal(expected.Length, UnicodeNormalization.GetNormalizedLength(text, form));

        var buffer = new char[expected.Length];
        Assert.True(UnicodeNormalization.TryNormalize(text, buffer, out var written, form));
        Assert.Equal(expected, new string(buffer, 0, written));
        if (expected.Length > 0)
        {
            Assert.False(UnicodeNormalization.TryNormalize(text, buffer.AsSpan(1), out written, form));
            Assert.Equal(0, written);
        }
    }

    [Fact]
    public void Normalize_ReturnsTheSameInstanceWhenTheTextIsNormalized()
    {
        var composed = "caf\u00E9 \u1EA1\u0301";
        Assert.Same(composed, UnicodeNormalization.Normalize(composed));

        // The quick check cannot decide for a mark that may combine: the text is still returned as is.
        var maybe = "x\u0301";
        Assert.Same(maybe, UnicodeNormalization.Normalize(maybe));
    }

    [Fact]
    public void Normalize_SortsALongRunOfMarks()
    {
        // 10,000 marks in decreasing class order, twice: the result is sorted and keeps the order of equal classes.
        var marks = new StringBuilder("a");
        for (var i = 0; i < 5000; i++)
        {
            marks.Append("\u0301\u0300\u0323\u0316");
        }

        var normalized = UnicodeNormalization.Normalize(marks.ToString(), NormalizationForm.FormD);

        var expected = new StringBuilder("a");
        for (var i = 0; i < 5000; i++)
        {
            expected.Append("\u0323\u0316");
        }

        for (var i = 0; i < 5000; i++)
        {
            expected.Append("\u0301\u0300");
        }

        Assert.Equal(expected.ToString(), normalized);
    }

    [Fact]
    public void Normalize_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>("value", () => UnicodeNormalization.Normalize(null!));
        Assert.Throws<ArgumentOutOfRangeException>("form", () => UnicodeNormalization.Normalize("a", (NormalizationForm)3));
        Assert.Throws<ArgumentOutOfRangeException>("form", () => UnicodeNormalization.IsNormalized("a", (NormalizationForm)0));
        Assert.Throws<ArgumentOutOfRangeException>("form", () => UnicodeNormalization.GetNormalizedLength("a", (NormalizationForm)7));
        Assert.Throws<ArgumentOutOfRangeException>("form", () => UnicodeNormalization.TryNormalize("a", new char[1], out _, (NormalizationForm)7));
    }

    [Theory]
    [InlineData(0x00E9, true, 0x0065, 0x0301)]
    // One step only: the first code point decomposes again.
    [InlineData(0x1EA5, true, 0x00E2, 0x0301)]
    [InlineData(0x2126, true, 0x03A9, -1)]
    [InlineData(0xD55C, true, 0xD558, 0x11AB)]
    [InlineData(0xD558, true, 0x1112, 0x1161)]
    [InlineData(0x0041, false, 0x0041, -1)]
    // A compatibility decomposition is not a canonical one.
    [InlineData(0xFB01, false, 0xFB01, -1)]
    [InlineData(-1, false, -1, -1)]
    public void TryDecomposeCanonical_ReturnsOneStep(int codePoint, bool expected, int expectedFirst, int expectedSecond)
    {
        Assert.Equal(expected, UnicodeNormalization.TryDecomposeCanonical(codePoint, out var first, out var second));
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedSecond, second);
    }

    [Theory]
    [InlineData(0x0065, 0x0301, true, 0x00E9)]
    [InlineData(0x1112, 0x1161, true, 0xD558)]
    [InlineData(0xD558, 0x11AB, true, 0xD55C)]
    // A syllable that already has a trailing consonant does not take another one.
    [InlineData(0xD55C, 0x11AB, false, 0)]
    // Excluded from composition.
    [InlineData(0x0915, 0x093C, false, 0)]
    [InlineData(0x0041, 0x0042, false, 0)]
    [InlineData(-1, 0x0301, false, 0)]
    [InlineData(0x0065, 0x110000, false, 0)]
    public void TryComposeCanonical_ReturnsThePrimaryComposite(int first, int second, bool expected, int expectedComposite)
    {
        Assert.Equal(expected, UnicodeNormalization.TryComposeCanonical(first, second, out var composite));
        Assert.Equal(expectedComposite, composite);
    }

    [Theory]
    [InlineData(0x0041, new[] { 0x0041 })]
    [InlineData(0x1EA5, new[] { 0x0061, 0x0302, 0x0301 })]
    [InlineData(0xD55C, new[] { 0x1112, 0x1161, 0x11AB })]
    // The longest canonical decompositions have four code points.
    [InlineData(0x1F82, new[] { 0x03B1, 0x0313, 0x0300, 0x0345 })]
    public void GetCanonicalDecomposition_ReturnsTheFullDecomposition(int codePoint, int[] expected)
    {
        var buffer = new int[UnicodeNormalization.MaxCanonicalDecompositionLength];

        var count = UnicodeNormalization.GetCanonicalDecomposition(codePoint, buffer);

        Assert.Equal(expected, buffer[..count]);
        if (expected.Length > 1)
        {
            Assert.Throws<ArgumentException>("destination", () => UnicodeNormalization.GetCanonicalDecomposition(codePoint, new int[expected.Length - 1]));
        }
    }

    [Theory]
    [InlineData("abc", false)]
    [InlineData("caf\u00E9 123", false)]
    [InlineData("a\u05D0", true)]
    [InlineData("a\u0661", true)]
    // An explicit embedding raises the level even in left-to-right text.
    [InlineData("a\u202Ab", true)]
    [InlineData("a\u2067b", true)]
    [InlineData("\U0001E900", true)]
    public void MayRequireBidi_DetectsTheTextThatNeedsTheAlgorithm(string text, bool expected)
    {
        Assert.Equal(expected, UnicodeBidi.MayRequireBidi(text));
        if (!expected)
        {
            var levels = new byte[text.Length];
            Assert.Equal(0, UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels));
            Assert.Equal(new byte[text.Length], levels);
        }
    }

    [Theory]
    [InlineData("abc", UnicodeBidiDirection.Auto, 0)]
    [InlineData("", UnicodeBidiDirection.Auto, 0)]
    [InlineData("123 \u05D0", UnicodeBidiDirection.Auto, 1)]
    [InlineData("\u0627 abc", UnicodeBidiDirection.Auto, 1)]
    // The content of an isolate does not count.
    [InlineData("\u2067\u05D0\u2069 a", UnicodeBidiDirection.Auto, 0)]
    [InlineData("\u05D0", UnicodeBidiDirection.LeftToRight, 0)]
    [InlineData("a", UnicodeBidiDirection.RightToLeft, 1)]
    public void GetParagraphLevel_ReturnsTheLevel(string text, UnicodeBidiDirection direction, int expected)
    {
        Assert.Equal(expected, UnicodeBidi.GetParagraphLevel(text, direction));
        Assert.Equal(expected, UnicodeBidi.ResolveLevels(text, direction, new byte[text.Length]));
    }

    [Fact]
    public void ResolveLevels_ReordersMixedText()
    {
        // "car means " then three Hebrew letters and an exclamation mark, in a left-to-right paragraph.
        var text = "car \u05D0\u05D1\u05D2!";
        var levels = new byte[text.Length];

        Assert.Equal(0, UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 1, 1, 0 }, levels);

        var order = new int[text.Length];
        UnicodeBidi.GetVisualOrder(levels, order);
        Assert.Equal(new[] { 0, 1, 2, 3, 6, 5, 4, 7 }, order);

        // In a right-to-left paragraph the Latin word is the embedded one, and the mark follows the paragraph.
        Assert.Equal(1, UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.RightToLeft, levels));
        Assert.Equal(new byte[] { 2, 2, 2, 1, 1, 1, 1, 1 }, levels);
        UnicodeBidi.GetVisualOrder(levels, order);
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 0, 1, 2 }, order);
    }

    [Fact]
    public void ResolveLevels_GivesBothCodeUnitsOfAPairTheSameLevel()
    {
        // Adlam is a right-to-left script outside the Basic Multilingual Plane.
        var text = "a\U0001E900\U0001E901b";
        var levels = new byte[text.Length];

        UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels);

        Assert.Equal(new byte[] { 0, 1, 1, 1, 1, 0 }, levels);
    }

    [Fact]
    public void ResetLineLevels_GivesTrailingWhiteSpaceTheParagraphLevel()
    {
        // The line ends with Hebrew then spaces: the spaces would be displayed on the left without the reset.
        var text = "a \u05D0\u05D1  ";
        var levels = new byte[text.Length];
        UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.RightToLeft, levels);
        Assert.Equal(new byte[] { 2, 1, 1, 1, 1, 1 }, levels);

        // The same text as a line of a left-to-right paragraph.
        var paragraphLevel = UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.LeftToRight, levels);
        levels.AsSpan(4).Fill(1);
        UnicodeBidi.ResetLineLevels(text, paragraphLevel, levels);
        Assert.Equal(new byte[] { 0, 0, 1, 1, 0, 0 }, levels);

        // A tab is a segment separator: it and the spaces before it take the paragraph level.
        var withTab = "\u05D0 \t\u05D1";
        var tabLevels = new byte[] { 1, 1, 1, 1 };
        UnicodeBidi.ResetLineLevels(withTab, 0, tabLevels);
        Assert.Equal(new byte[] { 1, 0, 0, 1 }, tabLevels);
    }

    [Fact]
    public void GetVisualOrder_ReversesNestedLevels()
    {
        byte[] levels = [0, 1, 2, 2, 1, 0];
        var order = new int[6];

        UnicodeBidi.GetVisualOrder(levels, order);

        Assert.Equal(new[] { 0, 4, 2, 3, 1, 5 }, order);
    }

    [Fact]
    public void Bidi_ValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("direction", () => UnicodeBidi.GetParagraphLevel("a", (UnicodeBidiDirection)3));
        Assert.Throws<ArgumentOutOfRangeException>("direction", () => UnicodeBidi.ResolveLevels("a", (UnicodeBidiDirection)3, new byte[1]));
        Assert.Throws<ArgumentException>("levels", () => UnicodeBidi.ResolveLevels("ab", UnicodeBidiDirection.Auto, new byte[1]));
        Assert.Throws<ArgumentException>("workspace", () => UnicodeBidi.ResolveLevels("ab", UnicodeBidiDirection.Auto, new byte[2], new int[UnicodeBidi.GetWorkspaceLength(2) - 1]));
        Assert.Throws<ArgumentOutOfRangeException>("textLength", () => UnicodeBidi.GetWorkspaceLength(-1));
        Assert.Throws<OverflowException>(() => UnicodeBidi.GetWorkspaceLength(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>("paragraphLevel", () => UnicodeBidi.ResetLineLevels("a", 2, new byte[1]));
        Assert.Throws<ArgumentException>("levels", () => UnicodeBidi.ResetLineLevels("ab", 0, new byte[1]));
        Assert.Throws<ArgumentException>("visualToLogical", () => UnicodeBidi.GetVisualOrder(new byte[2], new int[1]));
    }

    [Fact]
    public void ResolveLevels_HandlesDeepNestingAndLongParagraphs()
    {
        // More embeddings than the algorithm allows, and a paragraph longer than the stack-allocated workspace.
        var text = new string('\u202B', 300) + new string('a', 2000) + new string('\u05D0', 2000);
        var levels = new byte[text.Length];

        // An embedding is not a strong character: the paragraph takes the direction of the first letter.
        Assert.Equal(0, UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels));

        // Right-to-left embeddings give the odd levels up to 125; the Latin letters are one level above.
        Assert.Equal(126, levels[300]);
        Assert.Equal(125, levels[^1]);
    }

    [Theory]
    [InlineData("", new int[0], new UnicodeScript[0])]
    [InlineData("hello", new[] { 5 }, new[] { UnicodeScript.Latin })]
    // Spaces, digits and punctuation belong to the run before them.
    [InlineData("abc 123, \u0430\u0431\u0432!", new[] { 9, 4 }, new[] { UnicodeScript.Latin, UnicodeScript.Cyrillic })]
    // At the start of the text they belong to the run after them.
    [InlineData("123 \u0430\u0431", new[] { 6 }, new[] { UnicodeScript.Cyrillic })]
    [InlineData("123 !?", new[] { 6 }, new[] { UnicodeScript.Common })]
    // Both parentheses are in the script of the text around them.
    [InlineData("gamma (\u03B3) is", new[] { 7, 1, 4 }, new[] { UnicodeScript.Latin, UnicodeScript.Greek, UnicodeScript.Latin })]
    // A combining mark has the script of its base.
    [InlineData("e\u0301\u03B1\u0301", new[] { 2, 2 }, new[] { UnicodeScript.Latin, UnicodeScript.Greek })]
    // The prolonged sound mark is only used with Hiragana and Katakana: it does not continue a Latin run.
    [InlineData("a\u30FC\u30AB", new[] { 1, 2 }, new[] { UnicodeScript.Latin, UnicodeScript.Katakana })]
    [InlineData("\u3042\u30FC\u30AB", new[] { 2, 1 }, new[] { UnicodeScript.Hiragana, UnicodeScript.Katakana })]
    // Private use characters have no script: they are a run of their own.
    [InlineData("a\uE000b", new[] { 1, 1, 1 }, new[] { UnicodeScript.Latin, UnicodeScript.Unknown, UnicodeScript.Latin })]
    // The space after a Cyrillic word stays with it, and the Latin letters start the next run.
    [InlineData("\u0430\u0431 abc", new[] { 3, 3 }, new[] { UnicodeScript.Cyrillic, UnicodeScript.Latin })]
    // The last ASCII letter takes the mark that follows it.
    [InlineData("abc\u0301\u0430", new[] { 4, 1 }, new[] { UnicodeScript.Latin, UnicodeScript.Cyrillic })]
    [InlineData("(abc) \u0430", new[] { 6, 1 }, new[] { UnicodeScript.Latin, UnicodeScript.Cyrillic })]
    public void EnumerateScriptRuns_ReturnsTheRuns(string text, int[] expectedLengths, UnicodeScript[] expectedScripts)
    {
        var lengths = new List<int>();
        var scripts = new List<UnicodeScript>();
        var position = 0;
        foreach (var run in Unicode.EnumerateScriptRuns(text))
        {
            Assert.Equal(position, run.Index);
            lengths.Add(run.Length);
            scripts.Add(run.Script);
            position += run.Length;
        }

        Assert.Equal(text.Length, position);
        Assert.Equal(expectedLengths, lengths.ToArray());
        Assert.Equal(expectedScripts, scripts.ToArray());
    }

    [Fact]
    public void SegmentationValues_ImplementEquality()
    {
        var lineBreaks = new List<UnicodeLineBreak>();
        foreach (var lineBreak in Unicode.EnumerateLineBreaks("a b"))
        {
            lineBreaks.Add(lineBreak);
        }

        Assert.Equal(lineBreaks[0], lineBreaks[0]);
        Assert.True(lineBreaks[0] != lineBreaks[1]);
        Assert.False(lineBreaks[0].Equals((object)lineBreaks[1]));
        Assert.NotEqual(lineBreaks[0].GetHashCode(), lineBreaks[1].GetHashCode());
        Assert.Equal(default, new UnicodeLineBreak());

        var runs = new List<UnicodeScriptRun>();
        foreach (var run in Unicode.EnumerateScriptRuns("a\u0430"))
        {
            runs.Add(run);
        }

        Assert.Equal(runs[0], runs[0]);
        Assert.True(runs[0] != runs[1]);
        Assert.False(runs[0].Equals((object)runs[1]));
        Assert.Equal(default, new UnicodeScriptRun());
    }

    [Theory]
    [InlineData('a', true, true)]
    [InlineData('Z', true, true)]
    [InlineData('_', false, true)]
    [InlineData('0', false, true)]
    [InlineData('$', false, false)]
    [InlineData(' ', false, false)]
    [InlineData(0x00B7, false, true)] // MIDDLE DOT (Other_ID_Continue)
    [InlineData(0x0300, false, true)] // COMBINING GRAVE ACCENT
    [InlineData(0x2118, true, true)] // SCRIPT CAPITAL P (Other_ID_Start)
    [InlineData(0x309B, true, true)] // KATAKANA-HIRAGANA VOICED SOUND MARK (Other_ID_Start)
    [InlineData(0x4E00, true, true)]
    [InlineData(0x16EE, true, true)] // RUNIC ARLAUG SYMBOL (Nl)
    [InlineData(0x1F600, false, false)]
    [InlineData(0xD800, false, false)]
    [InlineData(-1, false, false)]
    [InlineData(0x110000, false, false)]
    public void IsIdStart_IsIdContinue(int codePoint, bool idStart, bool idContinue)
    {
        Assert.Equal(idStart, UnicodeProperties.IsIdStart(codePoint));
        Assert.Equal(idContinue, UnicodeProperties.IsIdContinue(codePoint));
    }

    [Fact]
    public void IsIdStart_ImpliesIsIdContinue()
    {
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (UnicodeProperties.IsIdStart(codePoint))
            {
                Assert.True(UnicodeProperties.IsIdContinue(codePoint), $"U+{codePoint:X4}");
            }
        }
    }

    [Theory]
    [InlineData('a', 'A')]
    [InlineData('A', 'A')]
    [InlineData(0x01C6, 0x01C5)] // dž → Dž
    [InlineData(0x01C4, 0x01C5)] // DŽ → Dž
    [InlineData(0x01F3, 0x01F2)] // dz → Dz
    [InlineData(0x10D0, 0x10D0)] // Georgian Mkhedruli letters are their own titlecase
    [InlineData(0x00DF, 0x00DF)]
    [InlineData(-1, -1)]
    public void ToSimpleTitlecase(int codePoint, int expected)
    {
        Assert.Equal(expected, UnicodeProperties.ToSimpleTitlecase(codePoint));
    }

    [Theory]
    [InlineData(0x61, "A")]
    [InlineData(0xDF, "SS")]
    [InlineData(0xFB03, "FFI")]
    [InlineData(0x149, "\u02BCN")]
    [InlineData(0x390, "\u0399\u0308\u0301")]
    [InlineData(0x10428, "\U00010400")]
    [InlineData(0x41, "A")]
    [InlineData(0x31, "1")]
    [InlineData(0x17F, "S")]
    [InlineData(0x131, "I")]
    [InlineData(0x10D0, "\u1C90")] // Georgian Mkhedruli → Mtavruli
    public void GetUppercaseMapping(int codePoint, string expected)
    {
        Span<int> buffer = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        var length = UnicodeProperties.GetUppercaseMapping(codePoint, buffer);
        Assert.Equal(expected, ToString(buffer[..length]));
    }

    [Theory]
    [InlineData(0xFB01, "Fi")]
    [InlineData(0xDF, "Ss")]
    [InlineData(0x1F80, "\u1F88")]
    [InlineData(0x61, "A")]
    [InlineData(0x1C6, "\u01C5")]
    [InlineData(0x1F1, "\u01F2")]
    [InlineData(0x10D0, "\u10D0")]
    public void GetTitlecaseMapping(int codePoint, string expected)
    {
        Span<int> buffer = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        var length = UnicodeProperties.GetTitlecaseMapping(codePoint, buffer);
        Assert.Equal(expected, ToString(buffer[..length]));
    }

    [Theory]
    [InlineData(0x130, "i\u0307")]
    [InlineData(0x41, "a")]
    [InlineData(0x10400, "\U00010428")]
    [InlineData(0x3A3, "\u03C3")] // The final sigma depends on the context: it is not applied
    [InlineData(0x31, "1")]
    public void GetLowercaseMapping(int codePoint, string expected)
    {
        Span<int> buffer = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        var length = UnicodeProperties.GetLowercaseMapping(codePoint, buffer);
        Assert.Equal(expected, ToString(buffer[..length]));
    }

    [Fact]
    public void GetCaseMapping_IsTheSimpleMappingWhenItIsOneCodePoint()
    {
        Span<int> buffer = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        for (var codePoint = -1; codePoint <= 0x110000; codePoint++)
        {
            var length = UnicodeProperties.GetUppercaseMapping(codePoint, buffer);
            Assert.InRange(length, 1, UnicodeProperties.MaxCaseMappingLength);
            if (length == 1)
            {
                // The only one-to-one full mappings that are not the simple mapping are the ones of SpecialCasing.txt
                // to the code point itself (e.g. the Greek letters with a prosgegrammeni, whose simple mapping is
                // their titlecase form)
                Assert.True(buffer[0] == UnicodeProperties.ToSimpleUppercase(codePoint) || buffer[0] == codePoint, $"U+{codePoint:X4}");
            }

            length = UnicodeProperties.GetLowercaseMapping(codePoint, buffer);
            Assert.InRange(length, 1, UnicodeProperties.MaxCaseMappingLength);
            if (length == 1)
            {
                Assert.Equal(UnicodeProperties.ToSimpleLowercase(codePoint), buffer[0]);
            }

            length = UnicodeProperties.GetTitlecaseMapping(codePoint, buffer);
            Assert.InRange(length, 1, UnicodeProperties.MaxCaseMappingLength);
            if (length == 1)
            {
                Assert.Equal(UnicodeProperties.ToSimpleTitlecase(codePoint), buffer[0]);
            }
        }
    }

    [Fact]
    public void GetCaseMapping_ThrowsWhenTheDestinationIsTooSmall()
    {
        Assert.Throws<ArgumentException>("destination", () => UnicodeProperties.GetUppercaseMapping('a', []));
        Assert.Throws<ArgumentException>("destination", () => UnicodeProperties.GetUppercaseMapping(0xDF, new int[1]));
        Assert.Throws<ArgumentException>("destination", () => UnicodeProperties.GetLowercaseMapping(0x130, new int[1]));
        Assert.Throws<ArgumentException>("destination", () => UnicodeProperties.GetTitlecaseMapping(0xFB01, new int[1]));
    }

    // RFC 3492, section 7.1
    [Theory]
    [InlineData("\u0644\u064A\u0647\u0645\u0627\u0628\u062A\u0643\u0644\u0645\u0648\u0634\u0639\u0631\u0628\u064A\u061F", "egbpdaj6bu4bxfgehfvwxn")]
    [InlineData("\u4ED6\u4EEC\u4E3A\u4EC0\u4E48\u4E0D\u8BF4\u4E2D\u6587", "ihqwcrb4cv8a8dqg056pqjye")]
    [InlineData("Pro\u010Dprost\u011Bnemluv\u00ED\u010Desky", "Proprostnemluvesky-uyb24dma41a")]
    [InlineData("\u05DC\u05DE\u05D4\u05D4\u05DD\u05E4\u05E9\u05D5\u05D8\u05DC\u05D0\u05DE\u05D3\u05D1\u05E8\u05D9\u05DD\u05E2\u05D1\u05E8\u05D9\u05EA", "4dbcagdahymbxekheh6e0a7fei0b")]
    [InlineData("3\u5E74B\u7D44\u91D1\u516B\u5148\u751F", "3B-ww4c5e180e575a65lsy2b")]
    [InlineData("-> $1.00 <-", "-> $1.00 <--")]
    [InlineData("b\u00FCcher", "bcher-kva")]
    [InlineData("", "")]
    public void Punycode_RoundTrips(string decoded, string encoded)
    {
        Assert.True(UnicodeIdna.TryEncodePunycode(decoded, out var actualEncoded));
        Assert.Equal(encoded, actualEncoded);
        Assert.True(UnicodeIdna.TryDecodePunycode(encoded, out var actualDecoded));
        Assert.Equal(decoded, actualDecoded);
    }

    [Theory]
    [InlineData("b\u00FCcher")] // Not ASCII
    [InlineData("a-!")] // Not a base-36 digit
    [InlineData("a-b")] // Truncated
    [InlineData("99999999999")] // Overflow
    public void Punycode_RejectsInvalidInput(string value)
    {
        Assert.False(UnicodeIdna.TryDecodePunycode(value, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Punycode_RejectsUnpairedSurrogates()
    {
        // InlineData would replace the unpaired surrogates
        foreach (var value in new[] { "\uD800", "\uDC00", "a\uD800b", "\uDC00\uD800", "\U0001F600\uD800", "a\uD83D" })
        {
            Assert.False(UnicodeIdna.TryEncodePunycode(value, out var result));
            Assert.Null(result);
        }

        Assert.True(UnicodeIdna.TryEncodePunycode("\uFFFD", out var replacement));
        Assert.Equal("zn7c", replacement);

        Assert.True(UnicodeIdna.TryEncodePunycode("a\U0001F600", out var pair));
        Assert.True(UnicodeIdna.TryDecodePunycode(pair, out var decoded));
        Assert.Equal("a\U0001F600", decoded);
    }

    [Fact]
    public void Punycode_RoundTripsLongInputs()
    {
        // The conversions are not quadratic: these sizes took seconds with a list insertion per code point
        Assert.True(UnicodeIdna.TryDecodePunycode(new string('b', 32_000), out var decoded));
        Assert.True(UnicodeIdna.TryEncodePunycode(decoded, out var encoded));
        Assert.Equal(new string('b', 32_000), encoded);

        var builder = new StringBuilder();
        for (var i = 0; i < 1_000; i++)
        {
            builder.Append(char.ConvertFromUtf32(0x10000 + (i * 7919 % 1_000)));
            builder.Append((char)('a' + (i % 26)));
            builder.Append((char)(0x4E00 + (i * 31 % 500)));
        }

        var text = builder.ToString();
        Assert.True(UnicodeIdna.TryEncodePunycode(text, out encoded));
        Assert.True(UnicodeIdna.TryDecodePunycode(encoded, out decoded));
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Punycode_EncodesWhatItDecodes()
    {
        // The decoding is unique: TryToAscii relies on it to reject a long ACE label without decoding it
        // Every value of 1 to 4 characters
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-";
        var value = new char[4];
        var decodedCount = 0;
        for (var length = 1; length <= value.Length; length++)
        {
            var combinations = (int)Math.Pow(Alphabet.Length, length);
            for (var combination = 0; combination < combinations; combination++)
            {
                var remainder = combination;
                for (var j = 0; j < length; j++)
                {
                    value[j] = Alphabet[remainder % Alphabet.Length];
                    remainder /= Alphabet.Length;
                }

                if (UnicodeIdna.TryDecodePunycode(value.AsSpan(0, length), out var decoded))
                {
                    decodedCount++;
                    Assert.True(UnicodeIdna.TryEncodePunycode(decoded, out var encoded));
                    Assert.Equal(new string(value, 0, length), encoded);
                }
            }
        }

        Assert.True(decodedCount > 10_000, $"Only {decodedCount} values were decoded");
    }

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("B\u00FCcher.example", "xn--bcher-kva.example")]
    [InlineData("fa\u00DF.de", "xn--fa-hia.de")]
    [InlineData("\u2163.example", "iv.example")] // Mapped (ROMAN NUMERAL FOUR)
    [InlineData("a\u00AD.example", "a.example")] // Ignored (SOFT HYPHEN)
    [InlineData("\uFF21\uFF22\u3002com", "ab.com")] // Fullwidth letters and ideographic full stop
    [InlineData("WWW.Example.COM", "www.example.com")]
    [InlineData("a-b.c-d", "a-b.c-d")]
    public void TryToAscii_ConvertsValidNames(string domainName, string expected)
    {
        Assert.True(UnicodeIdna.TryToAscii(domainName, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData("example.com.")] // The root label is empty
    [InlineData("-example.com")]
    [InlineData("ab--c.com")]
    [InlineData("exa_mple.com")]
    [InlineData("xn--a.com")] // Decodes to a disallowed code point
    [InlineData("xn--a!.com")] // Invalid Punycode
    [InlineData("\u0301a.com")] // Starts with a mark
    [InlineData("a\u200Db.com")] // ZWJ without a virama
    [InlineData("\u05D0a.com")] // Bidi rule
    [InlineData("\uFFFD.com")]
    [InlineData("a-.com")]
    [InlineData(".com")]
    public void TryToAscii_RejectsInvalidNames(string domainName)
    {
        Assert.False(UnicodeIdna.TryToAscii(domainName, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void TryToAscii_VerifiesTheDnsLength()
    {
        var label = new string('a', 64);
        Assert.False(UnicodeIdna.TryToAscii(label + ".com", out _));
        Assert.True(UnicodeIdna.TryToAscii(label + ".com", new UnicodeIdnaOptions { VerifyDnsLength = false }, out var result));
        Assert.Equal(label + ".com", result);

        Assert.True(UnicodeIdna.TryToAscii("example.com.", new UnicodeIdnaOptions { VerifyDnsLength = false }, out result));
        Assert.Equal("example.com.", result);
        Assert.True(UnicodeIdna.TryToAscii("a..b", new UnicodeIdnaOptions { VerifyDnsLength = false }, out result));
        Assert.Equal("a..b", result);
    }

    [Fact]
    public void TryToAscii_AppliesTheOptions()
    {
        Assert.False(UnicodeIdna.TryToAscii("exa_mple.com", out _));
        Assert.True(UnicodeIdna.TryToAscii("exa_mple.com", new UnicodeIdnaOptions { UseStd3AsciiRules = false }, out var result));
        Assert.Equal("exa_mple.com", result);

        Assert.False(UnicodeIdna.TryToAscii("-a.com", out _));
        Assert.True(UnicodeIdna.TryToAscii("-a.com", new UnicodeIdnaOptions { CheckHyphens = false }, out result));
        Assert.Equal("-a.com", result);

        Assert.True(UnicodeIdna.TryToAscii("fa\u00DF.de", new UnicodeIdnaOptions { TransitionalProcessing = true }, out result));
        Assert.Equal("fass.de", result);

        Assert.False(UnicodeIdna.TryToAscii("a\u200Db.com", new UnicodeIdnaOptions { UseStd3AsciiRules = false }, out _));
        Assert.True(UnicodeIdna.TryToAscii("a\u200Db.com", new UnicodeIdnaOptions { CheckJoiners = false }, out result));
        Assert.Equal("xn--ab-m1t.com", result);
    }

    [Theory]
    [InlineData("xn--bcher-kva.example", "b\u00FCcher.example", false)]
    [InlineData("B\u00FCcher.Example", "b\u00FCcher.example", false)]
    [InlineData("xn--a!.com", "xn--a!.com", true)]
    [InlineData("xn--ab-m1t.com", "a\u200Db.com", true)]
    [InlineData("example.com.", "example.com.", false)]
    [InlineData("a..b", "a..b", true)]
    [InlineData("", "", true)]
    [InlineData("Example.COM", "example.com", false)]
    [InlineData("-a.com", "-a.com", true)]
    [InlineData(".a", ".a", true)]
    [InlineData(".", ".", true)]
    public void ToUnicode(string domainName, string expected, bool expectedErrors)
    {
        Assert.Equal(expected, UnicodeIdna.ToUnicode(domainName, out var hasErrors));
        Assert.Equal(expectedErrors, hasErrors);
    }

    [Theory]
    [InlineData("xn--z.example", "xn--z.example")]
    [InlineData("example.xn--z", "example.xn--z")]
    [InlineData("xn--z.xn--a!", "xn--z.xn--a!")]
    [InlineData("xn--bcher-kva.xn--z", "b\u00FCcher.xn--z")]
    public void IgnoreInvalidPunycode_KeepsTheLabelButReportsAnError(string domainName, string expected)
    {
        // UTS #46, Processing, step 4: the kept label contains only ASCII code points, which is an error
        foreach (var checkHyphens in new[] { true, false })
        {
            foreach (var ignoreInvalidPunycode in new[] { true, false })
            {
                var options = new UnicodeIdnaOptions { IgnoreInvalidPunycode = ignoreInvalidPunycode, CheckHyphens = checkHyphens };
                Assert.Equal(expected, UnicodeIdna.ToUnicode(domainName, options, out var hasErrors));
                Assert.True(hasErrors);
                Assert.False(UnicodeIdna.TryToAscii(domainName, options, out var result));
                Assert.Null(result);
            }
        }
    }

    [Fact]
    public void TryToAscii_RejectsLongAceLabels()
    {
        var longLabel = "xn--" + new string('b', 32_000);
        Assert.False(UnicodeIdna.TryToAscii(longLabel, out _));

        // An ACE label is rejected without being decoded when it is too long: the result is the same as when it is
        // decoded and encoded again
        var noLengthCheck = new UnicodeIdnaOptions { VerifyDnsLength = false };
        Assert.True(UnicodeIdna.TryToAscii(longLabel, noLengthCheck, out var longResult));
        Assert.Equal(longLabel, longResult);
        for (var length = 40; length <= 70; length++)
        {
            Assert.True(UnicodeIdna.TryEncodePunycode(new string('a', length) + "\u00FC\u4E00", out var encoded));
            var label = "xn--" + encoded;
            Assert.Equal(label.Length <= 63, UnicodeIdna.TryToAscii(label + ".example", out _));
            Assert.True(UnicodeIdna.TryToAscii(label + ".example", noLengthCheck, out var result));
            Assert.Equal(label + ".example", result);
        }
    }

    [Fact]
    public void ToUnicode_DoesNotVerifyTheDnsLength()
    {
        Assert.True(UnicodeIdna.TryEncodePunycode(new string('a', 100) + "\u00FC", out var encoded));
        Assert.Equal(new string('a', 100) + "\u00FC.example", UnicodeIdna.ToUnicode("xn--" + encoded + ".example", out var hasErrors));
        Assert.False(hasErrors);
    }

    [Fact]
    public void Idna_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>("domainName", () => UnicodeIdna.TryToAscii(null!, out _));
        Assert.Throws<ArgumentNullException>("domainName", () => UnicodeIdna.ToUnicode(null!, out _));
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('a', 'a')]
    [InlineData(0x00DF, 0x00DF)] // the full folding of U+00DF is "ss": it has no simple folding
    [InlineData(0x1E9E, 0x00DF)] // capital sharp s
    [InlineData(0x017F, 's')] // long s
    [InlineData(0x03C2, 0x03C3)] // final sigma
    [InlineData(0x03A3, 0x03C3)]
    [InlineData(0x0130, 0x0130)] // only a full (F) and a Turkic (T) mapping
    [InlineData(0x13F8, 0x13F0)] // Cherokee small letters fold to the capital ones
    [InlineData(0xAB70, 0x13A0)]
    [InlineData(0x10400, 0x10428)] // Deseret
    [InlineData(0x1F600, 0x1F600)]
    [InlineData(-1, -1)]
    [InlineData(0x110000, 0x110000)]
    public void ToSimpleCaseFold(int codePoint, int expected)
    {
        Assert.Equal(expected, UnicodeProperties.ToSimpleCaseFold(codePoint));
    }

    [Fact]
    public void ToSimpleCaseFold_IsIdempotent()
    {
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            var folded = UnicodeProperties.ToSimpleCaseFold(codePoint);
            if (UnicodeProperties.ToSimpleCaseFold(folded) != folded)
                Assert.Fail($"U+{codePoint:X4}");
        }
    }

    [Theory]
    [InlineData(' ', UnicodeBinaryProperty.WhiteSpace, true)]
    [InlineData(0x3000, UnicodeBinaryProperty.WhiteSpace, true)]
    [InlineData('a', UnicodeBinaryProperty.WhiteSpace, false)]
    [InlineData(0x0345, UnicodeBinaryProperty.Alphabetic, true)] // through Other_Alphabetic
    [InlineData(0x0345, UnicodeBinaryProperty.Lowercase, true)]
    [InlineData(0x0345, UnicodeBinaryProperty.ChangesWhenCasefolded, true)]
    [InlineData('A', UnicodeBinaryProperty.ChangesWhenLowercased, true)]
    [InlineData('a', UnicodeBinaryProperty.ChangesWhenLowercased, false)]
    [InlineData('a', UnicodeBinaryProperty.ChangesWhenUppercased, true)]
    [InlineData(0x00AD, UnicodeBinaryProperty.ChangesWhenNFKCCasefolded, true)]
    [InlineData('\'', UnicodeBinaryProperty.CaseIgnorable, true)]
    [InlineData(0x1F600, UnicodeBinaryProperty.Emoji, true)]
    [InlineData(0x1F600, UnicodeBinaryProperty.EmojiPresentation, true)]
    [InlineData(0x1F3FB, UnicodeBinaryProperty.EmojiModifier, true)]
    [InlineData(0x261D, UnicodeBinaryProperty.EmojiModifierBase, true)]
    [InlineData('#', UnicodeBinaryProperty.EmojiComponent, true)]
    [InlineData(0x00A9, UnicodeBinaryProperty.ExtendedPictographic, true)]
    [InlineData('(', UnicodeBinaryProperty.BidiMirrored, true)]
    [InlineData(0x200E, UnicodeBinaryProperty.BidiControl, true)]
    [InlineData(0x4E00, UnicodeBinaryProperty.UnifiedIdeograph, true)]
    [InlineData(0x4E00, UnicodeBinaryProperty.Ideographic, true)]
    [InlineData(0xFDD0, UnicodeBinaryProperty.NoncharacterCodePoint, true)]
    [InlineData(0x10FFFF, UnicodeBinaryProperty.NoncharacterCodePoint, true)]
    [InlineData(0x1F1E6, UnicodeBinaryProperty.RegionalIndicator, true)]
    [InlineData('x', UnicodeBinaryProperty.XIDStart, true)]
    [InlineData(0x2E2F, UnicodeBinaryProperty.IDStart, false)]
    [InlineData(0x0340, UnicodeBinaryProperty.FullCompositionExclusion, true)]
    [InlineData(-1, UnicodeBinaryProperty.Alphabetic, false)]
    [InlineData(0x110000, UnicodeBinaryProperty.NoncharacterCodePoint, false)]
    public void HasBinaryProperty(int codePoint, UnicodeBinaryProperty property, bool expected)
    {
        Assert.Equal(expected, UnicodeProperties.HasBinaryProperty(codePoint, property));
    }

    [Fact]
    public void HasBinaryProperty_MatchesTheOtherAccessors()
    {
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.IDStart) != UnicodeProperties.IsIdStart(codePoint)
                || UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.IDContinue) != UnicodeProperties.IsIdContinue(codePoint)
                || UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.DefaultIgnorableCodePoint) != UnicodeProperties.IsDefaultIgnorable(codePoint)
                || UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.BidiMirrored) != UnicodeProperties.IsBidiMirrored(codePoint)
                || UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.Emoji) != UnicodeEmoji.IsEmoji(codePoint)
                || UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.ExtendedPictographic) != UnicodeEmoji.IsExtendedPictographic(codePoint))
            {
                Assert.Fail($"U+{codePoint:X4}");
            }
        }
    }

    [Fact]
    public void GetBinaryPropertyRanges_AreSortedAndMatchHasBinaryProperty()
    {
        foreach (var property in Enum.GetValues<UnicodeBinaryProperty>())
        {
            var ranges = UnicodeProperties.GetBinaryPropertyRanges(property);
            Assert.NotEmpty(ranges);
            for (var i = 1; i < ranges.Length; i++)
            {
                Assert.True(ranges[i].Start > ranges[i - 1].End + 1, $"{property}: {ranges[i - 1]} {ranges[i]}");
            }

            foreach (var range in ranges)
            {
                Assert.True(UnicodeProperties.HasBinaryProperty(range.Start, property));
                Assert.True(UnicodeProperties.HasBinaryProperty(range.End, property));
                if (range.Start > 0)
                {
                    Assert.False(UnicodeProperties.HasBinaryProperty(range.Start - 1, property));
                }

                if (range.End < 0x10FFFF)
                {
                    Assert.False(UnicodeProperties.HasBinaryProperty(range.End + 1, property));
                }
            }
        }
    }

    [Fact]
    public void BinaryProperty_ThrowsOnUndefinedValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>("property", () => UnicodeProperties.HasBinaryProperty('a', (UnicodeBinaryProperty)255));
        Assert.Throws<ArgumentOutOfRangeException>("property", () => UnicodeProperties.GetBinaryPropertyRanges((UnicodeBinaryProperty)255));
    }

    [Theory]
    [InlineData("Latin", UnicodeScript.Latin)]
    [InlineData("Latn", UnicodeScript.Latin)]
    [InlineData("Old_Italic", UnicodeScript.OldItalic)]
    [InlineData("Ital", UnicodeScript.OldItalic)]
    [InlineData("Qaac", UnicodeScript.Coptic)]
    [InlineData("Coptic", UnicodeScript.Coptic)]
    [InlineData("Qaai", UnicodeScript.Inherited)]
    [InlineData("Zinh", UnicodeScript.Inherited)]
    [InlineData("Zyyy", UnicodeScript.Common)]
    [InlineData("Unknown", UnicodeScript.Unknown)]
    [InlineData("Zzzz", UnicodeScript.Unknown)]
    public void TryParseScript(string name, UnicodeScript expected)
    {
        Assert.True(UnicodeScripts.TryParseScript(name, out var script));
        Assert.Equal(expected, script);
    }

    [Theory]
    [InlineData("latin")]
    [InlineData("Old Italic")]
    [InlineData("OldItalic")]
    [InlineData("")]
    public void TryParseScript_ComparesExactly(string name)
    {
        Assert.False(UnicodeScripts.TryParseScript(name, out var script));
        Assert.Equal(UnicodeScript.Unknown, script);
    }

    [Fact]
    public void TryParseScript_KnowsTheIso15924CodeOfEveryScript()
    {
        foreach (var script in Enum.GetValues<UnicodeScript>())
        {
            Assert.True(UnicodeScripts.TryParseScript(UnicodeScripts.GetIso15924Code(script), out var parsed));
            Assert.Equal(script, parsed);
        }
    }

    [Fact]
    public void GetSequences()
    {
        Assert.Contains("⌚", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.BasicEmoji));
        Assert.Contains("©️", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.BasicEmoji));
        Assert.Contains("#️⃣", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.EmojiKeycapSequence));
        Assert.Equal(12, UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.EmojiKeycapSequence).Count);
        Assert.Contains("\U0001F1EB\U0001F1F7", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiFlagSequence));
        Assert.Contains("☝\U0001F3FB", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiModifierSequence));
        Assert.Contains("\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiTagSequence));
        Assert.Contains("\U0001F468‍❤️‍\U0001F468", UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiZwjSequence));
        Assert.Same(UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.BasicEmoji), UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.BasicEmoji));
        Assert.Throws<ArgumentOutOfRangeException>("property", () => UnicodeEmoji.GetSequences((UnicodeEmojiSequenceProperty)6));
    }

    [Fact]
    public void GetSequences_AreDistinct()
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in Enum.GetValues<UnicodeEmojiSequenceProperty>())
        {
            foreach (var sequence in UnicodeEmoji.GetSequences(property))
            {
                Assert.NotEmpty(sequence);
                Assert.True(all.Add(sequence), sequence);
            }
        }
    }

    private static string ToString(ReadOnlySpan<int> codePoints)
    {
        var result = new StringBuilder();
        foreach (var codePoint in codePoints)
        {
            result.Append(char.ConvertFromUtf32(codePoint));
        }

        return result.ToString();
    }
}
