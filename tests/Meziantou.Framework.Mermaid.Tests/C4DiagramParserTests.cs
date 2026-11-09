namespace Meziantou.Framework.Mermaid.Tests;

public sealed class C4DiagramParserTests
{
    [Fact]
    public void ElementsBoundariesAndRelationships()
    {
        #region Mermaid_C4Diagram
        var diagram = (C4Diagram)MermaidDocument.Parse("""
            C4Container
                title Internet banking
                Person(customer, "Customer", "A customer of the bank")
                System_Boundary(bank, "Internet Banking") {
                    Container(web, "Web Application", "ASP.NET Core", "Serves the pages")
                    ContainerDb(db, "Database", "SQL Server")
                }
                System_Ext(mail, "E-mail system")
                Rel(customer, web, "Uses", "HTTPS")
                Rel(web, db, "Reads and writes")
                BiRel(web, mail, "Sends e-mails")
            """).Diagram;
        #endregion

        Assert.Equal((C4DiagramKind.Container, "Internet banking"), (diagram.Kind, diagram.Title));
        var boundary = Assert.Single(diagram.Boundaries);
        Assert.Equal((C4BoundaryKind.System, "Internet Banking"), (boundary.Kind, boundary.Label.Text));
        var web = boundary.Elements[0];
        Assert.Equal((C4ElementKind.Container, "ASP.NET Core", "Serves the pages"), (web.Kind, web.Technology, web.Description?.Text));
        Assert.Equal(C4ElementShape.Database, boundary.Elements[1].Shape);
        Assert.True(((C4Element)diagram.Find("mail")!).IsExternal);
        Assert.Equal(C4RelationshipKind.Bidirectional, diagram.Relationships[2].Kind);
        Assert.Equal("HTTPS", diagram.Relationships[0].Technology);
    }

    [Fact]
    public void NamedAttributesAndUpdates()
    {
        var document = MermaidDocument.Parse("""
            C4Deployment
                Deployment_Node(node, "Server", "Ubuntu", "The main server", $tags="v1"){
                    Container(api, "API", $descr="Serves the API", $link="https://example.com")
                }
                Node_R(right, "Right")
                {
                    SystemQueue_Ext(queue, Queue, , $descr="A queue", $sprite="x")
                }
                RelIndex(1, api, queue, "Sends", "AMQP")
                Rel(api, queue, "Sends again")
                Rel_Back(queue, node, "Back")
                UpdateElementStyle(api, $bgColor="red", $fontColor="#ffffff", $borderColor="blue")
                UpdateRelStyle(api, queue, $textColor="green", $offsetX="-40", $offsetY="10px")
                UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
            """);
        var diagram = (C4Diagram)document.Diagram;
        var node = diagram.Boundaries[0];
        Assert.Equal(("Ubuntu", "The main server", "v1"), (node.Type, node.Description?.Text, node.Tags));
        var api = (C4Element)diagram.Find("api")!;
        Assert.Equal(("Serves the API", "https://example.com"), (api.Description?.Text, api.Link));
        Assert.Equal(((SceneColor)"red", (SceneColor)"#ffffff", (SceneColor)"blue"), (api.Style?.Fill, api.Style?.TextColor, api.Style?.Stroke));
        Assert.Equal(C4BoundaryKind.DeploymentNodeRight, diagram.Boundaries[1].Kind);
        var queue = (C4Element)diagram.Find("queue")!;
        Assert.Equal((C4ElementKind.System, C4ElementShape.Queue, true, "Queue", "A queue"), (queue.Kind, queue.Shape, queue.IsExternal, queue.Label.Text, queue.Description?.Text));

        // A second relationship between the same items replaces the first one
        Assert.HasCount(2, diagram.Relationships);
        var sends = diagram.Relationships[0];
        Assert.Equal(("Sends again", -40.0, 10.0, (SceneColor)"green"), (sends.Label.Text, sends.LabelOffsetX, sends.LabelOffsetY, sends.Style?.TextColor));
        Assert.Equal(C4RelationshipKind.Back, diagram.Relationships[1].Kind);
        Assert.Equal((3, 1), (diagram.ShapesPerRow, diagram.BoundariesPerRow));
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("sprites", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("C4Context\nPerson(a, \"A\"\n", "',' or ')' is expected.")]
    [InlineData("C4Context\nPerson(a, \n", "The attributes are not closed by ')'.")]
    [InlineData("C4Context\nBoundary(b, \"B\")\nPerson(a, \"A\")", "'{' is expected after the boundary 'b'.")]
    [InlineData("C4Context\nBoundary(b, \"B\") {\nPerson(a, \"A\")", "The boundary 'b' is not closed by '}'.")]
    [InlineData("C4Context\nRel(a, b, \"x\")", "The relationship goes from 'a', which is not an element nor a boundary.")]
    [InlineData("C4Context\nPersonX(a, \"A\")", "'PersonX' is not a statement of a C4 diagram.")]
    [InlineData("C4Context\n}", "(2, 1): '}' closes no boundary.")]
    [InlineData("C4Context\ntitle Item #1", "(2, 12): The title cannot contain '#'.")]
    public void InvalidC4Diagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new C4Diagram { Kind = C4DiagramKind.Component, Title = "A; title", Wrap = false, ShapesPerRow = 2 };
        var person = diagram.AddElement("person,1", C4ElementKind.Person, "The \"person\"", "Line 1\nLine 2");
        person.Technology = "Human";
        person.Tags = "tag";
        var boundary = diagram.AddBoundary("b", "Boundary", C4BoundaryKind.DeploymentNodeLeft);
        boundary.Type = "VM";
        var inner = new C4Boundary("inner", "Inner", C4BoundaryKind.Enterprise) { Description = "Described" };
        boundary.Boundaries.Add(inner);
        var component = new C4Element("c", C4ElementKind.Component, "Component") { Description = "Only a description", Shape = C4ElementShape.Queue };
        inner.Elements.Add(component);
        component.Style = new DiagramStyle { Fill = (SceneColor)"#ff0000" };
        var relationship = diagram.AddRelationship(person, component, "Uses", "HTTPS");
        relationship.Kind = C4RelationshipKind.Down;
        relationship.LabelOffsetY = 12.4;
        relationship.Style = new DiagramStyle { Stroke = SceneColor.Black };
        diagram.AddRelationship(component, boundary, "Runs in");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (C4Diagram)parsed.Diagram;
        Assert.Equal((C4DiagramKind.Component, "A; title", false, 2), (result.Kind, result.Title, result.Wrap, result.ShapesPerRow));
        var resultPerson = (C4Element)result.Find("person,1")!;
        Assert.Equal(("The \"person\"", "Line 1\nLine 2", "Human", "tag"), (resultPerson.Label.Text, resultPerson.Description?.Text, resultPerson.Technology, resultPerson.Tags));
        var resultComponent = (C4Element)result.Find("c")!;
        Assert.Equal(("Only a description", C4ElementShape.Queue, (SceneColor)"#ff0000"), (resultComponent.Description?.Text, resultComponent.Shape, resultComponent.Style?.Fill));
        Assert.Equal(("VM", C4BoundaryKind.DeploymentNodeLeft), (result.Boundaries[0].Type, result.Boundaries[0].Kind));
        Assert.Equal("Described", ((C4Boundary)result.Find("inner")!).Description?.Text);
        Assert.Equal((C4RelationshipKind.Down, 12.0), (result.Relationships[0].Kind, result.Relationships[0].LabelOffsetY));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new C4Diagram();
        var a = diagram.AddElement("a", C4ElementKind.Person, "A");
        var b = diagram.AddElement("b", C4ElementKind.System, "B");
        diagram.AddRelationship(a, b, "1");
        diagram.AddRelationship(a, b, "2");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Relationships.RemoveAt(1);
        a.Shape = C4ElementShape.Database;
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
