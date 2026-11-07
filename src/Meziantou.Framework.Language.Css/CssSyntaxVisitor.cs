namespace Meziantou.Framework.Language.Css;

/// <summary>Dispatches on the kind of a CSS node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="CssSyntaxWalker"/> to walk a whole tree.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs" ref:region="CssSyntaxWalker_DeclarationCounter" ref:dedent="true">
/// private sealed class DeclarationCounter : CssSyntaxWalker
/// {
///     public int Count { get; private set; }
///
///     public override void VisitDeclaration(CssDeclarationSyntax node)
///     {
///         Count++;
///         base.VisitDeclaration(node);
///     }
/// }
/// </code>
/// </example>
public abstract partial class CssSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(CssSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(CssSyntaxNode node)
    {
    }
}

/// <summary>Dispatches on the kind of a CSS node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract partial class CssSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(CssSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(CssSyntaxNode node) => default;
}
