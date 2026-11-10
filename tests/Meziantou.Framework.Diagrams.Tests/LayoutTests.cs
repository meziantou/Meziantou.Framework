using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Diagrams.Layout;
using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void NetworkSimplexHonorsTheMinimumLengths()
    {
        // a -> b -> d and a -> c -> d, with a long edge from a to d
        var ranks = NetworkSimplex.Run(4, [(0, 1, 1, 1), (1, 3, 1, 1), (0, 2, 2, 1), (2, 3, 1, 1), (0, 3, 1, 1)]);
        Assert.Equal(0, ranks[0] - ranks[0]);
        Assert.True(ranks[1] - ranks[0] >= 1);
        Assert.True(ranks[2] - ranks[0] >= 2);
        Assert.True(ranks[3] - ranks[2] >= 1);
        Assert.True(ranks[3] - ranks[1] >= 1);

        // The edges are as short as their minimum lengths allow
        Assert.Equal(3, ranks[3] - ranks[0]);
    }

    [Theory]
    [InlineData(DiagramDirection.TopToBottom)]
    [InlineData(DiagramDirection.BottomToTop)]
    [InlineData(DiagramDirection.LeftToRight)]
    [InlineData(DiagramDirection.RightToLeft)]
    public void RanksFollowTheDirection(DiagramDirection diagramDirection)
    {
        var direction = diagramDirection.ToLayout();
        var graph = new LayoutGraph { Direction = direction };
        var a = graph.AddNode(40, 20);
        var b = graph.AddNode(40, 20);
        var c = graph.AddNode(40, 20);
        graph.AddEdge(a, b);
        graph.AddEdge(b, c);
        LayeredLayout.Run(graph);

        var (first, second) = direction switch
        {
            LayoutDirection.TopToBottom => (a.Y, b.Y),
            LayoutDirection.BottomToTop => (b.Y, a.Y),
            LayoutDirection.LeftToRight => (a.X, b.X),
            _ => (b.X, a.X),
        };
        Assert.True(first < second, $"{direction}: {first} >= {second}");
        Assert.True(graph.Width > 0 && graph.Height > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void RandomGraphsKeepTheNodesApart(int seed)
    {
        var random = new Random(seed);
        var graph = new LayoutGraph { Direction = (LayoutDirection)(seed % 4) };
        var clusters = new List<LayoutNode>();
        for (var i = 0; i < 4; i++)
        {
            var cluster = graph.AddCluster();
            cluster.TitleSpace = 20;
            if (clusters.Count > 0 && random.Next(3) == 0)
            {
                LayoutGraph.SetParent(cluster, clusters[random.Next(clusters.Count)]);
            }

            clusters.Add(cluster);
        }

        var nodes = new List<LayoutNode>();
        for (var i = 0; i < 25; i++)
        {
            var node = graph.AddNode(20 + random.Next(80), 20 + random.Next(30));
            if (random.Next(2) == 0)
            {
                LayoutGraph.SetParent(node, clusters[random.Next(clusters.Count)]);
            }

            nodes.Add(node);
        }

        for (var i = 0; i < 40; i++)
        {
            var edge = graph.AddEdge(nodes[random.Next(nodes.Count)], nodes[random.Next(nodes.Count)]);
            edge.MinLength = 1 + random.Next(2);
            if (random.Next(3) == 0)
            {
                edge.LabelWidth = 30;
                edge.LabelHeight = 15;
            }
        }

        LayeredLayout.Run(graph);

        foreach (var node in graph.Nodes)
        {
            Assert.True(double.IsFinite(node.X) && double.IsFinite(node.Y), node.ToString());
        }

        // No two nodes overlap
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var a = nodes[i];
                var b = nodes[j];
                var overlapX = Math.Abs(a.X - b.X) < ((a.Width + b.Width) / 2) - 1e-6;
                var overlapY = Math.Abs(a.Y - b.Y) < ((a.Height + b.Height) / 2) - 1e-6;
                Assert.False(overlapX && overlapY, $"{a} overlaps {b}");
            }
        }

        // A cluster contains its nodes
        foreach (var node in nodes.Where(node => node.Parent is not null))
        {
            var cluster = node.Parent!;
            Assert.True(node.X - (node.Width / 2) >= cluster.X - (cluster.Width / 2) - 1e-6, "left");
            Assert.True(node.X + (node.Width / 2) <= cluster.X + (cluster.Width / 2) + 1e-6, "right");
            Assert.True(node.Y - (node.Height / 2) >= cluster.Y - (cluster.Height / 2) - 1e-6, "top");
            Assert.True(node.Y + (node.Height / 2) <= cluster.Y + (cluster.Height / 2) + 1e-6, "bottom");
        }

        // Every edge has points, all finite
        foreach (var edge in graph.Edges)
        {
            Assert.All(edge.Points, point => Assert.True(Geometry.IsFinite(point)));
        }
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        static string Layout()
        {
            var flowchart = new FlowchartDiagram();
            for (var i = 0; i < 12; i++)
            {
                flowchart.AddEdge("n" + (i % 5).ToString(System.Globalization.CultureInfo.InvariantCulture), "n" + ((i * 7) % 9).ToString(System.Globalization.CultureInfo.InvariantCulture), i % 3 == 0 ? "label" : null);
            }

            return flowchart.ToSvg();
        }

        Assert.Equal(Layout(), Layout());
    }

    [Fact]
    public void CyclesAndSelfLoops()
    {
        var graph = new LayoutGraph();
        var a = graph.AddNode(30, 30);
        var b = graph.AddNode(30, 30);
        var loop = graph.AddEdge(a, a);
        var forward = graph.AddEdge(a, b);
        var back = graph.AddEdge(b, a);
        LayeredLayout.Run(graph);
        Assert.HasCount(5, loop.Points);
        Assert.NotEmpty(forward.Points);
        Assert.NotEmpty(back.Points);
        Assert.Same(b, back.V);
        Assert.Same(a, back.W);
    }

    [Fact]
    public void ShapesIntersectTheirOutline()
    {
        foreach (var shape in Enum.GetValues<FlowchartNodeShape>())
        {
            var drawing = Shapes.Create(shape, 60, 20);
            Assert.True(drawing.Width > 0 && drawing.Height > 0, shape.ToString());
            var point = drawing.Intersect(new LayoutPoint(0, 0), new LayoutPoint(1000, 0));
            Assert.InRange(point.X, 0, (drawing.Width / 2) + 1e-6);
        }
    }

    [Fact]
    public void CurvesStartAndEndOnThePoints()
    {
        LayoutPoint[] points = [new(0, 0), new(50, 10), new(100, 100), new(150, 120)];
        foreach (var curve in Enum.GetValues<EdgeCurve>())
        {
            var path = Curves.Build(points, curve);
            var commands = path.Commands;
            Assert.Equal((0, 0), (commands[0].X, commands[0].Y));
            Assert.Equal(150, commands[^1].X, 1e-9);
            Assert.Equal(120, commands[^1].Y, 1e-9);
            Assert.All(commands, command => Assert.True(double.IsFinite(command.X) && double.IsFinite(command.Y), curve.ToString()));
        }
    }

    [Fact]
    public void MarkdownRuns()
    {
        var runs = TextBlock.ParseMarkdown("a **b** _c_ snake_case \\*d", bold: false, italic: false);
        Assert.Equal([new TextRun("a ", false, false), new TextRun("b", true, false), new TextRun(" ", false, false), new TextRun("c", false, true), new TextRun(" snake_case *d", false, false)], runs);
    }
}
