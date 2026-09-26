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
}
