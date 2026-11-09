namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class ArchitectureDiagramParserTests
{
    private static ArchitectureDiagram Parse(string text) => (ArchitectureDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void GroupsServicesAndEdges()
    {
        #region Mermaid_ArchitectureDiagram
        var diagram = (ArchitectureDiagram)MermaidDocument.Parse("""
            architecture-beta
                group api(cloud)[API]
                service db(database)[Database] in api
                service disk(disk)[Storage] in api
                service server(server)[Server] in api
                db:L -- R:server
                disk:T --> B:server
            """).Diagram;
        #endregion

        var api = Assert.Single(diagram.Groups);
        Assert.Equal(("cloud", "API"), (api.Icon, api.Title?.Text));
        Assert.Equal(["db", "disk", "server"], api.Nodes.Select(node => node.Id));
        var edge = diagram.Edges[1];
        Assert.Equal((ArchitectureSide.Top, ArchitectureSide.Bottom, false, true), (edge.FromSide, edge.ToSide, edge.FromArrow, edge.ToArrow));
    }

    [Fact]
    public void JunctionsAlignmentsLabelsAndGroupEdges()
    {
        var diagram = Parse("""
            architecture-beta
                group one[One]
                group two["Two [2]"] in one
                group three
                service a "A" in two
                service b(logos:aws-s3)['B'] in three
                junction j
                service c
                a{group}:R <-[calls]-> L:b{group}
                c:B -- T:j
                align row c j
            """);
        var two = diagram.Groups[0].Groups[0];
        Assert.Equal("Two [2]", two.Title?.Text);
        Assert.Equal("A", ((ArchitectureService)two.Nodes[0]).IconText);
        Assert.Equal(("logos:aws-s3", "B"), (((ArchitectureService)diagram.Groups[1].Nodes[0]).Icon, ((ArchitectureService)diagram.Groups[1].Nodes[0]).Title?.Text));
        var edge = diagram.Edges[0];
        Assert.Equal((true, true, true, true, "calls"), (edge.FromGroup, edge.ToGroup, edge.FromArrow, edge.ToArrow, edge.Label?.Text));
        Assert.IsType<ArchitectureJunction>(diagram.Nodes[0]);
        var alignment = Assert.Single(diagram.Alignments);
        Assert.Equal((true, ["c", "j"]), (alignment.IsRow, alignment.Members.Select(member => member.Id).ToArray()));
    }

    [Theory]
    [InlineData("architecture-beta\nservice a in g", "The parent of the service 'a', 'g', does not exist")]
    [InlineData("architecture-beta\nservice a\nservice a", "The identifier 'a' is already in use.")]
    [InlineData("architecture-beta\nservice a\na:L -- R:b", "'b' is not declared")]
    [InlineData("architecture-beta\nservice a\nservice b\na{group}:L -- R:b", "The {group} modifier needs an edge between two different groups.")]
    [InlineData("architecture-beta\nservice a\nservice b\na:X -- R:b", "a side is expected")]
    [InlineData("architecture-beta\nservice a\nalign row a", "An alignment has two members at least.")]
    [InlineData("architecture-beta\ngroup g in g", "cannot be placed within itself")]
    [InlineData("architecture-beta\nservice L", "'L' is a keyword and cannot be an identifier.")]
    public void InvalidArchitectureDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new ArchitectureDiagram { IconSize = 60 };
        var outer = diagram.AddGroup("outer", "Outer [x]", "cloud");
        var inner = new ArchitectureGroup("inner") { Title = "Inner \"quoted\"" };
        outer.Groups.Add(inner);
        var a = new ArchitectureService("a") { IconText = "A\\B" };
        inner.Nodes.Add(a);
        var b = diagram.AddService("b-2", "B", "server");
        var junction = new ArchitectureJunction("j");
        outer.Nodes.Add(junction);
        var edge = diagram.AddEdge(a, ArchitectureSide.Bottom, b, ArchitectureSide.Top);
        edge.Label = "uses";
        edge.FromArrow = true;
        diagram.AddEdge(junction, ArchitectureSide.Left, a, ArchitectureSide.Right).ToArrow = true;
        var alignment = new ArchitectureAlignment(isRow: false);
        alignment.Members.Add(a);
        alignment.Members.Add(b);
        diagram.Alignments.Add(alignment);

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (ArchitectureDiagram)parsed.Diagram;
        Assert.Equal(60, result.IconSize);
        var resultOuter = Assert.Single(result.Groups);
        Assert.Equal(("Outer [x]", "cloud", "Inner \"quoted\""), (resultOuter.Title?.Text, resultOuter.Icon, resultOuter.Groups[0].Title?.Text));
        Assert.Equal("A\\B", ((ArchitectureService)resultOuter.Groups[0].Nodes[0]).IconText);
        Assert.Equal(("uses", true), (result.Edges[0].Label?.Text, result.Edges[0].FromArrow));
        Assert.False(Assert.Single(result.Alignments).IsRow);
    }
}
