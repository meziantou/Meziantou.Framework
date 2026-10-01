using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>The base of every node in a CSS tree.</summary>
/// <example>
/// <code>
/// foreach (var node in tree.GetRoot().DescendantNodes())
/// {
///     _ = node.Kind();
/// }
/// </code>
/// </example>
public abstract class CssSyntaxNode : SyntaxNode
{
    private protected CssSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what kind of node this is.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root of its tree.</summary>
    public new CssSyntaxNode? Parent => (CssSyntaxNode?)base.Parent;

    /// <summary>Returns every comment in this node, in source order.</summary>
    public IEnumerable<SyntaxTrivia> DescendantComments() => DescendantTrivia().Where(trivia => trivia.IsComment());

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(CssSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(CssSyntaxVisitor<TResult> visitor);
}
