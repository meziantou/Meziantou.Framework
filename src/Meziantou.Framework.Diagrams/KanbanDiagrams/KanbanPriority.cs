namespace Meziantou.Framework.Diagrams;

/// <summary>The priority of a card of a kanban board, shown as a colored stripe.</summary>
public enum KanbanPriority
{
    /// <summary>A very low priority: a light blue stripe (Mermaid <c>Very Low</c>).</summary>
    VeryLow,

    /// <summary>A low priority: a blue stripe (Mermaid <c>Low</c>).</summary>
    Low,

    /// <summary>A medium priority: no stripe (Mermaid <c>Medium</c>).</summary>
    Medium,

    /// <summary>A high priority: an orange stripe (Mermaid <c>High</c>).</summary>
    High,

    /// <summary>A very high priority: a red stripe (Mermaid <c>Very High</c>).</summary>
    VeryHigh,
}
