namespace Meziantou.Framework.Language.Ini.Tests;

public sealed class IniSyntaxTreeTests
{
    public static TheoryData<string> RoundTripSamples => new()
    {
        "name=value",
        """
; leading comment
[database]
server=localhost
port: 5432

# another comment
[features]
enabled=true
empty=
""",
        """
[invalid
key without separator
]=value
""",
    };

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_Save_RoundTripsSamples(string text)
    {
        var tree = IniSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_BuildsIniTree()
    {
        const string Text = """
[database]
server=localhost
port: 5432
""";

        var tree = IniSyntaxTree.ParseText(Text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Collection(
            tree.GetRoot().Entries,
            entry =>
            {
                var section = Assert.IsType<IniSectionSyntax>(entry);
                Assert.Equal("database", section.Name);
            },
            entry =>
            {
                var property = Assert.IsType<IniPropertySyntax>(entry);
                Assert.Equal("server", property.Key);
                Assert.Equal("localhost", property.Value);
                Assert.Equal(SyntaxKind.EqualsToken, property.SeparatorToken.Kind());
            },
            entry =>
            {
                var property = Assert.IsType<IniPropertySyntax>(entry);
                Assert.Equal("port", property.Key);
                Assert.Equal("5432", property.Value);
                Assert.Equal(SyntaxKind.ColonToken, property.SeparatorToken.Kind());
                Assert.Equal(" ", property.ValueToken.LeadingTrivia.ToFullString());
            });
    }

    [Fact]
    public void ParseText_CommentsAreTriviaWithSourceLocations()
    {
        const string Text = """
; comment
[section]
key=value
""";

        var tree = IniSyntaxTree.ParseText(Text);
        var comment = Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CommentTrivia);
        var section = Assert.IsType<IniSectionSyntax>(tree.GetRoot().Entries[0]);

        Assert.Equal("; comment", comment.ToString());
        Assert.Equal(Text.IndexOf(';', StringComparison.Ordinal), comment.Span.Start);
        Assert.Equal(Text.IndexOf('[', StringComparison.Ordinal), section.Span.Start);
    }

    [Fact]
    public void ParseText_InvalidIni_DoesNotThrowAndKeepsSkippedText()
    {
        const string Text = """
[missing
key without separator
]=value
[section] trailing text
""";

        var exception = Record.Exception(() => IniSyntaxTree.ParseText(Text));
        var tree = IniSyntaxTree.ParseText(Text);

        Assert.Null(exception);
        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.True(tree.GetRoot().ContainsSkippedText);
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = IniSyntaxTree.ParseText("a=1\na=1");
        var properties = tree.GetRoot().Entries.OfType<IniPropertySyntax>().ToArray();
        IniDocumentSyntax updated = tree.GetRoot().ReplaceNode(properties[1], SyntaxFactory.IniProperty("a", "2"));

        Assert.Equal("a=1\na=2\n", updated.ToFullString());
    }

    [Fact]
    public void Rewriter_CanUpdatePropertyValues()
    {
        var tree = IniSyntaxTree.ParseText("name=old");
        var rewriter = new RenameValueRewriter();

        var updated = Assert.IsType<IniDocumentSyntax>(rewriter.Visit(tree.GetRoot()));

        Assert.Equal("name=new", updated.ToFullString());
    }

    [Theory]
    [InlineData("my key=1", "my key", "1")]
    [InlineData("Name[fr]=Bonjour", "Name[fr]", "Bonjour")]
    [InlineData("  key  =  value  ", "key", "value")]
    [InlineData("key = value ; comment", "key", "value")]
    [InlineData("password=abc#123", "password", "abc#123")]
    [InlineData("url=http://host/#top # comment", "url", "http://host/#top")]
    [InlineData("k=;x", "k", ";x")]
    [InlineData("k= ;x", "k", "")]
    [InlineData("k=\"a;b\" ; comment", "k", "a;b")]
    [InlineData("k='say \"hi\"'", "k", "say \"hi\"")]
    [InlineData("k=\"a\" \"b\"", "k", "\"a\" \"b\"")]
    [InlineData("k=\"unclosed", "k", "\"unclosed")]
    [InlineData("a:b=c", "a", "b=c")]
    [InlineData("\fkey\u00A0=\u00A0value\u00A0", "key", "value")]
    public void ParseText_ReadsKeyAndValue(string text, string expectedKey, string expectedValue)
    {
        var tree = IniSyntaxTree.ParseText(text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
        var property = Assert.IsType<IniPropertySyntax>(Assert.Single(tree.GetRoot().Entries));
        Assert.Equal(expectedKey, property.Key);
        Assert.Equal(expectedValue, property.Value);
    }

    [Theory]
    [InlineData(IniInlineCommentMode.None, "a#b ; c")]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "a#b")]
    [InlineData(IniInlineCommentMode.Anywhere, "a")]
    public void ParseText_InlineCommentMode(IniInlineCommentMode mode, string expectedValue)
    {
        var tree = IniSyntaxTree.ParseText("k=a#b ; c", new IniParseOptions { InlineComments = mode });

        var property = Assert.IsType<IniPropertySyntax>(Assert.Single(tree.GetRoot().Entries));
        Assert.Equal(expectedValue, property.Value);
        Assert.Equal(mode, tree.Options.InlineComments);
    }

    [Fact]
    public void ParseText_CommentAfterSectionHeaderIsAlwaysAComment()
    {
        var tree = IniSyntaxTree.ParseText("[s];c", new IniParseOptions { InlineComments = IniInlineCommentMode.None });

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(";c", Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CommentTrivia).ToString());
    }

    [Theory]
    [InlineData("[my section]", "my section")]
    [InlineData("[remote \"origin\"]", "remote \"origin\"")]
    [InlineData("[ spaced ] ; comment", "spaced")]
    [InlineData("[a:b=c]", "a:b=c")]
    [InlineData("\uFEFF[section]", "section")]
    public void ParseText_ReadsSectionName(string text, string expectedName)
    {
        var tree = IniSyntaxTree.ParseText(text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expectedName, Assert.IsType<IniSectionSyntax>(Assert.Single(tree.GetRoot().Entries)).Name);
    }

    [Fact]
    public void ParseText_CommentsBelongToTheEntryBelowThem()
    {
        var tree = IniSyntaxTree.ParseText("a=1 ; about a\n\n; about db\n[db] ; inline\n; about x\nx=1\n");
        var entries = tree.GetRoot().Entries;

        Assert.Equal(" ; about a\n", entries[0].GetTrailingTrivia().ToFullString());
        Assert.Equal("\n; about db\n", entries[1].GetLeadingTrivia().ToFullString());
        Assert.Equal(" ; inline\n", entries[1].GetTrailingTrivia().ToFullString());
        Assert.Equal("; about x\n", entries[2].GetLeadingTrivia().ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepsTheCommentOfTheNextEntry()
    {
        var root = IniSyntaxTree.ParseText("a=1\n; about b\nb=2\n").GetRoot();

        var updated = root.RemoveNode(root.Entries[0], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("; about b\nb=2\n", updated!.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithFactoryProperty_KeepsTheLinesApart()
    {
        var root = IniSyntaxTree.ParseText("a=1\n; about b\nb=2\n").GetRoot();

        var updated = root.ReplaceNode(root.Entries[0], SyntaxFactory.IniProperty("a", "9"));

        Assert.Equal("a=9\n; about b\nb=2\n", updated.ToFullString());
    }

    [Theory]
    [InlineData("key\n=value", 0, 3)]
    [InlineData("[\nname]", 0, 1)]
    [InlineData("[a\n\n\nb=1", 0, 2)]
    [InlineData("key\n\n\nb=1", 0, 3)]
    [InlineData("[s]\nkey ; comment\nb=1", 1, 3)]
    public void ParseText_ReportsTheFirstErrorWhereTheLineEnds(string text, int expectedLine, int expectedCharacter)
    {
        var tree = IniSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        var diagnostic = tree.GetDiagnostics()[0];
        Assert.Equal(new LinePosition(expectedLine, expectedCharacter), diagnostic.Location.GetLineSpan().Start);
    }

    [Fact]
    public void ParseText_SeparatorOnTheNextLineIsNotPartOfTheKey()
    {
        var tree = IniSyntaxTree.ParseText("key\n=value");

        Assert.Collection(
            tree.GetRoot().Entries,
            entry => Assert.True(Assert.IsType<IniPropertySyntax>(entry).SeparatorToken.IsMissing),
            entry => Assert.True(Assert.IsType<IniPropertySyntax>(entry).KeyToken.IsMissing));
        Assert.Equal(["INI0004", "INI0003"], tree.GetDiagnostics().Select(diagnostic => diagnostic.Id));
    }

    [Fact]
    public void ParseText_TextAfterSectionHeader_IsOneError()
    {
        const string Text = "[a] b=1 c\n[b]]";
        var tree = IniSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Collection(
            tree.GetRoot().Entries,
            entry => Assert.IsType<IniSectionSyntax>(entry),
            entry => Assert.Equal("b=1 c", Assert.IsType<IniSkippedTextSyntax>(entry).Tokens.Single().Text),
            entry => Assert.IsType<IniSectionSyntax>(entry),
            entry => Assert.Equal("]", Assert.IsType<IniSkippedTextSyntax>(entry).Tokens.Single().Text));
        Assert.Collection(
            tree.GetDiagnostics(),
            diagnostic => Assert.Equal("Expected the end of the line after a section header, found 'b=1 c'.", diagnostic.Message),
            diagnostic => Assert.Equal("Expected the end of the line after a section header, found ']'.", diagnostic.Message));
    }

    [Fact]
    public void ParseText_KeyWithoutValue()
    {
        const string Text = "[mysqld]\nskip-networking ; comment\nport=3306\n";

        var strict = IniSyntaxTree.ParseText(Text);
        var lenient = IniSyntaxTree.ParseText(Text, new IniParseOptions { AllowKeysWithoutValue = true });

        Assert.Equal("INI0004", Assert.Single(strict.GetDiagnostics()).Id);
        Assert.Empty(lenient.GetDiagnostics());
        Assert.Equal(Text, lenient.GetRoot().ToFullString());
        var property = lenient.GetRoot().GetProperties("mysqld", "skip-networking").Single();
        Assert.True(property.SeparatorToken.IsMissing);
        Assert.Equal("", property.Value);
        Assert.Equal(" ; comment\n", property.GetTrailingTrivia().ToFullString());
        Assert.Equal("skip-networking=on ; comment\n", property.WithValue("on").ToFullString());
    }

    [Fact]
    public void ParseText_MultilineValues()
    {
        const string Text = "k=line1 ; note\n  line2\n\n  other=1\n[s]\n  a=\n    b\n  c=1\n";

        var tree = IniSyntaxTree.ParseText(Text, new IniParseOptions { AllowMultilineValues = true });

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        var root = tree.GetRoot();
        Assert.Equal("line1\nline2\n\nother=1", root.GetValue(section: null, "k"));
        Assert.Null(root.GetValue(section: null, "other"));
        Assert.Equal("\nb", root.GetValue("s", "a"));
        Assert.Equal("1", root.GetValue("s", "c"));
    }

    [Fact]
    public void ParseText_IndentedLineIsNotAContinuationByDefault()
    {
        var tree = IniSyntaxTree.ParseText("k=line1\n  line2");

        Assert.Equal("line1", tree.GetRoot().GetValue(section: null, "k"));
        Assert.Equal("INI0004", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void ParseText_ReportsDuplicatesWhenAsked()
    {
        const string Text = "g=1\ng=2\n[a]\nk=1\n[A]\nK=2\n[b]\nk=3\n";

        Assert.Empty(IniSyntaxTree.ParseText(Text).GetDiagnostics());
        Assert.Collection(
            IniSyntaxTree.ParseText(Text, new IniParseOptions { ReportDuplicates = true }).GetDiagnostics(),
            diagnostic => Assert.Equal(("INI0007", DiagnosticSeverity.Warning, "The key 'g' is already defined in the global section."), (diagnostic.Id, diagnostic.Severity, diagnostic.Message)),
            diagnostic => Assert.Equal(("INI0006", DiagnosticSeverity.Warning, "The section 'A' is already defined."), (diagnostic.Id, diagnostic.Severity, diagnostic.Message)),
            diagnostic => Assert.Equal(("INI0007", DiagnosticSeverity.Warning, "The key 'K' is already defined in the section 'A'."), (diagnostic.Id, diagnostic.Severity, diagnostic.Message)));
        Assert.DoesNotContain(IniSyntaxTree.ParseText(Text, new IniParseOptions { ReportDuplicates = true, NameComparer = StringComparer.Ordinal }).GetDiagnostics(), diagnostic => diagnostic.Id == "INI0006");
    }

    [Fact]
    public void ParseText_ByteOrderMarkBeforeComment()
    {
        var tree = IniSyntaxTree.ParseText("\uFEFF; comment\na=1");

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal("1", tree.GetRoot().GetValue(section: null, "a"));
    }

    [Fact]
    public void WithChangedText_KeepsTheOptions()
    {
        var options = new IniParseOptions { AllowKeysWithoutValue = true };
        var tree = IniSyntaxTree.ParseText("a=1", options);

        var changed = tree.WithChangedText(SourceText.From("flag"));

        Assert.Same(options, changed.Options);
        Assert.Empty(changed.GetDiagnostics());
    }

    [Fact]
    public void Lookup_GroupsPropertiesBySection()
    {
        var root = IniSyntaxTree.ParseText("g=0\n[a]\nk=1\nj=2\n[b]\nk=3\n[A]\nk=4\n").GetRoot();

        Assert.Equal("0", root.GetValue(section: null, "G"));
        Assert.Equal("4", root.GetValue("a", "k"));
        Assert.Equal("3", root.GetValue("b", "k"));
        Assert.Null(root.GetValue("a", "missing"));
        Assert.Equal("4", root.GetValue("A", "k", StringComparer.Ordinal));
        Assert.Null(root.GetValue("a", "K", StringComparer.Ordinal));
        Assert.Equal(["1", "4"], root.GetProperties("a", "k").Select(property => property.Value));
        Assert.Equal(["k", "j"], root.Sections.First().Properties.Select(property => property.Key));
        Assert.Equal("g", Assert.Single(root.GlobalProperties).Key);
        Assert.HasCount(2, root.GetSections("A"));
    }

    [Theory]
    [InlineData("http://host/#top")]
    [InlineData("abc#123")]
    [InlineData("a ; b")]
    [InlineData(";starts")]
    [InlineData(" padded ")]
    [InlineData("\"quoted\"")]
    [InlineData("say \"hi\"")]
    [InlineData("it's")]
    [InlineData("")]
    public void Value_ReadsBackAsTheSameValueWhateverTheCommentMode(string value)
    {
        var root = IniSyntaxTree.ParseText("url=old\nother=1").GetRoot();
        var property = (IniPropertySyntax)root.Entries[0];

        var updated = root.ReplaceNode(property, property.WithValueToken(SyntaxFactory.Value(value).WithTriviaFrom(property.ValueToken)));

        foreach (var mode in Enum.GetValues<IniInlineCommentMode>())
        {
            var reparsed = IniSyntaxTree.ParseText(updated.ToFullString(), new IniParseOptions { InlineComments = mode });
            Assert.Empty(reparsed.GetDiagnostics());
            Assert.Equal(value, reparsed.GetRoot().GetValue(section: null, "url"));
            Assert.Equal("1", reparsed.GetRoot().GetValue(section: null, "other"));
        }
    }

    [Theory]
    [InlineData("x\n[admin]\nenabled=true")]
    [InlineData("both \" and ' ;")]
    public void Value_RejectsWhatCannotBeWritten(string value)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Value(value));
    }

    [Theory]
    [InlineData("a=b")]
    [InlineData("a:b")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("")]
    [InlineData("[x")]
    [InlineData(";x")]
    [InlineData("a#b")]
    [InlineData("a\nb")]
    public void Key_RejectsWhatDoesNotReadBackAsTheKey(string text)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Key(text));
    }

    [Theory]
    [InlineData("a]")]
    [InlineData("a ;b")]
    [InlineData("")]
    [InlineData("a\nb")]
    public void SectionName_RejectsWhatDoesNotReadBackAsTheName(string text)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.IniSection(text));
    }

    [Theory]
    [InlineData("a;b")]
    [InlineData(" a")]
    [InlineData("a\nb")]
    public void RawValue_RejectsWhatDoesNotReadBackAsOneValue(string text)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.RawValue(text));
    }

    [Theory]
    [InlineData(SyntaxKind.CommentTrivia, "; a\n[x]")]
    [InlineData(SyntaxKind.CommentTrivia, "a")]
    [InlineData(SyntaxKind.WhitespaceTrivia, "a")]
    [InlineData(SyntaxKind.WhitespaceTrivia, "")]
    [InlineData(SyntaxKind.EndOfLineTrivia, " ")]
    [InlineData(SyntaxKind.KeyToken, "a")]
    public void Trivia_RejectsWhatIsNotThatTrivia(SyntaxKind kind, string text)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Trivia(kind, text));
    }

    [Fact]
    public void SyntaxFactory_AcceptsKeysAndSectionNamesWithSpaces()
    {
        Assert.Equal("my key=v\n", SyntaxFactory.IniProperty("my key", "v").ToFullString());
        Assert.Equal("[remote \"origin\"]\n", SyntaxFactory.IniSection("remote \"origin\"").ToFullString());
        Assert.Equal("\"a;b\"", SyntaxFactory.Value("a;b").Text);
        Assert.Equal("a;b", SyntaxFactory.Value("a;b").ValueText);
    }

    [Fact]
    public void IniDocument_EndsEveryLine()
    {
        var document = SyntaxFactory.IniDocument(
            SyntaxFactory.IniSection("s"),
            SyntaxFactory.IniProperty(SyntaxFactory.Key("a"), SyntaxFactory.Token(SyntaxKind.EqualsToken), SyntaxFactory.Value("1")),
            SyntaxFactory.IniProperty("b", "2"));

        Assert.Equal("[s]\na=1\nb=2\n", document.ToFullString());
    }

    [Theory]
    [InlineData("a=1", "a=1\nb=2\n")]
    [InlineData("a=1\r\nc=3", "a=1\r\nc=3\r\nb=2\r\n")]
    [InlineData("", "b=2\n")]
    public void AddEntries_StartsANewLine(string text, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        var updated = root.AddEntries(SyntaxFactory.IniProperty("b", "2"));

        Assert.Equal(expected, updated.ToFullString());
        Assert.Equal("2", IniSyntaxTree.ParseText(updated.ToFullString()).GetRoot().GetValue(section: null, "b"));
    }

    [Fact]
    public void AddEntries_AfterKeyWithoutValue_DoesNotAddABlankLine()
    {
        var root = IniSyntaxTree.ParseText("flag\n", new IniParseOptions { AllowKeysWithoutValue = true }).GetRoot();

        Assert.Equal("flag\nb=2\n", root.AddEntries(SyntaxFactory.IniProperty("b", "2")).ToFullString());
    }

    [Theory]
    [MemberData(nameof(WithValueSamples))]
    public void WithValue_ReadsBackAsTheSameValueWithTheDocumentOptions(string value, IniInlineCommentMode mode)
    {
        var options = new IniParseOptions { InlineComments = mode };
        var root = IniSyntaxTree.ParseText("url=old\nother=1", options).GetRoot();
        var property = (IniPropertySyntax)root.Entries[0];

        var updated = root.ReplaceNode(property, property.WithValue(value));

        var reparsed = IniSyntaxTree.ParseText(updated.ToFullString(), options);
        Assert.Empty(reparsed.GetDiagnostics());
        Assert.Equal(value, reparsed.GetRoot().GetValue(section: null, "url"));
        Assert.Equal("1", reparsed.GetRoot().GetValue(section: null, "other"));
    }

    public static TheoryData<string, IniInlineCommentMode> WithValueSamples()
    {
        var data = new TheoryData<string, IniInlineCommentMode>();
        foreach (var mode in Enum.GetValues<IniInlineCommentMode>())
        {
            foreach (var value in new[] { "http://host/#top", "abc#123", "a ; b", ";starts", " padded ", "\"quoted\"", "say \"hi\"", "it's", "" })
            {
                data.Add(value, mode);
            }
        }

        return data;
    }

    [Theory]
    [InlineData(IniInlineCommentMode.None, "a ; b", "k=a ; b\n")]
    [InlineData(IniInlineCommentMode.None, ";starts", "k=;starts\n")]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "abc#1", "k=abc#1\n")]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "a ;b", "k=\"a ;b\"\n")]
    [InlineData(IniInlineCommentMode.Anywhere, "abc#1", "k=\"abc#1\"\n")]
    public void WithValue_QuotesOnlyWhatTheDocumentOptionsRequire(IniInlineCommentMode mode, string value, string expected)
    {
        var root = IniSyntaxTree.ParseText("k=old\n", new IniParseOptions { InlineComments = mode }).GetRoot();
        var property = root.GlobalProperties[0];

        Assert.Equal(expected, root.ReplaceNode(property, property.WithValue(value)).ToFullString());
    }

    [Fact]
    public void WithValue_KeepsTheDocumentOptionsAcrossEdits()
    {
        var options = new IniParseOptions { InlineComments = IniInlineCommentMode.None, AllowQuotedValues = false };
        var root = IniSyntaxTree.ParseText("a=1\nb=2\n", options).GetRoot();
        root = root.ReplaceNode(root.GlobalProperties[0], root.GlobalProperties[0].WithValue("x ; y"));

        var updated = root.ReplaceNode(root.GlobalProperties[1], root.GlobalProperties[1].WithValue("p#q"));

        Assert.Same(options, updated.Options);
        Assert.Equal("a=x ; y\nb=p#q\n", updated.ToFullString());
    }

    [Theory]
    [InlineData(IniInlineCommentMode.None, "k=\"x\"\n", "\"x\"")]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "k=\"a ;b\"\n", "\"a")]
    [InlineData(IniInlineCommentMode.Anywhere, "k='a#b'\n", "'a")]
    public void ParseText_QuotesAreOrdinaryCharactersWhenQuotedValuesAreNotAllowed(IniInlineCommentMode mode, string text, string expected)
    {
        var root = IniSyntaxTree.ParseText(text, new IniParseOptions { InlineComments = mode, AllowQuotedValues = false }).GetRoot();

        Assert.Equal(expected, root.GetValue(section: null, "k"));
        Assert.Equal(text, root.ToFullString());
    }

    [Theory]
    [InlineData(IniInlineCommentMode.None, "\"x\"", "k=\"x\"\n")]
    [InlineData(IniInlineCommentMode.None, " padded", null)]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "a ;b", null)]
    [InlineData(IniInlineCommentMode.AfterWhitespace, "a;b", "k=a;b\n")]
    public void WithValue_NeverAddsQuotesWhenQuotedValuesAreNotAllowed(IniInlineCommentMode mode, string value, string? expected)
    {
        var root = IniSyntaxTree.ParseText("k=old\n", new IniParseOptions { InlineComments = mode, AllowQuotedValues = false }).GetRoot();
        var property = root.GlobalProperties[0];

        if (expected is null)
        {
            Assert.Throws<ArgumentException>(() => property.WithValue(value));
            return;
        }

        Assert.Equal(expected, root.ReplaceNode(property, property.WithValue(value)).ToFullString());
    }

    [Fact]
    public void SyntaxFactory_WritesValuesForTheGivenOptions()
    {
        var python = new IniParseOptions { InlineComments = IniInlineCommentMode.None, AllowQuotedValues = false, AllowMultilineValues = true };

        Assert.Equal("abc#1", SyntaxFactory.Value("abc#1", python).Text);
        Assert.Equal("\"abc#1\"", SyntaxFactory.Value("abc#1", new IniParseOptions { InlineComments = IniInlineCommentMode.Anywhere }).Text);
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Value(" x", python));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Value("a\nb", python));
        Assert.Equal("k=a;b\n    c\n", SyntaxFactory.IniProperty("k", "a;b\nc", python).ToFullString());
        Assert.Throws<ArgumentException>(() => SyntaxFactory.IniProperty("k", "a\nb", IniParseOptions.Default));
    }

    [Theory]
    [MemberData(nameof(ConfigParserMultilineSamples))]
    public void ParseText_MultilineValuesReadAsConfigParserReadsThem(string text, IniInlineCommentMode mode, string section, string key, string expected)
    {
        var tree = IniSyntaxTree.ParseText(text, new IniParseOptions { AllowMultilineValues = true, AllowQuotedValues = false, InlineComments = mode });

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, tree.GetRoot().GetValue(section, key));
    }

    // Generated with Python's configparser.ConfigParser(inline_comment_prefixes=None or (';', '#'), interpolation=None).
    public static TheoryData<string, IniInlineCommentMode, string, string, string> ConfigParserMultilineSamples => new()
    {
        { "[s]\nk=line1 ; note\n  line2\n\n  other=1\n[t]\n  a=\n    b\n  c=1\n", IniInlineCommentMode.None, "s", "k", "line1 ; note\nline2\n\nother=1" },
        { "[s]\nk=line1 ; note\n  line2\n\n  other=1\n[t]\n  a=\n    b\n  c=1\n", IniInlineCommentMode.None, "t", "a", "\nb" },
        { "[s]\nk=line1 ; note\n  line2\n\n  other=1\n[t]\n  a=\n    b\n  c=1\n", IniInlineCommentMode.None, "t", "c", "1" },
        { "[s]\nk=line1 ; note\n  line2\n\n  other=1\n[t]\n  a=\n    b\n  c=1\n", IniInlineCommentMode.AfterWhitespace, "s", "k", "line1\nline2\n\nother=1" },
        { "[s]\nkey=a\n  # c\n  b\n", IniInlineCommentMode.None, "s", "key", "a\nb" },
        { "[s]\nkey=a\n  # c\n  b\n", IniInlineCommentMode.AfterWhitespace, "s", "key", "a\nb" },
        { "[s]\nkey=a\n\n  b\n", IniInlineCommentMode.None, "s", "key", "a\n\nb" },
        { "[s]\nkey=a\n# c\n  b\n", IniInlineCommentMode.None, "s", "key", "a\nb" },
        { "[s]\nkey=a\n\n\n  b\n  ; c\n\n  d\n\nx=1\n", IniInlineCommentMode.None, "s", "key", "a\n\n\nb\n\nd" },
        { "[s]\nkey=a\n\n\n  b\n  ; c\n\n  d\n\nx=1\n", IniInlineCommentMode.None, "s", "x", "1" },
        { "[s]\nkey=a\n  b ; c\n", IniInlineCommentMode.None, "s", "key", "a\nb ; c" },
        { "[s]\nkey=a\n  b ; c\n", IniInlineCommentMode.AfterWhitespace, "s", "key", "a\nb" },
        { "[s]\nkey=a\n  \"q\"\n", IniInlineCommentMode.None, "s", "key", "a\n\"q\"" },
        { "[s]\nkey=a\n\n[t]\nx=1\n", IniInlineCommentMode.None, "s", "key", "a" },
        { "[s]\nkey=a\n\n[t]\nx=1\n", IniInlineCommentMode.None, "t", "x", "1" },
        { "[s]\n  key=a\n  b=2\n    c\n", IniInlineCommentMode.None, "s", "key", "a" },
        { "[s]\n  key=a\n  b=2\n    c\n", IniInlineCommentMode.None, "s", "b", "2\nc" },
        { "[s]\nkey=\n  a\n  b\n", IniInlineCommentMode.None, "s", "key", "\na\nb" },
        { "[s]\nkey=a\n\t\n  b\n", IniInlineCommentMode.None, "s", "key", "a\n\nb" },
    };

    [Fact]
    public void ParseText_MultilineValues_KeepsCommentsAsTrivia()
    {
        var root = IniSyntaxTree.ParseText("k=a ; c\n  # between\n  b\n", new IniParseOptions { AllowMultilineValues = true }).GetRoot();
        var property = root.GlobalProperties[0];

        Assert.Equal("a", property.ValueToken.Text);
        Assert.Equal("b", Assert.Single(property.ContinuationTokens).Text);
        Assert.Equal(["; c", "# between"], root.DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.CommentTrivia)).Select(trivia => trivia.ToFullString()));
    }

    [Fact]
    public void ParseText_MultilineValues_ReadsEveryLineLikeAOneLineValue()
    {
        var root = IniSyntaxTree.ParseText("k=\"a b\"\n  \"c ;d\"\n", new IniParseOptions { AllowMultilineValues = true }).GetRoot();

        Assert.Equal("a b\nc ;d", root.GetValue(section: null, "k"));
    }

    [Theory]
    [InlineData("[s]\nk=old ; note\nx=1\n", "a\n\nb", "[s]\nk=a ; note\n\n    b\nx=1\n")]
    [InlineData("k=a\n\tb\n", "x\ny", "k=x\n\ty\n")]
    [InlineData("k=a\n  b\n  c\nx=1", "z", "k=z\nx=1")]
    [InlineData("k=a", "x\ny", "k=x\n    y")]
    [InlineData("  k=a\r\n", "x\ny", "  k=x\r\n      y\r\n")]
    public void WithValue_WritesMultilineValues(string text, string value, string expected)
    {
        var options = new IniParseOptions { AllowMultilineValues = true };
        var root = IniSyntaxTree.ParseText(text, options).GetRoot();
        var property = root.GetProperties(root.Sections.Any() ? "s" : null, "k").Single();

        var updated = root.ReplaceNode(property, property.WithValue(value));

        Assert.Equal(expected, updated.ToFullString());
        Assert.Equal(value, IniSyntaxTree.ParseText(updated.ToFullString(), options).GetRoot().GetValue(root.Sections.Any() ? "s" : null, "k"));
    }

    [Theory]
    [InlineData("a\nb", false)]
    [InlineData("a\n", true)]
    public void WithValue_RejectsLinesTheDocumentCannotReadBack(string value, bool allowMultilineValues)
    {
        var root = IniSyntaxTree.ParseText("k=old\n", new IniParseOptions { AllowMultilineValues = allowMultilineValues }).GetRoot();

        Assert.Throws<ArgumentException>(() => root.GlobalProperties[0].WithValue(value));
    }

    [Theory]
    [InlineData("[s]\na=1", "[s]\na=1\nb=2\n")]
    [InlineData("[s]\na=1 ; note", "[s]\na=1 ; note\nb=2\n")]
    [InlineData("a=1\r\nb=0", "a=1\r\nb=0\r\nb=2\n")]
    public void InsertNodesAfter_EndsTheLineOfTheLastEntry(string text, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        var updated = root.InsertNodesAfter(root.Entries[^1], [SyntaxFactory.IniProperty("b", "2")]);

        Assert.Equal(expected, updated.ToFullString());
        Assert.Equal("2", IniSyntaxTree.ParseText(updated.ToFullString()).GetRoot().GetProperties(root.Sections.Any() ? "s" : null, "b").Last().Value);
    }

    [Fact]
    public void InsertNodesAfter_EndsTheLineOfASectionHeader()
    {
        var root = IniSyntaxTree.ParseText("[s]").GetRoot();

        var updated = root.InsertNodesAfter(root.Entries[0], [SyntaxFactory.IniProperty("b", "2")]);

        Assert.Equal("[s]\nb=2\n", updated.ToFullString());
        Assert.Equal("2", IniSyntaxTree.ParseText(updated.ToFullString()).GetRoot().GetValue("s", "b"));
    }

    [Fact]
    public void ReplaceNode_EndsTheLineOfAnEntryWithoutOne()
    {
        var root = IniSyntaxTree.ParseText("a=1\nb=2\n").GetRoot();
        var replacement = SyntaxFactory.IniProperty(SyntaxFactory.Key("a"), SyntaxFactory.Token(SyntaxKind.EqualsToken), SyntaxFactory.Value("9"));

        Assert.Equal("a=9\nb=2\n", root.ReplaceNode(root.Entries[0], replacement).ToFullString());
    }

    [Fact]
    public void Edit_LeavesSkippedTextOnTheLineOfItsEntry()
    {
        var root = IniSyntaxTree.ParseText("[a] junk\nk=v\n").GetRoot();

        Assert.Equal("[a] junk\nk=x\n", root.SetValue("a", "k", "x").ToFullString());
    }

    [Theory]
    [InlineData("# header\n", "# header\n[db]\na=1\n")]
    [InlineData("# header", "# header\n[db]\na=1\n")]
    [InlineData("# header\n\n  ", "# header\n\n[db]\na=1\n  ")]
    [InlineData("﻿; header\r\n", "﻿; header\r\n[db]\r\na=1\r\n")]
    public void AddEntries_KeepsTheHeaderOfADocumentWithoutEntries(string text, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        Assert.Equal(expected, root.AddEntries(SyntaxFactory.IniSection("db"), SyntaxFactory.IniProperty("a", "1")).ToFullString());
    }

    [Fact]
    public void AddEntries_UsesTheLineBreaksOfTheDocumentForMultilineValues()
    {
        var options = new IniParseOptions { AllowMultilineValues = true };
        var root = IniSyntaxTree.ParseText("a=1\r\n", options).GetRoot();

        Assert.Equal("a=1\r\nb=x\r\n    y\r\n", root.AddEntries(SyntaxFactory.IniProperty("b", "x\ny", options)).ToFullString());
    }

    [Fact]
    public void GetValue_UsesTheNameComparerOfTheDocument()
    {
        var root = IniSyntaxTree.ParseText("[S]\nk=1\n", new IniParseOptions { NameComparer = StringComparer.Ordinal }).GetRoot();

        Assert.Null(root.GetValue("s", "k"));
        Assert.Null(root.GetValue("S", "K"));
        Assert.Equal("1", root.GetValue("S", "k"));
        Assert.Equal("1", root.GetValue("s", "K", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_KeepsTheOptionsOfTheRoot()
    {
        var options = new IniParseOptions { NameComparer = StringComparer.Ordinal };
        var root = IniSyntaxTree.ParseText("[S]\nk=1\n", options).GetRoot();
        var edited = root.SetValue("S", "k", "2");

        Assert.Same(options, IniSyntaxTree.Create(edited).Options);
        Assert.Same(IniParseOptions.Default, IniSyntaxTree.Create(edited, IniParseOptions.Default).GetRoot().Options);
        Assert.Same(IniParseOptions.Default, SyntaxFactory.IniDocument(SyntaxFactory.IniSection("s")).Options);
    }

    [Fact]
    public void Sections_Properties_GroupsTheEntries()
    {
        var root = IniSyntaxTree.ParseText("g=0\n[a] junk\nx=1\ny=2\n[b]\n[a]\nz=3\n").GetRoot();

        Assert.Equal(["g"], root.GlobalProperties.Select(property => property.Key));
        Assert.Equal([["x", "y"], [], ["z"]], root.Sections.Select(section => section.Properties.Select(property => property.Key).ToArray()));
        Assert.Equal("3", root.GetValue("a", "z"));
        Assert.Empty(SyntaxFactory.IniSection("a").Properties);
    }

    [Fact]
    public void Whitespace_AcceptsTheByteOrderMarkTheParserReads()
    {
        var root = IniSyntaxTree.ParseText("﻿  a=1\n").GetRoot();
        var trivia = root.DescendantTrivia().ToArray();

        Assert.Equal("﻿", trivia[0].ToFullString());
        Assert.Equal("  ", trivia[1].ToFullString());
        Assert.Equal(root.ToFullString(), root.ReplaceTrivia(trivia, (original, _) => SyntaxFactory.Trivia(original.Kind(), original.ToFullString())).ToFullString());
    }

    [Theory]
    [InlineData("[s]\na=1\n", "s", "a", "2", "[s]\na=2\n")]
    [InlineData("[s]\na=1\na=3\n", "s", "a", "2", "[s]\na=1\na=2\n")]
    [InlineData("[s]\n  a = 1\n\n# next\n[t]\n", "s", "b", "2", "[s]\n  a = 1\n  b = 2\n\n# next\n[t]\n")]
    [InlineData("[s]\na: 1", "s", "b", "2", "[s]\na: 1\nb: 2\n")]
    [InlineData("[s]\n[t]\n", "s", "b", "2", "[s]\nb=2\n[t]\n")]
    [InlineData("[s]\na=1\n", "t", "b", "2", "[s]\na=1\n\n[t]\nb=2\n")]
    [InlineData("", "t", "b", "2", "[t]\nb=2\n")]
    [InlineData("", null, "b", "2", "b=2\n")]
    [InlineData("; cfg\n\n[s]\na=1\n", null, "k", "v", "; cfg\n\nk=v\n[s]\na=1\n")]
    [InlineData("﻿[s]\n", null, "k", "v", "﻿k=v\n[s]\n")]
    [InlineData("g=1\n[s]\n", null, "k", "v", "g=1\nk=v\n[s]\n")]
    [InlineData("[S]\na=1\n", "s", "A", "2", "[S]\na=2\n")]
    public void SetValue(string text, string? section, string key, string value, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        var updated = root.SetValue(section, key, value);

        Assert.Equal(expected, updated.ToFullString());
        Assert.Equal(value, IniSyntaxTree.ParseText(updated.ToFullString()).GetRoot().GetValue(section, key));
    }

    [Fact]
    public void SetValue_IndentsTheKeyAsMuchAsTheLineAfterIt()
    {
        var options = new IniParseOptions { AllowMultilineValues = true };
        var root = IniSyntaxTree.ParseText("[s]\n  [t]\n", options).GetRoot();

        var updated = root.SetValue("s", "k", "v");

        Assert.Equal("[s]\n  k=v\n  [t]\n", updated.ToFullString());
        Assert.HasCount(2, IniSyntaxTree.ParseText(updated.ToFullString(), options).GetRoot().Sections);
    }

    [Theory]
    [InlineData("[s]\n# about a\na=1\nb=2\na=3 ; last\n", "s", "a", "[s]\nb=2\n")]
    [InlineData("; cfg\n\na=1\n[s]\n", null, "a", "; cfg\n\n[s]\n")]
    [InlineData("; about a\na=1\nb=2\n", null, "a", "b=2\n")]
    [InlineData("a=1\nb=2", null, "b", "a=1\n")]
    public void RemoveProperties(string text, string? section, string key, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        Assert.Equal(expected, root.RemoveProperties(section, key).ToFullString());
    }

    [Theory]
    [InlineData("[a]\nx=1\n\n[b]\ny=2\n\n[c]\nz=3", "b", "[a]\nx=1\n\n[c]\nz=3")]
    [InlineData("; cfg\n\n[a]\nx=1\n\n[b]\ny=2\n", "a", "; cfg\n\n[b]\ny=2\n")]
    [InlineData("; cfg\n\n[a]\nx=1\n", "a", "; cfg\n\n")]
    [InlineData("[a] junk\nx=1\n[b]\n[A]\ny=2\n", "a", "[b]\n")]
    public void RemoveSections(string text, string name, string expected)
    {
        var root = IniSyntaxTree.ParseText(text).GetRoot();

        Assert.Equal(expected, root.RemoveSections(name).ToFullString());
    }

    [Fact]
    public void Remove_ReturnsTheSameDocumentWhenNothingMatches()
    {
        var root = IniSyntaxTree.ParseText("[a]\nx=1\n").GetRoot();

        Assert.Same(root, root.RemoveSections("b"));
        Assert.Same(root, root.RemoveProperties("a", "y"));
    }

    private sealed class RenameValueRewriter : IniSyntaxRewriter
    {
        public override SyntaxNode? VisitIniProperty(IniPropertySyntax node)
        {
            if (node.Key == "name")
                return node.WithValue("new");

            return base.VisitIniProperty(node);
        }
    }
}
