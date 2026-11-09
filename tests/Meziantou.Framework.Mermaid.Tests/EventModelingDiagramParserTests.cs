namespace Meziantou.Framework.Mermaid.Tests;

public sealed class EventModelingDiagramParserTests
{
    [Fact]
    public void FramesAndDataBlocks()
    {
        #region Mermaid_EventModeling
        var diagram = (EventModelingDiagram)MermaidDocument.Parse("""
            eventmodeling
            tf 01 ui CartUI
            tf 02 cmd AddItem { description: string }
            tf 03 evt ItemAdded [[ItemAdded]]
            rf 04 evt External.InventoryChanged
            tf 05 rmo CartItems ->> 03 ->> 04
            data ItemAdded {
              description: string
              price: number
            }
            """).Diagram;
        #endregion

        Assert.Equal(["01", "02", "03", "04", "05"], diagram.Frames.Select(frame => frame.Id));
        Assert.Equal((EventModelingEntityKind.Command, "description: string"), (diagram.Frames[1].Kind, diagram.Frames[1].Data?.Content));
        Assert.Same(diagram.DataBlocks[0], diagram.Frames[2].DataBlock);
        Assert.Equal("  description: string\n  price: number", diagram.DataBlocks[0].Data.Content);
        Assert.True(diagram.Frames[3].IsReset);
        Assert.Equal(["03", "04"], diagram.Frames[4].Sources.Select(frame => frame.Id));
    }

    [Fact]
    public void RelaxedNotationCommentsNotesAndScenarios()
    {
        var document = MermaidDocument.Parse("""
            eventmodeling
            timeframe 1 readmodel A.B.C // a comment
            /* a comment
               on several lines */ resetframe 2 processor P ->> 1 timeframe 3 command C ->> 9 ->> 1 [[Missing]] `json`"quoted { text }"
            entity Cart.Item
            data D `md`{
            # Title
            }
            note 1 {
            A note
            }
            gwt 3 given evt Item when cmd Add then rmo Cart
            gwt 3 given evt Item then evt Done
            """);
        var diagram = (EventModelingDiagram)document.Diagram;
        Assert.Equal((EventModelingEntityKind.ReadModel, "A.B.C"), (diagram.Frames[0].Kind, diagram.Frames[0].Entity));
        Assert.Equal((EventModelingEntityKind.Processor, true), (diagram.Frames[1].Kind, diagram.Frames[1].IsReset));
        Assert.Equal(("quoted { text }", EventModelingDataFormat.Json), (diagram.Frames[2].Data?.Content, diagram.Frames[2].Data?.Format));
        Assert.Equal(["1"], diagram.Frames[2].Sources.Select(frame => frame.Id));
        Assert.Null(diagram.Frames[2].DataBlock);
        Assert.Equal(["Cart.Item"], diagram.Entities);
        Assert.Equal(("# Title", EventModelingDataFormat.Markdown), (diagram.DataBlocks[0].Data.Content, diagram.DataBlocks[0].Data.Format));
        Assert.Equal("A note", Assert.Single(diagram.Notes).Data.Content);
        Assert.HasCount(2, diagram.Scenarios);
        var scenario = diagram.Scenarios[0];
        Assert.Equal((1, 1, 1, "Cart"), (scenario.Given.Count, scenario.When.Count, scenario.Then.Count, scenario.Then[0].Entity));
        Assert.Empty(diagram.Scenarios[1].When);

        // A command receives input from a screen or a processor, not a read model; the missing frame and data block are reported too
        Assert.HasCount(3, document.Warnings);
    }

    [Theory]
    [InlineData("eventmodeling\ntf 01 ui A\ntf 01 cmd B", "The frame '01' is already defined")]
    [InlineData("eventmodeling\ntf 0001 ui A", "A frame number of one to three digits is expected.")]
    [InlineData("eventmodeling\ntf 01 ui text", "'text' is a keyword and cannot be an identifier.")]
    [InlineData("eventmodeling\ntf 01 view A", "a kind is expected")]
    [InlineData("eventmodeling\ndata D { a }", "A data block starts with '{' alone at the end of its line.")]
    [InlineData("eventmodeling\ndata D {\n a\n  }", "The data block is not closed by '}' at the start of a line.")]
    [InlineData("eventmodeling\ntf 01 ui A {\n}", "The inline data is not closed on its line.")]
    [InlineData("eventmodeling\ntitle Hello", "Unexpected 'title': a statement is expected")]
    [InlineData("eventmodeling\n/* comment", "The comment is not closed by '*/'.")]
    [InlineData("eventmodeling\ngwt 01 given evt A", "'then' is expected.")]
    public void InvalidEventModels(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new EventModelingDiagram { Title = "Cart", Description = "Not written" };
        var block = new EventModelingDataBlock("Item", new EventModelingData("{\n  \"a\": 1\n},", EventModelingDataFormat.Json));
        diagram.DataBlocks.Add(block);
        var ui = diagram.AddFrame("1", EventModelingEntityKind.Ui, "CartUI");
        var command = diagram.AddFrame("02", EventModelingEntityKind.Command, "Cart.AddItem");
        command.Sources.Add(ui);
        command.DataBlock = block;
        command.Data = new EventModelingData(" inline } data ", EventModelingDataFormat.Text);
        var reset = diagram.AddFrame("003", EventModelingEntityKind.ReadModel, "Items");
        reset.IsReset = true;
        reset.Data = new EventModelingData(string.Empty);
        diagram.Entities.Add("Cart.Item");
        diagram.Notes.Add(new EventModelingNote(ui, new EventModelingData(string.Empty)));
        var scenario = new EventModelingScenario(command);
        scenario.Given.Add(new EventModelingStep(EventModelingEntityKind.Event, "Item"));
        scenario.Then.Add(new EventModelingStep(EventModelingEntityKind.Processor, "Item"));
        diagram.Scenarios.Add(scenario);

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (EventModelingDiagram)parsed.Diagram;
        Assert.Equal(("Cart", null), (result.Title, result.Description));
        var resultCommand = result.Find("02")!;
        Assert.Equal(("Cart.AddItem", "inline } data", EventModelingDataFormat.Text), (resultCommand.Entity, resultCommand.Data?.Content, resultCommand.Data?.Format));
        Assert.Equal("{\n  \"a\": 1\n},", resultCommand.DataBlock?.Data.Content);
        Assert.Same(result.Find("1"), Assert.Single(resultCommand.Sources));
        Assert.Equal((true, string.Empty), (result.Find("003")!.IsReset, result.Find("003")!.Data?.Content));
        Assert.Equal(EventModelingEntityKind.Processor, Assert.Single(result.Scenarios).Then[0].Kind);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new EventModelingDiagram();
        diagram.AddFrame("1234", EventModelingEntityKind.Event, "A");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Frames.Clear();
        diagram.AddFrame("1", EventModelingEntityKind.Event, "A").Data = new EventModelingData("two\nlines");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Frames.Clear();
        diagram.DataBlocks.Add(new EventModelingDataBlock("D", new EventModelingData("}\nend")));
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
