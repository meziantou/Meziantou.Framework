namespace Meziantou.Framework.Mermaid.Tests;

public sealed class RequirementDiagramParserTests
{
    private static RequirementDiagram Parse(string text) => (RequirementDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void RequirementsElementsAndRelationships()
    {
        #region Mermaid_RequirementDiagram
        var diagram = (RequirementDiagram)MermaidDocument.Parse("""
            requirementDiagram
                requirement test_req {
                    id: 1
                    text: the test text.
                    risk: high
                    verifymethod: test
                }
                element test_entity {
                    type: simulation
                }
                test_entity - satisfies -> test_req
            """).Content;
        #endregion

        var requirement = Assert.Single(diagram.Requirements);
        Assert.Equal(("test_req", "1", "the test text.", RequirementRisk.High, RequirementVerificationMethod.Test), (requirement.Name, requirement.RequirementId, requirement.Text?.Text, requirement.Risk, requirement.VerificationMethod));
        Assert.Equal("simulation", Assert.Single(diagram.Elements).Type?.Text);
        var relationship = Assert.Single(diagram.Relationships);
        Assert.Equal(("test_entity", "test_req", RequirementRelationshipKind.Satisfies), (relationship.Source.Name, relationship.Target.Name, relationship.Kind));
    }

    [Fact]
    public void KindsQuotesAndStyles()
    {
        var diagram = Parse("""
            requirementDiagram
                direction LR
                a <- copies - b
                designConstraint "__test_req__":::important {
                    text: "*italic* **bold**"
                    risk: Low # a comment
                }
                element b {
                    docRef: github.com/all_the_tests
                }
                a:::other
                functionalRequirement a {
                }
                classDef important fill:#f96
                class b important
                style a fill:#ffa,stroke:#000
            """);
        Assert.Equal(DiagramDirection.LeftToRight, diagram.Direction);
        var constraint = diagram.Requirements[0];
        Assert.Equal((RequirementKind.DesignConstraint, "__test_req__", RequirementRisk.Low), (constraint.Kind, constraint.Name, constraint.Risk));
        Assert.Equal(DiagramText.Markdown("*italic* **bold**"), constraint.Text);
        Assert.Equal(["important"], constraint.CssClasses);
        Assert.Equal("github.com/all_the_tests", diagram.Elements[0].DocumentReference?.Text);
        var relationship = Assert.Single(diagram.Relationships);
        Assert.Equal(("b", "a", RequirementRelationshipKind.Copies), (relationship.Source.Name, relationship.Target.Name, relationship.Kind));
        Assert.Equal((SceneColor)"#ffffaa", diagram.Find("a")!.Style?.Fill);
        Assert.Equal(["important"], diagram.Find("b")!.CssClasses);
    }

    [Fact]
    public void RelationshipsToUndefinedNodesAreIgnored()
    {
        var document = MermaidDocument.Parse("requirementDiagram\na - traces -> b");
        Assert.Empty(((RequirementDiagram)document.Content).Relationships);
        Assert.Contains("'a' is not defined", Assert.Single(document.Warnings).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("requirementDiagram\nrequirement a {\nrisk: huge\n}", "(3, 1): The risk must be low, medium or high.")]
    [InlineData("requirementDiagram\nrequirement a {\ncolor: red\n}", "'color' is not a field of a requirement")]
    [InlineData("requirementDiagram\nelement a {\ntype: x", "The element 'a' is not closed by '}'.")]
    [InlineData("requirementDiagram\na - likes -> b", "A relationship is expected")]
    [InlineData("requirementDiagram\nrequirement a {\n}\nelement a {\n}", "'a' is already defined as another kind of node.")]
    public void InvalidRequirementDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new RequirementDiagram { Direction = DiagramDirection.BottomToTop };
        var requirement = diagram.AddRequirement("test req", RequirementKind.PerformanceRequirement);
        requirement.RequirementId = "1.2";
        requirement.Text = "test the speed, quickly";
        requirement.Risk = RequirementRisk.Medium;
        requirement.VerificationMethod = RequirementVerificationMethod.Analysis;
        requirement.CssClasses.Add("hot");
        var element = diagram.AddElement("risk");
        element.Type = DiagramText.Markdown("**suite**");
        element.DocumentReference = "docs/a.md";
        diagram.AddRelationship(element, requirement, RequirementRelationshipKind.Verifies);
        diagram.AddRelationship(requirement, requirement, RequirementRelationshipKind.Refines);
        diagram.ClassDefinitions["hot"] = new DiagramStyle { Fill = (SceneColor)"#f00" };
        element.Style = new DiagramStyle { Stroke = SceneColor.Black };

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (RequirementDiagram)parsed.Content;
        var resultRequirement = Assert.Single(result.Requirements);
        Assert.Equal(("test req", "1.2", "test the speed, quickly"), (resultRequirement.Name, resultRequirement.RequirementId, resultRequirement.Text?.Text));
        Assert.Equal(DiagramText.Markdown("**suite**"), result.Elements[0].Type);
        Assert.HasCount(2, result.Relationships);
        Assert.Equal(["hot"], resultRequirement.CssClasses);
    }
}
