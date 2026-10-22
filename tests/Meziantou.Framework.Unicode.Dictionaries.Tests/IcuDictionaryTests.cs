namespace Meziantou.Framework.Tests;

public sealed class IcuDictionaryTests
{
    [Fact]
    public void ReadmeExample_FindBreaks()
    {
        #region IcuDictionaryBreakEngine_FindBreaks
        var text = "ภาษาไทยง่ายนิดเดียว"; // "Thai is very easy", written without spaces
        var engine = IcuDictionaryBreakEngine.Thai;

        Span<int> breaks = stackalloc int[text.Length];
        var count = engine.FindBreaks(text, breaks);

        // The words start at 4, 7, 11 and 14: ภาษา|ไทย|ง่าย|นิด|เดียว
        Assert.Equal([4, 7, 11, 14], breaks[..count].ToArray());
        #endregion
    }

    [Fact]
    public void ReadmeExample_Segment()
    {
        #region IcuDictionaryBreakEngine_Segment
        static List<string> SplitWords(string text)
        {
            var words = new List<string>();
            var breaks = new int[text.Length];
            var start = 0;
            while (start < text.Length)
            {
                // A run of the characters of an engine, or of the characters no engine handles
                var engine = IcuDictionaryBreakEngine.GetEngine(char.ConvertToUtf32(text, start));
                var end = start;
                while (end < text.Length && IcuDictionaryBreakEngine.GetEngine(char.ConvertToUtf32(text, end)) == engine)
                {
                    end += char.IsSurrogatePair(text, end) ? 2 : 1;
                }

                var wordStart = start;
                if (engine is not null)
                {
                    var count = engine.FindBreaks(text.AsSpan(start, end - start), breaks);
                    foreach (var position in breaks.AsSpan(0, count))
                    {
                        words.Add(text[wordStart..(start + position)]);
                        wordStart = start + position;
                    }
                }

                words.Add(text[wordStart..end]);
                start = end;
            }

            return words;
        }

        Assert.Equal(["今日", "は", "良い", "天気", "です", "ね"], SplitWords("今日は良い天気ですね"));
        Assert.Equal(["ພາສາ", "ລາວ", "ງ່າຍ"], SplitWords("ພາສາລາວງ່າຍ"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_WordDictionary()
    {
        #region IcuWordDictionary_Contains
        var thai = IcuWordDictionary.Get(IcuWordDictionaryKind.Thai);
        Assert.True(thai.Contains("ภาษา"));
        Assert.False(thai.Contains("ภาษาไทยง่าย"));

        // The words of the Chinese and Japanese list have a cost: the lower, the more frequent
        var chineseJapanese = IcuWordDictionary.Get(IcuWordDictionaryKind.ChineseJapanese);
        Assert.True(chineseJapanese.TryGetValue("天気", out var cost));
        Assert.True(cost > 0);
        #endregion
    }

    [Theory]
    [InlineData("東京都に住んでいます", new[] { "東京", "都", "に", "住", "んで", "い", "ます" })]
    [InlineData("ภาษาไทยง่ายนิดเดียว", new[] { "ภาษา", "ไทย", "ง่าย", "นิด", "เดียว" })]
    [InlineData("ພາສາລາວງ່າຍ", new[] { "ພາສາ", "ລາວ", "ງ່າຍ" })]
    [InlineData("ភាសាខ្មែរ", new[] { "ភាសាខ្មែរ" })]
    [InlineData("မြန်မာဘာသာ", new[] { "မြန်မာဘာသာ" })]
    public void FindBreaks_MatchesIcu(string text, string[] expected)
    {
        // The words of Intl.Segmenter in node 24 (ICU 78.2)
        var engine = IcuDictionaryBreakEngine.GetEngine(char.ConvertToUtf32(text, 0));
        Assert.NotNull(engine);

        var breaks = new int[text.Length];
        var count = engine.FindBreaks(text, breaks);
        var words = new List<string>();
        var start = 0;
        foreach (var position in breaks.AsSpan(0, count))
        {
            words.Add(text[start..position]);
            start = position;
        }

        words.Add(text[start..]);
        Assert.Equal(expected, words);
    }

    [Fact]
    public void FindBreaks_ShortText()
    {
        Span<int> breaks = stackalloc int[4];
        Assert.Equal(0, IcuDictionaryBreakEngine.Thai.FindBreaks("", breaks));
        Assert.Equal(0, IcuDictionaryBreakEngine.Thai.FindBreaks("ก", breaks));
        Assert.Equal(0, IcuDictionaryBreakEngine.ChineseJapanese.FindBreaks("", breaks));
        Assert.Equal(0, IcuDictionaryBreakEngine.ChineseJapanese.FindBreaks("日", breaks));
    }

    [Fact]
    public void FindBreaks_BufferTooSmall()
    {
        var text = "ภาษาไทยง่ายนิดเดียว";
        var breaks = new int[2];
        Assert.Throws<ArgumentException>(() => IcuDictionaryBreakEngine.Thai.FindBreaks(text, breaks));
    }

    [Fact]
    public void FindBreaks_ChineseJapanese_Normalization()
    {
        // Halfwidth katakana are normalized to NFKC before the words are looked up: the breaks are in the original text
        var text = "ﾃﾞｰﾀを東京に送る";
        var breaks = new int[text.Length];
        var count = IcuDictionaryBreakEngine.ChineseJapanese.FindBreaks(text, breaks);
        Assert.True(count > 0);
        Assert.Equal(4, breaks[0]);
        foreach (var position in breaks.AsSpan(0, count))
        {
            Assert.True(position > 0 && position < text.Length);
        }
    }

    [Fact]
    public void FindBreaks_ChineseJapanese_SupplementaryCharacters()
    {
        // U+20B9F (𠮟) is a supplementary Han character: the breaks are code unit indexes
        var text = "𠮟る日本語";
        var breaks = new int[text.Length];
        var count = IcuDictionaryBreakEngine.ChineseJapanese.FindBreaks(text, breaks);
        foreach (var position in breaks.AsSpan(0, count))
        {
            Assert.False(char.IsLowSurrogate(text[position]));
        }
    }

    [Fact]
    public void FindBreaks_UnpairedSurrogate()
    {
        var text = "日本" + (char)0xD800 + "語";
        var breaks = new int[text.Length];
        IcuDictionaryBreakEngine.ChineseJapanese.FindBreaks(text, breaks);
        IcuDictionaryBreakEngine.Thai.FindBreaks("ภาษา" + (char)0xDC00 + "ไทย", breaks);
    }

    [Theory]
    [InlineData('a', null)]
    [InlineData(' ', null)]
    [InlineData('ก', IcuWordDictionaryKind.Thai)]
    [InlineData('ກ', IcuWordDictionaryKind.Lao)]
    [InlineData('ក', IcuWordDictionaryKind.Khmer)]
    [InlineData('က', IcuWordDictionaryKind.Burmese)]
    [InlineData('東', IcuWordDictionaryKind.ChineseJapanese)]
    [InlineData('あ', IcuWordDictionaryKind.ChineseJapanese)]
    [InlineData('ア', IcuWordDictionaryKind.ChineseJapanese)]
    [InlineData('ー', IcuWordDictionaryKind.ChineseJapanese)]
    [InlineData('한', null)]
    [InlineData('๑', null)] // A Thai digit is not Complex_Context
    public void GetEngine(char value, IcuWordDictionaryKind? expected)
    {
        var engine = IcuDictionaryBreakEngine.GetEngine(value);
        Assert.Equal(expected, engine?.DictionaryKind);
        if (engine is not null)
        {
            Assert.True(engine.Handles(value));
        }
    }

    [Fact]
    public void GetEngine_InvalidCodePoint()
    {
        Assert.Null(IcuDictionaryBreakEngine.GetEngine(-1));
        Assert.Null(IcuDictionaryBreakEngine.GetEngine(0x110000));
        Assert.False(IcuDictionaryBreakEngine.Thai.Handles(-1));
        Assert.False(IcuDictionaryBreakEngine.ChineseJapanese.Handles(-1));
        Assert.False(IcuDictionaryBreakEngine.ChineseJapanese.Handles(0x110000));
    }

    [Fact]
    public void Engines_HandleDisjointCharacters()
    {
        IcuDictionaryBreakEngine[] engines = [IcuDictionaryBreakEngine.ChineseJapanese, IcuDictionaryBreakEngine.Thai, IcuDictionaryBreakEngine.Lao, IcuDictionaryBreakEngine.Khmer, IcuDictionaryBreakEngine.Burmese];
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            var engine = IcuDictionaryBreakEngine.GetEngine(codePoint);
            foreach (var candidate in engines)
            {
                Assert.Equal(candidate == engine, candidate.Handles(codePoint));
            }
        }
    }

    [Theory]
    [InlineData(IcuWordDictionaryKind.ChineseJapanese)]
    [InlineData(IcuWordDictionaryKind.Thai)]
    [InlineData(IcuWordDictionaryKind.Lao)]
    [InlineData(IcuWordDictionaryKind.Khmer)]
    [InlineData(IcuWordDictionaryKind.Burmese)]
    public void WordDictionary_Load(IcuWordDictionaryKind kind)
    {
        var dictionary = IcuWordDictionary.Get(kind);
        Assert.Same(dictionary, IcuWordDictionary.Get(kind));
        Assert.Equal(kind, dictionary.Kind);
        Assert.True(dictionary.WordCount > 10_000);

        var count = 0;
        string? previous = null;
        foreach (var word in dictionary.EnumerateWords())
        {
            Assert.True(previous is null || string.CompareOrdinal(previous, word) < 0);
            if (count % 97 == 0)
            {
                Assert.True(dictionary.Contains(word));
                Assert.False(dictionary.Contains(word + "a"));
            }

            previous = word;
            count++;
        }

        Assert.Equal(dictionary.WordCount, count);
    }

    [Fact]
    public void WordDictionary_Get_InvalidKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IcuWordDictionary.Get((IcuWordDictionaryKind)5));
    }

    [Fact]
    public void WordDictionary_Contains()
    {
        var thai = IcuWordDictionary.Get(IcuWordDictionaryKind.Thai);
        Assert.False(thai.Contains(""));
        Assert.False(thai.Contains("abc"));
        Assert.False(thai.Contains("ษษษ"));
        Assert.True(thai.TryGetValue("ภาษา", out var value));
        Assert.Equal(0, value);
        Assert.False(thai.Contains("ภาษ" + (char)0xD800));
    }

    [Fact]
    public void WordDictionary_IcuVersion()
    {
        Assert.Matches(@"^[0-9]+\.[0-9]+(\.[0-9]+)?$", IcuWordDictionary.IcuVersion);
    }

    [Fact]
    public void CreateLineBreakDictionary()
    {
        var dictionary = IcuWordDictionary.CreateLineBreakDictionary();
        var expected = 0;
        foreach (var kind in (IcuWordDictionaryKind[])[IcuWordDictionaryKind.Thai, IcuWordDictionaryKind.Lao, IcuWordDictionaryKind.Khmer, IcuWordDictionaryKind.Burmese])
        {
            expected += IcuWordDictionary.Get(kind).WordCount;
        }

        Assert.Equal(expected, dictionary.WordCount);
        Assert.True(dictionary.Contains("ภาษา"));
        Assert.True(dictionary.Contains("ພາສາ"));

        var text = "ภาษาไทยง่ายนิดเดียว";
        Span<int> breaks = stackalloc int[text.Length];
        var count = dictionary.FindBreaks(text, breaks);
        Assert.Equal([4, 7, 11, 14], breaks[..count].ToArray());
    }

    [Fact]
    public void WordDictionary_ThreadSafe()
    {
        var results = new IcuWordDictionary[8];
        Parallel.For(0, results.Length, i => results[i] = IcuWordDictionary.Get(IcuWordDictionaryKind.Lao));
        foreach (var result in results)
        {
            Assert.Same(results[0], result);
        }
    }
}
