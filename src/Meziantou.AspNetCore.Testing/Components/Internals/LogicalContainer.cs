namespace Meziantou.AspNetCore.Testing.Components.Internals;

internal abstract class LogicalContainer : LogicalNode
{
    public List<LogicalNode> Children { get; } = [];
}
