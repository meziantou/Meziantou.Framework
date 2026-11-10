namespace Meziantou.Framework.Diagrams.Tests;

public sealed class ErDiagramTests
{
    [Fact]
    public void ErDiagram()
    {
        #region Diagrams_ErDiagram
        var diagram = new ErDiagram();
        var customer = diagram.AddEntity("CUSTOMER");
        customer.AddAttribute("string", "email", ErAttributeKeys.PrimaryKey);
        customer.AddAttribute("string", "name");

        var order = diagram.AddEntity("ORDER");
        order.AddAttribute("int", "id", ErAttributeKeys.PrimaryKey);
        order.AddAttribute("string", "customerEmail", ErAttributeKeys.ForeignKey);
        order.AddAttribute("date", "placedAt", comment: "UTC");

        var lineItem = diagram.AddEntity("LINE-ITEM");
        lineItem.AddAttribute("int", "quantity");

        diagram.AddRelationship(customer, order, ErCardinality.ExactlyOne, ErCardinality.ZeroOrMore, "places");
        diagram.AddRelationship(order, lineItem, ErCardinality.ExactlyOne, ErCardinality.OneOrMore, "contains");
        #endregion

        DiagramAssert.Svg(diagram, "ErDiagram");
    }

    [Fact]
    public void Subgraphs()
    {
        #region Diagrams_ErDiagramSubgraphs
        var diagram = new ErDiagram { Direction = DiagramDirection.LeftToRight };
        var sales = diagram.AddSubgraph("sales", "Sales");
        var customer = diagram.AddEntity("CUSTOMER");
        var order = diagram.AddEntity("ORDER");
        sales.Entities.Add(customer);
        sales.Entities.Add(order);

        var warehouse = diagram.AddEntity("WAREHOUSE", "Warehouse");
        diagram.AddRelationship(customer, order, ErCardinality.ExactlyOne, ErCardinality.ZeroOrMore, "places");

        // A relationship can link a subgraph; a non-identifying one is dashed
        var relationship = diagram.AddRelationship(sales, warehouse, ErCardinality.OneOrMore, ErCardinality.ZeroOrOne, "ships from");
        relationship.IsIdentifying = false;
        #endregion

        Assert.Same(sales, diagram.Find("sales"));
        Assert.Same(order, diagram.Find("ORDER"));
        DiagramAssert.Svg(diagram, "ErDiagramSubgraphs");
    }

    [Fact]
    public void CardinalitiesAndStyles()
    {
        var diagram = new ErDiagram();
        diagram.ClassDefinitions["important"] = new DiagramStyle { Fill = (SceneColor)"#ffcc00" };
        var a = diagram.AddEntity("A");
        a.CssClasses.Add("important");
        var b = diagram.AddEntity("B");
        b.Style = new DiagramStyle { Stroke = (SceneColor)"#ff0000" };
        foreach (var cardinality in Enum.GetValues<ErCardinality>())
        {
            diagram.AddRelationship(a, b, cardinality, cardinality);
        }

        var nested = diagram.AddSubgraph("outer").Subgraphs;
        nested.Add(new ErSubgraph("inner") { Direction = DiagramDirection.LeftToRight });
        Assert.Equal(["outer", "inner"], diagram.EnumerateSubgraphs().Select(subgraph => subgraph.Id));

        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#ffcc00\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#ff0000\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void GenericTypesAreShownWithAngleBrackets()
    {
        var diagram = new ErDiagram();
        diagram.AddEntity("A").AddAttribute("List~int~", "values");
        Assert.Contains("List&lt;int&gt;", diagram.ToSvg(), StringComparison.Ordinal);
    }
}
