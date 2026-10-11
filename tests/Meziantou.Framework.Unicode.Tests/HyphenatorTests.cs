namespace Meziantou.Framework.Tests;

public sealed class HyphenatorTests
{
    // The patterns that apply to the word "hyphenation" in the English patterns of TeX, the example of Liang's
    // algorithm: hy3ph gives 3 before "p", hen5at gives 5 before "a", and the other values are lower or even.
    private const string LiangExample = "hy3ph he2n hena4 hen5at 1na n2at 1tio 2io";

    private static readonly HyphenatorOptions NoMinimum = new() { LeftHyphenMin = 1, RightHyphenMin = 1 };

    [Theory]
    [InlineData("hyphenation", new[] { 2, 6 })]
    [InlineData("HYPHENATION", new[] { 2, 6 })]
    [InlineData("Hyphenation", new[] { 2, 6 })]
    [InlineData("xyz", new int[0])]
    [InlineData("a", new int[0])]
    [InlineData("", new int[0])]
    public void Hyphenate_AppliesThePatterns(string word, int[] expected)
    {
        var hyphenator = Hyphenator.Parse(LiangExample);

        Assert.Equal(expected, GetBreaks(hyphenator, word));
        Assert.Equal(8, hyphenator.PatternCount);
        Assert.Equal(0, hyphenator.ExceptionCount);
        Assert.Equal(2, hyphenator.LeftHyphenMin);
        Assert.Equal(3, hyphenator.RightHyphenMin);
    }

    [Theory]
    // "hy-phen-ation": 2 letters before the first break, 5 after the last one.
    [InlineData(2, 5, new[] { 2, 6 })]
    [InlineData(3, 5, new[] { 6 })]
    [InlineData(2, 6, new[] { 2 })]
    [InlineData(7, 1, new int[0])]
    [InlineData(1, 1, new[] { 2, 6 })]
    public void Hyphenate_KeepsTheMinimumNumberOfCharactersOnEachSide(int left, int right, int[] expected)
    {
        var hyphenator = Hyphenator.Parse(LiangExample, options: new HyphenatorOptions { LeftHyphenMin = left, RightHyphenMin = right });

        Assert.Equal(expected, GetBreaks(hyphenator, "hyphenation"));
    }

    [Theory]
    // A dot is the start or the end of the word.
    [InlineData(".un1", "undo", new[] { 2 })]
    [InlineData(".un1", "sundo", new int[0])]
    [InlineData("1ly.", "badly", new[] { 3 })]
    [InlineData("1ly.", "lyric", new int[0])]
    // An even value forbids the break that an odd value of another pattern allows.
    [InlineData("1b", "cobx", new[] { 2 })]
    [InlineData("1b a2b", "cabx", new int[0])]
    [InlineData("1b a2b 3b", "cabx", new[] { 2 })]
    // The same letters twice: the last pattern replaces the first.
    [InlineData("a1b a2b", "xaby", new int[0])]
    [InlineData("a2b a1b", "xaby", new[] { 2 })]
    // A dot of the word is not a word boundary.
    [InlineData(".a1", "x.ab", new int[0])]
    [InlineData(".a1", "ab", new[] { 1 })]
    public void Hyphenate_HandlesWordBoundariesAndPriorities(string patterns, string word, int[] expected)
    {
        var hyphenator = Hyphenator.Parse(patterns, options: NoMinimum);

        Assert.Equal(expected, GetBreaks(hyphenator, word));
    }

    [Fact]
    public void Hyphenate_UsesTheExceptionsInsteadOfThePatterns()
    {
        var hyphenator = Hyphenator.Parse(LiangExample + " 1b", "hy-phe-na-tion table A-Bcd- -xy", NoMinimum);

        Assert.Equal(4, hyphenator.ExceptionCount);
        Assert.Equal(new[] { 2, 5, 7 }, GetBreaks(hyphenator, "Hyphenation"));

        // A word without a hyphen in the exceptions is never broken, whatever the patterns say.
        Assert.Empty(GetBreaks(hyphenator, "table"));
        Assert.Equal(new[] { 2 }, GetBreaks(hyphenator, "cable"));

        // The case of an exception does not matter, and a hyphen at an end of the word is not a break.
        Assert.Equal(new[] { 1 }, GetBreaks(hyphenator, "abcd"));
        Assert.Empty(GetBreaks(hyphenator, "xy"));

        // The minimums apply to the exceptions too.
        var withMinimum = Hyphenator.Parse("", "a-bc-def", new HyphenatorOptions { LeftHyphenMin = 2, RightHyphenMin = 3 });
        Assert.Equal(new[] { 3 }, GetBreaks(withMinimum, "abcdef"));
    }

    [Fact]
    public void Parse_ReadsTheGroupsAndTheCommentsOfATexFile()
    {
        var hyphenator = Hyphenator.Parse("""
            % The patterns of a language
            \patterns{ % a comment after the brace
              hy3ph he2n
              hena4 hen5at % and another one
              1na n2at 1tio 2io
            }
            \hyphenation {
              as-so-ciate
            }
            """);

        Assert.Equal(8, hyphenator.PatternCount);
        Assert.Equal(1, hyphenator.ExceptionCount);
        Assert.Equal(new[] { 2, 6 }, GetBreaks(hyphenator, "hyphenation"));
        Assert.Equal(new[] { 2, 4 }, GetBreaks(hyphenator, "associate"));

        // The second argument only holds exceptions, but a group is still what it says.
        var mixed = Hyphenator.Parse("\\hyphenation{ta-ble}", "ca-ble \\patterns{1d}", NoMinimum);
        Assert.Equal(1, mixed.PatternCount);
        Assert.Equal(2, mixed.ExceptionCount);
        Assert.Equal(new[] { 2 }, GetBreaks(mixed, "table"));
        Assert.Equal(new[] { 1 }, GetBreaks(mixed, "ad"));
    }

    [Theory]
    [InlineData(@"\lccode`a=`a 1a")]
    [InlineData(@"\input other")]
    [InlineData("a^^5eb")]
    [InlineData("ab{")]
    [InlineData(@"\patterns a1b")]
    [InlineData(@"\patterns{a1b")]
    [InlineData(@"\patterns{\hyphenation{a-b}}")]
    [InlineData("a1b}")]
    [InlineData("a12b")]
    [InlineData("a.b")]
    [InlineData(".")]
    [InlineData("12")]
    [InlineData("1")]
    public void Parse_RejectsMalformedPatterns(string patterns)
    {
        Assert.Throws<FormatException>(() => Hyphenator.Parse(patterns));
    }

    [Fact]
    public void Parse_RejectsMalformedExceptionsAndInvalidText()
    {
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", "-"));
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", "--"));

        // Built in code: test data that goes through UTF-8 cannot hold an unpaired surrogate.
        var unpaired = "a" + (char)0xD800 + "b";
        Assert.Throws<FormatException>(() => Hyphenator.Parse(unpaired));
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", unpaired));

        Assert.Throws<FormatException>(() => Hyphenator.Parse([(byte)'a', 0xFF, (byte)'b']));
        Assert.Throws<FormatException>(() => Hyphenator.Parse("a1b"u8, [0xC3]));
    }

    [Fact]
    public void Parse_EnforcesTheLimits()
    {
        var twoPatterns = new HyphenatorOptions { MaxPatternCount = 2 };
        Assert.Equal(2, Hyphenator.Parse("a1b c1d", options: twoPatterns).PatternCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("a1b c1d e1f", options: twoPatterns));
        Assert.Equal(2, Hyphenator.Parse("", "a-b c-d", twoPatterns).ExceptionCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", "a-b c-d e-f", twoPatterns));

        // The length counts the letters and the dots, not the digits.
        var threeLetters = new HyphenatorOptions { MaxPatternLength = 3 };
        Assert.Equal(2, Hyphenator.Parse("1a2b3c4 .ab", options: threeLetters).PatternCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("abcd", options: threeLetters));
        Assert.Throws<FormatException>(() => Hyphenator.Parse(".abc", options: threeLetters));
        Assert.Equal(1, Hyphenator.Parse("", "a-b-c", threeLetters).ExceptionCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", "ab-cd", threeLetters));

        var sevenUnits = new HyphenatorOptions { MaxInputLength = 7 };
        Assert.Equal(2, Hyphenator.Parse("a1b c1d", options: sevenUnits).PatternCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("a1b c1d ", options: sevenUnits));
        Assert.Throws<FormatException>(() => Hyphenator.Parse("", "a-b c-de", sevenUnits));
        Assert.Equal(2, Hyphenator.Parse("a1b c1d"u8, options: sevenUnits).PatternCount);
        Assert.Throws<FormatException>(() => Hyphenator.Parse("a1b c1d "u8, options: sevenUnits));
        using (var stream = new MemoryStream("a1b c1d"u8.ToArray()))
        {
            Assert.Equal(2, Hyphenator.Load(stream, options: sevenUnits).PatternCount);
        }

        using (var stream = new MemoryStream("a1b c1d "u8.ToArray()))
        {
            Assert.Throws<FormatException>(() => Hyphenator.Load(stream, options: sevenUnits));
        }
    }

    [Fact]
    public void Hyphenate_IgnoresAWordLongerThanTheLimit()
    {
        var hyphenator = Hyphenator.Parse("a1a", options: new HyphenatorOptions { LeftHyphenMin = 1, RightHyphenMin = 1, MaxWordLength = 6 });

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, GetBreaks(hyphenator, "aaaaaa"));
        Assert.Empty(GetBreaks(hyphenator, "aaaaaaa"));
    }

    [Fact]
    public void Hyphenate_NeverBreaksInsideAGraphemeCluster()
    {
        // The pattern allows a break after every "e". In a decomposed word that position is before the combining
        // acute accent, inside the character: the break is dropped. The patterns match code points, so a word is
        // hyphenated as intended when it is in the normalization form of the patterns.
        var hyphenator = Hyphenator.Parse("e1 \u00E91", options: NoMinimum);
        Assert.Empty(GetBreaks(hyphenator, "de\u0301be\u0301x"));
        Assert.Equal(new[] { 2, 4 }, GetBreaks(hyphenator, "d\u00E9b\u00E9x"));
        Assert.Equal(new[] { 2, 4 }, GetBreaks(hyphenator, "debex"));

        // The minimums count grapheme clusters, not code units: "e + accent" is one character.
        var before = Hyphenator.Parse("1x", options: new HyphenatorOptions { LeftHyphenMin = 2, RightHyphenMin = 1 });
        Assert.Empty(GetBreaks(before, "e\u0301xab"));
        Assert.Equal(new[] { 3 }, GetBreaks(before, "ae\u0301xab"));

        var after = Hyphenator.Parse("x1", options: new HyphenatorOptions { LeftHyphenMin = 1, RightHyphenMin = 2 });
        Assert.Empty(GetBreaks(after, "abxe\u0301"));
        Assert.Equal(new[] { 3 }, GetBreaks(after, "abxe\u0301a"));
    }

    [Fact]
    public void Hyphenate_ReturnsUtf16IndexesForSupplementaryLetters()
    {
        // Deseret has letters with case outside the Basic Multilingual Plane: the pattern is in lowercase, the word in uppercase.
        var hyphenator = Hyphenator.Parse("\U000104281\U00010429", options: NoMinimum);

        Assert.Equal(new[] { 2 }, GetBreaks(hyphenator, "\U00010400\U00010401"));
        Assert.Equal(new[] { 3 }, GetBreaks(hyphenator, "a\U00010428\U00010429b"));
    }

    [Fact]
    public void Hyphenate_ThrowsWhenTheDestinationIsTooSmall()
    {
        var hyphenator = Hyphenator.Parse(LiangExample);

        Assert.Throws<ArgumentException>("breakPositions", () => hyphenator.Hyphenate("hyphenation", new int[1]));
        Assert.Throws<ArgumentException>("breakPositions", () => hyphenator.Hyphenate("hyphenation", []));
        Assert.Equal(2, hyphenator.Hyphenate("hyphenation", new int[2]));
        Assert.Equal(0, hyphenator.Hyphenate("xyz", []));
    }

    [Fact]
    public void Hyphenate_DoesNotAllocate()
    {
        var hyphenator = Hyphenator.Parse(LiangExample, "as-so-ciate");
        var positions = new int[16];
        hyphenator.Hyphenate("hyphenation", positions);
        hyphenator.Hyphenate("associate", positions);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            hyphenator.Hyphenate("hyphenation", positions);
            hyphenator.Hyphenate("ASSOCIATE", positions);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task Load_ReadsUtf8FilesAndStreams()
    {
        // A byte order mark, then the patterns; the exceptions are in a second file.
        byte[] patterns = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(LiangExample + " \u00E91")];
        var exceptions = "as-so-ciate"u8.ToArray();
        var directory = Path.Combine(Path.GetTempPath(), "hyphenator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var patternsPath = Path.Combine(directory, "patterns.txt");
            var exceptionsPath = Path.Combine(directory, "exceptions.txt");
            await File.WriteAllBytesAsync(patternsPath, patterns, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(exceptionsPath, exceptions, TestContext.Current.CancellationToken);

            Check(Hyphenator.Load(patternsPath, exceptionsPath), exceptionCount: 1);
            Check(Hyphenator.Load(patternsPath), exceptionCount: 0);
            Check(await Hyphenator.LoadAsync(patternsPath, exceptionsPath, cancellationToken: TestContext.Current.CancellationToken), exceptionCount: 1);
            Check(await Hyphenator.LoadAsync(patternsPath, cancellationToken: TestContext.Current.CancellationToken), exceptionCount: 0);

            using var patternStream = new MemoryStream(patterns);
            using var exceptionStream = new MemoryStream(exceptions);
            Check(Hyphenator.Load(patternStream, exceptionStream), exceptionCount: 1);

            // The streams are left open.
            patternStream.Position = 0;
            exceptionStream.Position = 0;
            Check(await Hyphenator.LoadAsync(patternStream, exceptionStream, cancellationToken: TestContext.Current.CancellationToken), exceptionCount: 1);
            Check(Hyphenator.Parse(patterns, exceptions), exceptionCount: 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static void Check(Hyphenator hyphenator, int exceptionCount)
        {
            Assert.Equal(9, hyphenator.PatternCount);
            Assert.Equal(exceptionCount, hyphenator.ExceptionCount);
            Assert.Equal(new[] { 2, 6 }, GetBreaks(hyphenator, "hyphenation"));
            Assert.Equal(exceptionCount == 1 ? [2, 4] : [], GetBreaks(hyphenator, "associate"));
            Assert.Equal(new[] { 4 }, GetBreaks(hyphenator, "caf\u00E9teria"));
        }
    }

    [Fact]
    public async Task Load_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>("patternsPath", () => Hyphenator.Load((string)null!));
        Assert.Throws<ArgumentNullException>("patterns", () => Hyphenator.Load((Stream)null!));
        await Assert.ThrowsAsync<ArgumentNullException>("patternsPath", () => Hyphenator.LoadAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>("patterns", () => Hyphenator.LoadAsync((Stream)null!, cancellationToken: TestContext.Current.CancellationToken));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var stream = new MemoryStream("a1b"u8.ToArray());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Hyphenator.LoadAsync(new CancelingStream(stream), cancellationToken: canceled.Token));
    }

    [Fact]
    public void Options_ValidateTheirValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { LeftHyphenMin = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { RightHyphenMin = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxPatternCount = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxPatternLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxPatternLength = 256 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxInputLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxWordLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyphenatorOptions { MaxWordLength = 4097 });

        var options = HyphenatorOptions.Default;
        Assert.Equal(2, options.LeftHyphenMin);
        Assert.Equal(3, options.RightHyphenMin);
        Assert.Equal(250_000, options.MaxPatternCount);
        Assert.Equal(64, options.MaxPatternLength);
        Assert.Equal(8 * 1024 * 1024, options.MaxInputLength);
        Assert.Equal(255, options.MaxWordLength);
    }

    [Fact]
    public void Hyphenate_AgreesWithADirectApplicationOfEveryPattern()
    {
        // Random patterns and words over three letters: the reference slides each pattern along the word.
        var random = new Generator(20261011);
        for (var round = 0; round < 20; round++)
        {
            var patterns = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < 60; i++)
            {
                var letters = new StringBuilder();
                var text = new StringBuilder();
                var length = 1 + random.Next(4);
                var startsWord = random.Next(8) == 0;
                var endsWord = random.Next(8) == 0;
                if (startsWord)
                {
                    letters.Append('.');
                    text.Append('.');
                }

                for (var j = 0; j <= length; j++)
                {
                    if (random.Next(2) == 0)
                    {
                        text.Append((char)('0' + random.Next(6)));
                    }

                    if (j < length)
                    {
                        var letter = (char)('a' + random.Next(3));
                        letters.Append(letter);
                        text.Append(letter);
                    }
                }

                if (endsWord)
                {
                    letters.Append('.');
                    text.Append('.');
                }

                // The last pattern for the same letters wins, as in the library.
                patterns[letters.ToString()] = text.ToString();
            }

            var left = 1 + random.Next(3);
            var right = 1 + random.Next(3);
            var hyphenator = Hyphenator.Parse(string.Join(' ', patterns.Values), options: new HyphenatorOptions { LeftHyphenMin = left, RightHyphenMin = right });
            Assert.Equal(patterns.Count, hyphenator.PatternCount);

            for (var i = 0; i < 200; i++)
            {
                var word = new StringBuilder();
                var wordLength = 2 + random.Next(11);
                for (var j = 0; j < wordLength; j++)
                {
                    word.Append((char)('a' + random.Next(3)));
                }

                var expected = ReferenceHyphenate(patterns.Values, word.ToString(), left, right);
                var actual = GetBreaks(hyphenator, word.ToString());
                if (!expected.SequenceEqual(actual))
                {
                    Assert.Fail($"'{word}' with [{string.Join(' ', patterns.Values)}]: expected {string.Join(' ', expected)}, actual {string.Join(' ', actual)}");
                }
            }
        }
    }

    private static int[] ReferenceHyphenate(IEnumerable<string> patterns, string word, int left, int right)
    {
        var text = "." + word + ".";
        var scores = new int[text.Length + 1];
        foreach (var pattern in patterns)
        {
            // values[p] is the digit before the letter p of the pattern.
            var letters = new string(pattern.Where(c => !char.IsAsciiDigit(c)).ToArray());
            var values = new int[letters.Length + 1];
            var position = 0;
            foreach (var c in pattern)
            {
                if (char.IsAsciiDigit(c))
                {
                    values[position] = c - '0';
                }
                else
                {
                    position++;
                }
            }

            for (var start = 0; start + letters.Length <= text.Length; start++)
            {
                if (string.CompareOrdinal(text, start, letters, 0, letters.Length) != 0)
                    continue;

                for (var p = 0; p < values.Length; p++)
                {
                    scores[start + p] = Math.Max(scores[start + p], values[p]);
                }
            }
        }

        // scores[i + 1] is the value before the letter i of the word.
        var result = new List<int>();
        for (var i = left; i <= word.Length - right; i++)
        {
            if ((scores[i + 1] & 1) != 0)
            {
                result.Add(i);
            }
        }

        return [.. result];
    }

    private static int[] GetBreaks(Hyphenator hyphenator, string word)
    {
        var positions = new int[Math.Max(1, word.Length)];
        var count = hyphenator.Hyphenate(word, positions);
        return positions[..count];
    }

    /// <summary>A small deterministic generator: the tests must not depend on the algorithm of <see cref="Random"/>.</summary>
    private sealed class Generator(uint seed)
    {
        private uint _state = seed;

        public int Next(int exclusiveMaximum)
        {
            _state = (_state * 1664525) + 1013904223;
            return (int)((_state >> 8) % (uint)exclusiveMaximum);
        }
    }

    private sealed class CancelingStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
