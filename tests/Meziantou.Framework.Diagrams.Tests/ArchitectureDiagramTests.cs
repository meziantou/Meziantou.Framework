namespace Meziantou.Framework.Diagrams.Tests;

public sealed class ArchitectureDiagramTests
{
    [Fact]
    public void ArchitectureDiagram()
    {
        #region Diagrams_ArchitectureDiagram
        var diagram = new ArchitectureDiagram();
        var api = diagram.AddGroup("api", "API", icon: "cloud");
        var server = new ArchitectureService("server") { Title = "Server", Icon = "server" };
        var database = new ArchitectureService("db") { Title = "Database", Icon = "database" };
        var disk = new ArchitectureService("disk") { Title = "Storage", Icon = "disk" };
        api.Nodes.Add(server);
        api.Nodes.Add(database);
        api.Nodes.Add(disk);
        var internet = diagram.AddService("internet", "Internet", icon: "internet");

        // The sides place the services: the database is on the right of the server
        diagram.AddEdge(server, ArchitectureSide.Right, database, ArchitectureSide.Left).ToArrow = true;
        diagram.AddEdge(disk, ArchitectureSide.Top, server, ArchitectureSide.Bottom);
        diagram.AddEdge(internet, ArchitectureSide.Right, server, ArchitectureSide.Left).Label = "HTTPS";
        #endregion

        Assert.Equal(["internet", "server", "db", "disk"], diagram.EnumerateNodes().Select(node => node.Id));
        DiagramAssert.Svg(diagram, "ArchitectureDiagram");
    }

    [Fact]
    public void JunctionsAlignmentsAndGroupEdges()
    {
        var diagram = new ArchitectureDiagram { IconSize = 60, GroupPadding = 20 };
        var left = diagram.AddGroup("left");
        var right = diagram.AddGroup("right", "Right");
        var nested = new ArchitectureGroup("nested") { Title = "Nested", Icon = "logos:aws" };
        right.Groups.Add(nested);
        var a = new ArchitectureService("a") { IconText = "A" };
        var b = new ArchitectureService("b") { Icon = "unknown:icon", Title = "B" };
        var c = new ArchitectureService("c");
        left.Nodes.Add(a);
        nested.Nodes.Add(b);
        diagram.Nodes.Add(c);
        var junction = new ArchitectureJunction("j");
        diagram.Nodes.Add(junction);
        var edge = diagram.AddEdge(a, ArchitectureSide.Right, b, ArchitectureSide.Left);
        edge.FromGroup = true;
        edge.ToGroup = true;
        edge.FromArrow = true;
        diagram.AddEdge(c, ArchitectureSide.Bottom, junction, ArchitectureSide.Top);
        diagram.AddEdge(junction, ArchitectureSide.Right, b, ArchitectureSide.Bottom);
        var alignment = new ArchitectureAlignment(isRow: true);
        alignment.Members.Add(c);
        alignment.Members.Add(a);
        diagram.Alignments.Add(alignment);

        var svg = diagram.ToSvg();
        Assert.Contains(">?<", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }
}
