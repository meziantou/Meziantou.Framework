namespace Meziantou.Framework.Charts.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void Squarify_BrulsExample()
    {
        // Bruls, Huizing and van Wijk (2000), figure 4: 6, 6, 4, 3, 2, 2, 1 in a 6 by 4 rectangle, with an aspect ratio of 1
        var boxes = TreemapLayout.Squarify([6, 6, 4, 3, 2, 2, 1], 24, new TreemapLayout.Box(0, 0, 6, 4), ratio: 1);
        AssertBox(0, 0, 3, 2, boxes[0]);
        AssertBox(0, 2, 3, 4, boxes[1]);
        AssertBox(3, 0, 3 + (12 / 7.0), 7 / 3.0, boxes[2]);
        AssertBox(3 + (12 / 7.0), 0, 6, 7 / 3.0, boxes[3]);
        AssertBox(3, 7 / 3.0, 4.2, 4, boxes[4]);
        AssertBox(4.2, 7 / 3.0, 5.4, 4, boxes[5]);
        AssertBox(5.4, 7 / 3.0, 6, 4, boxes[6]);
    }

    [Fact]
    public void Squarify_SkipsEmptyValues()
    {
        var boxes = TreemapLayout.Squarify([0, 3, 1], 4, new TreemapLayout.Box(0, 0, 4, 4), ratio: 1);
        Assert.Equal(0, Area(boxes[0]), 1e-9);
        Assert.Equal(12, Area(boxes[1]), 1e-9);
        Assert.Equal(4, Area(boxes[2]), 1e-9);
    }

    [Fact]
    public void SliceDiceAndBinary()
    {
        var box = new TreemapLayout.Box(0, 0, 10, 4);
        var dice = TreemapLayout.Dice([1, 3], 4, box);
        AssertBox(0, 0, 2.5, 4, dice[0]);
        AssertBox(2.5, 0, 10, 4, dice[1]);
        var slice = TreemapLayout.Slice([1, 3], 4, box);
        AssertBox(0, 0, 10, 1, slice[0]);
        AssertBox(0, 1, 10, 4, slice[1]);
        var binary = TreemapLayout.Binary([1, 1, 2], 4, box);
        Assert.Equal([10, 10, 20], binary.Select(Area).ToArray());
        Assert.Empty(TreemapLayout.Binary([], 0, box));
        var zero = TreemapLayout.Dice([0, 0], 0, box);
        Assert.Equal(0, zero[1].X0);
        var tallBinary = TreemapLayout.Binary([1, 1], 2, new TreemapLayout.Box(0, 0, 2, 10));
        AssertBox(0, 0, 2, 5, tallBinary[0]);
        var shrunk = TreemapLayout.Shrink(new TreemapLayout.Box(0, 0, 2, 2), 3, 3, 3, 3);
        AssertBox(1, 1, 1, 1, shrunk);
    }

    [Theory]
    [InlineData(TreemapTiling.Squarified)]
    [InlineData(TreemapTiling.Binary)]
    [InlineData(TreemapTiling.Slice)]
    [InlineData(TreemapTiling.Dice)]
    [InlineData(TreemapTiling.SliceDice)]
    public void Treemap_AreasAreProportionalToTheValues(TreemapTiling tiling)
    {
        #region Charts_TreemapLayout
        var root = Hierarchy.FromPaths(["a/x", "a/y", "b"], path => path, value: path => path.Length).SortByValue();
        var layout = new TreemapLayout { Width = 300, Height = 200, Tiling = tiling };
        var rectangles = layout.Layout(root);
        #endregion

        Assert.HasCount(5, rectangles);
        var leaves = rectangles.Where(rectangle => rectangle.Node.IsLeaf).ToArray();
        Assert.Equal(60000, leaves.Sum(rectangle => rectangle.Width * rectangle.Height), 1e-6);
        foreach (var leaf in leaves)
        {
            Assert.Equal(60000 * leaf.Node.Value / root.Value, leaf.Width * leaf.Height, 1e-6);
        }
    }

    [Fact]
    public void Treemap_Padding()
    {
        var root = Hierarchy.FromPaths(["a/x", "a/y"], path => path, value: _ => 1);
        var rectangles = new TreemapLayout { Width = 100, Height = 100, PaddingInner = 2, PaddingOuter = 4, PaddingTop = 20 }.Layout(root);
        var a = rectangles.Single(rectangle => rectangle.Node.Name == "a");
        var x = rectangles.Single(rectangle => rectangle.Node.Name == "x");
        Assert.Equal(4, a.X0, 1e-9);
        Assert.Equal(20, a.Y0, 1e-9);
        Assert.Equal(40, x.Y0, 1e-9); // PaddingTop applies to every group, the root included, as in d3
        Assert.Equal(8, x.X0, 1e-9);
    }

    [Fact]
    public void Partition_LayersByDepth()
    {
        var root = Hierarchy.FromPaths(["a/x", "a/y", "b"], path => path, value: path => path.Length is 1 ? 2 : 1);
        var rectangles = new PartitionLayout { Width = 100, Height = 90, Padding = 1 }.Layout(root);
        var byName = rectangles.ToDictionary(rectangle => rectangle.Node.Name);
        AssertBox(0, 0, 99, 29, byName[string.Empty].X0, byName[string.Empty].Y0, byName[string.Empty].X1, byName[string.Empty].Y1);
        AssertBox(0, 30, 49, 59, byName["a"].X0, byName["a"].Y0, byName["a"].X1, byName["a"].Y1);
        AssertBox(50, 30, 99, 59, byName["b"].X0, byName["b"].Y0, byName["b"].X1, byName["b"].Y1);
        AssertBox(25, 60, 49, 89, byName["y"].X0, byName["y"].Y0, byName["y"].X1, byName["y"].Y1);
    }

    [Fact]
    public void Sankey_TwoNodes()
    {
        // A single link fills the height; the nodes are at the two ends
        var layout = SankeyLayout.Compute([("A", "B", 10)], new ChartRectangle(0, 0, 100, 100), nodeWidth: 10, nodePadding: 10, iterations: 6, SankeyAlignment.Justify);
        var (a, b) = (layout.Nodes[0], layout.Nodes[1]);
        Assert.Equal((0.0, 10.0, 0.0, 100.0), (a.X0, a.X1, a.Y0, a.Y1));
        Assert.Equal((90.0, 100.0, 0.0, 100.0), (b.X0, b.X1, b.Y0, b.Y1));
        Assert.Equal((100.0, 50.0, 50.0), (layout.Links[0].Width, layout.Links[0].Y0, layout.Links[0].Y1));
    }

    [Fact]
    public void Sankey_DepthsValuesAndAlignment()
    {
        (string, string, double)[] links = [("A", "B", 4), ("B", "C", 3), ("B", "D", 1), ("E", "C", 2)];
        var layout = SankeyLayout.Compute(links, new ChartRectangle(0, 0, 300, 100), nodeWidth: 20, nodePadding: 5, iterations: 6, SankeyAlignment.Justify);
        var nodes = layout.Nodes.ToDictionary(node => node.Name);
        Assert.Equal([0, 1, 2, 2, 0], layout.Nodes.Select(node => node.Depth).ToArray());
        Assert.Equal([2, 1, 0, 0, 1], layout.Nodes.Select(node => node.Height).ToArray());
        Assert.Equal([4, 4, 5, 1, 2], layout.Nodes.Select(node => node.Value).ToArray());

        // Justified: the sinks are in the last column; E has a single link, to the last column, so it stays in its own
        Assert.Equal([0, 1, 2, 2, 0], layout.Nodes.Select(node => node.Layer).ToArray());
        Assert.Equal(280, nodes["C"].X0);

        var right = SankeyLayout.Compute(links, new ChartRectangle(0, 0, 300, 100), 20, 5, 6, SankeyAlignment.Right);
        Assert.Equal(1, right.Nodes.Single(node => node.Name == "E").Layer);
        var center = SankeyLayout.Compute(links, new ChartRectangle(0, 0, 300, 100), 20, 5, 6, SankeyAlignment.Center);
        Assert.Equal(1, center.Nodes.Single(node => node.Name == "E").Layer);

        // Each node is as tall as its value, at the same scale, and the nodes of a column do not overlap
        var scale = (nodes["A"].Y1 - nodes["A"].Y0) / 4;
        Assert.Equal(5 * scale, nodes["C"].Y1 - nodes["C"].Y0, 1e-9);
        Assert.True(nodes["D"].Y0 >= nodes["C"].Y1 + 5 - 1e-9 || nodes["C"].Y0 >= nodes["D"].Y1 + 5 - 1e-9);
        Assert.Empty(SankeyLayout.Compute([], new ChartRectangle(0, 0, 10, 10), 1, 1, 6, SankeyAlignment.Justify).Nodes);
    }

    [Fact]
    public void Chord_TwoNodes()
    {
        // d3.chord: the rows total 2 and 1, so a unit of value is 2π / 3 radians; the chord between the nodes starts on
        // the arc of A (its flow to B, 2) and ends on the arc of B (its flow to A, 1)
        var (groups, chords) = ChordLayout.Compute([[0, 2], [1, 0]], padAngle: 0);
        Assert.Equal([(0, 0, 4 * Math.PI / 3, 2.0), (1, 4 * Math.PI / 3, 2 * Math.PI, 1.0)], groups.Select(group => (group.Index, group.StartAngle, group.EndAngle, group.Value)).ToArray());
        var chord = Assert.Single(chords);
        Assert.Equal(new ChordSubgroup(0, 0, 4 * Math.PI / 3, 2), chord.Source);
        Assert.Equal(new ChordSubgroup(1, 4 * Math.PI / 3, 2 * Math.PI, 1), chord.Target);
    }

    [Fact]
    public void Chord_PaddingSwapsAndSelfFlows()
    {
        // With a gap of 0.1 after each arc, a unit of value is (2π - 0.2) / 4 radians. The flow from B to A is the
        // larger one, so the end on B is the source of the chord.
        var k = ((2 * Math.PI) - 0.2) / 4;
        var (groups, chords) = ChordLayout.Compute([[0, 1], [3, 0]], padAngle: 0.1);
        Assert.Equal((0.0, k), (groups[0].StartAngle, groups[0].EndAngle));
        Assert.Equal((k + 0.1, (4 * k) + 0.1), (groups[1].StartAngle, groups[1].EndAngle));
        var chord = Assert.Single(chords);
        Assert.Equal(new ChordSubgroup(1, k + 0.1, (4 * k) + 0.1, 3), chord.Source);
        Assert.Equal(new ChordSubgroup(0, 0, k, 1), chord.Target);

        // A flow from a node to itself is a chord whose two ends are the same
        var (_, self) = ChordLayout.Compute([[2]], padAngle: 0);
        Assert.Same(self[0].Source, self[0].Target);
        Assert.Equal(new ChordSubgroup(0, 0, 2 * Math.PI, 2), self[0].Source);

        // Without flows, the empty arcs are evenly spaced
        var (empty, none) = ChordLayout.Compute([[0, 0], [0, 0]], padAngle: 0.1);
        Assert.Empty(none);
        Assert.Equal([0, Math.PI], empty.Select(group => group.StartAngle).ToArray());

        // Gaps larger than the circle leave no room for the arcs
        var (crowded, _) = ChordLayout.Compute([[0, 1], [1, 0]], padAngle: 4);
        Assert.All(crowded, group => Assert.Equal(group.StartAngle, group.EndAngle));
    }

    [Fact]
    public void Chord_Ribbons()
    {
        // The arc of the source, a curve through the center, the arc of the target, a curve back
        var ribbon = ChordLayout.Ribbon(0, 0, 10, new ChordSubgroup(0, 0, Math.PI / 2, 1), new ChordSubgroup(1, Math.PI, 3 * Math.PI / 2, 1));
        Assert.Equal("M0,-10 A10,10 0 0,1 10,0 Q0,0 0,10 A10,10 0 0,1 -10,0 Q0,0 0,-10 Z", ribbon.ToString());

        var self = new ChordSubgroup(0, 0, Math.PI / 2, 1);
        Assert.Equal("M0,-10 A10,10 0 0,1 10,0 Q0,0 0,-10 Z", ChordLayout.Ribbon(0, 0, 10, self, self).ToString());

        var full = new ChordSubgroup(0, 0, 2 * Math.PI, 1);
        Assert.Equal("M0,-10 A10,10 0 0,1 0,10 A10,10 0 0,1 0,-10 Q0,0 0,-10 Z", ChordLayout.Ribbon(0, 0, 10, full, full).ToString());

        var empty = new ChordSubgroup(0, 1, 1, 0);
        Assert.Equal(4, ChordLayout.Ribbon(0, 0, 10, empty, self).Commands.Count(command => command.Kind is not PathCommandKind.ArcTo));
    }

    [Fact]
    public void Force_StartsOnThePhyllotaxisSpiral()
    {
        // d3-force: the node i starts at a radius of 10 √(0.5 + i), at an angle of i π (3 - √5)
        var layout = new ForceLayout(3, []);
        Assert.Equal((10 * Math.Sqrt(0.5), 0.0), (layout.X[0], layout.Y[0]));
        var angle = Math.PI * (3 - Math.Sqrt(5));
        Assert.Equal(10 * Math.Sqrt(1.5) * Math.Cos(angle), layout.X[1], 12);
        Assert.Equal(10 * Math.Sqrt(1.5) * Math.Sin(angle), layout.Y[1], 12);
        Assert.Equal(10 * Math.Sqrt(2.5) * Math.Sin(2 * angle), layout.Y[2], 12);
        Assert.Equal(300, ForceLayout.DefaultTicks);
    }

    [Fact]
    public void Force_LinkPullsTowardItsLength()
    {
        // Two nodes 50 apart, linked with a length of 30: each end gets half of (50 - 30) / 50 × 50 × alpha, the velocity
        // keeps 60% of it, alpha is 0.001^(1/300) after the first tick, and the centering moves the middle to the origin
        var alpha = Math.Pow(0.001, 1.0 / 300);
        var layout = new ForceLayout(2, [(0, 1)]) { ManyBodyStrength = 0 };
        (layout.X[0], layout.Y[0], layout.X[1], layout.Y[1]) = (0, 0, 50, 0);
        layout.Tick();
        Assert.Equal(alpha, layout.Alpha, 12);
        Assert.Equal((6 * alpha) - 25, layout.X[0], 5);
        Assert.Equal(25 - (6 * alpha), layout.X[1], 5);
        Assert.Equal(0, layout.Y[0], 5);
    }

    [Fact]
    public void Force_ManyBodyRepels()
    {
        // Two nodes 10 apart: each one gets strength × alpha × distance / distance² = -30 × alpha / 10 away from the other,
        // 60% of it is kept, then the middle moves to the origin
        var alpha = Math.Pow(0.001, 1.0 / 300);
        var layout = new ForceLayout(2, []);
        (layout.X[0], layout.Y[0], layout.X[1], layout.Y[1]) = (0, 0, 10, 0);
        layout.Tick();
        Assert.Equal((-1.8 * alpha) - 5, layout.X[0], 5);
        Assert.Equal(5 + (1.8 * alpha), layout.X[1], 5);

        // Closer than 1, the squared distance is replaced by the distance: 0.5 × -30 × alpha / 0.5, 60% of it kept
        var close = new ForceLayout(2, []);
        (close.X[0], close.Y[0], close.X[1], close.Y[1]) = (0, 0, 0.5, 0);
        close.Tick();
        Assert.Equal((-0.6 * 30 * alpha) - 0.25, close.X[0], 5);
    }

    [Fact]
    public void Force_CentersTheNodes()
    {
        var layout = new ForceLayout(2, []) { ManyBodyStrength = 0 };
        (layout.X[0], layout.Y[0], layout.X[1], layout.Y[1]) = (10, 4, 20, 8);
        layout.Tick();
        Assert.Equal([-5.0, 5.0], layout.X);
        Assert.Equal([-2.0, 2.0], layout.Y);

        // Coincident nodes are separated by the jiggle of the seeded generator, the same on every run
        var coincident = new ForceLayout(2, [(0, 1)]);
        (coincident.X[0], coincident.Y[0], coincident.X[1], coincident.Y[1]) = (0, 0, 0, 0);
        coincident.Run(ForceLayout.DefaultTicks);
        var again = new ForceLayout(2, [(0, 1)]);
        (again.X[0], again.Y[0], again.X[1], again.Y[1]) = (0, 0, 0, 0);
        again.Run(ForceLayout.DefaultTicks);
        Assert.Equal(coincident.X, again.X);
        Assert.True(Math.Abs(coincident.X[0] - coincident.X[1]) + Math.Abs(coincident.Y[0] - coincident.Y[1]) > 1);
    }

    [Fact]
    public void Hexbin_MatchesD3()
    {
        // d3-hexbin with a radius of 10: the columns are 10√3 apart, the rows 15 apart, and the odd rows are shifted by
        // half a column
        var dx = 10 * Math.Sqrt(3);
        (double, double)[] points = [(0, 0), (2, 3), (dx / 2, 15), (dx, 0), (dx / 2, 7), (dx / 2, 8), (double.NaN, 0)];
        var bins = Hexbin.Compute(points, radius: 10);
        Assert.Equal([(0.0, 0.0), (dx / 2, 15.0), (dx, 0.0)], bins.Select(bin => (bin.X, bin.Y)).ToArray());

        // (dx / 2, 7) and (dx / 2, 8) are near the edge between the rows (more than a third of a row from the center of
        // their nearest row): both go to the hexagon of the odd row, whose center is closer
        Assert.Equal([0, 1], bins[0].Points);
        Assert.Equal([2, 4, 5], bins[1].Points);
        Assert.Equal([3], bins[2].Points);

        // Negative coordinates: the row -1 is odd, so its hexagons are shifted too
        var negative = Hexbin.Compute([(-dx / 2, -15)], radius: 10);
        Assert.Equal((-dx / 2, -15.0), (negative[0].X, negative[0].Y));
    }

    [Fact]
    public void Hexagon_PointyTop()
    {
        var corners = Hexbin.Hexagon(0, 0, 10);
        Assert.HasCount(6, corners);
        Assert.Equal(0, corners[0].X, 9);
        Assert.Equal(-10, corners[0].Y, 9);
        Assert.Equal(5 * Math.Sqrt(3), corners[1].X, 9);
        Assert.Equal(-5, corners[1].Y, 9);
    }

    [Fact]
    public void Contours_MarchingSquares()
    {
        // A single cell above the threshold, at the center of a 3×3 grid: the edges are halfway between the centers of the
        // cells (x + 0.5) when the threshold is halfway between the values
        double[] grid = [0, 0, 0, 0, 1, 0, 0, 0, 0];
        var polygon = Assert.Single(Contours.Compute(grid, 3, 3, 0.5));
        var ring = Assert.Single(polygon);
        Assert.Equal([(1.0, 1.5), (1.5, 1.0), (1.5, 2.0), (2.0, 1.5)], ring.Distinct().Order().ToArray());
        Assert.True(Contours.Area(ring) > 0);

        // Linear interpolation: a threshold of 0.25 is a quarter of the way from 0 to 1, so the edge is 0.25 past the center of the outer cell
        var smooth = Assert.Single(Assert.Single(Contours.Compute(grid, 3, 3, 0.25)));
        Assert.Contains((0.75, 1.5), smooth);
        Assert.Contains((2.25, 1.5), smooth);

        // A ring of cells around a cell below the threshold: a polygon with a hole
        double[] ringGrid =
        [
            0, 0, 0, 0, 0,
            0, 1, 1, 1, 0,
            0, 1, 0, 1, 0,
            0, 1, 1, 1, 0,
            0, 0, 0, 0, 0,
        ];
        var withHole = Assert.Single(Contours.Compute(ringGrid, 5, 5, 0.5));
        Assert.HasCount(2, withHole);
        Assert.True(Contours.Area(withHole[0]) > 0);
        Assert.True(Contours.Area(withHole[1]) < 0);

        // Nothing above the threshold, or everything: no polygon, or the whole grid
        Assert.Empty(Contours.Compute(grid, 3, 3, 2));
        var all = Assert.Single(Assert.Single(Contours.Compute(grid, 3, 3, -1)));
        Assert.True(Contours.Area(all) > 0);
        Assert.Contains((0.5, 0.0), all);
        Assert.Contains((0.0, 0.5), all);
        Assert.Contains((3.0, 2.5), all);
        Assert.Contains((2.5, 3.0), all);
    }

    [Fact]
    public void KernelDensity_TwoDimensions()
    {
        // A single point at the origin, bandwidths of 1, a 3×3 grid from -1 to 1: the density is e^(-r²/2) / 2π
        var (grid, x0, stepX, y0, stepY) = KernelDensity.Evaluate2D([0], [0], 1, 1, size: 3, cut: 1);
        Assert.Equal((-1.0, 1.0, -1.0, 1.0), (x0, stepX, y0, stepY));
        Assert.Equal(1 / (2 * Math.PI), grid[4], 12);
        Assert.Equal(Math.Exp(-0.5) / (2 * Math.PI), grid[1], 12);
        Assert.Equal(Math.Exp(-1) / (2 * Math.PI), grid[0], 12);
    }

    [Fact]
    public void Tree_TwoChildren()
    {
        // Siblings 1 apart, half a separation on each side: 0.25 and 0.75, the root centered over them
        var root = Hierarchy.FromPaths(["a", "b"], path => path);
        var points = new TreeLayout().Layout(root).ToDictionary(point => point.Node.Name);
        Assert.Equal((0.5, 0.0), (points[string.Empty].X, points[string.Empty].Y));
        Assert.Equal((0.25, 1.0), (points["a"].X, points["a"].Y));
        Assert.Equal((0.75, 1.0), (points["b"].X, points["b"].Y));
    }

    [Fact]
    public void Tree_IsTidy()
    {
        #region Charts_TreeLayout
        var root = Hierarchy.FromPaths(["a/1", "a/2", "a/3", "b", "c/1/x", "c/1/y", "c/2"], path => path);
        var points = new TreeLayout { Width = 600, Height = 300 }.Layout(root);
        #endregion

        var byNode = points.ToDictionary(point => point.Node);
        Assert.Equal(300, points.Max(point => point.Y));
        Assert.All(points, point => point.X is >= 0 and <= 600);
        foreach (var point in points)
        {
            // A parent is centered over its first and last children
            if (!point.Node.IsLeaf)
            {
                Assert.Equal((byNode[point.Node.Children[0]].X + byNode[point.Node.Children[^1]].X) / 2, point.X, 1e-9);
            }
        }

        // The nodes of a depth are in order, at least one separation apart
        var unit = byNode[root.Children[0].Children[1]].X - byNode[root.Children[0].Children[0]].X;
        foreach (var depth in points.GroupBy(point => point.Node.Depth))
        {
            var row = depth.ToList();
            for (var i = 1; i < row.Count; i++)
            {
                Assert.True(row[i].X - row[i - 1].X >= unit - 1e-9);
            }
        }

        Assert.Equal((0.5, 0.0), (new TreeLayout().Layout(Hierarchy.FromPaths(Array.Empty<string>(), path => path))[0].X, 0.0));
    }

    [Fact]
    public void Cluster_LeavesAtTheSameDepth()
    {
        // Leaves a1, a2 (siblings, 1 apart) and b (a cousin, 2 apart): 0, 1, 3; one separation of padding on each end
        var root = Hierarchy.FromPaths(["a/a1", "a/a2", "b"], path => path);
        var points = new ClusterLayout().Layout(root).ToDictionary(point => point.Node.Name);
        Assert.Equal(0.2, points["a1"].X, 1e-12);
        Assert.Equal(0.4, points["a2"].X, 1e-12);
        Assert.Equal(0.8, points["b"].X, 1e-12);
        Assert.Equal(0.3, points["a"].X, 1e-12);
        Assert.Equal(0.55, points[string.Empty].X, 1e-12);
        Assert.Equal([0.0, 0.5, 1, 1, 1], new[] { string.Empty, "a", "a1", "a2", "b" }.Select(name => points[name].Y).ToArray());

        var single = new ClusterLayout { Width = 10, Height = 20 }.Layout(Hierarchy.FromTree("only", _ => null));
        Assert.Equal((5.0, 0.0), (single[0].X, single[0].Y)); // As d3: a single node is at depth 0
    }

    [Fact]
    public void Random_IsTheLcgOfD3()
    {
        var random = CirclePacking.CreateRandom();
        Assert.Equal(1015568748 / 4294967296.0, random());
        Assert.Equal(1586005467 / 4294967296.0, random());
    }

    [Fact]
    public void Enclose_TwoAndThreeCircles()
    {
        var two = CirclePacking.EncloseBasis2(new PackCircle(0, 0, 1), new PackCircle(4, 0, 1));
        Assert.Equal((2.0, 0.0, 3.0), (two.X, two.Y, two.Radius));

        var three = CirclePacking.Enclose([new PackCircle(0, 0, 1), new PackCircle(10, 0, 1), new PackCircle(5, 8, 1)], CirclePacking.CreateRandom());
        foreach (var circle in new[] { new PackCircle(0, 0, 1), new PackCircle(10, 0, 1), new PackCircle(5, 8, 1) })
        {
            Assert.True(Distance(three, circle) + circle.Radius <= three.Radius + 1e-6);
        }

        // The circumscribed circle of the centers has a radius of 10 × 89 / 160 = 5.5625, plus the radius of the circles
        Assert.Equal(6.5625, three.Radius, 1e-6);

        var nested = CirclePacking.Enclose([new PackCircle(0, 0, 5), new PackCircle(1, 0, 1)], CirclePacking.CreateRandom());
        Assert.Equal((0.0, 0.0, 5.0), (nested.X, nested.Y, nested.Radius));
        Assert.Equal(0, CirclePacking.Enclose([], CirclePacking.CreateRandom()).Radius);
    }

    [Fact]
    public void PackSiblings_SmallCounts()
    {
        Assert.Equal(0, CirclePacking.PackSiblings([], CirclePacking.CreateRandom()));
        Assert.Equal(2, CirclePacking.PackSiblings([new PackCircle(5, 5, 2)], CirclePacking.CreateRandom()));
        var a = new PackCircle(0, 0, 1);
        var b = new PackCircle(0, 0, 2);
        Assert.Equal(3, CirclePacking.PackSiblings([a, b], CirclePacking.CreateRandom()));
        Assert.Equal(3, Distance(a, b), 1e-9);

        // Two circles at the same place: the third goes next to them
        var c = new PackCircle(0, 0, 1);
        CirclePacking.Place(new PackCircle(0, 0, 1), new PackCircle(0, 0, 1), c);
        Assert.Equal((1.0, 0.0), (c.X, c.Y));
    }

    [Fact]
    public void Pack_CirclesDoNotOverlapAndStayInTheirParent()
    {
        #region Charts_PackLayout
        var root = Hierarchy.FromPaths(Enumerable.Range(1, 40).Select(i => "g" + (i % 4) + "/n" + i), path => path, value: path => path.Length * 3 % 7 + 1).SortByValue();
        var circles = new PackLayout { Width = 500, Height = 500, Padding = 2 }.Layout(root);
        #endregion

        var byNode = circles.ToDictionary(circle => circle.Node);
        Assert.Equal((250.0, 250.0, 250.0), (byNode[root].X, byNode[root].Y, Math.Round(byNode[root].Radius, 9, MidpointRounding.AwayFromZero)));
        foreach (var circle in circles)
        {
            if (circle.Node.Parent is { } parent)
            {
                var parentCircle = byNode[parent];
                Assert.True(Distance(circle, parentCircle) + circle.Radius <= parentCircle.Radius + 1e-6, $"{circle.Node} is outside of {parent}");
            }

            foreach (var sibling in circle.Node.Parent?.Children ?? [])
            {
                if (sibling != circle.Node)
                {
                    // As in d3, the padding is about the gap between siblings: it is scaled with the layout
                    Assert.True(Distance(circle, byNode[sibling]) >= circle.Radius + byNode[sibling].Radius + 1, $"{circle.Node} overlaps {sibling}");
                }
            }
        }

        var again = new PackLayout { Width = 500, Height = 500, Padding = 2 }.Layout(root);
        Assert.Equal(circles.Select(circle => (circle.X, circle.Y, circle.Radius)).ToArray(), again.Select(circle => (circle.X, circle.Y, circle.Radius)).ToArray());
    }

    [Fact]
    public void Pack_EmptyTree()
    {
        var root = Hierarchy.FromPaths(["a"], path => path);
        var circles = new PackLayout { Width = 100, Height = 50 }.Layout(root);
        Assert.All(circles, circle => Assert.Equal(0, circle.Radius));
    }

    [Fact]
    public void MonotoneTangents()
    {
        Assert.Equal([1.5, 0, -1.5], CurveBuilder.GetMonotoneTangents([(0, 0), (1, 1), (2, 0)]));
        Assert.Equal([1, 1, 1], CurveBuilder.GetMonotoneTangents([(0, 0), (1, 1), (2, 2)]));
        Assert.Equal([0, 0, 0], CurveBuilder.GetMonotoneTangents([(0, 0), (0, 1), (0, 2)]));
    }

    [Fact]
    public void Curves()
    {
        (double X, double Y)[] points = [(0, 0), (10, 10), (20, 0)];
        Assert.Equal("M0,0 C3.33,5 6.67,10 10,10 C13.33,10 16.67,5 20,0", Build(points, Curve.MonotoneX));
        Assert.Equal("M0,0 L5,0 L5,10 L10,10 L15,10 L15,0 L20,0", Build(points, Curve.Step));
        Assert.Equal("M0,0 L0,10 L10,10 L10,0 L20,0", Build(points, Curve.StepBefore));
        Assert.Equal("M0,0 L10,0 L10,10 L20,10 L20,0", Build(points, Curve.StepAfter));
        Assert.Equal("M0,0 L10,10 L20,0 Z", Build(points, Curve.Step, closed: true));
        Assert.StartsWith("M0,0 C", Build(points, Curve.CatmullRom));
        Assert.EndsWith("Z", Build(points, Curve.CatmullRom, closed: true));
        Assert.Equal("M0,0 L10,10", Build(points[..2], Curve.MonotoneX));
        Assert.Equal(string.Empty, Build([], Curve.Linear));

        var builder = new PathBuilder().MoveTo(0, 0);
        CurveBuilder.Append(builder, points, Curve.Linear, connect: true);
        Assert.Equal("M0,0 L0,0 L10,10 L20,0", builder.Build().ToString());

        // A repeated point does not divide by zero
        Assert.DoesNotContain("NaN", Build([(0, 0), (0, 0), (10, 10)], Curve.CatmullRom));

        static string Build((double X, double Y)[] points, Curve curve, bool closed = false)
        {
            var builder = new PathBuilder();
            CurveBuilder.Append(builder, points, curve, closed: closed);
            return builder.Build().ToString();
        }
    }

    [Fact]
    public void Sectors()
    {
        // A quarter of a pie: a small arc, then the center
        Assert.Equal("M0,-10 A10,10 0 0,1 10,0 L0,0 Z", PolarGeometry.Sector(0, 0, 0, 10, 0, Math.PI / 2).ToString());

        // Three quarters of a ring: large arcs, the inner one counterclockwise
        Assert.Equal("M0,-10 A10,10 0 1,1 -10,0 L-5,0 A5,5 0 1,0 0,-5 Z", PolarGeometry.Sector(0, 0, 5, 10, 0, 3 * Math.PI / 2).ToString());

        // A full ring: two circles of two half arcs
        Assert.Equal("M0,-10 A10,10 0 0,1 0,10 A10,10 0 0,1 0,-10 Z M0,-5 A5,5 0 0,0 0,5 A5,5 0 0,0 0,-5 Z", PolarGeometry.Sector(0, 0, 5, 10, 0, 2 * Math.PI).ToString());
        Assert.True(PolarGeometry.Sector(0, 0, 0, 10, 1, 1).IsEmpty);
        Assert.True(PolarGeometry.Sector(0, 0, 0, 0, 0, 1).IsEmpty);

        Assert.Equal("M0,-10 A10,10 0 0,1 10,0", PolarGeometry.Arc(0, 0, 10, 0, Math.PI / 2).ToString());
        Assert.Equal("M0,-10 A10,10 0 0,1 0,10 A10,10 0 0,1 0,-10 Z", PolarGeometry.Arc(0, 0, 10, 0, 2 * Math.PI).ToString());
        Assert.True(PolarGeometry.Arc(0, 0, 10, 1, 1).IsEmpty);
    }

    [Fact]
    public void UnitBounds()
    {
        Assert.Equal((-1.0, -1.0, 1.0, 1.0), PolarGeometry.GetUnitBounds(0, 0, 2 * Math.PI));
        var (minX, minY, maxX, maxY) = PolarGeometry.GetUnitBounds(0.5, -Math.PI / 2, Math.PI / 2);
        Assert.Equal((-1.0, -1.0, 1.0, 0.0), (Math.Round(minX, 9, MidpointRounding.AwayFromZero), Math.Round(minY, 9, MidpointRounding.AwayFromZero), Math.Round(maxX, 9, MidpointRounding.AwayFromZero), Math.Round(maxY, 9, MidpointRounding.AwayFromZero)));
        (minX, minY, maxX, maxY) = PolarGeometry.GetUnitBounds(0, 0, Math.PI / 4);
        Assert.Equal((0.0, -1.0, 0.70711, 0.0), (Math.Round(minX, 5, MidpointRounding.AwayFromZero), Math.Round(minY, 5, MidpointRounding.AwayFromZero), Math.Round(maxX, 5, MidpointRounding.AwayFromZero), Math.Round(maxY, 5, MidpointRounding.AwayFromZero)));
    }

    [Fact]
    public void CoordinateSystems()
    {
        var cartesian = new CartesianSystem(transposed: true);
        Assert.Equal((2.0, 1.0), cartesian.Project(1, 2));
        var rectangle = Assert.IsType<SceneRectangle>(cartesian.Rectangle(10, 0, 5, 25));
        Assert.Equal((5.0, 0.0, 20.0, 10.0), (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));

        var polar = new PolarSystem(transposed: true, 100, 100, 10, 50, 0, Math.PI);
        Assert.False(polar.IsFullCircle);
        var (x, y) = polar.Project(20, Math.PI / 2);
        Assert.Equal((120.0, 100.0), (Math.Round(x, 9, MidpointRounding.AwayFromZero), Math.Round(y, 9, MidpointRounding.AwayFromZero)));
        Assert.IsType<ScenePath>(polar.Rectangle(10, 20, Math.PI, 0));
    }

    [Fact]
    public void ColorConversions()
    {
        var (l, a, b) = OkLab.FromColor(ChartColor.White);
        Assert.Equal(1, l, 1e-6);
        Assert.Equal(0, a, 1e-6);
        Assert.Equal(0, b, 1e-6);
        Assert.Equal(ChartColor.Parse("#336699"), OkLab.ToColor(OkLab.FromColor("#336699").L, OkLab.FromColor("#336699").A, OkLab.FromColor("#336699").B, 255));
        Assert.Equal((ChartColor)"#ff0000", OkLab.Interpolate("#ff0000", "#0000ff", 0));
        Assert.Equal((ChartColor)"#0000ff", OkLab.Interpolate("#ff0000", "#0000ff", 1));

        // OKLab passes through a purple, not a dark muddy color
        Assert.Equal((ChartColor)"#8c53a2", OkLab.Interpolate("#ff0000", "#0000ff", 0.5));
        Assert.Equal(new ChartColor(0, 0, 0, 128), OkLab.Interpolate(new ChartColor(0, 0, 0, 255), new ChartColor(0, 0, 0, 0), 0.5));

        Assert.Equal(ColorScheme.Blues.Stops[0], ColorScheme.Blues.Interpolate(-1));
        Assert.Equal(ColorScheme.Blues.Stops[^1], ColorScheme.Blues.Interpolate(2));
        Assert.Equal(ColorScheme.Blues.Stops[0], ColorScheme.Blues.Interpolate(double.NaN));
        Assert.Equal(ColorScheme.Blues.Stops[2], ColorScheme.Blues.Interpolate(0.5));
        Assert.Equal("Viridis", ColorScheme.Viridis.ToString());
        Assert.HasCount(9, new[] { ColorScheme.Viridis, ColorScheme.Magma, ColorScheme.Blues, ColorScheme.Greens, ColorScheme.Oranges, ColorScheme.Reds, ColorScheme.Greys, ColorScheme.RedBlue, ColorScheme.RedYellowGreen }.Distinct());
        Assert.Throws<ArgumentException>("stops", () => new ColorScheme("one", [ChartColor.Black]));
    }

    [Fact]
    public void ChartColors()
    {
        Assert.Equal(new ChartColor(0x11, 0x22, 0x33), ChartColor.Parse("#123"));
        Assert.Equal(new ChartColor(0x11, 0x22, 0x33, 0x44), ChartColor.Parse("#1234"));
        Assert.Equal(new ChartColor(0x12, 0x34, 0x56), ChartColor.Parse(" #123456 "));
        Assert.Equal(new ChartColor(0x12, 0x34, 0x56, 0x78), ChartColor.Parse("#12345678"));
        Assert.Equal(new ChartColor(70, 130, 180), ChartColor.Parse("SteelBlue"));
        Assert.Equal("#123456", new ChartColor(0x12, 0x34, 0x56).ToString());
        Assert.Equal("#12345678", new ChartColor(0x12, 0x34, 0x56, 0x78).ToString());
        Assert.Equal("#123456", new ChartColor(0x12, 0x34, 0x56, 0x78).ToHexRgb());
        Assert.Equal(128, ChartColor.White.WithOpacity(0.5).A);
        Assert.Equal(0, ChartColor.Transparent.Opacity);
        Assert.False(ChartColor.TryParse(null, out _));
        Assert.False(ChartColor.TryParse("#12", out _));
        Assert.False(ChartColor.TryParse("#12g", out _));
        Assert.False(ChartColor.TryParse("#12345g", out _));
        Assert.False(ChartColor.TryParse("not-a-color", out _));
        Assert.Throws<FormatException>(() => ChartColor.Parse("nope"));
        Assert.Throws<ArgumentNullException>("value", () => ChartColor.Parse(null!));
    }

    [Fact]
    public void NumberFormatting()
    {
        Assert.Equal("1,234.5", NumberFormats.Default(1234.5));
        Assert.Equal("0.3", NumberFormats.Default(0.1 + 0.2));
        Assert.Equal("123,457", NumberFormats.Default(123456.789));
        Assert.Equal("1E-7", NumberFormats.Default(1e-7));
        Assert.Equal("1.23457E+15", NumberFormats.Default(1234567890123456));
        Assert.Equal("0", NumberFormats.Default(-0.0));
        Assert.Equal("NaN", NumberFormats.Default(double.NaN));
        Assert.Equal("1.5k", NumberFormats.Si(1500));
        Assert.Equal("1M", NumberFormats.Si(999_950));
        Assert.Equal("-2M", NumberFormats.Si(-2e6));
        Assert.Equal("12", NumberFormats.Si(12));
        Assert.Equal("0", NumberFormats.Si(-0.0001));
        Assert.Equal("Infinity", NumberFormats.Si(double.PositiveInfinity));
        Assert.Equal("3.14", NumberFormats.Fixed(2)(3.14159));
        Assert.Equal("1,000", NumberFormats.Fixed(0)(999.5));
        Assert.Equal("0", NumberFormats.Fixed(0)(-0.2));
        Assert.Equal("26%", NumberFormats.Percent()(0.256));
        Assert.Equal("25.6%", NumberFormats.Percent(1)(0.256));
        Assert.Equal("0.5", NumberFormats.ForStep(0.5, 0.5));
        Assert.Equal("1.0", NumberFormats.ForStep(1, 0.5));
        Assert.Equal("1,000", NumberFormats.ForStep(1000, 500));
        Assert.Equal("0.3", NumberFormats.ForStep(0.30000000000000004, 0.1));
        Assert.Equal("0.05", NumberFormats.ForStep(0.05, 0.05));
        Assert.Equal("7", NumberFormats.ForStep(7, 0));
        Assert.Equal("1E+15", NumberFormats.ForStep(1e15, 1e14));
        Assert.Equal(123000000000000000, NumberFormats.RoundSignificant(123456789012345678, 3));
        Assert.Throws<ArgumentOutOfRangeException>("decimals", () => NumberFormats.Fixed(-1));
    }

    [Fact]
    public void SvgNumbers()
    {
        Assert.Equal("0", SvgNumber.Format(-0.001, 2));
        Assert.Equal("1.13", SvgNumber.Format(1.125, 2));
        Assert.Equal("1", SvgNumber.Format(1.005, 2)); // 1.005 is 1.00499999999999989 in binary
        Assert.Equal("-1.5", SvgNumber.Format(-1.5, 2));
        Assert.Equal("2", SvgNumber.Format(1.6, 0));
        Assert.Equal("0", SvgNumber.Format(double.NaN, 2));
        var path = new PathBuilder().MoveTo(0, 0).LineTo(1, 1).CubicTo(1, 2, 3, 4, 5, 6).QuadraticTo(7, 8, 9, 10).ArcTo(5, 5, 30, largeArc: true, sweep: false, 11, 12).Close().Build();
        Assert.Equal("M0,0 L1,1 C1,2 3,4 5,6 Q7,8 9,10 A5,5 30 1,0 11,12 Z", path.ToString());
        Assert.Equal(6, path.Commands.Count);
        Assert.Equal("M0,0 L1,0 L1,1 L0,1 Z", new PathBuilder().AddRectangle(0, 0, 1, 1).Build().ToString());
    }

    [Fact]
    public void Transforms()
    {
        Assert.True(SceneTransform.Identity.IsTranslation);
        Assert.Equal((11.0, 22.0), SceneTransform.Translate(10, 20).Apply(1, 2));
        var rotation = SceneTransform.Rotate(90, 10, 10);
        Assert.False(rotation.IsTranslation);
        var (x, y) = rotation.Apply(20, 10);
        Assert.Equal((10.0, 20.0), (Math.Round(x, 9, MidpointRounding.AwayFromZero), Math.Round(y, 9, MidpointRounding.AwayFromZero)));
    }

    private static void AssertBox(double x0, double y0, double x1, double y1, TreemapLayout.Box box) => AssertBox(x0, y0, x1, y1, box.X0, box.Y0, box.X1, box.Y1);

    private static void AssertBox(double x0, double y0, double x1, double y1, double actualX0, double actualY0, double actualX1, double actualY1)
    {
        Assert.Equal(x0, actualX0, 1e-9);
        Assert.Equal(y0, actualY0, 1e-9);
        Assert.Equal(x1, actualX1, 1e-9);
        Assert.Equal(y1, actualY1, 1e-9);
    }

    private static double Area(TreemapLayout.Box box) => (box.X1 - box.X0) * (box.Y1 - box.Y0);

    private static double Distance(PackCircle a, PackCircle b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double Distance<T>(HierarchyCircle<T> a, HierarchyCircle<T> b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    [Fact]
    public void GeoSystem_Fit()
    {
        // The bounds are scaled to fit the frame and centered in it, as d3's fitExtent
        var system = GeoSystem.Fit(new EquirectangularProjection(), (-1, -0.5, 1, 0.5), new ChartRectangle(10, 20, 200, 100));
        Assert.Equal((110.0, 70.0), system.Project(0, 0));
        var (x, y) = system.Project(180 / Math.PI, 90 / Math.PI);
        Assert.Equal(210, x, 1e-9);
        Assert.Equal(20, y, 1e-9);

        // A line fits its length; a point shows a region of 10 degrees around it
        var line = GeoSystem.Fit(new EquirectangularProjection(), (0, 0, 2, 0), new ChartRectangle(0, 0, 100, 50));
        Assert.Equal(100, line.Project(2 * 180 / Math.PI, 0).X, 1e-9);
        var point = GeoSystem.Fit(new EquirectangularProjection(), (0, 0, 0, 0), new ChartRectangle(0, 0, 100, 50));
        Assert.Equal(75, point.Project(5, 0).X, 1e-9);

        var bounds = GeoSystem.GetProjectedBounds(new MercatorProjection(), [new(-180, 0), new(double.NaN, 0), new(180, 90)]);
        Assert.Equal(-Math.PI, bounds.MinX, 1e-12);
        Assert.Equal(0, bounds.MinY, 1e-12);
        Assert.Equal(Math.PI, bounds.MaxX, 1e-12);
        Assert.Equal(Math.PI, bounds.MaxY, 1e-9);
        Assert.True(double.IsNaN(GeoSystem.GetProjectedBounds(new MercatorProjection(), []).MinX));
    }

    [Fact]
    public void GeoSystem_SampleBox()
    {
        // Straight meridians and parallels need only the corners; the curved meridians of Equal Earth get a point every 2.5°
        Assert.Equal([new GeoPosition(0, 0), new(10, 0), new(10, 5), new(0, 5), new(0, 0)], GeoSystem.SampleBox(new EquirectangularProjection(), 0, 0, 10, 5));
        Assert.HasCount(2 + 72 + 1 + 72, GeoSystem.SampleBox(new EqualEarthProjection(), -180, -90, 180, 90));
        Assert.HasCount(1 + 12 + 1 + 12 + 1, GeoSystem.SampleBox(new AlbersProjection(), -120, 30, -90, 40));
    }
}
