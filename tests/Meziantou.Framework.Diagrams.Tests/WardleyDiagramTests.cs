namespace Meziantou.Framework.Diagrams.Tests;

public sealed class WardleyDiagramTests
{
    [Fact]
    public void WardleyDiagram()
    {
        #region Diagrams_WardleyDiagram
        var map = new WardleyDiagram { Title = "Tea Shop Value Chain" };
        map.AddAnchor("Business", visibility: 0.95, evolution: 0.63);
        map.AddComponent("Cup of Tea", 0.79, 0.61);
        map.AddComponent("Tea", 0.63, 0.81);
        map.AddComponent("Hot Water", 0.52, 0.80);
        map.AddComponent("Kettle", 0.43, 0.35).Inertia = true;
        map.AddComponent("Power", 0.10, 0.70).Sourcing = WardleySourcing.Outsource;

        map.AddLink("Business", "Cup of Tea");
        map.AddLink("Cup of Tea", "Tea");
        map.AddLink("Cup of Tea", "Hot Water");
        map.AddLink("Hot Water", "Kettle");
        map.AddLink("Kettle", "Power").Flow = WardleyFlow.Forward;

        map.AddEvolve("Kettle", 0.62);
        map.AddEvolve("Power", 0.89);
        map.AddNote("Standardising power allows Kettles to evolve faster", 0.30, 0.49);
        #endregion

        Assert.Equal(["Business", "Cup of Tea", "Cup of Tea", "Hot Water", "Kettle"], map.Links.Select(link => link.Source));
        DiagramAssert.Svg(map, "WardleyDiagram");
    }

    [Fact]
    public void PipelinesSourcingAndForces()
    {
        var map = new WardleyDiagram { Title = "Platform", Width = 1100, Height = 800, ShowGrid = true };
        map.Stages.Add(new WardleyStage("Genesis", "Concept") { Boundary = 0.25 });
        map.Stages.Add(new WardleyStage("Custom", "Emerging") { Boundary = 0.5 });
        map.Stages.Add(new WardleyStage("Product") { Boundary = 0.75 });
        map.Stages.Add(new WardleyStage("Commodity") { Boundary = 1 });
        map.AddAnchor("Customer", 0.9, 0.95);
        map.AddComponent("Mobile App", 0.8, 0.85).Sourcing = WardleySourcing.Build;
        var web = map.AddComponent("Web App", 0.75, 0.8);
        web.LabelOffset = new WardleyLabelOffset(-60, 10);
        web.Sourcing = WardleySourcing.Build;
        map.AddComponent("API Gateway", 0.7, 0.65).Sourcing = WardleySourcing.Buy;
        var database = map.AddComponent("Database", 0.5, 0.45);
        database.Sourcing = WardleySourcing.Buy;
        database.Inertia = true;
        map.AddComponent("Cloud", 0.3, 0.95).Sourcing = WardleySourcing.Market;
        var storage = map.AddPipeline("Database");
        storage.AddComponent("File System", 0.25);
        storage.AddComponent("SQL", 0.5).LabelOffset = new WardleyLabelOffset(-10, 20);
        storage.AddComponent("NoSQL", 0.7);
        map.AddLink("Customer", "Mobile App");
        map.AddLink("Customer", "Web App").Flow = WardleyFlow.Bidirectional;
        map.AddLink("Mobile App", "API Gateway").Flow = WardleyFlow.Backward;
        var dashed = map.AddLink("API Gateway", "Database");
        dashed.IsDashed = true;
        dashed.Label = "reads";
        map.AddLink("NoSQL", "Cloud").Label = "hosted on";
        map.AddLink("Database", "Missing");
        map.AddEvolve("API Gateway", 0.85);
        map.AddEvolve("Database", 0.75);
        map.AddAccelerator("Cloud Native", 0.2, 0.85);
        map.AddDeaccelerator("Legacy Data", 0.45, 0.35);
        map.AnnotationsPosition = new WardleyPosition(0.1, 0.2);
        map.AddAnnotation(2, "Integration layer", 0.7, 0.6);
        map.AddAnnotation(1, "User touchpoints", 0.78, 0.82);
        map.AddAnnotation(3, string.Empty, 0.5, 0.4);
        DiagramAssert.Svg(map);
    }

    [Fact]
    public void DarkThemeAndConfiguration()
    {
        var map = new WardleyDiagram { Theme = DiagramTheme.Dark, Width = 500, Height = 300, MapPadding = 30, NodeRadius = 8, NodeLabelOffset = 4, AxisFontSize = 14, LabelFontSize = 12 };
        map.Stages.Add(new WardleyStage("Unmodelled"));
        map.Stages.Add(new WardleyStage("Divergent"));
        map.Stages.Add(new WardleyStage("Modelled"));
        map.AddComponent("Raw Data", 0.15, 0.2).Sourcing = WardleySourcing.Outsource;
        map.AddComponent("Reports", 0.75, 0.7).Inertia = true;
        map.AddLink("Reports", "Raw Data").Flow = WardleyFlow.Forward;
        DiagramAssert.Svg(map);
    }

    [Fact]
    public void NodesAreMergedByName()
    {
        // As in Mermaid, an anchor and a component of the same name are one node, drawn at the position of the component
        var map = new WardleyDiagram();
        map.AddAnchor("Shared", 0.9, 0.1);
        map.AddComponent("Shared", 0.5, 0.5).LabelOffset = new WardleyLabelOffset(5, 5);
        map.AddComponent("Shared", 0.4, 0.6);
        map.AddComponent("Other", 0.4, 0.6);
        map.AddLink("Shared", "Other");
        map.AddEvolve("Shared", 0.7);
        map.AddEvolve("Shared", 0.8);
        map.AddEvolve("Missing", 0.9);
        var svg = map.ToSvg();
        Assert.HasCount(2, svg.Split(">Shared<"));
        Assert.HasCount(2, svg.Split("wardley-trend\""));
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void EmptyMap()
    {
        DiagramAssert.Svg(new WardleyDiagram());
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>("visibility", () => new WardleyComponent("A", 1.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>("evolution", () => new WardleyAnchor("A", 0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("evolution", () => new WardleyPosition(0, -0.1));
        Assert.Throws<ArgumentNullException>("name", () => new WardleyComponent(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("number", () => new WardleyAnnotation(-1, "a", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new WardleyStage("A") { Boundary = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new WardleyDiagram { Width = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new WardleyDiagram { LabelFontSize = 0 });
        var component = new WardleyComponent("A", 0.2, 0.3);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => component.Evolution = 2);
        Assert.Throws<ArgumentNullException>("value", () => component.Name = null!);
        Assert.Equal("A", component.ToString());
        Assert.Equal("Genesis / Concept", new WardleyStage(" Genesis ", "Concept").Label);
        Assert.Equal("1. Text", new WardleyAnnotation(1, "Text", 0, 0).ToString());
        Assert.Equal("A -> B", new WardleyLink("A", "B").ToString());
    }
}
