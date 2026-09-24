using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Language.Toml.Tests;

public sealed class TomlSyntaxTreeTests
{
    [Fact]
    public void ParseDocumentPreservesTomlText()
    {
        const string text = "title = \"TOML Example\"\n[owner]\nname = \"Tom\"\nports = [8000, 8001]\n# comment\n";
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetText().ToString());
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(4, tree.GetRoot().Entries.Count);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseArrayOfTablesAndInlineValues()
    {
        const string text = "[[products]]\nname = 'Hammer'\ndetails = { color = \"gray\", weight = 1 }\n";
        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.IsType<TomlTableSyntax>(tree.GetRoot().Entries[0]);
        Assert.Empty(tree.GetDiagnostics());
    }
}
