namespace Meziantou.Framework.Language.Regex;

/// <summary>Dispatches on the kind of a regular-expression node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="RegexSyntaxWalker"/> to walk a whole tree.
/// </remarks>
public abstract partial class RegexSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(RegexSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(RegexSyntaxNode node)
    {
    }
}

/// <summary>Dispatches on the kind of a regular-expression node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract partial class RegexSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(RegexSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(RegexSyntaxNode node) => default;
}
