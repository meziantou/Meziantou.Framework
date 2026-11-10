namespace Meziantou.Framework.Diagrams.Tests;

public sealed class UseCaseDiagramTests
{
    [Fact]
    public void UseCaseDiagram()
    {
        #region Diagrams_UseCaseDiagram
        var diagram = new UseCaseDiagram();
        var customer = diagram.AddActor("customer", "Customer");
        var clerk = diagram.AddActor("clerk", "Payment clerk");
        clerk.Kind = UseCaseActorKind.Hollow;

        var store = diagram.AddBoundary("store", "Online store");
        store.Kind = UseCaseBoundaryKind.Package;
        var browse = diagram.AddUseCase("browse", "Browse products");
        var checkout = diagram.AddUseCase("checkout", "Checkout");
        var pay = diagram.AddUseCase("pay", "Process payment");
        store.Elements.Add(browse);
        store.Elements.Add(checkout);
        store.Elements.Add(pay);

        diagram.AddRelationship(customer, browse);
        diagram.AddRelationship(customer, checkout, label: "places order");
        diagram.AddRelationship(checkout, pay, UseCaseRelationshipKind.Include);
        diagram.AddRelationship(clerk, pay);
        diagram.AddNote(checkout, DiagramText.Markdown("Validates the **cart**"));
        #endregion

        Assert.Same(pay, diagram.Find("pay"));
        DiagramAssert.Svg(diagram, "UseCaseDiagram");
    }

    [Fact]
    public void ActorsShapesAndTables()
    {
        var diagram = new UseCaseDiagram { Direction = DiagramDirection.TopToBottom };
        var agent = diagram.AddActor("agent");
        agent.IsBusiness = true;
        agent.Stereotype = "Employee";
        var robot = diagram.AddActor("robot");
        robot.Kind = UseCaseActorKind.Awesome;
        var quote = diagram.AddUseCase("quote", "Prepare quote");
        quote.IsBusiness = true;
        var archive = diagram.AddUseCase("archive", "Archive", UseCaseShape.Rectangle);
        var data = diagram.AddTable("data", """{ "status": "pending", "tags": ["a", "b"], "items": [{ "name": "Book" }], "empty": {}, "none": [] }""");

        diagram.AddRelationship(agent, quote);
        diagram.AddRelationship(robot, quote).ToArrowHead = UseCaseArrowHead.Circle;
        diagram.AddRelationship(quote, archive, UseCaseRelationshipKind.Extend);
        diagram.AddRelationship(robot, agent, UseCaseRelationshipKind.Generalization);
        diagram.AddRelationship(archive, data).MinLength = 2;

        var svg = diagram.ToSvg();
        Assert.Contains("«extend»", svg, StringComparison.Ordinal);
        Assert.Contains("items[0].name", svg, StringComparison.Ordinal);
        Assert.Contains("«Employee»", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{ invalid }")]
    public void TableDataMustBeAJsonObject(string json)
    {
        Assert.Throws<ArgumentException>(nameof(json), () => new UseCaseJsonTable("data", json));
    }
}
