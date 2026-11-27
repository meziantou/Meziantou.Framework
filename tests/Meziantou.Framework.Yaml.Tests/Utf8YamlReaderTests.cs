namespace Meziantou.Framework.Yaml.Tests;

public sealed class Utf8YamlReaderTests
{
    public static TheoryData<string> CorpusDocuments() => new(["k8s-s", "workflow-m", "compose-m", "k8s-m", "openapi-m", "records-m"]);

    public static TheoryData<string> ValidYamlTestSuiteCases() => YamlTestSuite.Ids(testCase => !testCase.Error);

    public static TheoryData<string> InvalidYamlTestSuiteCases() => YamlTestSuite.Ids(testCase => testCase.Error);

    public static TheoryData<string> TestFiles()
    {
        var prefix = "Meziantou.Framework.Yaml.Tests.files.";
        return new TheoryData<string>(typeof(Utf8YamlReaderTests).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".yaml", StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .Where(name => !name.StartsWith("fail-", StringComparison.Ordinal) && !name.StartsWith("invalid-", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Corpus_EventsMatchParser(string name)
    {
        var yaml = ReadCorpus(name);
        var expected = YamlTestSuite.ReadEvents(Parser.CreateParser(new StringReader(yaml)));
        Assert.Equal(expected, ReadEvents(Encoding.UTF8.GetBytes(yaml)));
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Corpus_TokensAndTriviaReproduceInput(string name)
    {
        Assert.Null(CheckTokenCoverage(Encoding.UTF8.GetBytes(ReadCorpus(name))));
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Corpus_ValueEqualsMatchesGetString(string name)
    {
        var reader = new Utf8YamlReader(Encoding.UTF8.GetBytes(ReadCorpus(name)));
        var count = 0;
        while (reader.Read())
        {
            if (reader.TokenKind is Utf8YamlTokenKind.Scalar)
            {
                var value = reader.GetString();
                Assert.True(reader.ValueEquals(Encoding.UTF8.GetBytes(value)));
                Assert.True(reader.ValueEquals(value));
                Assert.False(reader.ValueEquals((value + "x").AsSpan()));
                Assert.False(reader.ValueEquals(Encoding.UTF8.GetBytes(value + "x")));
                count++;
            }
        }

        Assert.True(count > 10);
    }

    [Theory]
    [MemberData(nameof(TestFiles))]
    public void TestFiles_EventsMatchParser(string name)
    {
        string yaml;
        using (var reader = new StreamReader(typeof(Utf8YamlReaderTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Yaml.Tests.files." + name)!))
        {
            yaml = reader.ReadToEnd();
        }

        string expected;
        try
        {
            expected = YamlTestSuite.ReadEvents(Parser.CreateParser(new StringReader(yaml)));
        }
        catch (YamlException)
        {
            Assert.ThrowsAny<Exception>(() => ReadEvents(Encoding.UTF8.GetBytes(yaml)));
            return;
        }

        var utf8 = Encoding.UTF8.GetBytes(yaml);
        Assert.Equal(expected, ReadEvents(utf8));
        Assert.Null(CheckTokenCoverage(utf8));
    }

    [Theory]
    [MemberData(nameof(ValidYamlTestSuiteCases))]
    public void YamlTestSuite_ValidCase_EventsMatch(string id)
    {
        var testCase = YamlTestSuite.Get(id);
        var utf8 = Encoding.UTF8.GetBytes(testCase.Yaml);
        Assert.Equal(testCase.Events, ReadEvents(utf8));
        Assert.Null(CheckTokenCoverage(utf8));
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Corpus_PartialBuffers_ReadTheSameTokens(string name)
    {
        var utf8 = Encoding.UTF8.GetBytes(ReadCorpus(name));
        var expected = ReadEventsAndPositions(utf8, chunkSize: 0);
        foreach (var chunkSize in (int[])[1, 7, 64, 1000])
        {
            Assert.Equal(expected, ReadEventsAndPositions(utf8, chunkSize));
        }
    }

    [Theory]
    [MemberData(nameof(ValidYamlTestSuiteCases))]
    public void YamlTestSuite_ValidCase_PartialBuffers_ReadTheSameTokens(string id)
    {
        var utf8 = Encoding.UTF8.GetBytes(YamlTestSuite.Get(id).Yaml);
        var expected = ReadEventsAndPositions(utf8, chunkSize: 0);
        foreach (var chunkSize in (int[])[1, 2, 7])
        {
            Assert.Equal(expected, ReadEventsAndPositions(utf8, chunkSize));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidYamlTestSuiteCases))]
    public void YamlTestSuite_InvalidCase_PartialBuffers_IsRejected(string id)
    {
        var utf8 = Encoding.UTF8.GetBytes(YamlTestSuite.Get(id).Yaml);
        Assert.ThrowsAny<YamlException>(() => ReadEventsAndPositions(utf8, chunkSize: 3));
    }

    [Theory]
    [MemberData(nameof(InvalidYamlTestSuiteCases))]
    public void YamlTestSuite_InvalidCase_IsRejected(string id)
    {
        var testCase = YamlTestSuite.Get(id);
        Assert.ThrowsAny<YamlException>(() => ReadEvents(Encoding.UTF8.GetBytes(testCase.Yaml)));
    }

    [Theory]
    [InlineData("key: value", "value")]
    [InlineData("key: 'it''s'", "it's")]
    [InlineData("key: \"a\\tb\\u00e9\\x41\"", "a\tbéA")]
    [InlineData("key: \"a\n  b\n\n  c\"", "a b\nc")]
    [InlineData("key: plain\n  multi\n  line", "plain multi line")]
    [InlineData("key: |\n  a\n  b\n", "a\nb\n")]
    [InlineData("key: >-\n  a\n  b\n\n  c\n", "a b\nc")]
    [InlineData("key: |+\n  a\n\n", "a\n\n")]
    public void GetString_DecodesValue(string yaml, string expected)
    {
        var reader = new Utf8YamlReader(Encoding.UTF8.GetBytes(yaml));
        while (reader.Read())
        {
            if (reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.IndicatorOffset >= 0)
            {
                Assert.Equal(expected, reader.GetString());
                Assert.True(reader.ValueEquals(Encoding.UTF8.GetBytes(expected)));
                return;
            }
        }

        Assert.Fail("No value found");
    }

    [Fact]
    public void Read_RecordsTokenPositions()
    {
        var utf8 = "a: &x !t [b, c] # comment\n"u8.ToArray();
        var reader = new Utf8YamlReader(utf8);
        var tokens = new List<string>();
        while (reader.Read())
        {
            tokens.Add(FormattableString.Invariant($"{reader.TokenKind}@{reader.TokenStart}+{reader.TokenLength} i={reader.IndicatorOffset} a={reader.AnchorStart} t={reader.TagStart}"));
        }

        Assert.Equal(
            [
                "DocumentStart@0+0 i=-1 a=-1 t=-1",
                "MappingStart@0+0 i=-1 a=-1 t=-1",
                "Scalar@0+1 i=-1 a=-1 t=-1",
                "SequenceStart@9+1 i=1 a=3 t=6",
                "Scalar@10+1 i=-1 a=-1 t=-1",
                "Scalar@13+1 i=11 a=-1 t=-1",
                "SequenceEnd@14+1 i=-1 a=-1 t=-1",
                "MappingEnd@15+0 i=-1 a=-1 t=-1",
                "DocumentEnd@15+0 i=-1 a=-1 t=-1",
            ],
            tokens);
    }

    [Fact]
    public void ReadmeExample_ReadTokens()
    {
        #region Utf8YamlReader_ReadTokens
        var utf8 = "name: web\nports: [80, 443]\n"u8;
        var reader = new Utf8YamlReader(utf8);
        var keys = new List<string>();
        var ports = new List<long>();
        while (reader.Read())
        {
            if (reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.CurrentDepth == 1 && reader.IndicatorOffset < 0)
            {
                // A key of the root mapping: a scalar that no ':' precedes
                keys.Add(reader.GetString());
            }
            else if (reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.CurrentDepth == 2)
            {
                ports.Add(long.Parse(reader.GetString(), CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(["name", "ports"], keys);
        Assert.Equal([80L, 443L], ports);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Positions()
    {
        #region Utf8YamlReader_Positions
        var utf8 = "image: 'nginx' # web server\n"u8;
        var reader = new Utf8YamlReader(utf8);
        while (reader.Read() && !(reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.ValueEquals("nginx"u8)))
        {
        }

        // The token spans the quotes; the value is decoded on demand
        Assert.Equal("'nginx'", Encoding.UTF8.GetString(reader.TokenSpan));
        Assert.Equal(7, reader.TokenStart);
        Assert.Equal(5, reader.IndicatorOffset);
        Assert.Equal(ScalarStyle.SingleQuoted, reader.ScalarStyle);
        Assert.Equal("nginx", reader.GetString());
        #endregion
    }

    [Fact]
    public void Read_EmptyBlockThatIsNotTheFinalOne_ReadsNoToken()
    {
        var reader = new Utf8YamlReader([], isFinalBlock: false, new Utf8YamlReaderState(new Utf8YamlReaderOptions()));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Load_TextWithALoneHighSurrogate_ReportsAnInvalidSequence()
    {
        // The encoder keeps the high surrogate for the next block, so the first block has no byte
        Assert.Throws<SyntaxErrorException>(() => Meziantou.Framework.Yaml.Nodes.YamlStream.Load(new StringReader("\uDAEB")));
    }

    [Fact]
    public void ReadmeExample_PartialBuffers()
    {
        #region Utf8YamlReader_PartialBuffers
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("items:\n- first\n- second\n- third\n"));
        var buffer = new byte[16];
        var length = 0;
        var isFinalBlock = false;
        var state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = 32 });
        var values = new List<string>();
        while (true)
        {
            if (!isFinalBlock)
            {
                // Grow the buffer when the bytes that are left fill it: a token can be longer than the buffer
                if (length == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }

                var read = stream.Read(buffer, length, buffer.Length - length);
                length += read;
                isFinalBlock = read == 0;
            }

            var reader = new Utf8YamlReader(buffer.AsSpan(0, length), isFinalBlock, state);
            while (reader.Read())
            {
                if (reader.TokenKind is Utf8YamlTokenKind.Scalar)
                {
                    values.Add(reader.GetString());
                }
            }

            if (isFinalBlock)
                break;

            // Keep the bytes that the next reader needs, and its state
            var consumed = reader.BytesConsumed;
            state = reader.CurrentState;
            buffer.AsSpan(consumed, length - consumed).CopyTo(buffer);
            length -= consumed;
        }

        Assert.Equal(["items", "first", "second", "third"], values);
        #endregion
    }

    [Fact]
    public void Read_DeepDocument_GrowsTheStack()
    {
        var yaml = new string('[', 100) + new string(']', 100);
        var reader = new Utf8YamlReader(Encoding.UTF8.GetBytes(yaml), new Utf8YamlReaderOptions { MaxDepth = 100 });
        var maxDepth = 0;
        while (reader.Read())
        {
            maxDepth = Math.Max(maxDepth, reader.CurrentDepth);
        }

        Assert.Equal(100, maxDepth);
    }

    // Found by fuzzing: the reader reads the same nodes as the scanner-based parser
    [Theory]
    [InlineData("- [ !!float : separate ]\n- [ \"JSON like\":adjacent ]\n- [ {JSON: like}:adjacent ]\n")]
    [InlineData("&flowseq [\n a: b,\n &c : d,\n { &e e: f },\n &g { g: h }\n]\n")]
    [InlineData("- [ {JSON:\" like}:adjacent ]\n")]
    [InlineData("%TAG !m! #!my-\n--- !m!light green\n")]
    [InlineData("---\n&mapping\n&key [ item a, b, c\" ]: value\n")]
    [InlineData("---\n[\n [ a, [!<tag:yaml[.org,2002:str> [[b,c]]: d, e]]: 23\n]\n")]
    [InlineData("{? \"a ]: b\" : value}\n")]
    [InlineData("{? \"foo  }  :bar }\\n\" : bar}\n")]
    [InlineData("[? \"a ]: b\" : value]\n")]
    [InlineData("!<%6Aag:yaml.org,2002:str> foo\n")]
    public void Read_FuzzFindings_MatchTheScannerParser(string yaml)
    {
        var expected = YamlTestSuite.ReadEvents(new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml)));
        var utf8 = Encoding.UTF8.GetBytes(yaml);
        Assert.Equal(expected, ReadEvents(utf8));
        Assert.Null(CheckTokenCoverage(utf8));
    }

    [Theory]
    [InlineData("\uFEFF--- |1+")]
    [InlineData("\uFEFF--- |1-")]
    [InlineData("\uFEFFa: b\nc: d\n")]
    public void Read_ByteOrderMark_PartialBuffers_ReadTheSameTokens(string yaml)
    {
        var utf8 = Encoding.UTF8.GetBytes(yaml);
        var expected = ReadEventsAndPositions(utf8, chunkSize: 0);
        for (var chunkSize = 1; chunkSize < 8; chunkSize++)
        {
            Assert.Equal(expected, ReadEventsAndPositions(utf8, chunkSize));
        }
    }

    [Theory]
    [InlineData("%TAG !a#b! tag:example.com,2000:\n--- !a#b!c x\n")]
    public void Read_InvalidInput_ThrowsYamlException(string yaml)
    {
        Assert.ThrowsAny<YamlException>(() => ReadEvents(Encoding.UTF8.GetBytes(yaml)));
    }

    // A flow collection is a JSON-like key, in a flow sequence too (YAML 1.2 §7.4.2)
    [Theory]
    [InlineData("---\n[!!set\n  [ a, [ [[b,c]]: d, e]]: 23\n]\n")]
    public void Read_FlowCollectionKeys_GiveTheSameTokensInEveryMode(string yaml)
    {
        var utf8 = Encoding.UTF8.GetBytes(yaml);
        Assert.Equal("+STR\n+DOC ---\n+SEQ []\n+MAP {}\n+SEQ [] <tag:yaml.org,2002:set>\n=VAL :a\n+SEQ []\n+MAP {}\n+SEQ []\n+SEQ []\n=VAL :b\n=VAL :c\n-SEQ\n-SEQ\n=VAL :d\n-MAP\n=VAL :e\n-SEQ\n-SEQ\n=VAL :23\n-MAP\n-SEQ\n-DOC\n-STR", ReadEvents(utf8));
        var positions = ReadEventsAndPositions(utf8, chunkSize: 0);
        for (var chunkSize = 1; chunkSize < 14; chunkSize++)
        {
            Assert.Equal(positions, ReadEventsAndPositions(utf8, chunkSize));
        }

        using var document = YamlDocument.Parse(utf8);
        Assert.Equal(YamlElementKind.Sequence, document.Root.Kind);
        Assert.Null(CheckTokenCoverage(utf8));
    }

    [Theory]
    [InlineData("# comment \uFEFF\na: b\n")]
    [InlineData("%FOO \uFEFF\n--- a\n")]
    public void Read_ByteOrderMarkInCommentOrDirective_IsAccepted(string yaml)
    {
        var expected = YamlTestSuite.ReadEvents(new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml)));
        Assert.Equal(expected, ReadEvents(Encoding.UTF8.GetBytes(yaml)));
    }

    [Theory]
    [InlineData("% TAG !e! tag:example.com,2000:\n--- !e!a b\n")]
    [InlineData("%\t YAML 1.2\n--- a\n")]
    [InlineData("%\n--- a\n")]
    public void Read_DirectiveWithoutName_Throws(string yaml)
    {
        // The name follows the '%', like the scanner reads it: "% TAG" does not declare the handle
        Assert.Throws<SyntaxErrorException>(() => YamlTestSuite.ReadEvents(new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml))));
        var exception = Assert.Throws<SyntaxErrorException>(() => ReadEvents(Encoding.UTF8.GetBytes(yaml)));
        Assert.Contains("Expected a directive name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_DeeperThanMaxDepth_Throws()
    {
        var utf8 = Encoding.UTF8.GetBytes(new string('[', 65) + new string(']', 65));
        Assert.Throws<YamlException>(() =>
        {
            var reader = new Utf8YamlReader(utf8);
            while (reader.Read())
            {
            }
        });
    }

    private static string ReadCorpus(string name)
    {
        var assembly = typeof(Utf8YamlReaderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("corpus." + name + ".yaml", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);
        return reader.ReadToEnd();
    }

    /// <summary>Formats the tokens like the test.event files of the yaml-test-suite, see <see cref="YamlTestSuite.ReadEvents"/>.</summary>
    internal static string ReadEvents(byte[] utf8) => ReadEventsAndPositions(utf8, chunkSize: 0).Events;

    /// <summary>
    /// Reads the input in one buffer (<paramref name="chunkSize"/> 0), or in buffers that receive <paramref name="chunkSize"/>
    /// more bytes each time the reader needs more data, like a stream. Returns the events and the absolute position of every token.
    /// </summary>
    internal static (string Events, string Positions) ReadEventsAndPositions(byte[] utf8, int chunkSize)
    {
        var events = new List<string> { "+STR" };
        var positions = new List<string>();
        if (chunkSize == 0)
        {
            var reader = new Utf8YamlReader(utf8);
            while (reader.Read())
            {
                AddToken(ref reader, events, positions);
            }
        }
        else
        {
            var state = default(Utf8YamlReaderState);
            var start = 0;
            var available = Math.Min(chunkSize, utf8.Length);
            while (true)
            {
                var isFinalBlock = start + available == utf8.Length;
                var reader = new Utf8YamlReader(utf8.AsSpan(start, available), isFinalBlock, state);
                while (reader.Read())
                {
                    AddToken(ref reader, events, positions);
                }

                if (isFinalBlock)
                    break;

                var consumed = reader.BytesConsumed;
                state = reader.CurrentState;
                start += consumed;
                available = Math.Min(available - consumed + chunkSize, utf8.Length - start);
            }
        }

        events.Add("-STR");
        return (string.Join('\n', events), string.Join('\n', positions));

        static void AddToken(ref Utf8YamlReader reader, List<string> events, List<string> positions)
        {
            positions.Add(FormattableString.Invariant($"{reader.TokenKind} {reader.BufferOffset + reader.TokenStart}+{reader.TokenLength} i={Absolute(ref reader, reader.IndicatorOffset)} k={Absolute(ref reader, reader.ExplicitKeyIndicatorOffset)} a={Absolute(ref reader, reader.AnchorStart)} t={Absolute(ref reader, reader.TagStart)}"));
            switch (reader.TokenKind)
            {
                case Utf8YamlTokenKind.DocumentStart:
                    events.Add(reader.TokenLength > 0 ? "+DOC ---" : "+DOC");
                    break;

                case Utf8YamlTokenKind.DocumentEnd:
                    events.Add(reader.TokenLength > 0 ? "-DOC ..." : "-DOC");
                    break;

                case Utf8YamlTokenKind.MappingStart:
                    events.Add("+MAP" + (reader.IsFlow ? " {}" : "") + FormatProperties(ref reader));
                    break;

                case Utf8YamlTokenKind.MappingEnd:
                    events.Add("-MAP");
                    break;

                case Utf8YamlTokenKind.SequenceStart:
                    events.Add("+SEQ" + (reader.IsFlow ? " []" : "") + FormatProperties(ref reader));
                    break;

                case Utf8YamlTokenKind.SequenceEnd:
                    events.Add("-SEQ");
                    break;

                case Utf8YamlTokenKind.Alias:
                    events.Add("=ALI *" + Encoding.UTF8.GetString(reader.AnchorName));
                    break;

                case Utf8YamlTokenKind.Scalar:
                    events.Add("=VAL" + FormatProperties(ref reader) + " " + YamlTestSuite.ScalarIndicator(reader.ScalarStyle) + YamlTestSuite.EscapeEventValue(reader.GetString()));
                    break;
            }
        }

        static long Absolute(ref Utf8YamlReader reader, int position) => position < 0 ? -1 : reader.BufferOffset + position;

        static string FormatProperties(ref Utf8YamlReader reader)
        {
            var result = "";
            if (reader.AnchorStart >= 0)
            {
                result += " &" + Encoding.UTF8.GetString(reader.AnchorName);
            }

            if (reader.TagStart >= 0)
            {
                result += " <" + reader.GetTag() + ">";
            }

            return result;
        }
    }

    /// <summary>
    /// Checks that the tokens, indicators and properties do not overlap, that the gaps between them hold only whitespace,
    /// line breaks and comments, and that concatenating them reproduces the input.
    /// </summary>
    /// <returns><see langword="null"/> when the check passes, else the reason.</returns>
    internal static string? CheckTokenCoverage(byte[] utf8)
    {
        var spans = new List<(int Start, int Length, string Kind)>();
        var reader = new Utf8YamlReader(utf8);
        while (reader.Read())
        {
            if (reader.TokenLength > 0)
            {
                spans.Add((reader.TokenStart, reader.TokenLength, reader.TokenKind.ToString()));
            }

            if (reader.IndicatorOffset >= 0)
            {
                if (utf8[reader.IndicatorOffset] is not ((byte)'-' or (byte)'?' or (byte)':' or (byte)','))
                    return FormattableString.Invariant($"The indicator at {reader.IndicatorOffset} is '{(char)utf8[reader.IndicatorOffset]}'");

                spans.Add((reader.IndicatorOffset, 1, "Indicator"));
            }

            if (reader.ExplicitKeyIndicatorOffset >= 0)
            {
                if (utf8[reader.ExplicitKeyIndicatorOffset] != '?')
                    return FormattableString.Invariant($"The explicit key indicator at {reader.ExplicitKeyIndicatorOffset} is '{(char)utf8[reader.ExplicitKeyIndicatorOffset]}'");

                spans.Add((reader.ExplicitKeyIndicatorOffset, 1, "ExplicitKeyIndicator"));
            }

            if (reader.AnchorStart >= 0)
            {
                spans.Add((reader.AnchorStart, reader.AnchorLength, "Anchor"));
            }

            if (reader.TagStart >= 0)
            {
                spans.Add((reader.TagStart, reader.TagLength, "Tag"));
            }
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var output = new List<byte>(utf8.Length);
        var position = 0;
        foreach (var (start, length, kind) in spans)
        {
            if (start < position)
                return FormattableString.Invariant($"The {kind} at {start} overlaps the previous token, which ends at {position}");

            var error = CheckTrivia(utf8, position, start);
            if (error is not null)
                return error;

            output.AddRange(utf8.AsSpan(position, start - position));
            output.AddRange(utf8.AsSpan(start, length));
            position = start + length;
        }

        var tailError = CheckTrivia(utf8, position, utf8.Length);
        if (tailError is not null)
            return tailError;

        output.AddRange(utf8.AsSpan(position));
        return output.SequenceEqual(utf8) ? null : "The concatenation differs from the input";

        static string? CheckTrivia(byte[] utf8, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                switch (utf8[i])
                {
                    case (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n':
                        break;

                    case (byte)'#':
                        while (i + 1 < end && utf8[i + 1] is not ((byte)'\r' or (byte)'\n'))
                        {
                            i++;
                        }

                        break;

                    default:
                        return FormattableString.Invariant($"The gap {start}..{end} contains '{(char)utf8[i]}' at {i}: {Encoding.UTF8.GetString(utf8.AsSpan(start, end - start))}");
                }
            }

            return null;
        }
    }
}
