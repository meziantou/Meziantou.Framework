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
        Assert.Equal("line1\nline2", root.GetValue(section: null, "k"));
        Assert.Equal("1", root.GetValue(section: null, "other"));
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
    public void WithValue_ReadsBackAsTheSameValueWhateverTheCommentMode(string value)
    {
        var root = IniSyntaxTree.ParseText("url=old\nother=1").GetRoot();
        var property = (IniPropertySyntax)root.Entries[0];

        var updated = root.ReplaceNode(property, property.WithValue(value));

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
    [InlineData("a=1\r\nc=3", "a=1\r\nc=3\r\nb=2\n")]
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
