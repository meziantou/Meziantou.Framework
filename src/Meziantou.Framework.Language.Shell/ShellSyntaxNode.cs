using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>The base of every node in a shell tree.</summary>
/// <example>
/// <code>
/// foreach (var node in tree.GetRoot().DescendantNodes())
/// {
///     _ = node.Kind();
/// }
/// </code>
/// </example>
public abstract class ShellSyntaxNode : SyntaxNode
{
    private protected ShellSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what kind of node this is.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root of its tree.</summary>
    public new ShellSyntaxNode? Parent => (ShellSyntaxNode?)base.Parent;

    /// <summary>Gets the dialect the node was read as, or <see langword="null"/> when it belongs to no tree.</summary>
    public ShellDialect? Dialect => (SyntaxTree as ShellSyntaxTree)?.Dialect;

    /// <summary>Returns every comment in this node, in source order.</summary>
    public IEnumerable<SyntaxTrivia> DescendantComments() => DescendantTrivia().Where(trivia => trivia.IsComment());

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(ShellSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(ShellSyntaxVisitor<TResult> visitor);
}
