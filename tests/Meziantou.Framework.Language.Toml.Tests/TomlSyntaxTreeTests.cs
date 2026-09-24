using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Language.Toml.Tests;

public sealed class TomlSyntaxTreeTests
{
    [Fact]
    public void ParseDocumentPreservesTomlText()
    {
        const string Text = "title = \"TOML Example\"\n[owner]\nname = \"Tom\"\nports = [8000, 8001]\n# comment\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetText().ToString());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Equal(4, tree.GetRoot().Entries.Count);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseArrayOfTablesAndInlineValues()
    {
        const string Text = "[[products]]\nname = 'Hammer'\ndetails = { color = \"gray\", weight = 1 }\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.IsType<TomlTableSyntax>(tree.GetRoot().Entries[0]);
        Assert.Empty(tree.GetDiagnostics());
    }
}
