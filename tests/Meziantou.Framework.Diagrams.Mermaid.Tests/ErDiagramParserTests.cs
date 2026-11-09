namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class ErDiagramParserTests
{
    private static ErDiagram Parse(string text) => (ErDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void EntitiesAndRelationships()
    {
        #region Mermaid_ErDiagram
        var diagram = (ErDiagram)MermaidDocument.Parse("""
            erDiagram
                CUSTOMER ||--o{ ORDER : places
                ORDER ||--|{ LINE-ITEM : contains
                CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
                CUSTOMER {
                    string email PK
                    string name "Full name"
                }
            """).Diagram;
        #endregion

        Assert.Equal(["CUSTOMER", "ORDER", "LINE-ITEM", "DELIVERY-ADDRESS"], diagram.Entities.Select(entity => entity.Id));
        var places = diagram.Relationships[0];
        Assert.Equal((ErCardinality.ExactlyOne, ErCardinality.ZeroOrMore, true, "places"), (places.FromCardinality, places.ToCardinality, places.IsIdentifying, places.Label?.Text));
        var uses = diagram.Relationships[2];
        Assert.Equal((ErCardinality.OneOrMore, ErCardinality.OneOrMore, false), (uses.FromCardinality, uses.ToCardinality, uses.IsIdentifying));
        var customer = (ErEntity)diagram.Find("CUSTOMER")!;
        Assert.Equal(("string", "email", ErAttributeKeys.PrimaryKey), (customer.Attributes[0].Type, customer.Attributes[0].Name, customer.Attributes[0].Keys));
        Assert.Equal("Full name", customer.Attributes[1].Comment);
    }

    [Theory]
    [InlineData("A |o--o| B : r", ErCardinality.ZeroOrOne, ErCardinality.ZeroOrOne, true)]
    [InlineData("A }o..o{ B : r", ErCardinality.ZeroOrMore, ErCardinality.ZeroOrMore, false)]
    [InlineData("A o{--|| B : r", ErCardinality.ZeroOrMore, ErCardinality.ExactlyOne, true)]
    [InlineData("A only one to zero or more B : r", ErCardinality.ExactlyOne, ErCardinality.ZeroOrMore, true)]
    [InlineData("A many(0) optionally to 0+ B : r", ErCardinality.ZeroOrMore, ErCardinality.ZeroOrMore, false)]
    [InlineData("A 1 to one or many B : r", ErCardinality.ExactlyOne, ErCardinality.OneOrMore, true)]
    [InlineData("A one or zero .- 1+ B : r", ErCardinality.ZeroOrOne, ErCardinality.OneOrMore, false)]
    [InlineData("A u--|| B : r", ErCardinality.Parent, ErCardinality.ExactlyOne, true)]
    public void Cardinalities(string statement, ErCardinality from, ErCardinality to, bool identifying)
    {
        var relationship = Assert.Single(Parse("erDiagram\n" + statement).Relationships);
        Assert.Equal((from, to, identifying), (relationship.FromCardinality, relationship.ToCardinality, relationship.IsIdentifying));
    }

    [Fact]
    public void AttributesAliasesAndStyles()
    {
        var diagram = Parse("""
            erDiagram
                p["A person"]:::big {
                    string? middleName
                    `my type` *id PK, FK "The key"
                    Map~string, int~ values UK
                    int a int b
                }
                "Quoted name" ||--|| p : "has a"
                classDef big fill:#f00
                class p small
                style p stroke:#00f
            """);
        var person = (ErEntity)diagram.Find("p")!;
        Assert.Equal("A person", person.Label?.Text);
        Assert.Equal(["big", "small"], person.CssClasses);
        Assert.Equal((SceneColor)"#0000ff", person.Style?.Stroke);
        Assert.Equal(["string?", "my type", "Map~string, int~", "int", "int"], person.Attributes.Select(attribute => attribute.Type));
        Assert.Equal(["middleName", "*id", "values", "a", "b"], person.Attributes.Select(attribute => attribute.Name));
        Assert.Equal(ErAttributeKeys.PrimaryKey | ErAttributeKeys.ForeignKey, person.Attributes[1].Keys);
        Assert.Equal("has a", diagram.Relationships[0].Label?.Text);
        Assert.Equal("Quoted name", diagram.Relationships[0].From.Id);
    }

    [Fact]
    public void Subgraphs()
    {
        var diagram = Parse("""
            erDiagram
                direction LR
                subgraph TOP [Top level]
                    direction TB
                    subgraph inner
                        A ||--|| B : links
                    end
                    A
                    C
                end
                TOP ||--|| D : links
                inner ||--|| C : links
            """);
        var top = Assert.Single(diagram.Subgraphs);
        Assert.Equal(("TOP", "Top level", DiagramDirection.TopToBottom), (top.Id, top.Label?.Text, top.Direction));
        Assert.Equal(["C"], top.Entities.Select(entity => entity.Id));
        var inner = Assert.Single(top.Subgraphs);
        Assert.Equal(["A", "B"], inner.Entities.Select(entity => entity.Id));
        Assert.Same(top, diagram.Relationships[1].From);
        Assert.Same(inner, diagram.Relationships[2].From);
        Assert.Equal(DiagramDirection.LeftToRight, diagram.Direction);
    }

    [Theory]
    [InlineData("erDiagram\nA ||--|| B", "(2, 11): Unexpected 'end of text': ':' and the label of the relationship are expected.")]
    [InlineData("erDiagram\nA ||==|| B : r", "(2, 5): Unexpected '==||': '--' or '..' is expected.")]
    [InlineData("erDiagram\nA {\nstring", "the name of the attribute is expected")]
    [InlineData("erDiagram\nsubgraph S\nA", "The subgraph 'S' is not closed by 'end'.")]
    [InlineData("erDiagram\nend", "(2, 1): 'end' closes no subgraph.")]
    [InlineData("erDiagram\n\"a%b\"", "(2, 1): A quoted name cannot be empty")]
    public void InvalidErDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new ErDiagram { Direction = DiagramDirection.RightToLeft };
        var sales = diagram.AddSubgraph("sales", "Sales team");
        var customer = diagram.AddEntity("CUSTOMER", "The customer");
        customer.AddAttribute("List~int~", "PK", ErAttributeKeys.PrimaryKey | ErAttributeKeys.UniqueKey, "a comment");
        customer.CssClasses.Add("big");
        sales.Entities.Add(customer);
        var order = diagram.AddEntity("one");
        diagram.AddRelationship(customer, order, ErCardinality.ZeroOrOne, ErCardinality.OneOrMore, "places \"orders\"").IsIdentifying = false;
        diagram.AddRelationship(sales, order, ErCardinality.Parent, ErCardinality.ExactlyOne);
        diagram.ClassDefinitions["big"] = new DiagramStyle { StrokeWidth = 3 };
        order.Style = new DiagramStyle { Fill = SceneColor.White };

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (ErDiagram)parsed.Diagram;
        var resultCustomer = (ErEntity)result.Find("CUSTOMER")!;
        Assert.Equal("The customer", resultCustomer.Label?.Text);
        Assert.Equal(("List~int~", "PK", ErAttributeKeys.PrimaryKey | ErAttributeKeys.UniqueKey, "a comment"), (resultCustomer.Attributes[0].Type, resultCustomer.Attributes[0].Name, resultCustomer.Attributes[0].Keys, resultCustomer.Attributes[0].Comment));
        Assert.Equal("places \"orders\"", result.Relationships[0].Label?.Text);
        Assert.IsType<ErSubgraph>(result.Relationships[1].From);
        Assert.Equal(ErCardinality.Parent, result.Relationships[1].FromCardinality);
        Assert.Equal((SceneColor)"#ffffff", result.Find("one")!.Style?.Fill);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new ErDiagram();
        diagram.AddRelationship(diagram.AddEntity("A"), diagram.AddEntity("B"), ErCardinality.ExactlyOne, ErCardinality.Parent);
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
