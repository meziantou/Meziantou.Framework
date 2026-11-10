using System.Globalization;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class TreeViewDiagramTests
{
    [Fact]
    public void TreeViewDiagram()
    {
        #region Diagrams_TreeViewDiagram
        var diagram = new TreeViewDiagram { ShowIcons = true };
        var src = diagram.AddNode("src", isDirectory: true);
        var app = src.AddChild("App.tsx");
        app.CssClass = "highlight";
        app.Description = "main component";
        src.AddChild("index.ts").Description = "entry point";
        var tests = diagram.AddNode("tests", isDirectory: true);
        tests.AddChild("app.test.ts");
        diagram.AddNode(".env").Description = "environment variables";
        diagram.AddNode("package.json");
        #endregion

        Assert.Equal(["src", "App.tsx", "index.ts", "tests", "app.test.ts", ".env", "package.json"], diagram.EnumerateNodes().Select(node => node.Name));
        DiagramAssert.Svg(diagram, "TreeViewDiagram");
    }

    [Fact]
    public void IconsColorsAndThemes()
    {
        var diagram = new TreeViewDiagram
        {
            Theme = DiagramTheme.Dark,
            ShowIcons = true,
            DefaultIconPack = "material-icon-theme",
            RowIndent = 20,
            PaddingX = 8,
            PaddingY = 2,
            LineThickness = 2,
            FontSize = 12,
            LineColor = (SceneColor)"#00aa00",
            IconColor = (SceneColor)"#ff8800",
            DescriptionColor = (SceneColor)"#8888ff",
            HighlightBackground = (SceneColor)"#ffeeee",
            HighlightBorderColor = (SceneColor)"#ff0000",
        };
        diagram.FileNameIcons["Dockerfile"] = "docker";
        diagram.ExtensionIcons[".txt"] = "none";
        diagram.ExtensionIcons["md"] = "file";
        var folder = diagram.AddNode("folder", isDirectory: true);
        folder.AddChild("Dockerfile");
        folder.AddChild("notes.txt");
        folder.AddChild("README.MD");
        folder.AddChild("hidden icon").Icon = "none";
        folder.AddChild("pack icon").Icon = "logos:react";
        var directory = diagram.AddNode("A  name with  spaces ", isDirectory: true);
        directory.CssClass = "other highlight";
        directory.Description = "  described  ";
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void EmptyTree()
    {
        var diagram = new TreeViewDiagram { Title = "Empty" };
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void SharedNodesAreDrawnOnce()
    {
        var diagram = new TreeViewDiagram();
        var a = diagram.AddNode("a", isDirectory: true);
        var b = a.AddChild("b", isDirectory: true);
        b.Children.Add(a);
        b.Children.Add(b);
        diagram.Nodes.Add(b);
        Assert.Equal(["a", "b"], diagram.EnumerateNodes().Select(node => node.Name));
        var svg = diagram.ToSvg();
        Assert.Equal(3, svg.Split("<text", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DeepTreesAreDrawn()
    {
        var diagram = new TreeViewDiagram();
        var node = diagram.AddNode("0", isDirectory: true);
        for (var i = 1; i < 2000; i++)
        {
            node = node.AddChild(i.ToString(CultureInfo.InvariantCulture), isDirectory: true);
        }

        Assert.StartsWith("<svg", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValuesAreBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreeViewDiagram { RowIndent = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreeViewDiagram { PaddingX = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreeViewDiagram { PaddingY = 2000 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreeViewDiagram { LineThickness = 101 });
        Assert.Throws<ArgumentNullException>("name", () => new TreeViewNode(null!));
        Assert.Throws<ArgumentNullException>("value", () => new TreeViewNode("a").Name = null!);
    }
}
