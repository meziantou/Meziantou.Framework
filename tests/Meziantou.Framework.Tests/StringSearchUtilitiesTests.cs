namespace Meziantou.Framework.Tests;

public class StringSearchUtilitiesTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("aa", "", 2)]
    [InlineData("", "aa", 2)]
    [InlineData("test", "Test", 1)]
    [InlineData("chien", "chienne", 2)]
    [InlineData("Cat", "Cat", 0)]
    [InlineData("Car", "Kart", 2)]
    public void Levenshtein_Tests(string word1, string word2, int expected)
    {
        Assert.Equal(expected, StringSearchUtilities.Levenshtein(word1, word2));
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("a", "b", 1)]
    [InlineData("élan", "elan", 1)]
    [InlineData("😀abc", "😀axc", 1)]
    public void Levenshtein_SpanOverload_Tests(string word1, string word2, int expected)
    {
        Assert.Equal(expected, StringSearchUtilities.Levenshtein(word1.AsSpan(), word2.AsSpan()));
    }

    [Theory]
    [InlineData("", "", 0, 0)]
    [InlineData("kitten", "sitting", 2, 3)]
    [InlineData("kitten", "sitting", 3, 3)]
    [InlineData("abc", "abc", 0, 0)]
    [InlineData("abcdefgh", "abc", 2, 3)]
    public void Levenshtein_Bounded_Tests(string word1, string word2, int maxDistance, int expected)
    {
        Assert.Equal(expected, StringSearchUtilities.Levenshtein(word1.AsSpan(), word2.AsSpan(), maxDistance));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(101)]
    [InlineData(1000)]
    public void Levenshtein_Bounded_LargeMaxDistance(int maxDistance)
    {
        var word1 = new string('a', 100) + "x";
        var word2 = "y" + new string('b', 100);
        Assert.Equal(101, StringSearchUtilities.Levenshtein(word1, word2, maxDistance));
    }

    [Fact]
    public void Levenshtein_Bounded_LongWords()
    {
        var word1 = new string('a', 100) + "x";
        var word2 = "y" + new string('b', 100);
        Assert.Equal(100, StringSearchUtilities.Levenshtein(word1, word2, 99));
        Assert.Equal(101, StringSearchUtilities.Levenshtein(word1, word2, 100));
    }

    [Fact]
    public void LevenshteinBatch_Test()
    {
        var distances = StringSearchUtilities.LevenshteinBatch("kitten", ["sitting", "kitten", "mitten"]);
        Assert.Equal([3, 0, 1], distances);
    }

    [Fact]
    public void LevenshteinBatch_WithNullCandidate_Throws()
    {
        IReadOnlyList<string> candidates = ["a", null!];
        Assert.Throws<ArgumentException>(() => StringSearchUtilities.LevenshteinBatch("abc", candidates));
    }

    [Theory]
    [InlineData(0b101010u, 0b101010u, 0u)]
    [InlineData(0b010101u, 0b101010u, 6u)]
    [InlineData(0b1111u, 0b0u, 4u)]
    [InlineData(0b11111111u, 0b11110111u, 1u)]
    [InlineData(uint.MaxValue, 0u, 32u)]
    [InlineData(0x80000000u, 0u, 1u)]
    [InlineData(0x80000001u, 0x00000001u, 1u)]
    public void Hamming_Uint32Tests(uint word1, uint word2, uint expected)
    {
        Assert.Equal(expected, StringSearchUtilities.Hamming(word1, word2));
    }

    [Theory]
    [InlineData("ramer", "cases", 3)]
    public void Hamming_StringTests(string word1, string word2, int expected)
    {
        Assert.Equal(expected, StringSearchUtilities.Hamming(word1, word2));
    }

    [Fact]
    public void Hamming_Enumerable_SameLength()
    {
        Assert.Equal(1, StringSearchUtilities.Hamming(new[] { 1, 2, 3 }, new[] { 1, 2, 4 }));
        Assert.Equal(0, StringSearchUtilities.Hamming(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }));
        Assert.Equal(3, StringSearchUtilities.Hamming(new[] { 1, 2, 3 }, new[] { 4, 5, 6 }));
        Assert.Equal(1, StringSearchUtilities.Hamming(Enumerable.Range(0, 3), new[] { 0, 1, 3 }));
    }

    [Fact]
    public void Hamming_Enumerable_Empty()
    {
        Assert.Equal(0, StringSearchUtilities.Hamming(Array.Empty<int>(), Array.Empty<int>()));
    }

    [Fact]
    public void Hamming_Enumerable_DifferentLength()
    {
        Assert.Throws<ArgumentException>("word2", () => StringSearchUtilities.Hamming(new[] { 1, 2 }, new[] { 1, 2, 3 }));
        Assert.Throws<ArgumentException>("word2", () => StringSearchUtilities.Hamming(new[] { 1, 2, 3 }, new[] { 1, 2 }));
        Assert.Throws<ArgumentException>("word2", () => StringSearchUtilities.Hamming(Array.Empty<int>(), new[] { 1 }));
    }

    [Fact]
    public void Soundex_Test()
    {
        var soundex = StringSearchUtilities.Soundex("", new Dictionary<char, byte>());
        Assert.Equal("0000", soundex);
    }

    [Theory]
    [InlineData("R163", "Robert")]
    [InlineData("R163", "Rupert")]
    [InlineData("R150", "Rubin")]
    [InlineData("H400", "Hello")]
    [InlineData("E460", "Euler")]
    [InlineData("E460", "Ellery")]
    [InlineData("G200", "Gauss")]
    [InlineData("G200", "Ghosh")]
    [InlineData("H416", "Hilbert")]
    [InlineData("H416", "Heilbronn")]
    [InlineData("K530", "Knuth")]
    [InlineData("K530", "Kant")]
    [InlineData("L300", "Ladd")]
    [InlineData("L300", "Lloyd")]
    [InlineData("L222", "Lukasiewicz")]
    [InlineData("L222", "Lissajous")]
    [InlineData("L222", "Lishsajwjous")]
    [InlineData("A462", "Allricht")]
    [InlineData("E166", "Eberhard")]
    [InlineData("E521", "Engebrethson")]
    [InlineData("H512", "Heimbach")]
    [InlineData("H524", "Hanselmann")]
    [InlineData("H524", "Henzelmann")]
    [InlineData("H431", "Hildebrand")]
    [InlineData("K152", "Kavanagh")]
    [InlineData("L530", " Lind, Van")]
    [InlineData("L222", "Lukaschowsky")]
    [InlineData("M235", "McDonnell")]
    [InlineData("M200", "McGee")]
    [InlineData("O165", "O'Brien")]
    [InlineData("O155", "Opnian")]
    [InlineData("O155", "Oppenheimer")]
    [InlineData("S460", "Swhgler")]
    [InlineData("R355", "Riedemanas")]
    [InlineData("Z300", "Zita")]
    [InlineData("Z325", "Zitzmeinn")]
    public void SoundexEnglish_Test(string expected, string value)
    {
        Assert.Equal(expected, StringSearchUtilities.SoundexEnglish(value));
    }

    [Theory]
    [InlineData("    ", "")]
    [InlineData("MRTN", "MARTIN")]
    [InlineData("BRNR", "BERNARD")]
    [InlineData("FR  ", "FAURE")]
    [InlineData("PRZ ", "PEREZ")]
    [InlineData("GR  ", "GROS")]
    [InlineData("CHP ", "CHAPUIS ")]
    [InlineData("BYR ", "BOYER")]
    [InlineData("KTR ", "GAUTHIER")]
    [InlineData("RY  ", "REY")]
    [InlineData("BRTL", "BARTHELEMY")]
    [InlineData("ANR ", "HENRY")]
    [InlineData("MLN ", "MOULIN")]
    [InlineData("RS  ", "ROUSSEAU")]
    [InlineData("RS  ", "YROUSSYEAU")]
    [InlineData("FLP ", "PHILIPPE")]
    [InlineData("SMT ", "SCHMITT")]
    [InlineData("NG  ", "KNIGHT")]
    [InlineData("FR  ", "PFEIFFER")]
    [InlineData("AZ  ", "ASAS")]
    public void Soundex2_Test(string expected, string value)
    {
        Assert.Equal(expected, StringSearchUtilities.Soundex2(value));
    }

    [Theory]
    [InlineData("T830", "test")]
    [InlineData("H560", "Henry")]
    [InlineData("R000", "ray")]
    [InlineData("R000", "rey")]
    [InlineData("R000", "REY")]
    [InlineData("R000", "RAY")]
    public void SoundexFrench_Test(string expected, string value)
    {
        Assert.Equal(expected, StringSearchUtilities.SoundexFrench(value));
    }

    [Theory]
    [InlineData("TSTN", "testing")]
    [InlineData("0", "The")]
    [InlineData("KK", "quick")]
    [InlineData("BRN", "brown")]
    [InlineData("FKS", "fox")]
    [InlineData("JMPT", "jumped")]
    [InlineData("OFR", "over")]
    [InlineData("LS", "lazy")]
    [InlineData("TKS", "dogs")]
    [InlineData("TMP", "dump")]
    [InlineData("XFKS", "cheveux")]
    [InlineData("SKFK", "scheveux")]
    [InlineData("SFKS", "sciveux")]
    [InlineData("SKKF", "sccuveux")]
    [InlineData("KHLN", "khalens")]
    [InlineData("SFKS", "scyveux")]
    [InlineData("BSN", "buisson")]
    [InlineData("BB", "bebe")]
    [InlineData("LXS", "lacias")]
    [InlineData("TJKS", "dijkstra")]
    [InlineData("TJKS", "djikstra")]
    [InlineData("JKST", "dgikstra")]
    [InlineData("JKST", "dgekstra")]
    [InlineData("TKKS", "dgakstra")]
    [InlineData("KST", "ghost")]
    [InlineData("0R", "through")]
    [InlineData("NM", "gnome")]
    [InlineData("KSK", "chzkou")]
    [InlineData("BLKK", "blaggyguest")]
    [InlineData("AXXX", "atiatiotch")]
    [InlineData("JKSN", "Jackson")]
    [InlineData("TKNS", "Dickens")]
    [InlineData("KK", "kck")]
    public void Metaphone_Test(string expected, string value)
    {
        Assert.Equal(expected, StringSearchUtilities.Metaphone(value));
    }

    [Theory]
    [InlineData("Case", "case")]
    [InlineData("CASE", "Case")]
    [InlineData("caSe", "cAsE")]
    [InlineData("cookie", "quick")]
    [InlineData("T", "T")]
    [InlineData("Lorenza", "Lawrence")]
    [InlineData("Aero", "Eure")]
    [MemberData(nameof(MetaphoneList))]
    public void Metaphone_Test2(string value, string otherValue)
    {
        var v1 = StringSearchUtilities.Metaphone(value);
        var v2 = StringSearchUtilities.Metaphone(otherValue);
        Assert.Equal(v1, v2);
    }

    public static IEnumerable<object[]> MetaphoneList()
    {
        foreach (var item in new[] { "Wade", "Wait", "Waite", "Wat", "Whit", "Wiatt", "Wit", "Wittie", "Witty", "Wood", "Woodie", "Woody" })
            yield return new object[] { "White", item };

        foreach (var item in new[] { "Ailbert", "Alberik", "Albert", "Alberto", "Albrecht" })
            yield return new object[] { "Albert", item };

        foreach (var item in new[] {
                "Cahra",
                "Cara",
                "Carey",
                "Cari",
                "Caria",
                "Carie",
                "Caro",
                "Carree",
                "Carri",
                "Carrie",
                "Carry",
                "Cary",
                "Cora",
                "Corey",
                "Cori",
                "Corie",
                "Correy",
                "Corri",
                "Corrie",
                "Corry",
                "Cory",
                "Gray",
                "Kara",
                "Kare",
                "Karee",
                "Kari",
                "Karia",
                "Karie",
                "Karrah",
                "Karrie",
                "Karry",
                "Kary",
                "Keri",
                "Kerri",
                "Kerrie",
                "Kerry",
                "Kira",
                "Kiri",
                "Kora",
                "Kore",
                "Kori",
                "Korie",
                "Korrie",
                "Korry", })
        {
            yield return new object[] { "Gary", item };
        }

        foreach (var item in new[] {
                 "Gena",
                 "Gene",
                 "Genia",
                 "Genna",
                 "Genni",
                 "Gennie",
                 "Genny",
                 "Giana",
                 "Gianna",
                 "Gina",
                 "Ginni",
                 "Ginnie",
                 "Ginny",
                 "Jaine",
                 "Jan",
                 "Jana",
                 "Jane",
                 "Janey",
                 "Jania",
                 "Janie",
                 "Janna",
                 "Jany",
                 "Jayne",
                 "Jean",
                 "Jeana",
                 "Jeane",
                 "Jeanie",
                 "Jeanna",
                 "Jeanne",
                 "Jeannie",
                 "Jen",
                 "Jena",
                 "Jeni",
                 "Jenn",
                 "Jenna",
                 "Jennee",
                 "Jenni",
                 "Jennie",
                 "Jenny",
                 "Jinny",
                 "Jo Ann",
                 "Jo-Ann",
                 "Jo-Anne",
                 "Joan",
                 "Joana",
                 "Joane",
                 "Joanie",
                 "Joann",
                 "Joanna",
                 "Joanne",
                 "Joeann",
                 "Johna",
                 "Johnna",
                 "Joni",
                 "Jonie",
                 "Juana",
                 "June",
                 "Junia",
                 "Junie", })
        {
            yield return new object[] { "John", item };
        }

        foreach (var item in new[] {
                "Mair",
                "Maire",
                "Mara",
                "Mareah",
                "Mari",
                "Maria",
                "Marie",
                "Mary",
                "Maura",
                "Maure",
                "Meara",
                "Merrie",
                "Merry",
                "Mira",
                "Moira",
                "Mora",
                "Moria",
                "Moyra",
                "Muire",
                "Myra",
                "Myrah", })
        {
            yield return new object[] { "Mary", item };
        }

        foreach (var item in new[] { "Pearcy", "Perris", "Piercy", "Pierz", "Pryse" })
            yield return new object[] { "Paris", item };

        foreach (var item in new[] { "Peadar", "Peder", "Pedro", "Peter", "Petr", "Peyter", "Pieter", "Pietro", "Piotr" })
            yield return new object[] { "Peter", item };

        foreach (var item in new[] { "Ray", "Rey", "Roi", "Roy", "Ruy" })
            yield return new object[] { "Ray", item };

        foreach (var item in new[] { "Siusan", "Sosanna", "Susan", "Susana", "Susann", "Susanna", "Susannah", "Susanne", "Suzann", "Suzanna", "Suzanne", "Zuzana" })
            yield return new object[] { "Susan", item };

        foreach (var item in new[] { "Rota", "Rudd", "Ryde" })
            yield return new object[] { "Wright", item };

        foreach (var item in new[] { "Celene", "Celina", "Celine", "Selena", "Selene", "Selina", "Seline", "Suellen", "Xylina" })
            yield return new object[] { "Xalan", item };
    }

    [Fact]
    public void Documentation_Levenshtein()
    {
        #region StringSearchUtilities_Levenshtein
        // The number of insertions, deletions and substitutions that turn one word into the other
        Assert.Equal(3, StringSearchUtilities.Levenshtein("kitten", "sitting"));

        // With a maximum distance, the computation stops early and returns maxDistance + 1
        Assert.Equal(2, StringSearchUtilities.Levenshtein("kitten", "sitting", maxDistance: 1));

        // Compare one word with many candidates
        var commands = new[] { "build", "test", "publish" };
        Assert.Equal([1, 4, 5], StringSearchUtilities.LevenshteinBatch("buid", commands));
        #endregion
    }

    [Fact]
    public void Documentation_Hamming()
    {
        #region StringSearchUtilities_Hamming
        // The number of positions at which two strings of the same length differ
        Assert.Equal(3, StringSearchUtilities.Hamming("karolin", "kathrin"));

        // The number of different bits of two integers
        Assert.Equal(2u, StringSearchUtilities.Hamming(0b1010u, 0b0110u));
        #endregion
    }

    [Fact]
    public void Documentation_Soundex()
    {
        #region StringSearchUtilities_Soundex
        // Names that sound alike get the same code
        Assert.Equal("R163", StringSearchUtilities.SoundexEnglish("Robert"));
        Assert.Equal("R163", StringSearchUtilities.SoundexEnglish("Rupert"));
        Assert.Equal(StringSearchUtilities.Metaphone("Smith"), StringSearchUtilities.Metaphone("Smyth"));
        #endregion
    }
}
