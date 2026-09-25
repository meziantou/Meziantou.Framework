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
    [InlineData("a = 0x8000000000000000", "TOML0006")]
    [InlineData("a = 0x10000000000000000", "TOML0006")]
    [InlineData("a = 0x10000000000000005", "TOML0006")]
    [InlineData("a = 0o2000000000000000000000", "TOML0006")]
    [InlineData("a = 1e400", "TOML0006")]
    [InlineData("a = -1.7976931348623159e308", "TOML0006")]
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

    [Theory]
    [InlineData("a = 0x7FFFFFFFFFFFFFFF", long.MaxValue)]
    [InlineData("a = 0o777777777777777777777", long.MaxValue)]
    [InlineData("a = 0b111111111111111111111111111111111111111111111111111111111111111", long.MaxValue)]
    [InlineData("a = -9223372036854775808", long.MinValue)]
    public void Integers_AtTheLimitOf64Bits(string text, long expected)
        => Assert.Equal(expected, ParseSingleValue<TomlIntegerSyntax>(text).Value);

    [Theory]
    [InlineData("a = 1.7976931348623157e308", double.MaxValue)]
    [InlineData("a = 1e-400", 0d)]
    public void Floats_AtTheLimitOfADouble(string text, double expected)
        => Assert.Equal(expected, ParseSingleValue<TomlFloatSyntax>(text).Value);

    [Fact]
    public void ALongDottedKey_IsValidatedInLinearTime()
    {
        var text = string.Concat(Enumerable.Repeat("a.", 100_000)) + "a = 1\n[" + string.Concat(Enumerable.Repeat("b.", 20_000)) + "b]\n" + string.Concat(Enumerable.Range(0, 20_000).Select(i => $"p{i} = 1\n"));
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000_000);
    }

    [Theory]
    [InlineData("a = 1\n# bad \u0001 comment\na = 2\n")]
    [InlineData("# \u0001\na = 1\na = 2\n")]
    [InlineData("a = 1\na\u00a0= 2\n")]
    [InlineData("x = {\n # \u0001\n a = 1, a = 2 }\n")]
    public void AMistakeNextToAKey_DoesNotHideADuplicate(string text)
    {
        var ids = TomlSyntaxTree.ParseText(text).GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();

        Assert.Contains("TOML0010", ids);
        Assert.Contains("TOML0020", ids);
    }

    [Theory]
    [InlineData("\"\\e\" = 1\n\"\\e\" = 2\n", "TOML0020")]
    [InlineData("\"\\x61\" = 1\na = 2\n", "TOML0020")]
    [InlineData("[\"\\e\"]\nx = 1\nx = 2\n", "TOML0020")]
    [InlineData("[\"\\x61\"]\n[a]\n", "TOML0021")]
    public void ATomlVersionMistakeInAKey_DoesNotHideADuplicate(string text, string expectedId)
    {
        var ids = TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = TomlVersion.V1_0 }).GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();

        Assert.Contains("TOML0013", ids);
        Assert.Contains(expectedId, ids);
    }

    [Fact]
    public void ARejectedArrayOfTables_DoesNotResetTheTablesUnderIt()
    {
        var diagnostics = TomlSyntaxTree.ParseText("[a]\n[a.b]\n[[a]]\n[a.b]\n").GetDiagnostics();

        Assert.Equal(["TOML0021", "TOML0021"], diagnostics.Select(diagnostic => diagnostic.Id).ToArray());
        Assert.Equal(new TextSpan(17, 3), diagnostics[1].Location.SourceSpan);
    }

    [Fact]
    public void AnErrorInAHeader_StillChecksTheInlineTablesUnderIt()
    {
        var ids = TomlSyntaxTree.ParseText("[a]\n[a]\nx = {b = 1, b = 2}\n").GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();

        Assert.Equal(["TOML0021", "TOML0020"], ids);
    }

    [Fact]
    public void Diagnostics_AreCheckedAgainAfterAnEdit()
    {
        var tree = TomlSyntaxTree.ParseText("x = {a = 1, a = 2}\ny = 1\ny = 2\n");
        Assert.Equal(["TOML0020", "TOML0020"], tree.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray());
        var root = tree.GetRoot();
        root = root.ReplaceNode(root.RootProperties[0].Value, SyntaxFactory.TomlInteger(1).WithTriviaFrom(root.RootProperties[0].Value));
        root = root.RemoveNode(root.RootProperties[1], SyntaxRemoveOptions.KeepNoTrivia)!;

        var edited = tree.WithRoot(root);

        Assert.Equal("x = 1\ny = 2\n", edited.GetRoot().ToFullString());
        Assert.Empty(edited.GetDiagnostics());
    }

    [Fact]
    public void Diagnostics_OfAnEditedValue_AreWhereTheValueIsNow()
    {
        var tree = TomlSyntaxTree.ParseText("x = {a = 1, a = 2}");
        var root = tree.GetRoot();

        var edited = tree.WithRoot(root.ReplaceNode(root.RootProperties[0].Value, SyntaxFactory.TomlInteger(1)));

        Assert.Equal("x = 1", edited.GetRoot().ToFullString());
        Assert.Empty(edited.GetDiagnostics());
    }

    [Fact]
    public void Diagnostics_OfATreeBuiltByTheFactory_IncludeDuplicates()
    {
        var root = SyntaxFactory.TomlDocument(
            SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(1)),
            SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(2)),
            SyntaxFactory.TomlTable("a"));

        var diagnostics = TomlSyntaxTree.Create(root).GetDiagnostics();

        Assert.Equal(["TOML0020", "TOML0023"], diagnostics.Select(diagnostic => diagnostic.Id).ToArray());
        Assert.Equal(new TextSpan(6, 1), diagnostics[0].Location.SourceSpan);
    }

    [Fact]
    public void Diagnostics_OfANode_IncludeTheDuplicatesInIt()
    {
        var tree = TomlSyntaxTree.ParseText("[t]\na = 1\na = 2\n[u]\nb = 1\n");
        var entries = tree.GetRoot().Entries;

        Assert.Equal("TOML0020", Assert.Single(entries[2].GetDiagnostics()).Id);
        Assert.Empty(entries[4].GetDiagnostics());
        Assert.Equal("TOML0020", Assert.Single(tree.GetRoot().GetDiagnostics()).Id);
    }

    [Theory]
    [InlineData("[a\n", "[a\nx = 1\n")]
    [InlineData("[a\r\n", "[a\r\nx = 1\n")]
    [InlineData("a\n", "a\nx = 1\n")]
    [InlineData("a.\n", "a.\nx = 1\n")]
    [InlineData("a!b\n", "a!b\nx = 1\n")]
    [InlineData("a = [1\n", "a = [1\nx = 1\n")]
    [InlineData("a = {b = 1\n", "a = {b = 1\nx = 1\n")]
    [InlineData("a = [1, # c\n", "a = [1, # c\nx = 1\n")]
    [InlineData("a =\n", "a =\nx = 1\n")]
    public void AddEntries_AfterAnEntryWithAMissingToken_DoesNotAddABlankLine(string text, string expected)
    {
        var tree = TomlSyntaxTree.ParseText(text);
        Assert.Equal(text, tree.GetRoot().ToFullString());

        var updated = tree.GetRoot().AddEntries(SyntaxFactory.TomlProperty("x", SyntaxFactory.TomlInteger(1)));

        Assert.Equal(expected, updated.ToFullString());
    }

    [Theory]
    [InlineData("# header\n", "# header\nx = 1\n")]
    [InlineData("# header", "# header\nx = 1\n")]
    [InlineData("# header\n\n# more\n", "# header\n\n# more\nx = 1\n")]
    [InlineData("a = 1\n# footer\n", "a = 1\nx = 1\n# footer\n")]
    public void AddEntries_KeepsAHeaderCommentFirst(string text, string expected)
    {
        var updated = TomlSyntaxTree.ParseText(text).GetRoot().AddEntries(SyntaxFactory.TomlProperty("x", SyntaxFactory.TomlInteger(1)));

        Assert.Equal(expected, updated.ToFullString());
    }

    [Theory]
    [InlineData("[", "]")]
    [InlineData("{b=", "}")]
    [InlineData("[{b=", "}]")]
    public void DeepNesting_IsReportedOnce(string open, string close)
    {
        var text = "a = [" + string.Concat(Enumerable.Repeat(open, 200)) + "1" + string.Concat(Enumerable.Repeat(close, 200)) + ", 1]\nb = 2\n";
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal("TOML0012", Assert.Single(tree.GetDiagnostics()).Id);
        var array = Assert.IsType<TomlArraySyntax>(tree.GetRoot().RootProperties[0].Value);
        Assert.Equal(1, Assert.IsType<TomlIntegerSyntax>(array.Elements[^1]).Value);
        Assert.Equal(2, Assert.IsType<TomlIntegerSyntax>(tree.GetRoot().RootProperties[1].Value).Value);
    }

    [Theory]
    [InlineData("a = [[{b = 1]]\n")]
    [InlineData("a = [[{b = 1,]]\n")]
    [InlineData("a = [[{]]\n")]
    public void AnInlineTableWithoutItsBrace_LetsTheArraysAroundItClose(string text)
    {
        var tree = TomlSyntaxTree.ParseText(text);

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Expected '}'.", diagnostic.Message);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("a = [\n  [1, 2]\n  [3, 4]\n]\n", TomlVersion.V1_1)]
    [InlineData("a = [\n  [1, 2]\n  [[3, 4]]\n]\n", TomlVersion.V1_1)]
    [InlineData("a = {\n  b = 1\n  c = 2\n}\n", TomlVersion.V1_1)]
    [InlineData("a = [\n  1\n  2\n]\n", TomlVersion.V1_1)]
    public void AMissingComma_IsReportedAsOne(string text, TomlVersion version)
    {
        var tree = TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = version });

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Expected ','.", diagnostic.Message);
        Assert.Single(tree.GetRoot().Entries);
    }

    [Fact]
    public void StrayTextAfterAValue_IsReportedOnce()
    {
        var diagnostic = Assert.Single(TomlSyntaxTree.ParseText("a = 1979-05-27 x\n").GetDiagnostics());

        Assert.Equal("TOML0004", diagnostic.Id);
    }

    [Fact]
    public void ParseValue_KeepsTheDiagnosticsOfTheValueWhenTextFollowsIt()
    {
        var value = SyntaxFactory.ParseValue("[1 2] x");

        Assert.Equal(SyntaxKind.TomlSkippedValue, value.Kind());
        Assert.Equal(["TOML0002", "TOML0006"], value.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray());
        Assert.Equal("[1 2] x", value.ToFullString());
    }

    [Fact]
    public void Factory_AValueEndingWithAComment_DoesNotHideWhatFollowsIt()
    {
        var document = SyntaxFactory.TomlDocument(SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlArray(SyntaxFactory.ParseValue("1 # one"), SyntaxFactory.ParseValue("2 # two"))));

        Assert.Equal("a = [1 # one\n, 2 # two\n]\n", document.ToFullString());
        Assert.Empty(TomlSyntaxTree.ParseText(document.ToFullString()).GetDiagnostics());
    }

    [Fact]
    public void Factory_RefusesLoneSurrogates()
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.TomlString("a\uD800b"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.TomlString("\uDC00"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Key("\uD800"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.TomlString("x").WithValue("\uD800"));
        Assert.Equal("\"😀\"", SyntaxFactory.TomlString("😀").ToFullString());
    }

    [Theory]
    [InlineData(SyntaxKind.BareKeyToken)]
    [InlineData(SyntaxKind.IntegerToken)]
    [InlineData(SyntaxKind.BasicStringToken)]
    [InlineData(SyntaxKind.OffsetDateTimeToken)]
    [InlineData(SyntaxKind.BadToken)]
    [InlineData(SyntaxKind.CommentTrivia)]
    public void Factory_Token_RefusesAKindWithoutFixedText(SyntaxKind kind)
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Token(kind));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Token(SyntaxFactory.TriviaList(), kind, SyntaxFactory.TriviaList()));
    }

    [Fact]
    public void Factory_Trivia_RefusesTextThatIsNotWhatItSays()
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Whitespace("abc"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Whitespace(""));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Whitespace("\u00A0"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.EndOfLine("zz"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.EndOfLine("\r"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment("# a\u0001b"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment("# \u007F"));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment("# \uD800"));
        Assert.Equal(" \t", SyntaxFactory.Whitespace(" \t").ToFullString());
        Assert.Equal("\r\n", SyntaxFactory.EndOfLine("\r\n").ToFullString());
        Assert.Equal("# tab\there 😀", SyntaxFactory.Comment("# tab\there 😀").ToFullString());
    }

    [Fact]
    public void Factory_SeparatedList_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => SyntaxFactory.TomlArray(SyntaxFactory.TomlInteger(1), null!));
        Assert.Throws<ArgumentNullException>(() => SyntaxFactory.TomlInlineTable(SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(1)), null!));
    }

    [Fact]
    public void Rewriter_ReportsAnEntryReplacedWithSomethingElse()
    {
        var root = TomlSyntaxTree.ParseText("a = 1\nb = 2\n").GetRoot();

        var exception = Assert.Throws<InvalidOperationException>(() => new PropertyToInteger().Visit(root));

        Assert.Contains("TomlProperty", exception.Message);
    }

    [Fact]
    public void Rewriter_ReportsAnElementReplacedWithSomethingElse()
    {
        var root = TomlSyntaxTree.ParseText("a = [1, 2]\n").GetRoot();

        Assert.Throws<InvalidOperationException>(() => new IntegerToProperty().Visit(root));
    }

    [Theory]
    [InlineData("a = [1, 2, 3]\n", 2, "a = [1, 3]\n")]
    [InlineData("a = [1, 2, 3]\n", 3, "a = [1, 2]\n")]
    [InlineData("a = [1, 2, 3,]\n", 3, "a = [1, 2, ]\n")]
    [InlineData("a = [1]\n", 1, "a = []\n")]
    [InlineData("a = { x = 1, y = 2 }\n", 1, "a = { y = 2 }\n")]
    public void Rewriter_RemovesAnElementItReturnsNullFor(string text, long removed, string expected)
    {
        var root = TomlSyntaxTree.ParseText(text).GetRoot();

        var rewritten = new RemoveInteger(removed).Visit(root);

        Assert.Equal(expected, rewritten!.ToFullString());
    }

    [Fact]
    public void DiagnosticMessages_StayShortForALongKey()
    {
        var text = "[" + string.Join('.', Enumerable.Repeat("a", 20_000)) + "]\n" + string.Concat(Enumerable.Repeat("x = 1\n", 2_000)) + "[\"" + new string('b', 10_000) + "\"]\ny = 1\ny = 2\n";

        var diagnostics = TomlSyntaxTree.ParseText(text).GetDiagnostics();

        Assert.Equal(2_000, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.HasCountLessThan(300, diagnostic.Message));
        Assert.StartsWith("The key 'a.a.", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.EndsWith(".a.a.x' is already defined.", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains(".….", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Equal("The key '\"" + new string('b', 64) + "…\".y' is already defined.", diagnostics[^1].Message);
    }

    [Fact]
    public void Properties_OfEveryTable_OfALargeDocument()
    {
        var root = TomlSyntaxTree.ParseText(string.Concat(Enumerable.Range(0, 100_000).Select(i => $"[t{i}]\nx = {i}\ny = 1\n"))).GetRoot();

        var values = root.Tables.Select(table => ((TomlIntegerSyntax)table.Properties[0].Value).Value).ToArray();

        Assert.Equal(Enumerable.Range(0, 100_000).Select(i => (long)i), values);
    }

    [Fact]
    public void WithRoot_ReportsTheDiagnosticsOfTheText()
    {
        var tree = TomlSyntaxTree.ParseText("a = [1, 2]\n");
        var array = (TomlArraySyntax)tree.GetRoot().RootProperties[0].Value;

        var edited = tree.WithRoot(tree.GetRoot().ReplaceNode(array, array.WithCloseBracketToken(SyntaxFactory.MissingToken(SyntaxKind.CloseBracketToken))));

        Assert.Equal("a = [1, 2", edited.GetRoot().ToFullString());
        Assert.Equal("TOML0002", Assert.Single(edited.GetDiagnostics()).Id);
        Assert.Equal("TOML0002", Assert.Single(edited.GetRoot().RootProperties[0].GetDiagnostics()).Id);
    }

    [Fact]
    public void Create_ReportsTheDiagnosticsOfItsVersion()
    {
        var v11 = TomlSyntaxTree.ParseText("a = \"\\e\"\n").GetRoot();
        var v10 = TomlSyntaxTree.ParseText("a = 07:32\n", new TomlParseOptions { Version = TomlVersion.V1_0 }).GetRoot();

        Assert.Equal("TOML0013", Assert.Single(TomlSyntaxTree.Create(v11, new TomlParseOptions { Version = TomlVersion.V1_0 }).GetDiagnostics()).Id);
        Assert.Empty(TomlSyntaxTree.Create(v10).GetDiagnostics());
    }

    [Fact]
    public void WithRoot_MakesTheRootItsOwn()
    {
        var tree = TomlSyntaxTree.ParseText("a = 1\nb = 2\n");
        var root = tree.GetRoot();
        var updated = root.ReplaceNode(root.RootProperties[1].Key, SyntaxFactory.Key("a").WithTriviaFrom(root.RootProperties[1].Key));

        var edited = tree.WithRoot(updated);

        Assert.Same(updated, edited.GetRoot());
        Assert.Equal("TOML0020", Assert.Single(updated.RootProperties[1].GetDiagnostics()).Id);
        Assert.NotSame(root, TomlSyntaxTree.Create(root).GetRoot());
    }

    [Fact]
    public void WithRoot_RefusesARootThatIsNotADocument()
    {
        SyntaxTree tree = TomlSyntaxTree.ParseText("a = 1\n");

        Assert.Throws<ArgumentException>(() => tree.WithRoot(SyntaxFactory.Key("x")));
    }

    [Fact]
    public void Editing_EndsTheLineOfWhatItInserts()
    {
        var root = SyntaxFactory.ParseDocument("[server]\nhost = \"x\"\n[other]\ny = 1\n");
        var host = root.Tables.First().Properties[0];
        var port = SyntaxFactory.TomlProperty("port", SyntaxFactory.TomlInteger(80));

        Assert.Equal("[server]\nhost = \"x\"\nport = 80\n[other]\ny = 1\n", root.InsertNodesAfter(host, [port]).ToFullString());
        Assert.Equal("[server]\nport = 80\nhost = \"x\"\n[other]\ny = 1\n", root.InsertNodesBefore(host, [port]).ToFullString());
        Assert.Equal("[server]\nport = 80\n[other]\ny = 1\n", root.ReplaceNode(host, port).ToFullString());
        Assert.Equal("[srv]\nhost = \"x\"\n[other]\ny = 1\n", root.ReplaceNode(root.Tables.First(), SyntaxFactory.TomlTable("srv")).ToFullString());
        Assert.Equal("[server]\nw = 9\n[other]\nw = 9", new PropertyToW().Visit(root)!.ToFullString());
    }

    [Fact]
    public void Editing_KeepsTheTextOfAParsedDocument()
    {
        const string Text = "a = 1 b = 2\nc = 3\n";
        var root = SyntaxFactory.ParseDocument(Text);

        var edited = root.ReplaceNode(root.RootProperties[1].Value, SyntaxFactory.TomlInteger(4).WithTriviaFrom(root.RootProperties[1].Value));

        Assert.Equal("a = 1 b = 2\nc = 4\n", edited.ToFullString());
    }

    [Fact]
    public void Factory_EndsTheLineOfACommentInFrontOfAnEntry()
    {
        var document = SyntaxFactory.TomlDocument(
            SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(1)).WithLeadingTrivia(SyntaxFactory.Comment("# about a")),
            SyntaxFactory.TomlProperty("b", SyntaxFactory.TomlInteger(2)));

        Assert.Equal("# about a\na = 1\nb = 2\n", document.ToFullString());
    }

    [Theory]
    [InlineData("\n1")]
    [InlineData("# c\n1")]
    public void Factory_TomlProperty_RefusesAValueOnALineOfItsOwn(string value)
        => Assert.Throws<ArgumentException>(() => SyntaxFactory.TomlProperty("a", SyntaxFactory.ParseValue(value)));

    [Theory]
    [InlineData("a = [1, 2]\n", "a = [1, 2, 3 # three\n]\n")]
    [InlineData("a = []\n", "a = [3 # three\n]\n")]
    [InlineData("a = [1]\n", "a = [1, 3 # three\n]\n")]
    [InlineData("a = [1, 2,]\n", "a = [1, 2, 3 # three\n,]\n")]
    [InlineData("a = [\n  1, # one\n  2 # two\n]\n", "a = [\n  1, # one\n  2, # two\n  3 # three\n]\n")]
    [InlineData("a = [\n  1,\n  2,\n]\n", "a = [\n  1,\n  2,\n  3 # three\n,\n]\n")]
    public void AddElements_LaysOutTheNewElementsAsTheArrayIs(string text, string expected)
    {
        var root = SyntaxFactory.ParseDocument(text);
        var array = (TomlArraySyntax)root.RootProperties[0].Value;

        var edited = root.ReplaceNode(array, array.AddElements(SyntaxFactory.ParseValue("3 # three")));

        Assert.Equal(expected, edited.ToFullString());
        Assert.Empty(TomlSyntaxTree.ParseText(edited.ToFullString()).GetDiagnostics());
    }

    [Theory]
    [InlineData("a = [1, 2]\n", "a = [1, 2, 3, 4]\n")]
    [InlineData("a = [1, 2,]\n", "a = [1, 2, 3, 4,]\n")]
    [InlineData("a = [\n  1, # one\n  2 # two\n]\n", "a = [\n  1, # one\n  2, # two\n  3,\n  4\n]\n")]
    [InlineData("a = [\n    1,\n    2,\n]\n", "a = [\n    1,\n    2,\n    3,\n    4,\n]\n")]
    public void AddElements_AddsSeveralElements(string text, string expected)
    {
        var root = SyntaxFactory.ParseDocument(text);
        var array = (TomlArraySyntax)root.RootProperties[0].Value;

        var edited = root.ReplaceNode(array, array.AddElements(SyntaxFactory.TomlInteger(3), SyntaxFactory.TomlInteger(4)));

        Assert.Equal(expected, edited.ToFullString());
    }

    [Theory]
    [InlineData("t = { a = 1, b = 2 }\n", "t = { a = 1, b = 2, c = 3 }\n")]
    [InlineData("t = {a = 1}\n", "t = {a = 1, c = 3}\n")]
    [InlineData("t = {}\n", "t = { c = 3 }\n")]
    [InlineData("t = { a = 1, }\n", "t = { a = 1, c = 3, }\n")]
    public void AddProperties_LaysOutTheNewPairsAsTheTableIs(string text, string expected)
    {
        var root = SyntaxFactory.ParseDocument(text);
        var table = (TomlInlineTableSyntax)root.RootProperties[0].Value;

        var edited = root.ReplaceNode(table, table.AddProperties(SyntaxFactory.TomlProperty("c", SyntaxFactory.TomlInteger(3))));

        Assert.Equal(expected, edited.ToFullString());
    }

    [Theory]
    [InlineData("[1]\n# keep me\n")]
    [InlineData("1 # c\n  \n")]
    [InlineData("'x'\n\n")]
    [InlineData("# c")]
    [InlineData("\t")]
    public void ParseValue_KeepsTheTextAfterTheValue(string text)
        => Assert.Equal(text, SyntaxFactory.ParseValue(text).ToFullString());

    [Theory]
    [InlineData("a.b\na = 1\n")]
    [InlineData("a\n[a]\n")]
    [InlineData("x = { a\n, a }\n")]
    public void AKeyWithoutItsEquals_DefinesNothing(string text)
    {
        var ids = TomlSyntaxTree.ParseText(text).GetDiagnostics().Select(diagnostic => diagnostic.Id);

        Assert.All(ids, id => Assert.Equal("TOML0002", id));
    }

    [Fact]
    public void Factory_BuildsWhatTheParserBuilds()
    {
        Assert.True(SyntaxFactory.AreEquivalent(SyntaxFactory.Key("a"), SyntaxFactory.ParseDocument("a = 1\n").RootProperties[0].Key));
        Assert.True(SyntaxFactory.AreEquivalent(SyntaxFactory.TomlDocument(SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(1))), SyntaxFactory.ParseDocument("a = 1\n")));
        Assert.True(SyntaxFactory.AreEquivalent(SyntaxFactory.TomlInlineTable(SyntaxFactory.TomlProperty("a", SyntaxFactory.TomlInteger(1))), SyntaxFactory.ParseValue("{ a = 1 }")));
    }

    [Fact]
    public void Options_RefuseWhatTheyCannotMean()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlParseOptions { MaxDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlParseOptions { MaxDepth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlParseOptions { Version = (TomlVersion)42 });
    }

    [Fact]
    public void DeepNesting_BeyondTheStack_IsReportedRatherThanOverflowing()
    {
        var text = "a = " + new string('[', 100_000) + new string(']', 100_000) + "\n";
        TomlSyntaxTree? tree = null;

        var thread = new Thread(() => tree = TomlSyntaxTree.ParseText(text, new TomlParseOptions { MaxDepth = int.MaxValue }), maxStackSize: 1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(tree);
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal("TOML0012", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void Rewriter_ThrowsRatherThanOverflowingTheStack()
    {
        TomlValueSyntax value = SyntaxFactory.TomlArray();
        for (var i = 0; i < 20_000; i++)
        {
            value = SyntaxFactory.TomlArray(SyntaxFactory.SingletonSeparatedList(value));
        }

        var document = SyntaxFactory.TomlDocument(SyntaxFactory.TomlProperty("a", value));
        Exception? exception = null;

        var thread = new Thread(() => exception = Record.Exception(() => new TomlSyntaxRewriter().Visit(document)), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.IsType<InsufficientExecutionStackException>(exception);
    }

    [Fact]
    public void GetKeyValues_GivesTheFullKeyOfEveryPair()
    {
        var root = SyntaxFactory.ParseDocument("""
            name = "app"
            [dependencies]
            serde = { version = "1.0", features = ["derive"] }
            tokio.version = "1.2"
            [dependencies.'my crate']
            version = "0.3"
            [[bin]]
            name = "a"
            [[bin]]
            name = "b"
            """);

        var pairs = root.GetKeyValues().ToArray();

        Assert.Equal(
            ["name", "dependencies.serde", "dependencies.serde.version", "dependencies.serde.features", "dependencies.tokio.version", "dependencies.my crate.version", "bin.name", "bin.name"],
            pairs.Select(pair => string.Join('.', pair.Names)).ToArray());
        Assert.Equal(["dependencies", "my crate", "version"], pairs[5].Parts.Select(part => part.ValueText).ToArray());
        Assert.Null(pairs[0].Table);
        Assert.NotSame(pairs[6].Table, pairs[7].Table);
        Assert.Equal("1.0", ((TomlStringSyntax)pairs[2].Value).Value);
    }

    [Theory]
    [InlineData("[server]\nport = 80\n")]
    [InlineData("server.port = 80\n")]
    [InlineData("server = { port = 80 }\n")]
    [InlineData("[server]\nhost = { name = 'x' }\n[other]\nport = 1\n[server.x]\n[a]\nserver.port = 1\n[b]\n[server]\nport = 80\n")]
    public void GetValue_FindsTheKeyWhereverItIsWritten(string text)
    {
        var root = SyntaxFactory.ParseDocument(text);

        Assert.Equal(80, Assert.IsType<TomlIntegerSyntax>(root.GetValue("server", "port")).Value);
        Assert.Null(root.GetValue("server", "Port"));
        Assert.Throws<ArgumentException>(() => root.GetValue());
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

    private sealed class PropertyToW : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlProperty(TomlPropertySyntax node) => SyntaxFactory.TomlProperty("w", SyntaxFactory.TomlInteger(9));
    }

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

    private sealed class PropertyToInteger : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlProperty(TomlPropertySyntax node) => node.Key.Names[0] == "a" ? SyntaxFactory.TomlInteger(5) : node;
    }

    private sealed class IntegerToProperty : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => SyntaxFactory.TomlProperty("x", node);
    }

    private sealed class RemoveInteger(long value) : TomlSyntaxRewriter
    {
        public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => node.Value == value ? null : node;

        public override SyntaxNode? VisitTomlProperty(TomlPropertySyntax node)
            => node.Parent is TomlInlineTableSyntax && node.Value is TomlIntegerSyntax integer && integer.Value == value ? null : base.VisitTomlProperty(node);
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
