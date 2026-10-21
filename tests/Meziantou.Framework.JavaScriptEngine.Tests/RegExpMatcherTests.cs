namespace Meziantou.Framework.JavaScriptEngine.Tests;

public sealed class RegExpMatcherTests
{
    private static RegExpProgram Compile(string pattern, string flags)
    {
        Assert.True(RegExpFlagsExtensions.TryParse(flags, out var parsedFlags), "flags: " + flags);
        var program = RegExpProgram.Compile(pattern, parsedFlags, out var error);
        if (program is null)
        {
            Assert.Fail(error);
        }

        return program;
    }

    private static string?[]? Exec(string pattern, string flags, string input, int lastIndex = 0)
    {
        var program = Compile(pattern, flags);
        var captures = program.Exec(input, lastIndex, flags.Contains('y', StringComparison.Ordinal), engine: null);
        if (captures is null)
            return null;

        var result = new string?[captures.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            var start = captures[i * 2];
            var end = captures[(i * 2) + 1];
            result[i] = start < 0 || end < 0 ? null : input[start..end];
        }

        return result;
    }

    private static int Index(string pattern, string flags, string input, int lastIndex = 0)
    {
        var captures = Compile(pattern, flags).Exec(input, lastIndex, flags.Contains('y', StringComparison.Ordinal), engine: null);
        return captures is null ? -1 : captures[0];
    }

    private static bool Test(string pattern, string flags, string input) => Exec(pattern, flags, input) is not null;

    [Theory]
    [InlineData("a|ab", "", "abc", new[] { "a" })]
    [InlineData("((a)|(ab))((c)|(bc))", "", "abc", new[] { "abc", "a", "a", null, "bc", null, "bc" })]
    [InlineData("a[a-z]{2,4}", "", "abcdefghi", new[] { "abcde" })]
    [InlineData("a[a-z]{2,4}?", "", "abcdefghi", new[] { "abc" })]
    [InlineData("(aa|aabaac|ba|b|c)*", "", "aabaac", new[] { "aaba", "ba" })]
    [InlineData("^(a+)\\1*,\\1+$", "", "aaaaaaaaaa,aaaaaaaaaaaaaaa", new[] { "aaaaaaaaaa,aaaaaaaaaaaaaaa", "aaaaa" })]
    [InlineData("(z)((a+)?(b+)?(c))*", "", "zaacbbbcac", new[] { "zaacbbbcac", "z", "ac", "a", null, "c" })]
    [InlineData("(a*)*", "", "b", new[] { "", null })]
    [InlineData("(a*)+", "", "b", new[] { "", "" })]
    [InlineData("(a*)b\\1+", "", "baaaac", new[] { "b", "" })]
    [InlineData("(?=(a+))", "", "baaabac", new[] { "", "aaa" })]
    [InlineData("(?=(a+))a*b\\1", "", "baaabac", new[] { "aba", "a" })]
    [InlineData("(.*?)a(?!(a+)b\\2c)\\2(.*)", "", "baaabaac", new[] { "baaabaac", "ba", null, "abaac" })]
    [InlineData("(a)|b", "", "b", new[] { "b", null })]
    [InlineData("(?:(a)|b)+", "", "ab", new[] { "ab", null })]
    [InlineData("(?:(a)|b)*?c", "", "abc", new[] { "abc", null })]
    [InlineData("(a)?\\1b", "", "b", new[] { "b", null })]
    [InlineData("\\1(a)", "", "aa", new[] { "a", "a" })]
    [InlineData("(a\\1)", "", "aa", new[] { "a", "a" })]
    [InlineData("(?:a?)*?b", "", "aab", new[] { "aab" })]
    [InlineData("(?:x|())*y", "", "xxy", new[] { "xxy", null })]
    [InlineData("(a|ab)(c|bcd)(d*)", "", "abcd", new[] { "abcd", "a", "bcd", "" })]
    [InlineData("x*", "", "", new[] { "" })]
    [InlineData("[^]", "", "\n", new[] { "\n" })]
    [InlineData("a{0}b", "", "ab", new[] { "b" })]
    [InlineData("(a){0}b", "", "ab", new[] { "b", null })]
    [InlineData("(?:a{2})*b", "", "aaab", new[] { "aab" })]
    [InlineData("(a{2,3})+?b", "", "aaaaab", new[] { "aaaaab", "aa" })]
    [InlineData("(?:(?=(abc)))a", "", "abc", new[] { "a", "abc" })]
    [InlineData("(?:(?=(abc)))?a", "", "abc", new[] { "a", null })]
    [InlineData("(?:(?=(abc))){1,1}a", "", "abc", new[] { "a", "abc" })]
    [InlineData("(?:(?=(abc))){0,1}a", "", "abc", new[] { "a", null })]
    [InlineData("(a?b??)*", "", "ab", new[] { "ab", "b" })]
    [InlineData("(?:(?:(?<x>a)|(?<x>b)|c)\\k<x>){2}", "", "aac", new[] { "aac", null, null })]
    [InlineData("(?:(?<x>a)|(?<y>a)(?<x>b))(?:(?<z>c)|(?<z>d))", "", "abc", new[] { "abc", null, "a", "b", "c", null })]
    public void Exec_SpecificationExamples(string pattern, string flags, string input, string?[] expected)
    {
        Assert.Equal(expected, Exec(pattern, flags, input));
    }

    [Theory]
    [InlineData("(?<=\\$)\\d+(\\.\\d*)?", "", "$10.53", new[] { "10.53", ".53" })]
    [InlineData("(?<!\\$)\\d+(?:\\.\\d*)", "", "$10.53 \u20AC9.25", new[] { "0.53" })]
    [InlineData("(?<=(\\d+)(\\d+))$", "", "1053", new[] { "", "1", "053" })]
    [InlineData("(?<=(\\d+?)(\\d+?))$", "", "1053", new[] { "", "5", "3" })]
    [InlineData("(?<=\\1(a))b", "", "aab", new[] { "b", "a" })]
    [InlineData("(?<=(a)\\1)b", "", "aab", new[] { "b", "a" })]
    [InlineData("(?<=([ab])+)c", "", "abc", new[] { "c", "a" })]
    [InlineData("(?<!abc)\\w\\w\\w", "", "abcdef", new[] { "abc" })]
    [InlineData("(?<=^abc)def", "", "abcdef", new[] { "def" })]
    [InlineData("(?<=a(?=b))b", "", "ab", new[] { "b" })]
    [InlineData("(?<=\\u{1F600})x", "u", "\uD83D\uDE00x", new[] { "x" })]
    [InlineData("(?<=.)x", "u", "\uD83D\uDE00x", new[] { "x" })]
    [InlineData("(?<=^.)x", "u", "\uD83D\uDE00x", new[] { "x" })]
    [InlineData("(?<=^.*)x", "u", "\uD83D\uDE00\uD83D\uDE00x", new[] { "x" })]
    [InlineData("(?<=(?<a>\\w){3})f", "", "abcdef", new[] { "f", "c" })]
    public void Exec_Lookbehind(string pattern, string flags, string input, string?[] expected)
    {
        Assert.Equal(expected, Exec(pattern, flags, input));
    }

    [Theory]
    [InlineData("(?<!(^|[ab]))\\w{2}", "", "abcdefg", new[] { "de", null })]
    [InlineData("(?<!(a))b", "", "ab cb", new[] { "b", null })]
    [InlineData("(?=(a))a|b", "", "b", new[] { "b", null })]
    [InlineData("(?!(a))b\\1", "", "b", new[] { "b", null })]
    public void Exec_NegativeAssertionsUndoCaptures(string pattern, string flags, string input, string?[] expected)
    {
        Assert.Equal(expected, Exec(pattern, flags, input));
    }

    [Theory]
    [InlineData("[a-z]", "i", "K", true)]
    [InlineData("[a-z]", "i", "\u212A", false)] // the Kelvin sign is not matched without the "u" flag
    [InlineData("[a-z]", "iu", "\u212A", true)]
    [InlineData("[a-z]", "iv", "\u212A", true)]
    [InlineData("\u212A", "i", "k", false)]
    [InlineData("\u212A", "iu", "k", true)]
    [InlineData("\u212A", "iu", "K", true)]
    [InlineData("\u017F", "i", "s", false)]
    [InlineData("\u017F", "iu", "S", true)]
    [InlineData("\u00DF", "i", "SS", false)]
    [InlineData("\u00DF", "iu", "\u1E9E", true)]
    [InlineData("\u00DF", "i", "\u1E9E", false)]
    [InlineData("[\u03C9]", "iu", "\u2126", true)]
    [InlineData("[\u03C9]", "i", "\u2126", false)]
    [InlineData("\u03C3", "i", "\u03C2", true)]
    [InlineData("[^a]", "i", "A", false)]
    [InlineData("[^a]", "iu", "A", false)]
    [InlineData("[^a]", "iv", "A", false)]
    [InlineData("\\w", "iu", "\u017F", true)]
    [InlineData("\\w", "i", "\u017F", false)]
    [InlineData("\\W", "iu", "\u017F", false)]
    [InlineData("\\W", "iu", "S", false)]
    [InlineData("[\\W]", "iv", "\u017F", false)]
    [InlineData("[^\\W]", "iu", "\u017F", true)]
    [InlineData("\\bx", "iu", "\u017Fx", false)]
    [InlineData("\\bx", "i", "\u017Fx", true)]
    [InlineData("\\u{10400}", "iu", "\uD801\uDC28", true)]
    [InlineData("[\\u{10400}-\\u{10410}]", "iu", "\uD801\uDC28", true)]
    [InlineData("(a)\\1", "i", "aA", true)]
    [InlineData("(\\u{10400})\\1", "iu", "\uD801\uDC00\uD801\uDC28", true)]
    [InlineData("\\p{Lu}", "iu", "a", true)]
    [InlineData("\\P{Lu}", "iu", "A", true)]
    [InlineData("\\p{Lowercase}", "iu", "A", true)]
    [InlineData("[\\p{Lu}--[A-Z]]", "iv", "a", true)]
    [InlineData("[\\p{Lowercase}--[a-z]]", "iv", "A", false)]
    [InlineData("i", "i", "\u0130", false)]
    [InlineData("\u0131", "i", "I", false)]
    [InlineData("\u01C5", "i", "\u01C4", true)]
    [InlineData("\u01C5", "i", "\u01C6", true)]
    [InlineData("\u0390", "iu", "\u1FD3", true)]
    [InlineData("\u0390", "i", "\u1FD3", false)]
    public void Test_IgnoreCase(string pattern, string flags, string input, bool expected)
    {
        Assert.Equal(expected, Test(pattern, flags, input));
    }

    [Theory]
    [InlineData("(?i:a)b", "", "Ab", true)]
    [InlineData("(?i:a)b", "", "AB", false)]
    [InlineData("(?-i:a)b", "i", "aB", true)]
    [InlineData("(?-i:a)b", "i", "AB", false)]
    [InlineData("(?m:^b)", "", "a\nb", true)]
    [InlineData("^b", "", "a\nb", false)]
    [InlineData("(?s:.)", "", "\n", true)]
    [InlineData("(?-s:.)", "s", "\n", false)]
    [InlineData("(?i:\\bk)", "u", "\u017Fk", false)]
    [InlineData("(?i:\\Bk)", "u", "\u017Fk", true)]
    [InlineData("(?i:(a))\\1", "", "aA", false)]
    [InlineData("(a)(?i:\\1)", "", "aA", true)]
    public void Test_Modifiers(string pattern, string flags, string input, bool expected)
    {
        Assert.Equal(expected, Test(pattern, flags, input));
    }

    [Theory]
    [InlineData("^.$", "u", "\uD83D\uDE00", true)]
    [InlineData("^.$", "", "\uD83D\uDE00", false)]
    [InlineData("^..$", "", "\uD83D\uDE00", true)]
    [InlineData("\\uD83D", "u", "\uD83D\uDE00", false)]
    [InlineData("\\uD83D", "", "\uD83D\uDE00", true)]
    [InlineData("\\udf06", "u", "\uD834\uDF06", false)]
    [InlineData("^[\\uD83D\\uDE00]$", "u", "\uD83D\uDE00", true)]
    [InlineData("^[\\uD83D\\uDE00]$", "", "\uD83D\uDE00", false)]
    [InlineData("^[\\uD83D\\uDE00]{2}$", "", "\uD83D\uDE00", true)]
    [InlineData("^\\S$", "u", "\uD83D\uDE00", true)]
    [InlineData("^[^x]$", "u", "\uD83D\uDE00", true)]
    [InlineData("^\\W$", "u", "\uD83D\uDE00", true)]
    [InlineData("^\\uD83D\\uDE00$", "u", "\uD83D\uDE00", true)]
    [InlineData("^[\\u{1F600}-\\u{1F64F}]+$", "u", "\uD83D\uDE00\uD83D\uDE4F", true)]
    [InlineData("^\\p{Emoji_Presentation}$", "u", "\uD83D\uDE00", true)]
    [InlineData("^.+?$", "u", "\uD83D\uDE00\uD83D\uDE00", true)]
    [InlineData("^(.)\\1$", "u", "\uD83D\uDE00\uD83D\uDE00", true)]
    [InlineData("\\uDE00", "u", "\uD83D\uDE00", false)]
    [InlineData("(\\uD83D)\\uDE00", "", "\uD83D\uDE00", true)]
    public void Test_Unicode(string pattern, string flags, string input, bool expected)
    {
        Assert.Equal(expected, Test(pattern, flags, input));
    }

    [Fact]
    public void Test_LoneSurrogates()
    {
        // Not in InlineData: the attribute data is encoded as UTF-8, which does not keep a lone surrogate
        Assert.True(Test("\\uD83D", "u", "\uD83Dx"));
        Assert.True(Test("\\uDE00", "u", "x\uDE00"));
        Assert.True(Test("^.$", "u", "\uDE00"));
        Assert.True(Test("^[^x]$", "u", "\uD83D"));
        Assert.Equal(["\uD83D", "\uD83D"], Exec("(.)", "u", "\uD83D\uD83D"));
        Assert.Equal(-1, Index("\\uD83D", "u", "\uD83D\uD83D\uDE00", 1));
        Assert.Equal(-1, Index("\\uD83D$", "u", "\uD83D\uD83D\uDE00"));
    }

    [Fact]
    public void Exec_UnicodeBackreferenceDoesNotSplitAPair()
    {
        // \1 is a lone high surrogate: it must not match the first half of the pair
        Assert.Null(Exec("(\\uD83D)x?\\1", "u", "\uD83D\uD83D\uDE00"));
        Assert.Equal(["\uD83D\uD83D", "\uD83D"], Exec("(\\uD83D)x?\\1", "u", "\uD83D\uD83Dx"));
    }

    [Fact]
    public void Exec_UnicodeLastIndexInsideAPair()
    {
        // The character of element lastIndex is the pair: the match starts with it
        var captures = Compile(".", "gu").Exec("\uD834\uDF06", 1, sticky: false, engine: null);
        Assert.NotNull(captures);
        Assert.Equal(0, captures[0]);
        Assert.Equal(2, captures[1]);
        Assert.Null(Compile("\\udf06", "u").Exec("\uD834\uDF06", 1, sticky: false, engine: null));
    }

    [Theory]
    [InlineData("[\\p{RGI_Emoji_Flag_Sequence}]", "v", "\uD83C\uDDEB\uD83C\uDDF7", "\uD83C\uDDEB\uD83C\uDDF7")]
    [InlineData("\\p{RGI_Emoji}", "v", "\uD83D\uDC68\u200D\u2764\uFE0F\u200D\uD83D\uDC68", "\uD83D\uDC68\u200D\u2764\uFE0F\u200D\uD83D\uDC68")]
    [InlineData("\\p{Emoji_Keycap_Sequence}", "v", "#\uFE0F\u20E3", "#\uFE0F\u20E3")]
    [InlineData("\\p{Basic_Emoji}", "v", "\u231A", "\u231A")]
    [InlineData("[\\q{abc|ab}]", "v", "abc", "abc")]
    [InlineData("[\\q{ab|abc}]", "v", "abc", "abc")]
    [InlineData("[\\q{}a]", "v", "b", "")]
    [InlineData("[\\q{a\\|b}]", "v", "a|b", "a|b")]
    [InlineData("[\\q{\\u{1F600}x}]", "v", "\uD83D\uDE00x", "\uD83D\uDE00x")]
    [InlineData("[[a-z]--[aeiou]]+", "v", "abcde", "bcd")]
    [InlineData("[\\w&&\\d]+", "v", "ab12", "12")]
    [InlineData("[^\\d]+", "v", "12ab", "ab")]
    [InlineData("[[^\\d]&&[a-c]]+", "v", "12abcd", "abc")]
    [InlineData("[\\q{ABC}]", "iv", "abc", "abc")]
    [InlineData("[\\q{ABC}--\\q{abc}]", "iv", "abc", null)]
    [InlineData("[\\p{ASCII}--\\p{Lowercase}]+", "v", "ABc", "AB")]
    [InlineData("[[a-c]&&b]", "v", "abc", "b")]
    [InlineData("[\\q{abc|a}--\\q{abc}]", "v", "abc", "a")]
    public void Exec_UnicodeSets(string pattern, string flags, string input, string? expected)
    {
        Assert.Equal(expected, Exec(pattern, flags, input)?[0]);
    }

    [Fact]
    public void Exec_NamedGroups()
    {
        var program = Compile("(?<year>\\d{4})-(?<month>\\d{2})", "");
        Assert.True(program.HasNamedGroups);
        Assert.Equal([null, "year", "month"], program.GroupNames);
        Assert.Equal(["2020-12", "2020", "12"], Exec("(?<year>\\d{4})-(?<month>\\d{2})", "", "x2020-12"));
        Assert.Equal(["aa", "a"], Exec("(?<a>.)\\k<a>", "", "baa"));
        Assert.Equal(["k<a>"], Exec("\\k<a>", "", "k<a>"));
    }

    [Fact]
    public void Exec_DuplicateNamedGroups()
    {
        Assert.Equal(["y", null, "y"], Exec("(?<a>x)|(?<a>y)", "", "y"));
        Assert.Equal(["yy", null, "y"], Exec("(?:(?<a>x)|(?<a>y))\\k<a>", "", "yy"));
        Assert.Equal(["xx", "x", null], Exec("(?:(?<a>x)|(?<a>y))\\k<a>", "", "xx"));
        Assert.Null(Exec("(?:(?<a>x)|(?<a>y))\\k<a>", "", "xy"));
        Assert.Equal(["b", null, null, "b"], Exec("(?:(?:(?<a>x)|(?<a>y))\\k<a>)?b(?<=(?<b>b))", "", "bab"));
    }

    [Theory]
    [InlineData("a", "y", "ba", 0, -1)]
    [InlineData("a", "y", "ba", 1, 1)]
    [InlineData("a", "g", "ba", 0, 1)]
    [InlineData("a", "g", "ba", 2, -1)]
    [InlineData("^a", "gy", "aa", 1, -1)]
    [InlineData("^a", "gmy", "a\na", 2, 2)]
    [InlineData("", "g", "ab", 2, 2)]
    [InlineData("b", "", "abcb", 0, 1)]
    [InlineData("b|c", "", "abcb", 2, 2)]
    [InlineData("[bc]", "", "abcb", 3, 3)]
    [InlineData("\\u{1F600}", "u", "x\uD83D\uDE00", 0, 1)]
    public void Exec_LastIndexAndSticky(string pattern, string flags, string input, int lastIndex, int expectedIndex)
    {
        Assert.Equal(expectedIndex, Index(pattern, flags, input, lastIndex));
    }

    [Theory]
    [InlineData("^b", "m", "a\nb", 2)]
    [InlineData("a$", "m", "a\nb", 0)]
    [InlineData("a$", "", "a\nb", -1)]
    [InlineData("^b", "m", "a\u2028b", 2)]
    [InlineData("^b", "m", "a\u2029b", 2)]
    [InlineData("^b", "m", "a\rb", 2)]
    [InlineData("^b", "m", "a\u0085b", -1)]
    [InlineData(".", "", "\n\r\u2028\u2029a", 4)]
    [InlineData(".", "s", "\n", 0)]
    [InlineData("\\bb", "", "a b", 2)]
    [InlineData("\\Bb", "", "a bab", 4)]
    [InlineData("\\s", "", "a\uFEFFb", 1)]
    [InlineData("\\s", "", "a\u0085b", -1)]
    [InlineData("\\s", "", "a\u3000b", 1)]
    [InlineData("\\S", "", "\u00A0a", 1)]
    public void Exec_Assertions(string pattern, string flags, string input, int expectedIndex)
    {
        Assert.Equal(expectedIndex, Index(pattern, flags, input));
    }

    [Theory]
    [InlineData("\\8", "", "8", new[] { "8" })]
    [InlineData("\\9", "", "9", new[] { "9" })]
    [InlineData("[\\8]", "", "8", new[] { "8" })]
    [InlineData("\\10", "", "\b", new[] { "\b" })]
    [InlineData("\\377", "", "\u00FF", new[] { "\u00FF" })]
    [InlineData("\\400", "", " 0", new[] { " 0" })]
    [InlineData("\\0", "", "\0", new[] { "\0" })]
    [InlineData("[\\1]", "", "\u0001", new[] { "\u0001" })]
    [InlineData("\\c", "", "\\c", new[] { "\\c" })]
    [InlineData("\\c1", "", "\\c1", new[] { "\\c1" })]
    [InlineData("[\\c]+", "", "c\\", new[] { "c\\" })]
    [InlineData("[\\c1]", "", "\u0011", new[] { "\u0011" })]
    [InlineData("[\\c_]", "", "\u001F", new[] { "\u001F" })]
    [InlineData("\\cJ", "", "\n", new[] { "\n" })]
    [InlineData("\\cj", "", "\n", new[] { "\n" })]
    [InlineData("a{", "", "a{", new[] { "a{" })]
    [InlineData("a{1", "", "a{1", new[] { "a{1" })]
    [InlineData("{}", "", "{}", new[] { "{}" })]
    [InlineData("]", "", "]", new[] { "]" })]
    [InlineData("x{1,2}{", "", "xx{", new[] { "xx{" })]
    [InlineData("\\a", "", "a", new[] { "a" })]
    [InlineData("\\x4", "", "x4", new[] { "x4" })]
    [InlineData("\\u12", "", "u12", new[] { "u12" })]
    [InlineData("[\\d-a]+", "", "1-a", new[] { "1-a" })]
    [InlineData("[a-\\d]+", "", "1-a", new[] { "1-a" })]
    [InlineData("(?=a)*a", "", "a", new[] { "a" })]
    [InlineData("(?!a)+b", "", "b", new[] { "b" })]
    [InlineData("\\k", "", "k", new[] { "k" })]
    [InlineData("\\2(a)", "", "\u0002a", new[] { "\u0002a", "a" })]
    public void Exec_AnnexB(string pattern, string flags, string input, string?[] expected)
    {
        Assert.Equal(expected, Exec(pattern, flags, input));
    }

    [Theory]
    [InlineData("(", "")]
    [InlineData("a**", "")]
    [InlineData("[z-a]", "")]
    [InlineData("a{2,1}", "")]
    [InlineData("\\p{Foo}", "u")]
    [InlineData("\\p{WSpace}", "u")]
    [InlineData("\\p{Script=Foo}", "u")]
    [InlineData("\\p{RGI_Emoji}", "u")]
    [InlineData("[^\\p{RGI_Emoji}]", "v")]
    [InlineData("\\P{RGI_Emoji}", "v")]
    [InlineData("(?<a>x)(?<a>y)", "")]
    [InlineData("\\u{110000}", "u")]
    [InlineData("\\1", "u")]
    [InlineData("\\k<a>", "u")]
    [InlineData("(?<=a)+", "")]
    [InlineData("(?=a)+", "u")]
    [InlineData("[a&&&b]", "v")]
    [InlineData("[a-z&&b]", "v")]
    [InlineData("(?ii:a)", "")]
    [InlineData("(?i-i:a)", "")]
    [InlineData("a{", "u")]
    [InlineData("]", "u")]
    [InlineData("\\c", "u")]
    [InlineData("[(]", "v")]
    [InlineData("[a-c&&b]", "v")]
    [InlineData("[\\q{abc}--\\q{abc}a]", "v")]
    public void Compile_SyntaxErrors(string pattern, string flags)
    {
        Assert.True(RegExpFlagsExtensions.TryParse(flags, out var parsedFlags));
        Assert.Null(RegExpProgram.Compile(pattern, parsedFlags, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("dgimsuy")]
    [InlineData("v")]
    [InlineData("")]
    public void Flags_Valid(string flags) => Assert.True(RegExpFlagsExtensions.TryParse(flags, out _));

    [Theory]
    [InlineData("uv")]
    [InlineData("gg")]
    [InlineData("x")]
    [InlineData("G")]
    public void Flags_Invalid(string flags) => Assert.False(RegExpFlagsExtensions.TryParse(flags, out _));

    [Theory]
    [InlineData("\\p{Script=Greek}", "u", "\u03B1", true)]
    [InlineData("\\p{sc=Grek}", "u", "\u03B1", true)]
    [InlineData("\\p{Script=Latin}", "u", "\u03B1", false)]
    [InlineData("\\p{scx=Hira}", "u", "\u30FC", true)]
    [InlineData("\\p{sc=Hira}", "u", "\u30FC", false)]
    [InlineData("\\p{General_Category=Decimal_Number}", "u", "\u0661", true)]
    [InlineData("\\p{gc=Nd}", "u", "a", false)]
    [InlineData("\\p{L}", "u", "\u00E9", true)]
    [InlineData("\\p{LC}", "u", "\u01C5", true)]
    [InlineData("\\p{Cased_Letter}", "u", "\u02B0", false)]
    [InlineData("\\p{Any}", "u", "\uD83D\uDE00", true)]
    [InlineData("\\p{ASCII}", "u", "\u0080", false)]
    [InlineData("\\p{Assigned}", "u", "\u0378", false)]
    [InlineData("\\p{Cn}", "u", "\u0378", true)]
    [InlineData("\\P{Any}", "u", "a", false)]
    [InlineData("\\p{White_Space}", "u", "\u3000", true)]
    [InlineData("\\p{space}", "u", "\u3000", true)]
    [InlineData("\\p{Alpha}", "u", "\u0345", true)]
    [InlineData("\\p{Extended_Pictographic}", "u", "\u00A9", true)]
    [InlineData("\\p{punct}", "u", "!", true)]
    [InlineData("\\p{Lu}", "", "p{Lu}", true)]
    public void Test_PropertyEscapes(string pattern, string flags, string input, bool expected)
    {
        Assert.Equal(expected, Test(pattern, flags, input));
    }

    [Fact]
    public void Exec_DeepBacktrackingDoesNotOverflowTheStack()
    {
        var input = new string('a', 200_000) + "c";
        Assert.Equal(input, Exec("(a|b)*c", "", input)?[0]);
        Assert.Equal(input, Exec("(?:a|b)*c", "", input)?[0]);
        Assert.Equal(input, Exec("(?:(a)|b)*?c", "", input)?[0]);
        Assert.Equal(input, Exec("(?:a(?=a|c))*c", "", input)?[0]);
        Assert.Equal(input, Exec("(?<=^a*)c", "", input) is null ? null : input);
        Assert.Null(Exec("(a|b)*d", "", input[..2000]));
    }

    [Fact]
    public void Exec_DeepNesting()
    {
        var pattern = new string('(', 200) + "a" + new string(')', 200);
        Assert.Equal("a", Exec(pattern, "", "a")?[0]);

        var tooDeep = new string('(', 100_000) + "a" + new string(')', 100_000);
        Assert.Null(RegExpProgram.Compile(tooDeep, RegExpFlags.None, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Exec_LargeQuantifiers()
    {
        Assert.Equal(new string('a', 1000), Exec("a{1000}", "", new string('a', 1500))?[0]);
        Assert.Null(Exec("a{1001,}", "", new string('a', 1000)));
        Assert.Null(Exec("(?:a|b){4294967295}", "", "ab"));
        Assert.Equal("", Exec("a{0,4294967295}?", "", "aaa")?[0]);
    }

    /// <summary>
    /// Replays random patterns and inputs whose results were recorded with V8 (node 24): see
    /// <c>Corpus/RegExpV8.md</c>. Each line has the pattern and the flags, and either <c>"e":true</c> (a SyntaxError) or,
    /// for each input, the index, the captures and the lastIndex of the exec.
    /// </summary>
    [Fact]
    public void Exec_MatchesV8()
    {
        using var stream = typeof(RegExpMatcherTests).Assembly.GetManifestResourceStream("Meziantou.Framework.JavaScriptEngine.Tests.Corpus.RegExpV8.jsonl")!;
        using var reader = new StreamReader(stream);
        var failures = new List<string>();
        var count = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            count++;
            using var document = System.Text.Json.JsonDocument.Parse(line);
            var root = document.RootElement;
            var pattern = Decode(root.GetProperty("p").GetString()!);
            var flags = root.GetProperty("f").GetString()!;
            Assert.True(RegExpFlagsExtensions.TryParse(flags, out var parsedFlags));
            var program = RegExpProgram.Compile(pattern, parsedFlags, out var error);
            var description = $"/{Escape(pattern)}/{flags}";
            if (root.TryGetProperty("e", out _))
            {
                if (program is not null)
                {
                    failures.Add($"{description}: expected a SyntaxError");
                }

                continue;
            }

            if (program is null)
            {
                failures.Add($"{description}: unexpected SyntaxError: {error}");
                continue;
            }

            foreach (var test in root.GetProperty("t").EnumerateArray())
            {
                var input = Decode(test.GetProperty("s").GetString()!);
                var lastIndex = test.GetProperty("i").GetInt32();
                var expected = test.GetProperty("r");
                var global = (parsedFlags & (RegExpFlags.Global | RegExpFlags.Sticky)) != 0;
                var start = global ? lastIndex : 0;
                var captures = start > input.Length ? null : program.Exec(input, start, (parsedFlags & RegExpFlags.Sticky) != 0, engine: null);
                var actual = FormatResult(input, captures);
                var expectedText = FormatExpected(expected);
                if (actual != expectedText)
                {
                    failures.Add($"{description} on \"{Escape(input)}\" (lastIndex {lastIndex}): expected {expectedText}, got {actual}");
                }
            }
        }

        Assert.True(count > 1000);
        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} differences:\n" + string.Join('\n', failures.Take(50)));
        }

        static string FormatResult(string input, int[]? captures)
        {
            if (captures is null)
                return "null";

            var parts = new List<string> { captures[0].ToString(System.Globalization.CultureInfo.InvariantCulture) };
            for (var i = 0; i < captures.Length; i += 2)
            {
                var start = i == 0 ? captures[0] : captures[i];
                var end = captures[i + 1];
                parts.Add(end < 0 || start < 0 ? "undefined" : start > end ? $"<invalid {start}..{end}>" : "\"" + Escape(input[start..end]) + "\"");
            }

            return "[" + string.Join(", ", parts) + "]";
        }

        static string FormatExpected(System.Text.Json.JsonElement expected)
        {
            if (expected.ValueKind == System.Text.Json.JsonValueKind.Null)
                return "null";

            var values = expected.EnumerateArray().ToArray();
            var parts = new List<string> { values[0].GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) };
            for (var i = 1; i < values.Length - 1; i++)
            {
                parts.Add(values[i].ValueKind == System.Text.Json.JsonValueKind.Null ? "undefined" : "\"" + Escape(Decode(values[i].GetString()!)) + "\"");
            }

            return "[" + string.Join(", ", parts) + "]";
        }
    }

    /// <summary>Decodes a string of the corpus: <c>%XXXX</c> is a UTF-16 code unit (the JSON reader rejects lone surrogates).</summary>
    private static string Decode(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%')
            {
                builder.Append((char)int.Parse(value.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                i += 4;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in value)
        {
            if (c is >= ' ' and <= '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            }
        }

        return builder.ToString();
    }

    [Theory]
    [InlineData("a+b", "", "aaac aab", 5, "aab")]
    [InlineData("[\\w.-]+@[\\w-]+\\.\\w+", "", "foo bar.baz@example.com", 4, "bar.baz@example.com")]
    [InlineData("\\d+px", "", "12em 34px", 5, "34px")]
    [InlineData("a*b", "", "aac ab", 4, "ab")]
    [InlineData("a*b", "", "aac b", 4, "b")]
    [InlineData("[^,]+,x", "", "ab,cd,x", 3, "cd,x")]
    [InlineData("\\u{1F600}+x", "u", "\uD83D\uDE00\uD83D\uDE00y\uD83D\uDE00x", 5, "\uD83D\uDE00x")]
    [InlineData("a{2,}b", "", "ab aaab", 3, "aaab")]
    [InlineData("a{0,2}b", "", "aaab", 1, "aab")]
    [InlineData("\\s+x", "", "  y  x", 3, "  x")]
    [InlineData(".+;", "", "ab\ncd;", 3, "cd;")]
    [InlineData("[a-c]+(?=d)", "", "abce abcd", 5, "abc")]
    public void Exec_GreedyLoopsThatDoNotBacktrack(string pattern, string flags, string input, int expectedIndex, string expected)
    {
        Assert.Equal(expectedIndex, Index(pattern, flags, input));
        Assert.Equal(expected, Exec(pattern, flags, input)?[0]);
    }

    [Fact]
    public void Exec_StringsAndPrefixes()
    {
        Assert.Equal(7, Index("hello", "", "abcdefghello"));
        Assert.Equal(7, Index("hel+o", "", "abcdefghelllo"));
        Assert.Equal(-1, Index("hello", "", "abcdefghell"));
        Assert.Equal(1, Index("(?:ab|cd)e", "", "xcde"));
        Assert.Equal(["abc", "b"], Exec("a(b)c", "", "xxabc"));
    }
}
