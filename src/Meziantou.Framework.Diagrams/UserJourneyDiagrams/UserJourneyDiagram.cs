using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A user journey: the tasks a user does to reach a goal, grouped in sections, each with the satisfaction of the actors
/// who take part, drawn as a face (Mermaid <c>journey</c>).
/// </summary>
public sealed class UserJourneyDiagram : Diagram
{
    /// <summary>Gets the sections, drawn left to right with their tasks.</summary>
    public IList<UserJourneySection> Sections { get; } = new List<UserJourneySection>();

    /// <summary>Adds a section.</summary>
    /// <param name="name">The name of the section.</param>
    /// <returns>The section.</returns>
    public UserJourneySection AddSection(string name)
    {
        var section = new UserJourneySection(name);
        Sections.Add(section);
        return section;
    }

    /// <summary>Returns the actors of the tasks, without duplicates, in ordinal order: the order of the legend and of the colors.</summary>
    /// <returns>The actors.</returns>
    public IReadOnlyList<string> GetActors() => Sections.SelectMany(section => section.Tasks).SelectMany(task => task.Actors).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => UserJourneyDiagramRenderer.Render(this, context, group);
}
