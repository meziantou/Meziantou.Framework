namespace Meziantou.Framework.Mermaid.Tests;

public sealed class TreeViewDiagramParserTests
{
    [Fact]
    public void IndentedNodes()
    {
        #region Mermaid_TreeViewDiagram
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("""
            ---
            config:
              treeView:
                showIcons: true
            ---
            treeView-beta
                src/
                    App.tsx :::highlight icon(logos:react) ## main component
                    index.ts ## entry point
                "my notes.txt"
                package.json
            """).Diagram;
        #endregion

        Assert.True(diagram.ShowIcons);
        Assert.Equal(["src", "my notes.txt", "package.json"], diagram.Nodes.Select(node => node.Name));
        var src = diagram.Nodes[0];
        Assert.True(src.IsDirectory);
        Assert.False(diagram.Nodes[1].IsDirectory);
        var app = src.Children[0];
        Assert.Equal(("App.tsx", "highlight", "logos:react", "main component"), (app.Name, app.CssClass, app.Icon, app.Description));
        Assert.Equal(("index.ts", null, null, "entry point"), (src.Children[1].Name, src.Children[1].CssClass, src.Children[1].Icon, src.Children[1].Description));
    }

    [Fact]
    public void BoxDrawingNodes()
    {
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("""
            treeView-beta
            ├── packages/
            │   ├── mermaid/
            │   │   ├── src/
            │   │   │   └── parser.ts
            │   │   └── package.json
            │   │
            │   └── parser/
            ┣━━ heavy/
            ┃   ┗━━ child
            └── README.md ## read me
            """).Diagram;
        Assert.Equal(["packages", "heavy", "README.md"], diagram.Nodes.Select(node => node.Name));
        var mermaid = diagram.Nodes[0].Children[0];
        Assert.Equal(["src", "package.json"], mermaid.Children.Select(node => node.Name));
        Assert.Equal("parser.ts", mermaid.Children[0].Children.Single().Name);
        Assert.Equal("parser", diagram.Nodes[0].Children[1].Name);
        Assert.Equal("child", diagram.Nodes[1].Children.Single().Name);
        Assert.Equal("read me", diagram.Nodes[2].Description);
    }

    [Fact]
    public void BoxDrawingNodesWithoutBranches()
    {
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("treeView-beta\nroot/\n\t├── a\n\t│\t└── b\nSection ─ A.txt\n").Diagram;
        Assert.Equal(["root", "Section ─ A.txt"], diagram.Nodes.Select(node => node.Name));
        Assert.Equal("b", diagram.Nodes[0].Children.Single().Children.Single().Name);
    }

    [Fact]
    public void NamesAndAnnotations()
    {
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("""
            treeView-beta
                a  b  %% a comment
                'it"s' :::cls ## first ## second %% comment
                c :::   other-class icon() :::last
                d ##
                e icon(none)
                f icon(pack:name)
                "" icon(:x)
                g:::h
                i #j
                "dir/"
                %% a comment line
                j ::: 1
            """).Diagram;
        Assert.Equal(["a  b", "it\"s", "c", "d", "e", "f", "", "g:::h", "i #j", "dir", "j ::: 1"], diagram.Nodes.Select(node => node.Name));
        Assert.Equal(("cls", "first ## second"), (diagram.Nodes[1].CssClass, diagram.Nodes[1].Description));
        Assert.Equal(("last", "none"), (diagram.Nodes[2].CssClass, diagram.Nodes[2].Icon));
        Assert.Null(diagram.Nodes[3].Description);
        Assert.Equal(["none", "pack:name", ":x"], diagram.Nodes.Skip(4).Take(3).Select(node => node.Icon));
        Assert.True(diagram.Nodes[9].IsDirectory);
    }

    [Fact]
    public void NestingFollowsTheIndentation()
    {
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("treeView-beta\n  a\n      b\n    c\n d\n\t\te\n").Diagram;
        Assert.Equal(["a", "d"], diagram.Nodes.Select(node => node.Name));
        Assert.Equal(["b", "c"], diagram.Nodes[0].Children.Select(node => node.Name));
        Assert.Equal("e", diagram.Nodes[1].Children.Single().Name);
    }

    [Fact]
    public void TitleAccessibilityAndConfiguration()
    {
        var diagram = (TreeViewDiagram)MermaidDocument.Parse("""
            ---
            config:
              treeView:
                rowIndent: 80
                paddingX: 2px
                paddingY: 3
                lineThickness: 3
                diagramPadding: 12
                defaultIconPack: material-icon-theme
                filenameIcons:
                  Dockerfile: docker
                extensionIcons:
                  .ts: typescript
              themeVariables:
                treeView:
                  labelFontSize: '20px'
                  labelColor: '#FF0000'
                  lineColor: '#00FF00'
                  iconColor: red
                  descriptionColor: blue
                  highlightBg: '#ffffff'
                  highlightStroke: black
            ---
            treeView-beta
                title Files %% comment
                accTitle: Accessible title
                accDescr {
                    Line 1
                    Line 2
                }
                a
            """).Diagram;
        Assert.Equal(("Files", "Accessible title", "Line 1\nLine 2"), (diagram.Title, diagram.AccessibleTitle, diagram.Description));
        Assert.Equal((80.0, 2.0, 3.0, 3.0, 12.0), (diagram.RowIndent, diagram.PaddingX, diagram.PaddingY, diagram.LineThickness, diagram.Padding));
        Assert.Equal("material-icon-theme", diagram.DefaultIconPack);
        Assert.Equal("docker", diagram.FileNameIcons["Dockerfile"]);
        Assert.Equal("typescript", diagram.ExtensionIcons[".ts"]);
        Assert.Equal(20.0, diagram.FontSize);
        Assert.Equal((SceneColor)"#FF0000", diagram.LabelColor);
        Assert.Equal((SceneColor)"#00FF00", diagram.LineColor);
        Assert.Equal((SceneColor)"red", diagram.IconColor);
        Assert.Equal((SceneColor)"blue", diagram.DescriptionColor);
        Assert.Equal((SceneColor)"#ffffff", diagram.HighlightBackground);
        Assert.Equal((SceneColor)"black", diagram.HighlightBorderColor);
        Assert.Equal("a", diagram.Nodes.Single().Name);
    }

    [Fact]
    public void UnsupportedValuesAreWarnings()
    {
        var document = MermaidDocument.Parse("---\nconfig:\n  treeView:\n    rowIndent: -5\n  themeVariables:\n    treeView:\n      labelColor: not-a-color\n---\ntreeView-beta\n    a\n");
        Assert.Equal(10.0, ((TreeViewDiagram)document.Diagram).RowIndent);
        Assert.Contains("not a supported color", document.Warnings.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DepthIsLimited()
    {
        var text = "treeView-beta\n" + string.Concat(Enumerable.Range(0, 40).Select(i => new string(' ', i + 1) + "n\n"));
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("deeper than 32 levels", exception.Message, StringComparison.Ordinal);
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("treeView-beta\na\nb\nc\n", new MermaidParseOptions { MaxNodes = 2 }));
    }

    [Theory]
    [InlineData("treeView-beta a", "The nodes start on the line after 'treeView-beta'.")]
    [InlineData("treeView-beta\n    \"a", "The name is not closed by its quote.")]
    [InlineData("treeView-beta\n    \"a\"b", "Unexpected 'b'")]
    [InlineData("treeView-beta\n    \"a %% b\"", "A name cannot contain '%%'.")]
    [InlineData("treeView-beta\n    a icon(bad name)", "an annotation is expected")]
    [InlineData("treeView-beta\n    a ## b\n    \"c\" :::", "an annotation is expected")]
    [InlineData("treeView-beta\n    :::cls", "a name is expected before the annotations")]
    [InlineData("treeView-beta\n    icon(x)", "a name is expected before the annotations")]
    [InlineData("treeView-beta\n    a\n    title Late", "The title and the accessibility statements come before the nodes.")]
    [InlineData("treeView-beta\n├── a\n    b", "indents its nodes with '├──' or '└──', not with spaces")]
    [InlineData("treeView-beta\n├──\n", "The node has no name after its branch.")]
    [InlineData("treeView-beta\n    accDescr {\n    a", "The description is not closed by '}'.")]
    [InlineData("treeView-beta\n    ##x", "a name is expected before the annotations")]
    public void InvalidTreeViewDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new TreeViewDiagram
        {
            Title = "Files",
            AccessibleTitle = "Accessible",
            Description = "Line 1\nLine 2",
            RowIndent = 20,
            PaddingX = 4,
            PaddingY = 6,
            LineThickness = 2,
            Padding = 10,
            ShowIcons = true,
            DefaultIconPack = "material-icon-theme",
            FontSize = 14,
            LabelColor = (SceneColor)"#112233",
            LineColor = (SceneColor)"#445566",
            IconColor = (SceneColor)"#778899",
            DescriptionColor = (SceneColor)"#aabbcc",
            HighlightBackground = (SceneColor)"rgba(255, 0, 0, 0.5)",
            HighlightBorderColor = (SceneColor)"#ddeeff",
        };
        diagram.FileNameIcons["Docker file"] = "docker";
        diagram.ExtensionIcons[".ts"] = "typescript";
        var src = diagram.AddNode("src", isDirectory: true);
        var app = src.AddChild("App.tsx");
        app.CssClass = "highlight";
        app.Icon = "logos:react";
        app.Description = " main component ";
        src.AddChild("a :::b ## c").Icon = "";
        src.AddChild("title");
        src.AddChild("say \"hi\"", isDirectory: true);
        src.AddChild(" spaced ");
        src.AddChild("x icon(y)");
        src.AddChild(string.Empty);
        diagram.AddNode("icon(z)").CssClass = string.Empty;

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (TreeViewDiagram)parsed.Diagram;
        Assert.Equal(("Files", "Accessible", "Line 1\nLine 2"), (result.Title, result.AccessibleTitle, result.Description));
        Assert.Equal((20.0, 4.0, 6.0, 2.0, 10.0, true, "material-icon-theme"), (result.RowIndent, result.PaddingX, result.PaddingY, result.LineThickness, result.Padding, result.ShowIcons, result.DefaultIconPack));
        Assert.Equal(("docker", "typescript"), (result.FileNameIcons["Docker file"], result.ExtensionIcons[".ts"]));
        Assert.Equal((14.0, (SceneColor?)(SceneColor)"#112233", (SceneColor?)(SceneColor)"rgba(255, 0, 0, 0.5)"), (result.FontSize, result.LabelColor, result.HighlightBackground));
        Assert.Equal(["src", "App.tsx", "a :::b ## c", "title", "say \"hi\"", " spaced ", "x icon(y)", "", "icon(z)"], result.EnumerateNodes().Select(node => node.Name));
        var resultApp = result.Nodes[0].Children[0];
        Assert.Equal(("highlight", "logos:react", "main component"), (resultApp.CssClass, resultApp.Icon, resultApp.Description));
        Assert.Equal("none", result.Nodes[0].Children[1].Icon);
        Assert.True(result.Nodes[0].Children[3].IsDirectory);
    }

    [Fact]
    public void WriteUsesBoxDrawingWhenANameHasABoxCharacter()
    {
        var diagram = new TreeViewDiagram();
        var a = diagram.AddNode("a", isDirectory: true);
        a.AddChild("Section ─ A.txt").AddChild("├── x");
        a.AddChild("b").Description = "│ described";
        diagram.AddNode("c\td");

        var text = MermaidWriter.Write(diagram);
        Assert.Equal("treeView-beta\n├── a/\n│   ├── \"Section ─ A.txt\"\n│   │   └── \"├── x\"\n│   └── b ## │ described\n└── c\td\n", text);
        var result = (TreeViewDiagram)MermaidDocument.Parse(text).Diagram;
        Assert.Equal(text, MermaidWriter.Write(result));
        Assert.Equal("├── x", result.Nodes[0].Children[0].Children.Single().Name);
        Assert.Equal("c\td", result.Nodes[1].Name);
    }

    [Fact]
    public void WriteUsesBoxDrawingForTitlesThatCannotBeQuoted()
    {
        var diagram = new TreeViewDiagram();
        diagram.AddNode("title \"a'");
        diagram.AddNode("titles");

        var text = MermaidWriter.Write(diagram);
        Assert.Equal("treeView-beta\n├── title \"a'\n└── \"titles\"\n", text);
        Assert.Equal(["title \"a'", "titles"], ((TreeViewDiagram)MermaidDocument.Parse(text).Diagram).Nodes.Select(node => node.Name));
    }

    [Fact]
    public void WriteSkipsRepeatedNodes()
    {
        var diagram = new TreeViewDiagram();
        var a = diagram.AddNode("a");
        var b = a.AddChild("b");
        b.Children.Add(a);
        diagram.Nodes.Add(b);
        Assert.Equal("treeView-beta\na\n    b\n", MermaidWriter.Write(diagram));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a %% b")]
    [InlineData("file/")]
    [InlineData("\"'")]
    public void WriteRejectsNames(string name)
    {
        var diagram = new TreeViewDiagram();
        diagram.AddNode(name);
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }

    [Fact]
    public void WriteRejectsInvalidAnnotations()
    {
        Assert.Throws<ArgumentException>("node", () => Write(node => node.CssClass = "1class"));
        Assert.Throws<ArgumentException>("node", () => Write(node => node.Icon = "a:b:c"));
        Assert.Throws<ArgumentException>("value", () => Write(node => node.Description = "a %% b"));
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(new TreeViewDiagram { FontSize = -1 }));

        static string Write(Action<TreeViewNode> configure)
        {
            var diagram = new TreeViewDiagram();
            configure(diagram.AddNode("a"));
            return MermaidWriter.Write(diagram);
        }
    }
}
