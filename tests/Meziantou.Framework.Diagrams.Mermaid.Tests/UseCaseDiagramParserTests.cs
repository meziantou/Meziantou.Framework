namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class UseCaseDiagramParserTests
{
    private static UseCaseDiagram Parse(string text) => (UseCaseDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void ActorsUseCasesAndBoundaries()
    {
        #region Mermaid_UseCaseDiagram
        var diagram = (UseCaseDiagram)MermaidDocument.Parse("""
            usecase-beta
            direction LR
            actor Customer("Customer")
            systemBoundary "Order system"
              Checkout("Place order")
              Pay[Pay online]
            end
            Customer --> Checkout
            Checkout ..> : include Pay
            """).Diagram;
        #endregion

        Assert.Equal("Customer", Assert.Single(diagram.Actors).Label?.Text);
        var boundary = Assert.Single(diagram.Boundaries);
        Assert.Equal(("Order_system", "Order system"), (boundary.Id, boundary.Title?.Text));
        Assert.Equal(["Checkout", "Pay"], boundary.Elements.Select(element => element.Id));
        Assert.Equal(UseCaseShape.Rectangle, ((UseCase)diagram.Find("Pay")!).Shape);
        Assert.Equal(UseCaseRelationshipKind.Include, diagram.Relationships[1].Kind);
        Assert.Equal(UseCaseArrowHead.Arrow, diagram.Relationships[0].ToArrowHead);
    }

    [Theory]
    [InlineData("A --> B", UseCaseArrowHead.None, UseCaseArrowHead.Arrow, 1)]
    [InlineData("A ----> B", UseCaseArrowHead.None, UseCaseArrowHead.Arrow, 3)]
    [InlineData("A <--- B", UseCaseArrowHead.Arrow, UseCaseArrowHead.None, 2)]
    [InlineData("A -- B", UseCaseArrowHead.None, UseCaseArrowHead.None, 1)]
    [InlineData("A --o B", UseCaseArrowHead.None, UseCaseArrowHead.Circle, 1)]
    [InlineData("A o-- B", UseCaseArrowHead.Circle, UseCaseArrowHead.None, 1)]
    [InlineData("A --x B", UseCaseArrowHead.None, UseCaseArrowHead.Cross, 1)]
    [InlineData("A x-- B", UseCaseArrowHead.Cross, UseCaseArrowHead.None, 1)]
    public void Associations(string statement, UseCaseArrowHead from, UseCaseArrowHead to, int minLength)
    {
        var relationship = Assert.Single(Parse("usecase-beta\n" + statement).Relationships);
        Assert.Equal((UseCaseRelationshipKind.Association, from, to, minLength), (relationship.Kind, relationship.FromArrowHead, relationship.ToArrowHead, relationship.MinLength));
    }

    [Fact]
    public void LabelsMetadataAndStyles()
    {
        var document = MermaidDocument.Parse("""
            usecase-beta
            actor Agent("Sales agent")@{ business: true } <<Employee>>
            actor Broker@{ type: hollow }, Bot@{ icon: "fa:user" }
            Quote("`Prepare **quote**`")@{ business: true } <<Core>>:::critical
            Agent opens@-- files R&D report ---> Quote
            Broker -- "`a **label**`" --o Quote
            Bot --|> Broker
            Agent --|> Broker
            json Data@{
              "status": "pending"
            }:::data
            Quote --> Data
            note for Quote "Requires a #quot;session#quot;"
            classDef critical stroke:#c33
            class opens critical
            style Agent stroke:#00f
            opens@{ animation: fast }
            """);
        var diagram = (UseCaseDiagram)document.Diagram;
        var agent = (UseCaseActor)diagram.Find("Agent")!;
        Assert.Equal((true, "Employee", (SceneColor)"#0000ff"), (agent.IsBusiness, agent.Stereotype, agent.Style?.Stroke));
        Assert.Equal(UseCaseActorKind.Hollow, ((UseCaseActor)diagram.Find("Broker")!).Kind);
        var quote = (UseCase)diagram.Find("Quote")!;
        Assert.Equal((DiagramText.Markdown("Prepare **quote**"), true, "Core"), (quote.Label, quote.IsBusiness, quote.Stereotype));
        Assert.Equal(["critical"], quote.CssClasses);
        var opens = diagram.Relationships[0];
        Assert.Equal(("opens", "files R&D report", 2), (opens.Id, opens.Label?.Text, opens.MinLength));
        Assert.Equal(["critical"], opens.CssClasses);
        Assert.Equal(DiagramText.Markdown("a **label**"), diagram.Relationships[1].Label);
        Assert.Equal(UseCaseRelationshipKind.Generalization, diagram.Relationships[2].Kind);
        Assert.Equal(["data"], Assert.Single(diagram.Tables).CssClasses);
        Assert.Equal("Requires a \"session\"", Assert.Single(diagram.Notes).Text.Text);
        Assert.HasCount(2, document.Warnings);
    }

    [Theory]
    [InlineData("usecase-beta\nactor A\nA ..> : include B", "An include relationship links two use cases.")]
    [InlineData("usecase-beta\nactor A\nA --|> B", "A generalization links two actors or two use cases.")]
    [InlineData("usecase-beta\nA --> B; B --> C", "a statement is alone on its line.")]
    [InlineData("usecase-beta\nactor A\nA(\"x\")", "'A' is already declared as an actor.")]
    [InlineData("usecase-beta\nsystemBoundary S\nA", "The boundary 'S' is not closed by 'end'.")]
    [InlineData("usecase-beta\njson D@{ \"a\": }", "The JSON object is not valid")]
    [InlineData("usecase-beta\nnote for X \"a\"\nactor B\nX --> B\nsystemBoundary X2\nend\nnote for X2 \"b\"", "The target of the note, 'X2', is not an actor nor a use case.")]
    [InlineData("usecase-beta\nA[Archive]@{ business: true }", "cannot be a business use case.")]
    [InlineData("usecase-beta\nclass Z c", "The class or style target 'Z' is not defined.")]
    [InlineData("usecase-beta\nA --> end", "(2, 7): 'end' is a keyword and cannot be an identifier.")]
    public void InvalidUseCaseDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new UseCaseDiagram { Direction = DiagramDirection.TopToBottom };
        var customer = diagram.AddActor("customer", "The \"customer\"");
        customer.Kind = UseCaseActorKind.Hollow;
        customer.IsBusiness = true;
        customer.Stereotype = "Person";
        var boundary = diagram.AddBoundary("shop", DiagramText.Markdown("The **shop**"));
        boundary.Kind = UseCaseBoundaryKind.Package;
        boundary.CssClasses.Add("system");
        var browse = diagram.AddUseCase("browse", "Browse\nproducts");
        var archive = diagram.AddUseCase("archive", shape: UseCaseShape.Rectangle);
        boundary.Elements.Add(browse);
        var table = diagram.AddTable("data", """{ "a": [1, 2] }""");
        var association = diagram.AddRelationship(customer, browse, label: "uses -- often");
        association.MinLength = 3;
        association.Id = "e1";
        association.CssClasses.Add("hot");
        association.Style = new DiagramStyle { Stroke = SceneColor.Black };
        diagram.AddRelationship(browse, archive, UseCaseRelationshipKind.Extend);
        diagram.AddRelationship(archive, table).ToArrowHead = UseCaseArrowHead.None;
        var crossed = diagram.AddRelationship(browse, customer);
        crossed.FromArrowHead = UseCaseArrowHead.Cross;
        crossed.ToArrowHead = UseCaseArrowHead.None;
        diagram.AddNote(browse, "Fast");
        diagram.ClassDefinitions["hot"] = new DiagramStyle { StrokeWidth = 2 };

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (UseCaseDiagram)parsed.Diagram;
        var resultCustomer = Assert.Single(result.Actors);
        Assert.Equal(("The \"customer\"", UseCaseActorKind.Hollow, true, "Person"), (resultCustomer.Label?.Text, resultCustomer.Kind, resultCustomer.IsBusiness, resultCustomer.Stereotype));
        var resultBoundary = Assert.Single(result.Boundaries);
        Assert.Equal((DiagramText.Markdown("The **shop**"), UseCaseBoundaryKind.Package), (resultBoundary.Title, resultBoundary.Kind));
        Assert.Equal("Browse\nproducts", result.Find("browse")!.Label?.Text);
        Assert.Equal(UseCaseShape.Rectangle, ((UseCase)result.Find("archive")!).Shape);
        var resultAssociation = result.Relationships[0];
        Assert.Equal(("e1", "uses -- often", 3, ["hot"]), (resultAssociation.Id, resultAssociation.Label?.Text, resultAssociation.MinLength, resultAssociation.CssClasses.ToArray()));
        Assert.Equal((SceneColor)"#000000", resultAssociation.Style?.Stroke);
        Assert.Equal(UseCaseArrowHead.Cross, result.Relationships[3].FromArrowHead);
        Assert.Equal("""{ "a": [1, 2] }""", Assert.Single(result.Tables).Json);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new UseCaseDiagram();
        var relationship = diagram.AddRelationship(diagram.AddActor("a"), diagram.AddUseCase("b"));
        relationship.FromArrowHead = UseCaseArrowHead.Arrow;
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Relationships.Clear();
        diagram.AddUseCase("with space");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.UseCases.RemoveAt(diagram.UseCases.Count - 1);
        diagram.AddUseCase("note");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
