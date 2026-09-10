namespace Meziantou.Framework.Language.Shell;

/// <summary>Dispatches on the kind of a shell node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="ShellSyntaxWalker"/> to walk a whole tree.
/// </remarks>
/// <example>
/// <code>
/// sealed class Counter : ShellSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitCommand(ShellCommandSyntax node) { Count++; base.VisitCommand(node); }
/// }
/// </code>
/// </example>
public abstract partial class ShellSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(ShellSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(ShellSyntaxNode node)
    {
    }
}

/// <summary>Dispatches on the kind of a shell node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract partial class ShellSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(ShellSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(ShellSyntaxNode node) => default;
}
