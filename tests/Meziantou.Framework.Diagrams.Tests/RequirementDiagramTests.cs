namespace Meziantou.Framework.Diagrams.Tests;

public sealed class RequirementDiagramTests
{
    [Fact]
    public void RequirementDiagram()
    {
        #region Diagrams_RequirementDiagram
        var diagram = new RequirementDiagram();
        var checkout = diagram.AddRequirement("checkout");
        checkout.RequirementId = "1";
        checkout.Text = "Orders can be paid online.";
        checkout.Risk = RequirementRisk.High;
        checkout.VerificationMethod = RequirementVerificationMethod.Test;

        var payment = diagram.AddRequirement("payment", RequirementKind.FunctionalRequirement);
        payment.RequirementId = "1.1";
        payment.Text = DiagramText.Markdown("Card payments are **authorized**.");
        payment.Risk = RequirementRisk.Medium;
        payment.VerificationMethod = RequirementVerificationMethod.Demonstration;

        var service = diagram.AddElement("payment_service");
        service.Type = "service";
        service.DocumentReference = "src/Payments";

        diagram.AddRelationship(checkout, payment, RequirementRelationshipKind.Contains);
        diagram.AddRelationship(service, payment, RequirementRelationshipKind.Satisfies);
        #endregion

        Assert.Same(service, diagram.Find("payment_service"));
        DiagramAssert.Svg(diagram, "RequirementDiagram");
    }

    [Fact]
    public void RelationshipKinds()
    {
        var diagram = new RequirementDiagram { Direction = DiagramDirection.LeftToRight };
        diagram.ClassDefinitions["default"] = new DiagramStyle { Fill = (SceneColor)"#eeeeee" };
        var source = diagram.AddElement("source");
        foreach (var kind in Enum.GetValues<RequirementRelationshipKind>())
        {
            var requirement = diagram.AddRequirement(kind.ToString(), RequirementKind.DesignConstraint);
            diagram.AddRelationship(source, requirement, kind);
        }

        var svg = diagram.ToSvg();
        Assert.Contains("&lt;&lt;traces&gt;&gt;", svg, StringComparison.Ordinal);
        Assert.Contains("&lt;&lt;Design Constraint&gt;&gt;", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#eeeeee\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }
}
