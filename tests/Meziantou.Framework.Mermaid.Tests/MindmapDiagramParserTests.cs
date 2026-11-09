namespace Meziantou.Framework.Mermaid.Tests;

public sealed class MindmapDiagramParserTests
{
    private static MindmapDiagram Parse(string text) => (MindmapDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void NodesAndIndentation()
    {
        #region Mermaid_MindmapDiagram
        var diagram = (MindmapDiagram)MermaidDocument.Parse("""
            mindmap
              root((mindmap))
                Origins
                  Long history
                  Popularisation
                Research
                  On effectiveness<br/>and features
                Tools
                  Pen and paper
                  Mermaid
            """).Diagram;
        #endregion

        var root = diagram.Root!;
        Assert.Equal(("root", "mindmap", MindmapNodeShape.Circle), (root.Id, root.Label.Text, root.Shape));
        Assert.Equal(["Origins", "Research", "Tools"], root.Children.Select(child => child.Label.Text));
        Assert.Equal("On effectiveness\nand features", root.Children[1].Children[0].Label.Text);
        Assert.HasCount(9, diagram.EnumerateNodes());
    }

    [Theory]
    [InlineData("id[I am a square]", MindmapNodeShape.Square)]
    [InlineData("id(I am a rounded square)", MindmapNodeShape.RoundedSquare)]
    [InlineData("id((I am a circle))", MindmapNodeShape.Circle)]
    [InlineData("id))I am a bang((", MindmapNodeShape.Bang)]
    [InlineData("id)I am a cloud(", MindmapNodeShape.Cloud)]
    [InlineData("id{{I am a hexagon}}", MindmapNodeShape.Hexagon)]
    [InlineData("id(I am a cloud too(", MindmapNodeShape.Cloud)]
    [InlineData("id(-I am the default shape-)", MindmapNodeShape.Default)]
    public void Shapes(string node, MindmapNodeShape shape)
    {
        var root = Parse("mindmap\n    " + node).Root!;
        Assert.Equal(("id", shape), (root.Id, root.Shape));
        Assert.StartsWith("I am ", root.Label.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void IconsClassesMarkdownAndConfiguration()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              mindmap:
                colorBy: depth
                padding: 12
                maxNodeWidth: 150
            ---
            mindmap Root
                A
                ::icon(fa fa-book)
                B(B)
                :::urgent large
                  id1["`**Bold** with
                  a second line`"]
                  ["Quoted #quot;text#quot;"]
                C %% not a comment
              %% a comment
                D
            """);
        var diagram = (MindmapDiagram)document.Diagram;
        Assert.Equal((MindmapColorMode.Depth, 12.0, 150.0), (diagram.ColorBy, diagram.NodePadding, diagram.MaxNodeWidth));
        var root = diagram.Root!;
        Assert.Equal("Root", root.Label.Text);
        Assert.Equal("fa fa-book", root.Children[0].Icon);
        Assert.Equal(["urgent", "large"], root.Children[1].CssClasses);
        Assert.Equal(DiagramText.Markdown("**Bold** with\na second line"), root.Children[1].Children[0].Label);
        Assert.Equal(("Quoted \"text\"", (string?)null), (root.Children[1].Children[1].Label.Text, root.Children[1].Children[1].Id));
        Assert.Equal(["A", "B", "C %% not a comment", "D"], root.Children.Select(child => child.Label.Text));
        Assert.Single(document.Warnings);
    }

    [Fact]
    public void UnclearIndentationMakesASibling()
    {
        var root = Parse("""
            mindmap
            Root
                A
                    B
                  C
            """).Root!;
        Assert.Equal(["B", "C"], root.Children[0].Children.Select(child => child.Label.Text));
    }

    [Theory]
    [InlineData("mindmap\nA\nB", "(3, 1): There can be only one root")]
    [InlineData("mindmap\n  A\nB", "There can be only one root")]
    [InlineData("mindmap\n::icon(x)", "An icon decorates the node before it")]
    [InlineData("mindmap\nA[x", "the shape is not closed")]
    [InlineData("mindmap\nA[]", "The text of the node is empty.")]
    [InlineData("mindmap\nA{x}", "a hexagon opens with '{{'")]
    [InlineData("mindmap\nA[x] y", "a statement is alone on its line.")]
    [InlineData("mindmap\nA\n  mindmap", "A node cannot start with the keyword 'mindmap'.")]
    [InlineData("mindmap\nA\n  ::icon(x", "The icon is not closed by ')'.")]
    public void InvalidMindmaps(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepNestingIsRejected()
    {
        var text = "mindmap\n" + string.Concat(Enumerable.Range(0, 40).Select(i => new string(' ', i) + "n\n"));
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("deeper than", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new MindmapDiagram { ColorBy = MindmapColorMode.Depth, NodePadding = 5, MaxNodeWidth = 120, Theme = DiagramTheme.Forest, Title = "Ideas" };
        var root = diagram.SetRoot("Root (main)", MindmapNodeShape.Circle);
        root.Id = "root";
        var plain = root.AddChild("Plain text");
        plain.Icon = "fa fa-book";
        plain.CssClasses.Add("urgent");
        plain.AddChild("mindmap is a keyword");
        plain.AddChild("Line 1\nLine 2", MindmapNodeShape.Cloud);
        root.AddChild(DiagramText.Markdown("**bold** `code`"), MindmapNodeShape.Bang);
        var withId = root.AddChild("With id");
        withId.Id = "the-id";
        root.AddChild(" spaced ", MindmapNodeShape.Hexagon);
        root.AddChild("%% not a comment");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (MindmapDiagram)parsed.Diagram;
        Assert.Equal((MindmapColorMode.Depth, 5.0, 120.0, "Ideas"), (result.ColorBy, result.NodePadding, result.MaxNodeWidth, result.Title));
        var resultRoot = result.Root!;
        Assert.Equal(("root", "Root (main)", MindmapNodeShape.Circle), (resultRoot.Id, resultRoot.Label.Text, resultRoot.Shape));
        Assert.Equal(("fa fa-book", "urgent"), (resultRoot.Children[0].Icon, resultRoot.Children[0].CssClasses.Single()));
        Assert.Equal(["mindmap is a keyword", "Line 1\nLine 2"], resultRoot.Children[0].Children.Select(child => child.Label.Text));
        Assert.Equal(DiagramText.Markdown("**bold** `code`"), resultRoot.Children[1].Label);
        Assert.Equal(("the-id", MindmapNodeShape.Default), (resultRoot.Children[2].Id, resultRoot.Children[2].Shape));
        Assert.Equal(" spaced ", resultRoot.Children[3].Label.Text);
        Assert.Equal("%% not a comment", resultRoot.Children[4].Label.Text);
    }

    [Fact]
    public void WriteDropsTheIdentifiersThatCannotBeWritten()
    {
        var diagram = new MindmapDiagram();
        var root = diagram.SetRoot("Root");
        root.Id = "a<b";
        var child = root.AddChild("Child", MindmapNodeShape.Cloud);
        child.Id = "dash-";
        root.AddChild("Not an icon", MindmapNodeShape.RoundedSquare).Id = "::icon";

        var text = MermaidWriter.Write(diagram);
        Assert.Equal("mindmap\n  Root\n    dash- )\"Child\"(\n    ::icon (\"Not an icon\")\n", text);
        Assert.Equal(("dash-", MindmapNodeShape.Cloud), (Parse(text).Root!.Children[0].Id, Parse(text).Root!.Children[0].Shape));
    }

    [Fact]
    public void WhitespaceLinesAreSkipped()
    {
        var root = Parse("mindmap\nRoot\n    \u000B\n\u3000 A").Root!;
        Assert.Equal("A", root.Children.Single().Label.Text);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new MindmapDiagram { AccessibleTitle = "Title" };
        diagram.SetRoot("A");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.AccessibleTitle = null;
        diagram.Root!.Icon = "a)b";
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
