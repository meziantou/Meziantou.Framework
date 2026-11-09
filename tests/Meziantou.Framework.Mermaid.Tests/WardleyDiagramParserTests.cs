namespace Meziantou.Framework.Mermaid.Tests;

public sealed class WardleyDiagramParserTests
{
    [Fact]
    public void ComponentsLinksAndMovements()
    {
        #region Mermaid_WardleyDiagram
        var map = (WardleyDiagram)MermaidDocument.Parse("""
            wardley-beta
            title Tea Shop Value Chain

            anchor Business [0.95, 0.63]
            component Cup of Tea [0.79, 0.61]
            component Tea [0.63, 0.81]
            component Hot Water [0.52, 0.80]
            component Kettle [0.43, 0.35] (inertia)
            component Power [0.10, 0.70] (outsource)

            Business -> Cup of Tea
            Cup of Tea -> Tea
            Cup of Tea -> Hot Water
            Hot Water -> Kettle
            Kettle +> Power

            evolve Kettle 0.62
            evolve Power 0.89

            note "Standardising power allows Kettles to evolve faster" [0.30, 0.49]
            """).Diagram;
        #endregion

        Assert.Equal("Tea Shop Value Chain", map.Title);
        var business = Assert.Single(map.Anchors);
        Assert.Equal(("Business", 0.95, 0.63), (business.Name, business.Visibility, business.Evolution));
        Assert.Equal(["Cup of Tea", "Tea", "Hot Water", "Kettle", "Power"], map.Components.Select(component => component.Name));
        Assert.True(map.Components[3].Inertia);
        Assert.Equal(WardleySourcing.Outsource, map.Components[4].Sourcing);
        Assert.Equal(WardleyFlow.Forward, map.Links[4].Flow);
        Assert.Equal([("Kettle", 0.62), ("Power", 0.89)], map.Evolves.Select(evolve => (evolve.Component, evolve.Evolution)));
        Assert.Equal("Standardising power allows Kettles to evolve faster", Assert.Single(map.Notes).Text);
    }

    [Fact]
    public void LinkStyles()
    {
        var map = (WardleyDiagram)MermaidDocument.Parse("""
            wardley-beta
            A -> B
            A --> B
            A -.-> B
            A > B
            A +> B
            A +< B
            A +<> B
            A +'backup'> B
            A +'two ways'<> B; ignored
            A -> B; constraint #35;1
            A -.-> B +<
            "A" "B"
            A->B
            foo-bar->baz
            """).Diagram;
        Assert.Equal(
            [
                (false, WardleyFlow.None, null),
                (false, WardleyFlow.None, null),
                (true, WardleyFlow.None, null),
                (false, WardleyFlow.None, null),
                (false, WardleyFlow.Forward, null),
                (false, WardleyFlow.Backward, null),
                (false, WardleyFlow.Bidirectional, null),
                (false, WardleyFlow.Forward, "backup"),
                (false, WardleyFlow.Bidirectional, "two ways"),
                (false, WardleyFlow.None, "constraint #1"),
                (true, WardleyFlow.Backward, null),
                (false, WardleyFlow.None, null),
                (false, WardleyFlow.None, null),
                (false, WardleyFlow.None, (string?)null),
            ],
            map.Links.Select(link => (link.IsDashed, link.Flow, link.Label)));
        Assert.Equal(("foo-bar", "baz"), (map.Links[^1].Source, map.Links[^1].Target));
    }

    [Fact]
    public void ComponentOptions()
    {
        var map = (WardleyDiagram)MermaidDocument.Parse("""
            wardley-beta
            component "Mobile App" [0.2, 0.4] (build) (inertia)
            component API [0.3, 0.5] label [-50, 10] (buy) inertia
            component Cache [0.5, 0.6] (market)
            component real-time processing [55.5, 40.0]
            component byte pair encoding (BPE) [0.1, 0.1]
            component _id [0.5, 0.5]
            """).Diagram;
        Assert.Equal(["Mobile App", "API", "Cache", "real-time processing", "byte pair encoding (BPE)", "_id"], map.Components.Select(component => component.Name));
        Assert.Equal([WardleySourcing.Build, WardleySourcing.Buy, WardleySourcing.Market, WardleySourcing.None, WardleySourcing.None, WardleySourcing.None], map.Components.Select(component => component.Sourcing));
        Assert.Equal([true, true, false, false, false, false], map.Components.Select(component => component.Inertia));
        Assert.Equal(new WardleyLabelOffset(-50, 10), map.Components[1].LabelOffset);

        // A value above 1 is a percentage
        Assert.Equal((0.555, 0.4), (map.Components[3].Visibility, map.Components[3].Evolution));
    }

    [Fact]
    public void StagesPipelinesAnnotationsAndForces()
    {
        var map = (WardleyDiagram)MermaidDocument.Parse("""
            ---
            config:
              wardley-beta:
                padding: 20
                showGrid: true
                nodeRadius: 4
            ---
            wardley-beta
            size [1100, 800]
            evolution Genesis@0.25 / Concept -> "Custom Built"@0.5 -> Product -> Commodity / Accepted
            component Kettle [0.45, 0.57]
            pipeline Kettle {
              component Campfire Kettle [0.35] label [-60, 35]

              component Electric Kettle [0.53] %% comment
            }
            annotations [0.10, 90]
            annotations [5, 500]
            annotation 1,[0.60, 0.65] "Critical component"
            annotation 2,[1, 0] ""
            accelerator "AI Adoption" [0.55, 0.25]
            deaccelerator Legacy Constraints [0.15, 0.75]
            accTitle: Kettles
            accDescr: The evolution of the kettles
            """).Diagram;
        Assert.Equal((1100.0, 800.0, 20.0, 4.0, true), (map.Width, map.Height, map.MapPadding, map.NodeRadius, map.ShowGrid));
        Assert.Equal(["Genesis / Concept", "Custom Built", "Product", "Commodity / Accepted"], map.Stages.Select(stage => stage.Label));
        Assert.Equal([0.25, 0.5, null, null], map.Stages.Select(stage => stage.Boundary));
        var pipeline = Assert.Single(map.Pipelines);
        Assert.Equal("Kettle", pipeline.Parent);
        Assert.Equal([("Campfire Kettle", 0.35), ("Electric Kettle", 0.53)], pipeline.Components.Select(component => (component.Name, component.Evolution)));
        Assert.Equal(new WardleyLabelOffset(-60, 35), pipeline.Components[0].LabelOffset);
        Assert.Equal(new WardleyPosition(0.1, 0.9), map.AnnotationsPosition);
        Assert.Equal([(1, "Critical component", 0.6, 0.65), (2, string.Empty, 1.0, 0.0)], map.Annotations.Select(annotation => (annotation.Number, annotation.Text, annotation.Visibility, annotation.Evolution)));
        Assert.Equal(("AI Adoption", 0.55, 0.25), (map.Accelerators[0].Name, map.Accelerators[0].Visibility, map.Accelerators[0].Evolution));
        Assert.Equal("Legacy Constraints", Assert.Single(map.Deaccelerators).Name);
        Assert.Equal(("Kettles", "The evolution of the kettles"), (map.AccessibleTitle, map.Description));
    }

    [Fact]
    public void KeywordsWinOverNames()
    {
        // The lexer of Mermaid tries the keywords before the names: "builder" is "build" followed by "er"
        Assert.False(MermaidDocument.TryParse("wardley-beta\ncomponent builder [0.5, 0.5]", out _));
        Assert.False(MermaidDocument.TryParse("wardley-beta\ncomponent notebook [0.5, 0.5]", out _));
        var map = (WardleyDiagram)MermaidDocument.Parse("wardley-beta\ncomponent \"builder\" [0.5, 0.5]\ncomponent Notebook [0.5, 0.5]\ntitles -> x").Diagram;
        Assert.Equal(["builder", "Notebook"], map.Components.Select(component => component.Name));
        Assert.Equal(("titles", "x"), (map.Links[0].Source, map.Links[0].Target));
    }

    [Fact]
    public void QuirksOfTheGrammarOfMermaid()
    {
        // Checked against the parser of Mermaid 12.3.0
        var map = (WardleyDiagram)MermaidDocument.Parse("wardley-beta component A [0.1, 0.1]\naccTitle x\nA title -> B\nfoo- -> x\nA -> B;   \naccDescr\n  {\n  first\n  second\n  }").Diagram;
        Assert.Equal("A", Assert.Single(map.Components).Name);
        Assert.Equal([("accTitle", "x", (string?)null), ("A title", "B", null), ("foo-", "x", null), ("A", "B", null)], map.Links.Select(link => (link.Source, link.Target, link.Label)));
        Assert.Null(map.AccessibleTitle);
        Assert.Equal("first\nsecond", map.Description);
    }

    [Fact]
    public void MovementsOfMissingComponentsAreKept()
    {
        // Mermaid checks the target of a movement only when the component is on the map
        var document = MermaidDocument.Parse("wardley-beta\nevolve Missing 0.5\nevolve Unknown 500.0\ncomponent A [0.1, 0.1]\nevolve A 50.0");
        var map = (WardleyDiagram)document.Diagram;
        Assert.Equal([("Missing", 0.5), ("A", 0.5)], map.Evolves.Select(evolve => (evolve.Component, evolve.Evolution)));
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("Unknown", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wardley-beta\ncomponent A [1.5, 101.0]", "Component \"A\" evolution must be between 0-1 (decimal) or 0-100 (percentage). Received: 101")]
    [InlineData("wardley-beta\ncomponent A [1, 0.5]", "a decimal number, such as 0.5, is expected.")]
    [InlineData("wardley-beta\ncomponent A [0.5, 0.5] label [1.5, 2]", "an integer is expected.")]
    [InlineData("wardley-beta\nevolution Genesis", "The evolution needs at least two stages")]
    [InlineData("wardley-beta\npipeline Missing {\n  component A [0.5]\n}", "Pipeline \"Missing\" must reference an existing component with coordinates.")]
    [InlineData("wardley-beta\ncomponent A [0.5, 0.5]\npipeline A {\n}", "a pipeline needs at least one component.")]
    [InlineData("wardley-beta\ncomponent A [0.5, 0.5]\npipeline A { component B [0.5]\n}", "The components of a pipeline start on the next line.")]
    [InlineData("wardley-beta\ncomponent A [0.5, 0.5]\npipeline A {\n  component B [0.5]", "The pipeline is not closed by '}'.")]
    [InlineData("wardley-beta\nnote text [0.5, 0.5]", "the text of the note must be quoted.")]
    [InlineData("wardley-beta\nA -> B;", "Unexpected ';'")]
    [InlineData("wardley-beta\nA -> B 1", "Unexpected '1'")]
    [InlineData("wardley-beta\nsize [100, 200000]", "The height cannot be larger than 100000.")]
    [InlineData("wardley-beta\n[0.1]", "a statement is expected.")]
    [InlineData("wardley-beta\ncomponent A [0.5, 0.5] (other)", "'inertia' or a strategy (build, buy, outsource, market) is expected.")]
    public void InvalidWardleyDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyElements()
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("wardley-beta\ncomponent A [0.1, 0.1]\ncomponent B [0.1, 0.1]\nnote \"c\" [0.1, 0.1]", new MermaidParseOptions { MaxNodes = 2 }));
        Assert.Contains("The diagram has more than 2 elements.", exception.Message, StringComparison.Ordinal);
        exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("wardley-beta\nA -> B\nevolve A 0.5", new MermaidParseOptions { MaxEdges = 1 }));
        Assert.Contains("The diagram has more than 1 links and movements.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var map = new WardleyDiagram { Title = "Map", Width = 1000, Height = 700, NodeLabelOffset = 5, ShowGrid = true, AccessibleTitle = "Accessible" };
        map.Stages.Add(new WardleyStage("Genesis", "Concept") { Boundary = 0.3 });
        map.Stages.Add(new WardleyStage("custom build") { Boundary = 1 });
        map.AddAnchor("User", 0.9, 0.95);
        var component = map.AddComponent("size matters", 0.5, 1E-07);
        component.LabelOffset = new WardleyLabelOffset(int.MinValue, int.MaxValue);
        component.Sourcing = WardleySourcing.Market;
        component.Inertia = true;
        map.AddComponent("Database #1 %% \"quoted\"\n", 0.4, 0);
        map.AddPipeline("User").AddComponent("_v1", 1).LabelOffset = new WardleyLabelOffset(0, -3);
        var link = map.AddLink("User", "size matters");
        link.IsDashed = true;
        link.Flow = WardleyFlow.Bidirectional;
        link.Label = " #1 \n %% end ";
        map.AddLink("User", "_v1").Flow = WardleyFlow.Backward;
        map.AddLink("a", "b").Label = string.Empty;
        map.AddEvolve("User", 0.99);
        map.AddNote("Note with 'quotes' and \\", 0.1, 0.2);
        map.AnnotationsPosition = new WardleyPosition(0.5, 0.5);
        map.AddAnnotation(7, "#quot;", 0.3, 0.3);
        map.AddAccelerator("Fast", 0.2, 0.2);
        map.AddDeaccelerator("Slow", 0.3, 0.3);

        var text = MermaidWriter.Write(map);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (WardleyDiagram)parsed.Diagram;
        Assert.Equal(("Map", "Accessible", 1000.0, 700.0, 5.0, true), (result.Title, result.AccessibleTitle, result.Width, result.Height, result.NodeLabelOffset, result.ShowGrid));
        Assert.Equal(["Genesis / Concept", "custom build"], result.Stages.Select(stage => stage.Label));
        Assert.Equal(["size matters", "Database #1 %% \"quoted\"\n"], result.Components.Select(component => component.Name));
        Assert.Equal((1E-07, new WardleyLabelOffset(int.MinValue, int.MaxValue), WardleySourcing.Market, true), (result.Components[0].Evolution, result.Components[0].LabelOffset, result.Components[0].Sourcing, result.Components[0].Inertia));
        Assert.Equal((true, WardleyFlow.Bidirectional, " #1 \n %% end "), (result.Links[0].IsDashed, result.Links[0].Flow, result.Links[0].Label));
        Assert.Equal((WardleyFlow.Backward, (string?)null), (result.Links[1].Flow, result.Links[2].Label));
        Assert.Equal("Note with 'quotes' and \\", result.Notes[0].Text);
        Assert.Equal("#quot;", result.Annotations[0].Text);
        Assert.Equal(1.0, result.Pipelines[0].Components[0].Evolution);
        Assert.Contains("component \"size matters\" [0.5, 0.0000001]", text, StringComparison.Ordinal);
        Assert.Contains("evolution Genesis@0.3 / Concept -> custom build@1.0", text, StringComparison.Ordinal);
        Assert.Contains("User -.-> \"size matters\" +<>; #32;#35;1 #10; #37;% end#32;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSizesInTheConfiguration()
    {
        var map = new WardleyDiagram { Width = 900.5, Padding = 10, MapPadding = 30, NodeRadius = 7, AxisFontSize = 13, LabelFontSize = 11, Theme = DiagramTheme.Forest, Title = "  spaced  title" };
        var text = MermaidWriter.Write(map);
        Assert.Equal(text, MermaidWriter.Write(MermaidDocument.Parse(text).Diagram));
        var result = (WardleyDiagram)MermaidDocument.Parse(text).Diagram;
        Assert.Equal((900.5, 600.0, 10.0, 30.0, 7.0, 13.0, 11.0, "  spaced  title"), (result.Width, result.Height, result.Padding, result.MapPadding, result.NodeRadius, result.AxisFontSize, result.LabelFontSize, result.Title));
        Assert.Same(DiagramTheme.Forest, result.Theme);
    }

    [Fact]
    public void WriteEmptyDiagram()
    {
        Assert.Equal("wardley-beta\n", MermaidWriter.Write(new WardleyDiagram()));
    }

    [Fact]
    public void WriteRejectsUnsupportedDiagrams()
    {
        var oneStage = new WardleyDiagram();
        oneStage.Stages.Add(new WardleyStage("Only"));
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(oneStage));
        var emptyPipeline = new WardleyDiagram();
        emptyPipeline.AddComponent("A", 0.5, 0.5);
        emptyPipeline.AddPipeline("A");
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(emptyPipeline));
    }
}
