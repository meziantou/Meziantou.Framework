using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>The base of every node in a regular-expression tree.</summary>
public abstract class RegexSyntaxNode : SyntaxNode
{
    private protected RegexSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what kind of node this is.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root of its tree.</summary>
    public new RegexSyntaxNode? Parent => (RegexSyntaxNode?)base.Parent;

    /// <summary>Gets the tree this node belongs to, or <see langword="null"/> when it was built rather than parsed.</summary>
    public new RegexSyntaxTree? SyntaxTree => (RegexSyntaxTree?)base.SyntaxTree;

    /// <summary>Gets the dialect the node was parsed as, or <see langword="null"/> when it is not part of a tree.</summary>
    public RegexDialect? Dialect => SyntaxTree?.Dialect;

    /// <summary>Gets the options in effect at the first character of this node.</summary>
    /// <remarks>
    /// Inline constructs such as <c>(?i)</c> change the options part-way through a pattern, so this is what was in
    /// effect where the node starts rather than what the whole pattern was parsed with. A node built by
    /// <see cref="SyntaxFactory"/> rather than parsed reports <see cref="RegexPatternOptions.None"/>.
    /// </remarks>
    public RegexPatternOptions Options => ((Syntax.InternalSyntax.RegexSyntaxNode)Green).Options;

    /// <summary>Gets every comment in this node, in source order.</summary>
    public IEnumerable<SyntaxTrivia> DescendantComments() => DescendantTrivia().Where(trivia => trivia.IsComment());

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(RegexSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(RegexSyntaxVisitor<TResult> visitor);
}
