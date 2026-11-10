using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An event model: a timeline of frames (screens, processors, commands, read models and events) placed in swimlanes,
/// left to right in order, with the relations between them inferred or explicit (Mermaid <c>eventmodeling</c>).
/// </summary>
public sealed class EventModelingDiagram : Diagram
{
    /// <summary>Gets the frames of the timeline, drawn left to right in order.</summary>
    public IList<EventModelingFrame> Frames { get; } = new List<EventModelingFrame>();

    /// <summary>Gets the data blocks that frames reference.</summary>
    public IList<EventModelingDataBlock> DataBlocks { get; } = new List<EventModelingDataBlock>();

    /// <summary>Gets the names of the declared entities (Mermaid <c>entity</c>), which the scenarios name. They are not drawn.</summary>
    public IList<string> Entities { get; } = new List<string>();

    /// <summary>Gets the notes attached to frames. They are not drawn.</summary>
    public IList<EventModelingNote> Notes { get; } = new List<EventModelingNote>();

    /// <summary>Gets the given/when/then scenarios of frames (Mermaid <c>gwt</c>). They are not drawn.</summary>
    public IList<EventModelingScenario> Scenarios { get; } = new List<EventModelingScenario>();

    /// <summary>Adds a frame at the end of the timeline.</summary>
    /// <param name="id">The identifier: one to three digits, unique in the diagram.</param>
    /// <param name="kind">The kind of the entity of the frame.</param>
    /// <param name="entity">The name of the entity, such as <c>AddItem</c>, or <c>Cart.AddItem</c> to put it in the swimlane of the namespace <c>Cart</c>.</param>
    /// <returns>The frame.</returns>
    public EventModelingFrame AddFrame(string id, EventModelingEntityKind kind, string entity)
    {
        var frame = new EventModelingFrame(id, kind, entity);
        Frames.Add(frame);
        return frame;
    }

    /// <summary>Finds a frame by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The frame, or <see langword="null"/> when there is none.</returns>
    public EventModelingFrame? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var frame in Frames)
        {
            if (string.Equals(frame.Id, id, StringComparison.Ordinal))
                return frame;
        }

        return null;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => EventModelingDiagramRenderer.Render(this, context, group);
}
