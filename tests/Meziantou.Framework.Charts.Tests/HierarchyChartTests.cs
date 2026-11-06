namespace Meziantou.Framework.Charts.Tests;

public sealed class HierarchyChartTests
{
    [Fact]
    public void PackedCircles()
    {
        #region Charts_PackedCircles
        FileEntry[] files =
        [
            new("src/app/main.cs", 120), new("src/app/view.cs", 80), new("src/app/model.cs", 60),
            new("src/core/parser.cs", 140), new("src/core/lexer.cs", 90), new("src/core/ast.cs", 45),
            new("tests/app.tests.cs", 70), new("tests/core.tests.cs", 85),
            new("docs/readme.md", 40), new("docs/guide.md", 55),
        ];

        var root = Hierarchy.FromPaths(files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot { Title = "Size of the files", Width = 480, Height = 520 };
        plot.Add(new PackMark<FileEntry?>(root));
        #endregion

        ChartAssert.Svg(plot, "PackedCircles");
    }

    [Fact]
    public void Treemap()
    {
        #region Charts_Treemap
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot { Title = "Size of the files" };
        plot.Add(new TreemapMark<FileEntry?>(root) { PaddingTop = 18, PaddingOuter = 2, PaddingInner = 2 });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "Treemap");
    }

    [Theory]
    [InlineData(TreemapTiling.Binary)]
    [InlineData(TreemapTiling.Slice)]
    [InlineData(TreemapTiling.Dice)]
    [InlineData(TreemapTiling.SliceDice)]
    public void TreemapTilings(TreemapTiling tiling)
    {
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot { Width = 400, Height = 300 };
        plot.Add(new TreemapMark<FileEntry?>(root) { Tiling = tiling, ShowValues = false, Fill = node => node.Depth });
        Snapshot(plot, tiling.ToString());
    }

    [Fact]
    public void Sunburst()
    {
        #region Charts_Sunburst
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot
        {
            Title = "Size of the files",
            Width = 480,
            Height = 480,
            Coordinates = new PolarCoordinates { InnerRadius = 0.2 },
        };
        plot.Add(new PartitionMark<FileEntry?>(root));
        #endregion

        ChartAssert.Svg(plot, "Sunburst");
    }

    [Fact]
    public void Icicle()
    {
        #region Charts_Icicle
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot { Title = "Size of the files", Coordinates = new CartesianCoordinates { Transposed = true } };
        plot.Add(new PartitionMark<FileEntry?>(root) { ShowRoot = true, Label = node => node.Depth is 0 ? "all" : node.Name });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "Icicle");
    }

    [Fact]
    public void VerticalIcicleWithSingleColor()
    {
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size);
        var plot = new Plot { Height = 300 };
        plot.Add(new PartitionMark<FileEntry?>(root) { FillColor = "steelblue", Title = node => node.Path });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void HierarchyFromObjects()
    {
        #region Charts_HierarchyFromTree
        var tree = new Department("Company", 0,
        [
            new("Engineering", 0, [new("Platform", 12, []), new("Product", 18, []), new("Data", 7, [])]),
            new("Sales", 0, [new("Europe", 9, []), new("Americas", 11, [])]),
            new("Support", 6, []),
        ]);

        var root = Hierarchy.FromTree(tree, children: department => department.Teams, name: department => department.Name, value: department => department.People);
        Assert.Equal(63, root.Value);

        var plot = new Plot { Title = "People by team", Width = 400, Height = 440 };
        plot.Add(new PackMark<Department>(root) { Padding = 4 });
        #endregion

        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TreeDiagram()
    {
        #region Charts_TreeDiagram
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path);
        var plot = new Plot { Title = "Files of the repository", Height = 420, Coordinates = new CartesianCoordinates { Transposed = true } };
        plot.Add(new TreeMark<FileEntry?>(root) { Label = node => node.Depth is 0 ? "repository" : node.Name });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "TreeDiagram");
    }

    [Fact]
    public void RadialDendrogram()
    {
        #region Charts_RadialDendrogram
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path);
        var plot = new Plot { Title = "Files of the repository", Width = 520, Height = 520, Coordinates = new PolarCoordinates() };
        plot.Add(new TreeMark<FileEntry?>(root) { Layout = TreeLayoutKind.Cluster });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "RadialDendrogram");
    }

    [Fact]
    public void VerticalTree()
    {
        var root = Hierarchy.FromTree(new Department("CEO", 1, [new("CTO", 1, [new("Platform", 1, []), new("Data", 1, [])]), new("CFO", 1, [new("Accounting", 1, [])]), new("COO", 1, [])]), department => department.Teams, department => department.Name);
        var plot = new Plot { Height = 300, Theme = ChartTheme.Dark };
        plot.Add(new TreeMark<Department>(root) { NodeRadius = 5, Fill = node => node.Depth });
        plot.Add(new TreeMark<Department>(root) { Layout = TreeLayoutKind.Cluster, Label = _ => null, NodeRadius = 2, Opacity = 0.3 });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void PackWithCustomLabelsAndColors()
    {
        var root = Hierarchy.FromPaths(SampleData.Files, file => file.Path, value: file => file.Size).SortByValue();
        var plot = new Plot { Width = 360, Height = 360, Theme = ChartTheme.Dark };
        plot.Add(new PackMark<FileEntry?>(root) { Padding = 1, Fill = node => node.Value, Label = node => node.IsLeaf ? node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null });
        plot.ColorScale = new SequentialColorScale { Scheme = ColorScheme.Magma };
        ChartAssert.Svg(plot);
    }

    private static void Snapshot(Plot plot, string name, [System.Runtime.CompilerServices.CallerFilePath] string? filePath = null, [System.Runtime.CompilerServices.CallerMemberName] string? memberName = null)
        => ChartAssert.Svg(plot, documentationImage: null, filePath, -1, memberName + "_" + name);
}
