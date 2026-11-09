namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class ClassDiagramParserTests
{
    private static ClassDiagram Parse(string text) => (ClassDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void Classes()
    {
        var diagram = Parse("""
            classDiagram
                class Animal["An animal"]
                class Square~Shape~ {
                    <<interface>>
                    +int id
                    -List~int~ position
                    +getPoints()$ List~int~
                    #draw()*
                    ~packagePrivate
                }
                Square : +String name
                <<abstract>> Animal
                class `Name with spaces`:::highlight
            """);
        var animal = (ClassDiagramClass)diagram.Find("Animal")!;
        Assert.Equal("An animal", animal.Label?.Text);
        Assert.Equal(["abstract"], animal.Annotations);
        var square = (ClassDiagramClass)diagram.Find("Square")!;
        Assert.Equal("Shape", square.GenericType);
        Assert.Equal(["interface"], square.Annotations);
        Assert.Equal(["+int id", "-List<int> position", "+getPoints() : List<int>", "#draw()", "~packagePrivate", "+String name"], square.Members.Select(member => member.GetDisplayText()));
        Assert.True(square.Members[2].IsStatic);
        Assert.True(square.Members[3].IsAbstract);
        Assert.Equal(["highlight"], diagram.Find("Name with spaces")!.CssClasses);
    }

    [Theory]
    [InlineData("A <|-- B", ClassRelationEnd.Inheritance, false, ClassRelationEnd.None)]
    [InlineData("A *-- B", ClassRelationEnd.Composition, false, ClassRelationEnd.None)]
    [InlineData("A o-- B", ClassRelationEnd.Aggregation, false, ClassRelationEnd.None)]
    [InlineData("A <-- B", ClassRelationEnd.Arrow, false, ClassRelationEnd.None)]
    [InlineData("A -- B", ClassRelationEnd.None, false, ClassRelationEnd.None)]
    [InlineData("A ..> B", ClassRelationEnd.None, true, ClassRelationEnd.Arrow)]
    [InlineData("A ..|> B", ClassRelationEnd.None, true, ClassRelationEnd.Inheritance)]
    [InlineData("A .. B", ClassRelationEnd.None, true, ClassRelationEnd.None)]
    [InlineData("A --* B", ClassRelationEnd.None, false, ClassRelationEnd.Composition)]
    [InlineData("A --o B", ClassRelationEnd.None, false, ClassRelationEnd.Aggregation)]
    [InlineData("A <|--|> B", ClassRelationEnd.Inheritance, false, ClassRelationEnd.Inheritance)]
    public void Relations(string text, ClassRelationEnd fromEnd, bool dashed, ClassRelationEnd toEnd)
    {
        var relation = Assert.Single(Parse("classDiagram\n" + text).Relations);
        Assert.Equal((fromEnd, dashed, toEnd), (relation.FromEnd, relation.IsDashed, relation.ToEnd));
        Assert.Equal(("A", "B"), (relation.From.Id, relation.To.Id));
    }

    [Fact]
    public void CardinalitiesLabelsLollipopsAndNotes()
    {
        var diagram = Parse("""
            classDiagram
                Customer "1" --> "*" Ticket : buys; then waits
                bar ()-- Foo
                note "A general note"
                note for Foo "A note<br>on two lines"
            """);
        var relation = diagram.Relations[0];
        Assert.Equal(("1", "*", "buys; then waits"), (relation.FromCardinality, relation.ToCardinality, relation.Label?.Text));
        Assert.IsType<ClassDiagramInterface>(diagram.Relations[1].From);
        Assert.Equal(ClassRelationEnd.Lollipop, diagram.Relations[1].FromEnd);
        Assert.Null(diagram.Notes[0].Class);
        Assert.Equal(("A note\non two lines", "Foo"), (diagram.Notes[1].Text.Text, diagram.Notes[1].Class?.Id));
    }

    [Fact]
    public void Namespaces()
    {
        var diagram = Parse("""
            classDiagram
                namespace Company.Engineering.Backend {
                    class Developer
                }
                namespace Platform["The platform"] {
                    namespace Auth {
                        class UserService
                    }
                    class Gateway
                }
            """);
        Assert.Equal(["Company", "Platform"], diagram.Namespaces.Select(ns => ns.Id));
        var engineering = Assert.Single(diagram.Namespaces[0].Namespaces);
        Assert.Equal(("Company.Engineering", "Engineering"), (engineering.Id, engineering.Label?.Text));
        Assert.Equal("Developer", Assert.Single(Assert.Single(engineering.Namespaces).Classes).Id);
        Assert.Equal("The platform", diagram.Namespaces[1].Label?.Text);
        Assert.Equal("UserService", Assert.Single(Assert.Single(diagram.Namespaces[1].Namespaces).Classes).Id);
        Assert.Equal("Gateway", Assert.Single(diagram.Namespaces[1].Classes).Id);
        Assert.Empty(diagram.Classes);
    }

    [Fact]
    public void StylesAndInteractions()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              class:
                hideEmptyMembersBox: true
            ---
            classDiagram
                direction RL
                class A
                class B
                classDef warm fill:#f96
                cssClass "A,B" warm
                style A stroke:#333
                click A href "https://example.com" "Tooltip"
                link B "https://example.com/b"
                callback A "fn" "Callback tooltip"
            """);
        var diagram = (ClassDiagram)document.Diagram;
        Assert.True(diagram.HideEmptyMembersBox);
        Assert.Equal(DiagramDirection.RightToLeft, diagram.Direction);
        Assert.Equal(["warm"], diagram.Classes[0].CssClasses);
        Assert.Equal(["warm"], diagram.Classes[1].CssClasses);
        Assert.Equal((SceneColor)"#333333", diagram.Classes[0].Style?.Stroke);
        Assert.Equal(("https://example.com", "Callback tooltip"), (diagram.Classes[0].Link, diagram.Classes[0].Tooltip));
        Assert.Equal("https://example.com/b", diagram.Classes[1].Link);
        Assert.Single(document.Warnings);
    }

    [Theory]
    [InlineData("classDiagram\nclass A {\n+x", "(2, 1): The members of the class 'A' are not closed by '}'.")]
    [InlineData("classDiagram\nnamespace N {\nclass A", "The namespace 'N' is not closed by '}'.")]
    [InlineData("classDiagram\n}", "(2, 1): '}' closes no namespace.")]
    [InlineData("classDiagram\nA <|-- ", "(2, 8): Unexpected 'end of text': a class name is expected.")]
    [InlineData("classDiagram\nA ~~ B", "(2, 3): Unexpected '~~'")]
    [InlineData("classDiagram\n<<interface A", "(2, 1): The annotation is not closed by '>>'.")]
    [InlineData("classDiagram\nbar ()-- A\nclass bar", "(3, 7): 'bar' is an interface, not a class.")]
    public void InvalidClassDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new ClassDiagram { Direction = DiagramDirection.LeftToRight, HideEmptyMembersBox = true };
        var ns = diagram.AddNamespace("App.Core", "Core");
        var service = new ClassDiagramClass("Service", "The service") { GenericType = "T" };
        service.Annotations.Add("interface");
        service.AddMember("run() bool", ClassMemberVisibility.Public).IsAbstract = true;
        service.AddMember("count", ClassMemberVisibility.Private).IsStatic = true;
        ns.Classes.Add(service);
        var other = diagram.AddClass("Other Class");
        other.CssClasses.Add("hot");
        other.Style = new DiagramStyle { Fill = SceneColor.White };
        other.Link = "https://example.com";
        diagram.ClassDefinitions["hot"] = new DiagramStyle { Fill = (SceneColor)"#ff0000" };
        var relation = diagram.AddRelation(service, other, ClassRelationEnd.Composition, ClassRelationEnd.Arrow, "uses #1 & more");
        relation.FromCardinality = "1";
        relation.ToCardinality = "0..*";
        relation.IsDashed = true;
        diagram.AddNote("A \"note\"", service);

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (ClassDiagram)parsed.Diagram;
        var resultService = (ClassDiagramClass)result.Find("Service")!;
        Assert.Equal(("The service", "T", "interface"), (resultService.Label?.Text, resultService.GenericType, resultService.Annotations[0]));
        Assert.Equal(["+run() : bool", "-count"], resultService.Members.Select(member => member.GetDisplayText()));
        Assert.True(resultService.Members[0].IsAbstract);
        Assert.True(resultService.Members[1].IsStatic);
        Assert.Equal("uses #1 & more", result.Relations[0].Label?.Text);
        Assert.Equal(("1", "0..*", true), (result.Relations[0].FromCardinality, result.Relations[0].ToCardinality, result.Relations[0].IsDashed));
        Assert.Equal("A \"note\"", result.Notes[0].Text.Text);
        Assert.Equal("App.Core", Assert.Single(result.Namespaces).Id);
        Assert.Equal(["hot"], result.Find("Other Class")!.CssClasses);
    }
}
