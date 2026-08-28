namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a list of tasks.</summary>
public sealed class AdfTaskList : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.TaskList;
}
