namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class SequenceDiagramParserTests
{
    private static SequenceDiagram Parse(string text) => (SequenceDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void ParticipantsAndMessages()
    {
        #region Mermaid_SequenceDiagram
        var diagram = (SequenceDiagram)MermaidDocument.Parse("""
            sequenceDiagram
                actor Customer
                participant Shop as Web shop
                participant Bank@{ "type": "database" }
                Customer->>+Shop: Place order
                alt card accepted
                    Shop->>Bank: Charge card
                    Bank-->>Shop: Approved
                else card declined
                    Shop-)Customer: Ask for another card
                end
                Shop-->>-Customer: Order confirmed
                Note over Customer,Bank: One order, one payment
            """).Diagram;
        #endregion

        Assert.Equal([SequenceParticipantKind.Actor, SequenceParticipantKind.Participant, SequenceParticipantKind.Database], diagram.Participants.Select(participant => participant.Kind));
        Assert.Equal("Web shop", diagram.Find("Shop")!.Label?.Text);
        var order = (SequenceMessage)diagram.Steps[0];
        Assert.Equal((SequenceLineStyle.Solid, SequenceArrowHead.Arrow, true), (order.LineStyle, order.EndArrowHead, order.ActivatesReceiver));
        var alternative = (SequenceBlock)diagram.Steps[1];
        Assert.Equal((SequenceBlockKind.Alternative, 2, "card declined"), (alternative.Kind, alternative.Sections.Count, alternative.Sections[1].Label?.Text));
        Assert.Equal(SequenceArrowHead.OpenArrow, ((SequenceMessage)alternative.Sections[1].Steps[0]).EndArrowHead);
        Assert.True(((SequenceMessage)diagram.Steps[2]).DeactivatesSender);
        var note = (SequenceNote)diagram.Steps[3];
        Assert.Equal((SequenceNotePosition.Over, 2), (note.Position, note.Participants.Count));
    }

    [Theory]
    [InlineData("->", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.None)]
    [InlineData("-->>", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.Arrow)]
    [InlineData("<<->>", SequenceLineStyle.Solid, SequenceArrowHead.Arrow, SequenceArrowHead.Arrow)]
    [InlineData("<<-->>", SequenceLineStyle.Dotted, SequenceArrowHead.Arrow, SequenceArrowHead.Arrow)]
    [InlineData("--x", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.Cross)]
    [InlineData("-)", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.OpenArrow)]
    [InlineData("-|\\", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.HalfArrowTop)]
    [InlineData("--|/", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.HalfArrowBottom)]
    [InlineData("-\\\\", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.StickTop)]
    [InlineData("--//", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.StickBottom)]
    [InlineData("/|-", SequenceLineStyle.Solid, SequenceArrowHead.HalfArrowTop, SequenceArrowHead.None)]
    [InlineData("\\\\--", SequenceLineStyle.Dotted, SequenceArrowHead.StickBottom, SequenceArrowHead.None)]
    public void Arrows(string arrow, SequenceLineStyle lineStyle, SequenceArrowHead start, SequenceArrowHead end)
    {
        var message = (SequenceMessage)Assert.Single(Parse("sequenceDiagram\nA" + arrow + "B: text").Steps);
        Assert.Equal(("A", "B", lineStyle, start, end, "text"), (message.From.Id, message.To.Id, message.LineStyle, message.StartArrowHead, message.EndArrowHead, message.Text?.Text));
    }

    [Fact]
    public void BlocksBoxesAndNumbering()
    {
        var document = MermaidDocument.Parse("""
            sequenceDiagram
                title Sequence title
                box Aqua Alice & John
                    participant A as Alice
                    participant J as John
                end
                box Group without color
                    actor B
                end
                autonumber 10 5
                loop Every minute
                    A->>J: Ping; J-->>A: Pong #9829;
                end
                rect rgba(0, 0, 255, 0.5)
                    par_over first
                        A->>B: one
                    and second
                        J->>B: two
                    end
                end
                critical Connect
                    A->>()J: connect
                option Timeout
                    A()->>()J: retry
                end
                create participant C as Carl
                A->>C: Hi
                destroy C
                C-xA: Bye # a comment
                activate A
                deactivate A
                autonumber off
                link A: Dashboard @ https://example.com
                break when failing
                    A->>A: wrap:Self
                end
            """);
        var diagram = (SequenceDiagram)document.Diagram;
        Assert.Equal("Sequence title", diagram.Title);
        Assert.Equal((SceneColor)"aqua", diagram.Boxes[0].Color);
        Assert.Equal("Alice & John", diagram.Boxes[0].Title?.Text);
        Assert.Equal((null, "Group without color"), (diagram.Boxes[1].Color, diagram.Boxes[1].Title?.Text));
        var autonumber = (SequenceAutonumber)diagram.Steps[0];
        Assert.Equal((10m, 5m), (autonumber.Start, autonumber.Increment));
        var loop = (SequenceBlock)diagram.Steps[1];
        Assert.Equal("Pong ♥", ((SequenceMessage)loop.Sections[0].Steps[1]).Text?.Text);
        var rect = (SequenceBlock)diagram.Steps[2];
        Assert.Equal(SceneColor.Parse("rgba(0, 0, 255, 0.5)"), rect.Color);
        Assert.Equal(SequenceBlockKind.ParallelOverlap, ((SequenceBlock)rect.Sections[0].Steps[0]).Kind);
        var critical = (SequenceBlock)diagram.Steps[3];
        var retry = (SequenceMessage)critical.Sections[1].Steps[0];
        Assert.Equal((true, true), (retry.SenderConnection, retry.ReceiverConnection));
        var create = (SequenceMessage)diagram.Steps[4];
        Assert.Equal((true, "Carl"), (create.CreatesReceiver, create.To.Label?.Text));
        Assert.Same(create.To, ((SequenceMessage)diagram.Steps[5]).DestroyedParticipant);
        Assert.Equal("Self", ((SequenceMessage)((SequenceBlock)diagram.Steps[^1]).Sections[0].Steps[0]).Text?.Text);
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("actor menus", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sequenceDiagram\nA->>B", "':' and the text of the message are expected.")]
    [InlineData("sequenceDiagram\nA->>-B: x", "The participant 'A' is not active")]
    [InlineData("sequenceDiagram\ndeactivate A", "The participant 'A' is not active")]
    [InlineData("sequenceDiagram\nloop x\nA->>B: y", "The block is not closed by 'end'.")]
    [InlineData("sequenceDiagram\nelse x", "'else' is not in a block that accepts it.")]
    [InlineData("sequenceDiagram\nbox\nA->>B: x\nend", "A box holds only participant and actor declarations.")]
    [InlineData("sequenceDiagram\ncreate participant B\nA->>C: x", "The created participant 'B' has no creating message")]
    [InlineData("sequenceDiagram\nA->>B: x\ncreate participant B\nA->>B: y", "The participant 'B' already exists")]
    [InlineData("sequenceDiagram\nparticipant A@{ \"type\": \"unknown\" }", "'unknown' is not a type of participant")]
    [InlineData("sequenceDiagram\nend", "'end' closes no block.")]
    public void InvalidSequenceDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new SequenceDiagram { MirrorParticipants = false, Title = "Title" };
        var alice = diagram.AddParticipant("Alice", "Alice; the #1", SequenceParticipantKind.Queue);
        var bob = diagram.AddParticipant("Bob Smith");
        var box = new SequenceBox { Title = "Team (A)", Color = SceneColor.Parse("rgba(10, 20, 30, 0.5)") };
        box.Participants.Add(alice);
        diagram.Boxes.Add(box);
        diagram.Steps.Add(new SequenceAutonumber { Start = 1.5m, Increment = 2 });
        diagram.AddMessage(alice, bob, "wrap: line 1\nline 2").ActivatesReceiver = true;
        var block = diagram.AddBlock(SequenceBlockKind.Critical, "must");
        block.Sections.Add(new SequenceBlockSection("option 1"));
        block.Sections[1].Steps.Add(new SequenceMessage(bob, alice) { DeactivatesSender = true, LineStyle = SequenceLineStyle.Dotted, StartArrowHead = SequenceArrowHead.StickTop });
        var rect = diagram.AddBlock(SequenceBlockKind.Rectangle);
        rect.Color = SceneColor.Parse("#ff0000");
        var carl = new SequenceParticipant("Carl", "Carl C") { Kind = SequenceParticipantKind.Entity };
        diagram.Participants.Add(carl);
        rect.Sections[0].Steps.Add(new SequenceMessage(alice, carl) { CreatesReceiver = true, EndArrowHead = SequenceArrowHead.Arrow, ReceiverConnection = true });
        diagram.Steps.Add(new SequenceMessage(carl, carl) { DestroyedParticipant = carl, Text = "end" });
        diagram.AddNote("left", SequenceNotePosition.LeftOf, alice);
        diagram.Steps.Add(new SequenceActivation(bob));
        diagram.Steps.Add(new SequenceActivation(bob, isActivation: false));

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (SequenceDiagram)parsed.Diagram;
        Assert.Equal(("Title", false), (result.Title, result.MirrorParticipants));
        Assert.Equal(("Alice; the #1", SequenceParticipantKind.Queue), (result.Find("Alice")!.Label?.Text, result.Find("Alice")!.Kind));
        Assert.Equal(("Team (A)", SceneColor.Parse("rgba(10, 20, 30, 0.5)")), (result.Boxes[0].Title?.Text, result.Boxes[0].Color));
        Assert.Equal((1.5m, 2m), (((SequenceAutonumber)result.Steps[0]).Start, ((SequenceAutonumber)result.Steps[0]).Increment));
        Assert.Equal("wrap: line 1\nline 2", ((SequenceMessage)result.Steps[1]).Text?.Text);
        var resultCreate = (SequenceMessage)((SequenceBlock)result.Steps[3]).Sections[0].Steps[0];
        Assert.Equal((true, true, SequenceParticipantKind.Entity, "Carl C"), (resultCreate.CreatesReceiver, resultCreate.ReceiverConnection, resultCreate.To.Kind, resultCreate.To.Label?.Text));
        Assert.Equal((SceneColor)"#ff0000", ((SequenceBlock)result.Steps[3]).Color);
        Assert.Equal("end", ((SequenceMessage)result.Steps[4]).Text?.Text);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new SequenceDiagram();
        var a = diagram.AddParticipant("a");
        diagram.Steps.Add(new SequenceActivation(a, isActivation: false));
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Steps.Clear();
        diagram.Steps.Add(new SequenceMessage(a, a) { StartArrowHead = SequenceArrowHead.Cross });
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Steps.Clear();
        diagram.AddParticipant("end");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
