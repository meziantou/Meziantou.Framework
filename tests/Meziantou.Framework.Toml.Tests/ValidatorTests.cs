using Meziantou.Framework.Toml.Parsing;

namespace Meziantou.Framework.Toml.Tests;

public class ValidatorTests
{
    [Fact]
    public void TestNoErrors()
    {
        var input = @"a = 1";
        var doc = SyntaxParser.Parse(input);
        Assert.False(doc.HasErrors, message: "The document should not have any errors");
        var docAsStr = doc.ToString();
        Assert.Equal(input, docAsStr);
    }

    [Fact]
    public void TestRedefineKey()
    {
        var input = @"a = 1
a = true
";
        var doc = SyntaxParser.Parse(input);
        var roundTrip = doc.ToString();
        StandardTests.Dump(input, doc, roundTrip);
        Assert.True(doc.HasErrors, message: "The document should have errors");
    }

    [Fact]
    public void TestRedefineKeyAndTable()
    {
        var input = @"a = 1
[a]
b = 1
";
        var doc = SyntaxParser.Parse(input);
        var roundTrip = doc.ToString();
        StandardTests.Dump(input, doc, roundTrip);
        Assert.True(doc.HasErrors, message: "The document should have errors");
    }


    [Fact]
    public void TestTableArray()
    {
        var input = @"[[a]]
b = 1
[[a]]
b = true
";
        var doc = SyntaxParser.Parse(input);
        var roundTrip = doc.ToString();
        StandardTests.Dump(input, doc, roundTrip);
        Assert.False(doc.HasErrors, message: "The document should not have any errors");
    }


    [Fact]
    public void TestTableArrayNested()
    {
        var input = @"[[a]]
b = 1
[[a.c]]
b = true
";
        var doc = SyntaxParser.Parse(input);
        var roundTrip = doc.ToString();
        StandardTests.Dump(input, doc, roundTrip);
        Assert.False(doc.HasErrors, message: "The document should not have any errors");
    }


    [Fact]
    public void TestTableArrayAndInvalidTable()
    {
        var input = @"[[a]]
b = 1
[[a.b]]
c = true
[a.b]
d = true
";
        var doc = SyntaxParser.Parse(input);
        var roundTrip = doc.ToString();
        StandardTests.Dump(input, doc, roundTrip);
        Assert.True(doc.HasErrors, message: "The document should have errors");
    }

    [Fact]
    public void TestAllowTab()
    {
        var input = "";
        input += "tab_multiline_basic=\"\"\"\t\"\"\"\n";
        input += "tab_singleline_literal='\t'\n";
        input += "tab_multiline_literal='''\t'''\n";
        var doc = SyntaxParser.Parse(input);
        Assert.False(doc.HasErrors, message: "The document should not have errors");
    }

    [Theory]
    [InlineData("[a]\nt = 1\ne = 2\n[a.t]\n")]
    [InlineData("[a]\nb = {c = 1}\nc = 2\n[a.b]\n")]
    [InlineData("[[a]]\nb = 1\n[c]\n[a.b]\n")]
    [InlineData("[[b]]\nb = {}\n[[c]]\n[b.b.d]\n")]
    [InlineData("[a.b.c]\n[a]\nb.d = 1\n[a.b]\n")]
    public void Redefinition_IsRejectedByBothParsers(string toml)
    {
        Assert.True(SyntaxParser.Parse(toml).HasErrors, message: "SyntaxParser should reject the document");
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(toml));
    }

    [Theory]
    [InlineData("[[a]]\nx = 1\n[[a]]\nx = 2\n[a.b]\ny = 1\n")]
    [InlineData("[[a]]\nx = 1\n[c]\n[a.b]\n")]
    [InlineData("[a.b.c]\n[a]\nb.d = 1\n[a.b.e]\n")]
    [InlineData("[a]\nt = 1\ne = 2\n[a.u]\n")]
    public void ValidDocument_IsAcceptedByBothParsers(string toml)
    {
        Assert.False(SyntaxParser.Parse(toml).HasErrors, message: "SyntaxParser should accept the document");
        Assert.NotNull(TomlSerializer.Deserialize<Model.TomlTable>(toml));
    }

    [Fact]
    public void Validate_LargeArrayOfInlineTables_IsLinear()
    {
        var toml = "a = [" + string.Join(",", Enumerable.Range(0, 5000).Select(i => "{x=" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", y={z=1}}")) + "]\n";
        SyntaxParser.Parse(toml);

        // The path of each key used to include the index of every previous item, so validating allocated O(n²) memory
        var before = GC.GetAllocatedBytesForCurrentThread();
        var doc = SyntaxParser.Parse(toml);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(doc.HasErrors);
        Assert.True(allocated < 50_000_000, $"Allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("a = [{x=1}, {x=2}]\nb = [[{x=1}], [{x=2}, {x=3}]]\n", false)]
    [InlineData("a = [{x=1, x=2}]\n", true)]
    [InlineData("a = [[{x=1}], [{y=1, y=2}]]\n", true)]
    public void Validate_ArrayItems_AreValidatedIndependently(string toml, bool hasErrors)
    {
        Assert.Equal(hasErrors, SyntaxParser.Parse(toml).HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_Redefinitions_DoNotCopyThePreviousDefinition(bool isTable)
    {
        var first = isTable
            ? "[t]\n" + string.Concat(Enumerable.Range(0, 5000).Select(i => $"k{i} = 1\n"))
            : "t = [" + string.Join(",", Enumerable.Repeat("1", 5000)) + "]\n";
        var toml = first + string.Concat(Enumerable.Repeat(isTable ? "[t]\n" : "t = 1\n", 200));

        var doc = SyntaxParser.Parse(toml);

        Assert.HasCount(200, doc.Diagnostics);
        Assert.All(doc.Diagnostics, diagnostic => Assert.HasCountLessThan(200, diagnostic.Message));
        Assert.StartsWith(isTable ? "The key `t` is already defined at (1,2)" : "The key `t` is already defined at (1,1)", doc.Diagnostics[0].Message);
    }

    [Theory]
    [InlineData("p.q = 1\np.q.r = 2\n")]
    [InlineData("a = 1\nb = 2\na = 3\n")]
    [InlineData("[t]\n[t]\n")]
    [InlineData("[a.b]\nx = 1\n[a.b]\n")]
    [InlineData("a.b = 1\n[a]\n")]
    [InlineData("[[a]]\n[a]\n")]
    [InlineData("a = [1]\n[[a]]\n")]
    [InlineData("[a]\nb.c = 1\n[a.b]\n")]
    [InlineData("x = { a.b = 1, a.b.c = 2 }\n")]
    [InlineData("[[x]]\n[[x]]\ny = 1\ny = 2\n")]
    [InlineData("a = [{b = 1, b = 2}]\n")]
    [InlineData("\"a.b\" = 1\n\"a.b\" = 2\n")]
    public void Validate_Redefinitions_AreReportedWhereTheDeserializerReportsThem(string toml)
    {
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(toml));
        var diagnostic = Assert.Single(SyntaxParser.Parse(toml).Diagnostics);

        Assert.Equal(exception.Span!.Value.Start.ToString(), diagnostic.Span.Start.ToString());
        Assert.Equal(GetPreviousDefinition(exception.Message), GetPreviousDefinition(diagnostic.Message));
        Assert.Equal(GetKey(exception.Message), GetKey(diagnostic.Message));

        static string GetKey(string message) => System.Text.RegularExpressions.Regex.Match(message, "The key `[^`]*`", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Value;

        static string GetPreviousDefinition(string message) => System.Text.RegularExpressions.Regex.Match(message, @"defined at \(\d+,\d+\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_LongKeys_AreLinear(bool isHeader)
    {
        var options = TomlSerializerOptions.Default with { MaxDepth = int.MaxValue };
        var key = string.Join('.', Enumerable.Repeat("a", 2000));
        var toml = string.Concat(Enumerable.Range(0, 20).Select(i => isHeader ? $"[k{i}.{key}]\n" : $"k{i}.{key} = 1\n"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var doc = SyntaxParser.Parse(toml, options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(doc.HasErrors, doc.Diagnostics.ToString());
        Assert.True(allocated < 200_000_000, $"Allocated {allocated} bytes");
    }
}
