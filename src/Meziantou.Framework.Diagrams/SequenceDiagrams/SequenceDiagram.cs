using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A sequence diagram: participants side by side, with their lifelines, and the messages they exchange over time, from
/// the top to the bottom, with activations, notes and blocks such as loops and alternatives (Mermaid <c>sequenceDiagram</c>).
/// </summary>
public sealed class SequenceDiagram : Diagram
{
    /// <summary>Gets the participants, from left to right. A participant that a message creates is in this list too.</summary>
    public IList<SequenceParticipant> Participants { get; } = new List<SequenceParticipant>();

    /// <summary>Gets the boxes that group participants.</summary>
    public IList<SequenceBox> Boxes { get; } = new List<SequenceBox>();

    /// <summary>Gets the steps of the diagram, from the top to the bottom: messages, notes, activations, blocks...</summary>
    public IList<SequenceStep> Steps { get; } = new List<SequenceStep>();

    /// <summary>Gets or sets a value that indicates whether the participants are drawn below the lifelines too. The default is <see langword="true"/> (Mermaid <c>mirrorActors</c>).</summary>
    public bool MirrorParticipants { get; set; } = true;

    /// <summary>Adds a participant.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <param name="kind">How the participant is drawn.</param>
    /// <returns>The participant.</returns>
    public SequenceParticipant AddParticipant(string id, DiagramText? label = null, SequenceParticipantKind kind = SequenceParticipantKind.Participant)
    {
        var participant = new SequenceParticipant(id, label) { Kind = kind };
        Participants.Add(participant);
        return participant;
    }

    /// <summary>Adds a message, with an arrowhead at the target.</summary>
    /// <param name="from">The sender.</param>
    /// <param name="to">The receiver, which can be the sender.</param>
    /// <param name="text">The text.</param>
    /// <param name="lineStyle">The style of the line.</param>
    /// <returns>The message.</returns>
    public SequenceMessage AddMessage(SequenceParticipant from, SequenceParticipant to, DiagramText? text = null, SequenceLineStyle lineStyle = SequenceLineStyle.Solid)
    {
        var message = new SequenceMessage(from, to) { Text = text, LineStyle = lineStyle, EndArrowHead = SequenceArrowHead.Arrow };
        Steps.Add(message);
        return message;
    }

    /// <summary>Adds a note.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">Where the note is, relative to the participants.</param>
    /// <param name="participants">One participant, or two for a note over both.</param>
    /// <returns>The note.</returns>
    public SequenceNote AddNote(DiagramText text, SequenceNotePosition position, params SequenceParticipant[] participants)
    {
        var note = new SequenceNote(text, position);
        foreach (var participant in participants)
        {
            note.Participants.Add(participant);
        }

        Steps.Add(note);
        return note;
    }

    /// <summary>Adds a block, such as a loop or an alternative, with its first section.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="label">The text of the first section, or <see langword="null"/> for none.</param>
    /// <returns>The block.</returns>
    public SequenceBlock AddBlock(SequenceBlockKind kind, DiagramText? label = null)
    {
        var block = new SequenceBlock(kind);
        block.Sections.Add(new SequenceBlockSection(label));
        Steps.Add(block);
        return block;
    }

    /// <summary>Finds a participant by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The participant, or <see langword="null"/> when there is none.</returns>
    public SequenceParticipant? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var participant in Participants)
        {
            if (string.Equals(participant.Id, id, StringComparison.Ordinal))
                return participant;
        }

        return null;
    }

    /// <summary>Enumerates the steps and the steps of the blocks, depth first, in order.</summary>
    /// <returns>The steps.</returns>
    public IEnumerable<SequenceStep> EnumerateSteps()
    {
        var visited = new HashSet<SequenceBlock>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IEnumerator<SequenceStep>>();
        stack.Push(Steps.GetEnumerator());
        while (stack.Count > 0)
        {
            var enumerator = stack.Peek();
            if (!enumerator.MoveNext())
            {
                enumerator.Dispose();
                stack.Pop();
                continue;
            }

            var step = enumerator.Current;
            yield return step;
            if (step is SequenceBlock block && visited.Add(block))
            {
                stack.Push(block.Sections.SelectMany(section => section.Steps).GetEnumerator());
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => SequenceDiagramRenderer.Render(this, context, group);
}
