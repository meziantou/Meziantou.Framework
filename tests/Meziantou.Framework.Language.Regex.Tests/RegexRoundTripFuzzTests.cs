namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexRoundTripFuzzTests
{
    private static readonly string[] Fragments =
    [
        // literals, including a surrogate pair and a lone surrogate
        "a", "ab", "0", "-", "_", "é", "😀", "\ud800",
        // metacharacters, bare and escaped
        ".", "^", "$", "|", "\\", "\\\\", "\\.", "\\-", "\\/",
        // groups
        "(", ")", "(?:", "(?=", "(?!", "(?<=", "(?<!", "(?>", "(?<name>", "(?'name'", "(?<n1-n2>",
        "(?(1)", "(?(name)", "(?((?=a))", "(?#comment)", "(?#", "(?)", "(?",
        // inline options
        "(?i)", "(?-i)", "(?x)", "(?n)", "(?imnsx-imnsx:", "(?ix-ms:", "(?q)",
        // character classes
        "[", "]", "[]", "[]]", "[^", "[^]", "[a-z]", "[z-a]", "[a-z-[aeiou]]", "[a-z-[b]c]",
        "[\\w]", "[a-\\d]", "[[:alpha:]]", "[\\]]", "[-a]", "[a-]", "[a-[b]]",
        // quantifiers
        "*", "+", "?", "*?", "+?", "??", "*+", "**", "{2}", "{2,}", "{2,5}", "{2,5}?",
        "{,5}", "{5,2}", "{", "}", "{2147483648}", "{a}",
        // escapes
        "\\d", "\\D", "\\w", "\\W", "\\s", "\\S", "\\b", "\\B", "\\A", "\\Z", "\\z", "\\G",
        "\\n", "\\r", "\\t", "\\f", "\\v", "\\a", "\\e", "\\0", "\\q",
        "\\1", "\\10", "\\12", "\\123", "\\k<name>", "\\k", "\\<name>", "\\'name'",
        "\\x41", "\\x4", "\\xZZ", "\\u0041", "\\u00", "\\cA", "\\ca", "\\c1", "\\c",
        "\\p{L}", "\\p{Lu}", "\\p{IsGreek}", "\\P{Lu}", "\\p{Bogus}", "\\p{", "\\pL", "\\p",
        // extended-mode trivia
        " ", "\t", "\n", "\r\n", "\r", "#comment\n", "#comment",
    ];

    private static readonly RegexFlavor[] Flavors =
    [
        RegexFlavor.Net,
        RegexFlavor.JavaScript,
        RegexFlavor.PcrePerl,
        RegexFlavor.PosixExtended,
        RegexFlavor.PosixBasic,
    ];

    /// <summary>Generates the same patterns the differential test uses, so a failure there is reproducible here.</summary>
    internal static IEnumerable<string> GenerateFragmentSequences(int seed, int count)
    {
        var random = new DeterministicRandom(seed);
        for (var iteration = 0; iteration < count; iteration++)
        {
            var builder = new StringBuilder();
            var length = 1 + random.Next(13);
            for (var index = 0; index < length; index++)
            {
                builder.Append(Fragments[random.Next(Fragments.Length)]);
            }

            yield return builder.ToString();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomFragmentSequences_RoundTripExactly(int seed)
    {
        var random = new DeterministicRandom(seed + 1000);
        foreach (var pattern in GenerateFragmentSequences(seed, count: 400))
        {
            RegexSyntaxAssert.TextIsFaithful(pattern, Flavors[random.Next(Flavors.Length)]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RandomFragmentSequences_RoundTripExactlyInExtendedMode(int seed)
    {
        var options = new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        foreach (var pattern in GenerateFragmentSequences(seed + 50, count: 400))
        {
            RegexSyntaxAssert.TextIsFaithful(pattern, options);
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void RandomCharacterSoup_RoundTripsExactly(int seed)
    {
        const string Alphabet = "ab01 \t\n\r\\^$.|?*+()[]{}-<>=!:'\"#,/pPkQEwWdDsSbBAZzGxuc";
        var random = new DeterministicRandom(seed);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var builder = new StringBuilder();
            var length = random.Next(40);
            for (var index = 0; index < length; index++)
            {
                builder.Append(Alphabet[random.Next(Alphabet.Length)]);
            }

            RegexSyntaxAssert.TextIsFaithful(builder.ToString(), RegexFlavor.Net);
        }
    }

    /// <summary>
    /// Every prefix of a realistic pattern, which is where a scanner that consumes without emitting loses characters.
    /// </summary>
    [Fact]
    public void EveryPrefixOfARealisticPattern_RoundTripsExactly()
    {
        const string Pattern = """
            (?x)                                          # free-spacing mode
            ^
            (?<ip> \d{1,3} (?: \. \d{1,3} ){3} )          # client address
            \s+ - \s+ (?<user> \S+ )
            \s+ \[ (?<ts> [^\]]+ ) \]
            \s+ " (?<method> [A-Z]+ ) \s+ (?<path> \S+ ) \s+ HTTP/(?<ver> \d\.\d ) "
            \s+ (?<status> [1-5]\d{2} )
            \s+ (?<size> \d+ | - )
            (?: \s+ " (?<ref> (?: [^"\\] | \\. )* ) " )?
            (?(ref) \s* (?# a referrer implies a user agent ) " (?<ua> [^"]* ) " )?
            $
            """;

        for (var length = 0; length <= Pattern.Length; length++)
        {
            RegexSyntaxAssert.TextIsFaithful(Pattern[..length], RegexFlavor.Net);
        }
    }

    [Fact]
    public void EveryPrefixOfASubtractionChain_RoundTripsExactly()
    {
        const string Pattern = @"[a-z-[b-[c-[d]]]]x[[:alpha:]]\p{IsGreek}(?<a-b>x)";

        for (var length = 0; length <= Pattern.Length; length++)
        {
            RegexSyntaxAssert.TextIsFaithful(Pattern[..length], RegexFlavor.Net);
        }
    }

    [Fact]
    public void ParseText_NeverThrows()
    {
        foreach (var flavor in Flavors)
        {
            foreach (var pattern in GenerateFragmentSequences(seed: 7, count: 200))
            {
                Assert.Null(Record.Exception(() => RegexSyntaxTree.ParseText(pattern, flavor)));
            }
        }
    }

    /// <summary>
    /// A fixed xorshift generator. The corpus has to be identical on every runtime, which <see cref="Random"/> does
    /// not promise, and a failing seed has to stay reproducible.
    /// </summary>
    internal sealed class DeterministicRandom(int seed)
    {
        private uint _state = (uint)seed | 1u;

        public int Next(int exclusiveUpperBound)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (int)(_state % (uint)exclusiveUpperBound);
        }
    }
}
