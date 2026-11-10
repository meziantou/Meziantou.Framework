namespace Meziantou.Framework.Diagrams.Tests;

public sealed class SequenceDiagramTests
{
    [Fact]
    public void SequenceDiagram()
    {
        #region Diagrams_SequenceDiagram
        var diagram = new SequenceDiagram();
        var customer = diagram.AddParticipant("customer", "Customer", SequenceParticipantKind.Actor);
        var shop = diagram.AddParticipant("shop", "Web shop");
        var bank = diagram.AddParticipant("bank", "Bank", SequenceParticipantKind.Database);

        diagram.Steps.Add(new SequenceAutonumber());
        diagram.AddMessage(customer, shop, "Place order").ActivatesReceiver = true;
        var payment = diagram.AddBlock(SequenceBlockKind.Alternative, "card accepted");
        payment.Sections[0].Steps.Add(new SequenceMessage(shop, bank) { Text = "Charge card", EndArrowHead = SequenceArrowHead.Arrow });
        payment.Sections[0].Steps.Add(new SequenceMessage(bank, shop) { Text = "Approved", LineStyle = SequenceLineStyle.Dotted, EndArrowHead = SequenceArrowHead.Arrow });
        var declined = new SequenceBlockSection("card declined");
        declined.Steps.Add(new SequenceMessage(shop, customer) { Text = "Ask for another card", EndArrowHead = SequenceArrowHead.OpenArrow });
        payment.Sections.Add(declined);
        var confirmation = diagram.AddMessage(shop, customer, "Order confirmed", SequenceLineStyle.Dotted);
        confirmation.DeactivatesSender = true;
        diagram.AddNote("One order, one payment", SequenceNotePosition.Over, customer, bank);
        #endregion

        Assert.Same(bank, diagram.Find("bank"));
        Assert.HasCount(8, diagram.EnumerateSteps());
        DiagramAssert.Svg(diagram, "SequenceDiagram");
    }

    [Fact]
    public void ParticipantsArrowsAndBlocks()
    {
        var diagram = new SequenceDiagram { MirrorParticipants = false };
        var kinds = Enum.GetValues<SequenceParticipantKind>().Select(kind => diagram.AddParticipant(kind.ToString(), kind: kind)).ToList();
        var box = new SequenceBox { Title = "Group", Color = (SceneColor)"#eeeeff" };
        box.Participants.Add(kinds[0]);
        box.Participants.Add(kinds[1]);
        diagram.Boxes.Add(box);
        foreach (var head in Enum.GetValues<SequenceArrowHead>())
        {
            diagram.Steps.Add(new SequenceMessage(kinds[0], kinds[2]) { Text = head.ToString(), EndArrowHead = head, StartArrowHead = head });
        }

        diagram.Steps.Add(new SequenceMessage(kinds[2], kinds[2]) { Text = "Self", EndArrowHead = SequenceArrowHead.Arrow });
        diagram.Steps.Add(new SequenceMessage(kinds[3], kinds[4]) { SenderConnection = true, ReceiverConnection = true });
        foreach (var kind in Enum.GetValues<SequenceBlockKind>())
        {
            var block = diagram.AddBlock(kind, kind.ToString());
            block.Sections[0].Steps.Add(new SequenceNote("Note", SequenceNotePosition.RightOf) { Participants = { kinds[5] } });
            block.Sections.Add(new SequenceBlockSection("Second"));
            block.Sections[1].Steps.Add(new SequenceNote("Left", SequenceNotePosition.LeftOf) { Participants = { kinds[6] } });
        }

        var created = diagram.AddParticipant("created");
        diagram.Steps.Add(new SequenceActivation(kinds[7]));
        diagram.Steps.Add(new SequenceMessage(kinds[7], created) { Text = "Create", CreatesReceiver = true, EndArrowHead = SequenceArrowHead.Arrow });
        diagram.Steps.Add(new SequenceMessage(kinds[7], created) { Text = "Destroy", DestroyedParticipant = created, EndArrowHead = SequenceArrowHead.Cross });
        diagram.Steps.Add(new SequenceAutonumber { Start = 10, Increment = 0.5m });
        diagram.Steps.Add(new SequenceMessage(kinds[7], kinds[0]) { Text = "Numbered\nmessage" });

        var svg = diagram.ToSvg();
        Assert.Contains("[Second]", svg, StringComparison.Ordinal);
        Assert.Contains(">10<", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }
}
