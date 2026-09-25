namespace Meziantou.Framework.Language.Toml.Tests;

public sealed class TomlSyntaxTreeTests
{
    public static TheoryData<string> RoundTripSamples => new()
    {
        "title = \"TOML Example\"\n[owner]\nname = \"Tom\"\nports = [8000, 8001]\n# comment\n",
        "[[products]]\nname = 'Hammer'\ndetails = { color = \"gray\", weight = 1 }\n",
        "a = [\n  1, # one\n  2,\n]\r\nb = \"\"\"\nline\\\n  continued\"\"\"\n",
        "a = \"unterminated\nb = {c = 1\n[t\nd = [1, 2\n= 3\n]]\n\"key = 1\n",
        "\uFEFFa = 1 # BOM\n",
        "a = 1\rb = 2\u3000\n",
    };

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_RoundTripsText(string text)
    {
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_BuildsTypedTree()
    {
        const string Text = """
            title = "TOML Example"

            [owner]
            name = 'Tom'
            dob = 1979-05-27T07:32:00-08:00

            [database]
            ports = [ 8000, 8001, 8002 ]
            temp_targets = { cpu = 79.5, case = 72.0 }
            enabled = true
            """;
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Empty(tree.GetDiagnostics());
        var root = tree.GetRoot();
        Assert.Equal("TOML Example", Assert.IsType<TomlStringSyntax>(Assert.Single(root.RootProperties).Value).Value);

        var tables = root.Tables.ToArray();
        Assert.Equal(["owner"], tables[0].Key.Names);
        Assert.Equal(new DateTimeOffset(1979, 5, 27, 7, 32, 0, TimeSpan.FromHours(-8)), Assert.IsType<TomlDateTimeSyntax>(tables[0].Properties[1].Value).Value);

        var database = tables[1].Properties;
        var ports = Assert.IsType<TomlArraySyntax>(database[0].Value);
        Assert.Equal([8000L, 8001L, 8002L], ports.Elements.Select(element => Assert.IsType<TomlIntegerSyntax>(element).Value).ToArray());
        var targets = Assert.IsType<TomlInlineTableSyntax>(database[1].Value);
        Assert.Equal(79.5, Assert.IsType<TomlFloatSyntax>(targets.Properties[0].Value).Value);
        Assert.True(Assert.IsType<TomlBooleanSyntax>(database[2].Value).Value);
    }

    [Theory]
    [InlineData("a = 0xDEAD_BEEF", 0xDEADBEEF)]
    [InlineData("a = 0o755", 493)]
    [InlineData("a = 0b1101", 13)]
    [InlineData("a = -9_223_372_036_854_775_808", long.MinValue)]
    [InlineData("a = +17", 17)]
    public void Integers(string text, long expected)
        => Assert.Equal(expected, Assert.IsType<TomlIntegerSyntax>(ParseSingleValue(text)).Value);

    [Theory]
    [InlineData("a = inf", double.PositiveInfinity)]
    [InlineData("a = -inf", double.NegativeInfinity)]
    [InlineData("a = 6.626e-34", 6.626e-34)]
    [InlineData("a = 224_617.445_991", 224617.445991)]
    [InlineData("a = 1e6", 1e6)]
    public void Floats(string text, double expected)
        => Assert.Equal(expected, Assert.IsType<TomlFloatSyntax>(ParseSingleValue(text)).Value);

    [Fact]
    public void Floats_NaN() => Assert.True(double.IsNaN(Assert.IsType<TomlFloatSyntax>(ParseSingleValue("a = -nan")).Value));

    [Fact]
    public void DateTimes()
    {
        Assert.Equal(new DateTime(1979, 5, 27, 7, 32, 0, DateTimeKind.Unspecified).AddTicks(9_999_999), ParseSingleValue<TomlDateTimeSyntax>("a = 1979-05-27 07:32:00.999999999").Value);
        Assert.Equal(new DateOnly(2024, 2, 29), ParseSingleValue<TomlDateTimeSyntax>("a = 2024-02-29").Value);
        Assert.Equal(new TimeOnly(7, 32), ParseSingleValue<TomlDateTimeSyntax>("a = 07:32").Value);
        Assert.Equal(SyntaxKind.TomlLocalTime, ParseSingleValue("a = 07:32:00").Kind());

        // An offset past the ±14:00 .NET supports keeps its instant.
        Assert.Equal(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), ParseSingleValue<TomlDateTimeSyntax>("a = 2000-01-01T20:00:00+20:00").Value);
    }

    [Fact]
    public void Strings_ResolveEscapesAndLineEndingBackslashes()
    {
        Assert.Equal("tab\tquote\"\u00E9\U0001F600", ParseSingleValue<TomlStringSyntax>("a = \"tab\\tquote\\\"\\u00E9\\U0001F600\"").Value);
        Assert.Equal("The quick brown fox.", ParseSingleValue<TomlStringSyntax>("a = \"\"\"\nThe quick \\\n\n    brown fox.\"\"\"").Value);
        Assert.Equal("He said \"hi\n", ParseSingleValue<TomlStringSyntax>("a = \"\"\"\nHe said \"hi\n\"\"\"").Value);
        Assert.Equal("it's\r\n", ParseSingleValue<TomlStringSyntax>("a = '''\r\nit's\r\n'''").Value.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal("\"a\"", ParseSingleValue<TomlStringSyntax>("a = \"\"\"\"a\"\"\"\"").Value);
        Assert.Equal("C:\\path", ParseSingleValue<TomlStringSyntax>("a = 'C:\\path'").Value);
    }

    [Fact]
    public void Keys_ExposeDecodedNames()
    {
        var property = ParseSingleProperty("site . \"google.com\".'x y' = 1");

        Assert.Equal(["site", "google.com", "x y"], property.Key.Names);
        Assert.True(property.Key.IsDotted);
        Assert.Equal("site . \"google.com\".'x y'", property.Key.ToString());
    }

    [Theory]
    [InlineData("hex = 0xDEADBEEF")]
    [InlineData("oct = 0o755")]
    [InlineData("bin = 0b1101")]
    [InlineData("f = +inf")]
    [InlineData("\"a\".b = 1")]
    [InlineData("[\"a\".b]")]
    [InlineData("a.\"b=c\" = 1")]
    [InlineData("a.\"#\" = 1")]
    [InlineData("\"a\\\"b\" = 1")]
    [InlineData("t = { \"a=b\" = 1 }")]
    [InlineData("t = {a = [1, 2,]}")]
    [InlineData("s = \"\"\"\nHe said \"hi\n\"\"\"")]
    [InlineData("s = '''\nit's\n'''")]
    [InlineData("a = [{x = 1, y = 2}]")]
    [InlineData("a = [\n  1 # it's odd\n  , 2\n]\nb = 3")]
    [InlineData("a = [\n  1 # a ] b\n]")]
    [InlineData("d = 1979-05-27t07:32:00z")]
    [InlineData("\"\" = 1")]
    [InlineData("true = true")]
    [InlineData("1.5 = 2")]
    public void ValidToml_HasNoDiagnostics(string text)
        => Assert.Empty(TomlSyntaxTree.ParseText(text).GetDiagnostics());

    [Theory]
    [InlineData("a = \"foo\" bar\"", "TOML0004")]
    [InlineData("a = 'it''s'", "TOML0004")]
    [InlineData("a = \"\\q\"", "TOML0009")]
    [InlineData("a = \"\\uD800\"", "TOML0009")]
    [InlineData("a = 1/2/2020", "TOML0006")]
    [InlineData("a = Jan 1 2020", "TOML0006")]
    [InlineData("a = 12:00 PM", "TOML0004")]
    [InlineData("a = 99:99:99", "TOML0006")]
    [InlineData("a = 2021-02-29", "TOML0006")]
    [InlineData("a = -07:32:00", "TOML0006")]
    [InlineData("a = 01", "TOML0006")]
    [InlineData("a = 1.", "TOML0006")]
    [InlineData("a = 0X10", "TOML0006")]
    [InlineData("a = 9223372036854775808", "TOML0006")]
    [InlineData("a = [1] b = 2", "TOML0004")]
    [InlineData("[a] b = 1", "TOML0004")]
    [InlineData("a = 1\rb = 2", "TOML0011")]
    [InlineData("a = [foo bar, !!!]", "TOML0006")]
    [InlineData("a = [,,]", "TOML0003")]
    [InlineData("t = { !!! = 1 }", "TOML0007")]
    [InlineData("a!b = 1", "TOML0007")]
    [InlineData("\"\"\"a\"\"\" = 1", "TOML0007")]
    [InlineData("a = \"\u0001\"", "TOML0010")]
    [InlineData("# comment \u007F", "TOML0010")]
    [InlineData("a\n= 1", "TOML0002")]
    [InlineData("a =\n1", "TOML0003")]
    [InlineData("[a\n]", "TOML0002")]
    [InlineData("[[a]", "TOML0002")]
    [InlineData("[a]]", "TOML0002")]
    [InlineData("a = 1\na = 2", "TOML0020")]
    [InlineData("a = {b = 1, b = 2}", "TOML0020")]
    [InlineData("[a]\n[a]", "TOML0021")]
    [InlineData("[a]\nb = 1\n[[a]]", "TOML0021")]
    [InlineData("a = {b = 1}\n[a.c]", "TOML0022")]
    [InlineData("a = [1]\n[[a]]", "TOML0022")]
    [InlineData("a = {b = 1}\na.c = 2", "TOML0022")]
    [InlineData("a = 1\n[a.b]", "TOML0023")]
    [InlineData("[a.b]\n[a]\nb.c = 1", "TOML0024")]
    public void InvalidToml_IsReported(string text, string expectedId)
    {
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Contains(expectedId, tree.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray());
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("a = \"\\e\"")]
    [InlineData("a = \"\\x41\"")]
    [InlineData("a = 07:32")]
    [InlineData("a = 1979-05-27 07:32Z")]
    [InlineData("a = {b = 1,}")]
    [InlineData("a = {\n  b = 1\n}")]
    public void Toml11Features_AreReportedWhenParsingToml10(string text)
    {
        Assert.Empty(TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = TomlVersion.V1_1 }).GetDiagnostics());

        var diagnostic = Assert.Single(TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = TomlVersion.V1_0 }).GetDiagnostics());
        Assert.Equal("TOML0013", diagnostic.Id);
    }

    [Theory]
    [InlineData("a = \"oops\nb = 1\n[table]\nc = 2\n", "TOML0008")]
    [InlineData("a = {b = 1\n[table]\nc = 2\n", "TOML0002")]
    [InlineData("a = [1, 2\n[table]\nc = 2\n", "TOML0002")]
    [InlineData("a = [1, 2\nb = 1\n[table]\nc = 2\n", "TOML0002")]
    [InlineData("\"key = 1\n[table]\nc = 2\n", "TOML0008")]
    public void AnUnterminatedConstruct_DoesNotSwallowTheLinesAfterIt(string text, string expectedId)
    {
        var tree = TomlSyntaxTree.ParseText(text);
        var root = tree.GetRoot();

        Assert.Contains(expectedId, tree.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray());
        var table = Assert.Single(root.Tables);
        Assert.Equal(["table"], table.Key.Names);
        Assert.Equal(2, Assert.IsType<TomlIntegerSyntax>(Assert.Single(table.Properties).Value).Value);
        Assert.Equal(text, root.ToFullString());
    }

    [Fact]
    public void AnUnterminatedArrayAtTheEnd_IsReported()
    {
        var diagnostic = Assert.Single(TomlSyntaxTree.ParseText("a = [1, 2").GetDiagnostics());

        Assert.Equal("TOML0002", diagnostic.Id);
        Assert.Equal("Expected ']'.", diagnostic.Message);
    }

    [Fact]
    public void Diagnostics_PointAtTheKey()
    {
        const string Text = "a = 1\n  a = 2\n";
        var diagnostic = Assert.Single(TomlSyntaxTree.ParseText(Text).GetDiagnostics());

        Assert.Equal("The key 'a' is already defined.", diagnostic.Message);
        Assert.Equal(new TextSpan(8, 1), diagnostic.Location.SourceSpan);
    }

    [Fact]
    public void ArrayElements_KeepWhitespaceAndCommentsAsTrivia()
    {
        var array = Assert.IsType<TomlArraySyntax>(ParseSingleValue("a = [ 1 , # one\n 2 ]"));

        Assert.Equal(["1", "2"], array.Elements.Select(element => element.ToString()).ToArray());
        Assert.Equal(" # one\n", array.Elements.GetSeparator(0).TrailingTrivia.ToFullString());
    }

    [Fact]
    public void NestedArrays_AreNodes()
    {
        var array = Assert.IsType<TomlArraySyntax>(ParseSingleValue("values = [[1], [2, 3]]"));

        Assert.Equal([1, 2], array.Elements.Select(element => Assert.IsType<TomlArraySyntax>(element).Elements.Count).ToArray());
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void DeepNesting_IsReportedRatherThanOverflowingTheStack(int depth)
    {
        var text = "a = " + new string('[', depth) + new string(']', depth) + "\nb = " + string.Concat(Enumerable.Repeat("{c = [", depth)) + string.Concat(Enumerable.Repeat("]}", depth)) + "\n";
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains("TOML0012", tree.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray());
    }

    [Fact]
    public void MaxDepth_CanBeRaised()
    {
        var text = "a = " + new string('[', 200) + new string(']', 200);

        Assert.NotEmpty(TomlSyntaxTree.ParseText(text).GetDiagnostics());
        Assert.Empty(TomlSyntaxTree.ParseText(text, new TomlParseOptions { MaxDepth = 256 }).GetDiagnostics());
    }

    [Fact]
    public void PropertyValue_IsTheSameNodeEveryTime()
    {
        var property = ParseSingleProperty("a = [1, 2]");

        Assert.Same(property.Value, property.Value);
        Assert.Same(property.Value, property.DescendantNodes().OfType<TomlArraySyntax>().Single());
    }

    [Fact]
    public void ReplaceNode_ReplacesAnArray()
    {
        var root = TomlSyntaxTree.ParseText("a = [1, 2] # numbers\n").GetRoot();
        var array = root.DescendantNodes().OfType<TomlArraySyntax>().Single();

        var updated = root.ReplaceNode(array, SyntaxFactory.TomlArray(SyntaxFactory.TomlInteger(3)).WithTriviaFrom(array));

        Assert.Equal("a = [3] # numbers\n", updated.ToFullString());
    }

    [Fact]
    public void InsertNodesAfter_AddsTheSeparatorOfASingleElementArray()
    {
        var root = TomlSyntaxTree.ParseText("a = [1]\n").GetRoot();
        var element = root.DescendantNodes().OfType<TomlIntegerSyntax>().Single();

        var updated = root.InsertNodesAfter(element, [SyntaxFactory.TomlInteger(2)]);

        Assert.Equal("a = [1,2]\n", updated.ToFullString());
        Assert.Empty(TomlSyntaxTree.ParseText(updated.ToFullString()).GetDiagnostics());
    }

    [Fact]
    public void WithValue_KeepsTheTriviaAroundTheValue()
    {
        var property = ParseSingleProperty("value =  42 # comment");
        var integer = Assert.IsType<TomlIntegerSyntax>(property.Value);

        Assert.Equal("value =  43 # comment", property.WithValue(integer.WithValue(43)).ToFullString());
    }

    [Fact]
    public void TableProperties_AreTheEntriesUpToTheNextHeader()
    {
        var root = TomlSyntaxTree.ParseText("a = 1\n[t]\nb = 2\nc = 3\n[[u]]\n[v]\n").GetRoot();
        var tables = root.Tables.ToArray();

        Assert.Equal(["a"], root.RootProperties.Select(property => property.Key.ToString()).ToArray());
        Assert.Equal(["b", "c"], tables[0].Properties.Select(property => property.Key.ToString()).ToArray());
        Assert.True(tables[1].IsArrayOfTables);
        Assert.Empty(tables[1].Properties);
        Assert.Empty(SyntaxFactory.TomlTable("t").Properties);
    }

    [Fact]
    public void Factory_WritesValidToml()
    {
        var document = SyntaxFactory.TomlDocument(
            SyntaxFactory.TomlProperty("title", SyntaxFactory.TomlString("quote \" and\nnewline")),
            SyntaxFactory.TomlTable("server", "http"),
            SyntaxFactory.TomlProperty(SyntaxFactory.Key("site", "google.com"), SyntaxFactory.TomlArray(SyntaxFactory.TomlFloat(1), SyntaxFactory.TomlFloat(double.NaN), SyntaxFactory.TomlBoolean(value: true))),
            SyntaxFactory.TomlArrayOfTables("products"),
            SyntaxFactory.TomlProperty("dimensions", SyntaxFactory.TomlInlineTable(SyntaxFactory.TomlProperty("width", SyntaxFactory.TomlInteger(-1)))),
            SyntaxFactory.TomlProperty("when", SyntaxFactory.TomlDateTime(new DateTimeOffset(1979, 5, 27, 7, 32, 0, 500, TimeSpan.FromHours(2)))));

        const string Expected = """
            title = "quote \" and\nnewline"
            [server.http]
            site."google.com" = [1.0, nan, true]
            [[products]]
            dimensions = { width = -1 }
            when = 1979-05-27T07:32:00.5+02:00

            """;
        Assert.Equal(Expected.ReplaceLineEndings("\n"), document.ToFullString());
        Assert.Empty(TomlSyntaxTree.ParseText(document.ToFullString()).GetDiagnostics());
    }

    [Fact]
    public void Factory_AddEntries_EndsTheLastLine()
    {
        var root = TomlSyntaxTree.ParseText("a = 1").GetRoot();

        var updated = root.AddEntries(SyntaxFactory.TomlProperty("b", SyntaxFactory.TomlInteger(2)));

        Assert.Equal("a = 1\nb = 2\n", updated.ToFullString());
    }

    [Fact]
    public void Factory_KeyPart_QuotesWhatCannotBeBare()
    {
        Assert.Equal("\"a b\".c", SyntaxFactory.Key("a b", "c").ToString());
        Assert.Equal(["a b", "c"], SyntaxFactory.Key("a b", "c").Names);
    }

    [Theory]
    [InlineData("[1, 2]", SyntaxKind.TomlArray)]
    [InlineData("'''literal'''", SyntaxKind.TomlString)]
    [InlineData("{ a = 1 }", SyntaxKind.TomlInlineTable)]
    public void ParseValue_ReadsASingleValue(string text, SyntaxKind expectedKind)
    {
        var value = SyntaxFactory.ParseValue(text);

        Assert.Equal(expectedKind, value.Kind());
        Assert.False(value.ContainsDiagnostics);
    }

    [Fact]
    public void ParseValue_ReportsWhatFollowsTheValue()
    {
        var value = SyntaxFactory.ParseValue("1 2");

        Assert.Equal(SyntaxKind.TomlSkippedValue, value.Kind());
        Assert.True(value.ContainsDiagnostics);
        Assert.Equal("1 2", value.ToFullString());
    }

    [Fact]
    public void Rewriter_VisitsNestedValues()
    {
        var root = TomlSyntaxTree.ParseText("a = [1, {b = 2}]\n[t]\nc = 3\n").GetRoot();

        var rewritten = new DoubleIntegers().Visit(root);

        Assert.Equal("a = [2, {b = 4}]\n[t]\nc = 6\n", rewritten!.ToFullString());
    }

    [Fact]
    public void Rewriter_CanReplaceAValueWithAnotherKindOfValue()
    {
        var root = TomlSyntaxTree.ParseText("a = [1]\n").GetRoot();

        var rewritten = new ArrayToInlineTable().Visit(root);

        Assert.Equal("a = {}\n", rewritten!.ToFullString());
    }

    [Fact]
    public void Rewriter_ReportsANodeThatCannotTakeThePlaceOfAnother()
    {
        var root = TomlSyntaxTree.ParseText("a = 1\n").GetRoot();

        var exception = Assert.Throws<InvalidOperationException>(() => new KeyToString().Visit(root));

        Assert.Contains("TomlKey", exception.Message);
    }

    [Fact]
    public void Walker_VisitsComments()
    {
        var walker = new CommentWalker();

        walker.Visit(TomlSyntaxTree.ParseText("values = [1, # keep this\n 2]\n").GetRoot());

        Assert.Equal("# keep this", Assert.Single(walker.Comments).ToString());
    }

    private static TomlPropertySyntax ParseSingleProperty(string text)
    {
        var tree = TomlSyntaxTree.ParseText(text);
        Assert.Empty(tree.GetDiagnostics());

        return Assert.IsType<TomlPropertySyntax>(Assert.Single(tree.GetRoot().Entries));
    }

    private static TomlValueSyntax ParseSingleValue(string text) => ParseSingleProperty(text).Value;

    private static T ParseSingleValue<T>(string text)
        where T : TomlValueSyntax
        => Assert.IsType<T>(ParseSingleValue(text));

    private sealed class DoubleIntegers : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => node.WithValue(node.Value * 2);
    }

    private sealed class ArrayToInlineTable : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlArray(TomlArraySyntax node) => SyntaxFactory.TomlInlineTable().WithTriviaFrom(node);
    }

    private sealed class KeyToString : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlKey(TomlKeySyntax node) => SyntaxFactory.TomlString("x");
    }

    private sealed class CommentWalker : TomlSyntaxWalker
    {
        public CommentWalker()
            : base(SyntaxWalkerDepth.Trivia)
        {
        }

        public List<SyntaxTrivia> Comments { get; } = [];

        public override void VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.CommentTrivia))
            {
                Comments.Add(trivia);
            }
        }
    }
}
