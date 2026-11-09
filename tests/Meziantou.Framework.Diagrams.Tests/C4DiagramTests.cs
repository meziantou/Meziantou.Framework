namespace Meziantou.Framework.Diagrams.Tests;

public sealed class C4DiagramTests
{
    [Fact]
    public void C4Diagram()
    {
        #region Diagrams_C4Diagram
        var diagram = new C4Diagram { Kind = C4DiagramKind.Container, Title = "Internet banking" };
        var customer = diagram.AddElement("customer", C4ElementKind.Person, "Customer", "A customer of the bank");
        var bank = diagram.AddBoundary("bank", "Internet Banking", C4BoundaryKind.System);
        var webApp = new C4Element("web", C4ElementKind.Container, "Web Application") { Technology = "ASP.NET Core", Description = "Serves the pages" };
        var database = new C4Element("db", C4ElementKind.Container, "Database") { Shape = C4ElementShape.Database, Technology = "SQL Server" };
        bank.Elements.Add(webApp);
        bank.Elements.Add(database);
        var mail = diagram.AddElement("mail", C4ElementKind.System, "E-mail system");
        mail.IsExternal = true;

        diagram.AddRelationship(customer, webApp, "Uses", "HTTPS");
        diagram.AddRelationship(webApp, database, "Reads and writes", "SQL");
        diagram.AddRelationship(webApp, mail, "Sends e-mails").Kind = C4RelationshipKind.Bidirectional;
        #endregion

        Assert.Same(database, diagram.Find("db"));
        Assert.Equal(["bank"], diagram.EnumerateBoundaries().Select(boundary => boundary.Id));
        DiagramAssert.Svg(diagram, "C4Diagram");
    }

    [Fact]
    public void DeploymentNodesStylesAndRows()
    {
        var diagram = new C4Diagram { Kind = C4DiagramKind.Dynamic, ShapesPerRow = 2, BoundariesPerRow = 1, Wrap = false };
        var node = diagram.AddBoundary("node", "Server", C4BoundaryKind.DeploymentNode);
        node.Type = "Ubuntu";
        node.Description = "The main server";
        var inner = new C4Boundary("inner", "Container", C4BoundaryKind.DeploymentNodeRight);
        node.Boundaries.Add(inner);
        var queue = new C4Element("queue", C4ElementKind.Component, "Queue") { Shape = C4ElementShape.Queue, IsExternal = true };
        inner.Elements.Add(queue);
        var a = diagram.AddElement("a", C4ElementKind.Person, "A");
        a.Style = new DiagramStyle { Fill = (SceneColor)"#ff0000", TextColor = SceneColor.Black };
        var b = diagram.AddElement("b", C4ElementKind.System, "B");
        var c = diagram.AddElement("c", C4ElementKind.Component, "C");
        diagram.AddRelationship(a, b, "first");
        var back = diagram.AddRelationship(b, queue, "second");
        back.Kind = C4RelationshipKind.Back;
        back.LabelOffsetX = 20;
        back.Style = new DiagramStyle { Stroke = (SceneColor)"#0000ff" };
        diagram.AddRelationship(c, node, "third");

        var svg = diagram.ToSvg();
        Assert.Contains("1: first", svg, StringComparison.Ordinal);
        Assert.Contains("[Ubuntu]", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ff0000\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void RowsMustHoldOneItemAtLeast()
    {
        var diagram = new C4Diagram();
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.ShapesPerRow = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.BoundariesPerRow = 0);
    }
}
