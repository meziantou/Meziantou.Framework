namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a single task.</summary>
public sealed class AdfTaskItem : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.TaskItem;

    /// <summary>Gets whether the task is done.</summary>
    public required AdfTaskState State { get; init; }
}
