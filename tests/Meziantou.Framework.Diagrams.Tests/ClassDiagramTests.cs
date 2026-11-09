using System.Xml.Linq;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class ClassDiagramTests
{
    [Fact]
    public void ClassDiagram()
    {
        #region Diagrams_ClassDiagram
        var diagram = new ClassDiagram();
        var animal = diagram.AddClass("Animal");
        animal.Annotations.Add("abstract");
        animal.AddMember("int age", ClassMemberVisibility.Public);
        animal.AddMember("makeSound() void", ClassMemberVisibility.Public).IsAbstract = true;

        var duck = diagram.AddClass("Duck");
        duck.AddMember("String beakColor", ClassMemberVisibility.Private);
        duck.AddMember("swim()", ClassMemberVisibility.Public);

        var pond = diagram.AddClass("Pond");
        pond.AddMember("count() int", ClassMemberVisibility.Public).IsStatic = true;

        // Duck inherits from Animal; a Pond is made of Ducks
        diagram.AddRelation(animal, duck, fromEnd: ClassRelationEnd.Inheritance);
        var relation = diagram.AddRelation(pond, duck, fromEnd: ClassRelationEnd.Aggregation, label: "hosts");
        relation.FromCardinality = "1";
        relation.ToCardinality = "*";
        #endregion

        var svg = diagram.ToSvg();
        Assert.Contains(">«abstract»</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">+makeSound() : void</text>", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg, "ClassDiagram");
    }

    [Fact]
    public void NamespacesNotesAndInterfaces()
    {
        #region Diagrams_ClassDiagramNamespaces
        var diagram = new ClassDiagram { Direction = DiagramDirection.LeftToRight };
        var orders = diagram.AddNamespace("Orders");
        var order = new ClassDiagramClass("Order") { GenericType = "TItem" };
        order.AddMember("List~TItem~ items");
        orders.Classes.Add(order);

        var serializable = new ClassDiagramInterface("ISerializable");
        diagram.Interfaces.Add(serializable);
        diagram.AddRelation(order, serializable, toEnd: ClassRelationEnd.Lollipop);
        diagram.AddNote("Orders are immutable once paid", order);
        #endregion

        var svg = diagram.ToSvg();
        Assert.Contains(">Order&lt;TItem&gt;</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">List&lt;TItem&gt; items</text>", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg, "ClassDiagramNamespaces");
    }

    [Fact]
    public void EveryRelation()
    {
        var diagram = new ClassDiagram();
        var index = 0;
        foreach (var end in Enum.GetValues<ClassRelationEnd>())
        {
            foreach (var dashed in new[] { false, true })
            {
                var from = diagram.AddClass("A" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var to = diagram.AddClass("B" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                diagram.AddRelation(from, to, fromEnd: end, label: end + (dashed ? " dashed" : string.Empty)).IsDashed = dashed;
                index++;
            }
        }

        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void Members()
    {
        Assert.Equal("+getMessages() : List<string>", new ClassMember("getMessages() List~string~") { Visibility = ClassMemberVisibility.Public }.GetDisplayText());
        Assert.Equal("-List<List<int>> matrix", new ClassMember("List~List~int~~ matrix") { Visibility = ClassMemberVisibility.Private }.ToString());
        Assert.Equal("#run(a) : bool", new ClassMember("run(a) : bool") { Visibility = ClassMemberVisibility.Protected }.GetDisplayText());
        Assert.Equal("~id", new ClassMember("id") { Visibility = ClassMemberVisibility.Package }.GetDisplayText());
        Assert.True(new ClassMember("run()").IsMethod);
        Assert.False(new ClassMember("name").IsMethod);
    }

    [Fact]
    public void StylesLinksAndHiddenMembers()
    {
        var diagram = new ClassDiagram { HideEmptyMembersBox = true };
        diagram.ClassDefinitions["default"] = new DiagramStyle { Fill = (SceneColor)"#fef3c7" };
        var item = diagram.AddClass("Empty", "An empty class");
        item.Link = "https://example.com";
        item.Tooltip = "Tip";
        item.Style = new DiagramStyle { Stroke = (SceneColor)"#b45309" };
        var document = XDocument.Parse(diagram.ToSvg());
        // Without members, the compartments are hidden: no line divides the class
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "line");
        var svg = document.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("fill=\"#fef3c7\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#b45309\"", svg, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com\"", svg, StringComparison.Ordinal);
        Assert.Contains(">An empty class</text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAndEnumerate()
    {
        var diagram = new ClassDiagram();
        var outer = diagram.AddNamespace("Outer");
        var inner = new ClassDiagramNamespace("Inner");
        outer.Namespaces.Add(inner);
        inner.Classes.Add(new ClassDiagramClass("Deep"));
        diagram.Interfaces.Add(new ClassDiagramInterface("IFace"));
        Assert.Equal("Deep", diagram.Find("Deep")?.Id);
        Assert.IsType<ClassDiagramInterface>(diagram.Find("IFace"));
        Assert.Null(diagram.Find("Missing"));
        Assert.Equal(["Outer", "Inner"], diagram.EnumerateNamespaces().Select(ns => ns.Id));
        Assert.Throws<ArgumentNullException>("text", () => new ClassMember(null!));
        Assert.Throws<ArgumentException>("id", () => new ClassDiagramNamespace(string.Empty));
    }
}
