using Meziantou.Framework.Language.Syntax;

namespace Meziantou.Framework.Language;

/// <summary>A parsed document: its text, the tree over it, and the diagnostics the parser produced.</summary>
/// <remarks>
/// Only a handful of members are left to a language: the text, the root, and how to make a new tree from either.
/// Everything else here is answered from the immutable nodes alone.
/// </remarks>
public abstract class SyntaxTree
{
    /// <summary>Gets the path the text came from, or <see langword="null"/> when it did not come from a file.</summary>
    public abstract string? FilePath { get; }

    /// <summary>Gets the text this tree was parsed from.</summary>
    /// <remarks>
    /// The same instance every time: diagnostics compare the text they point into by reference, so a tree that
    /// returned a new one each call would produce diagnostics that never compare equal.
    /// </remarks>
    public abstract SourceText GetText();

    public int Length => GetText().Length;

    /// <summary>Gets the root of the tree.</summary>
    public SyntaxNode GetRoot() => GetRootCore();

    /// <summary>Gets the root of the tree. A language overrides this and offers its own typed <c>GetRoot</c>.</summary>
    protected abstract SyntaxNode GetRootCore();

    /// <summary>Returns a tree over <paramref name="newText"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
    public SyntaxTree WithChangedText(SourceText newText)
    {
        ArgumentNullException.ThrowIfNull(newText);

        return WithChangedTextCore(newText);
    }

    /// <summary>Returns a tree over <paramref name="newText"/>.</summary>
    protected abstract SyntaxTree WithChangedTextCore(SourceText newText);

    /// <summary>Returns a tree whose root is <paramref name="root"/> and whose text is the text of that root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public SyntaxTree WithRoot(SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return WithRootCore(root);
    }

    /// <summary>Returns a tree whose root is <paramref name="root"/>.</summary>
    protected abstract SyntaxTree WithRootCore(SyntaxNode root);

    /// <summary>Gets every diagnostic in the tree, in source order.</summary>
    public virtual IEnumerable<Diagnostic> GetDiagnostics()
    {
        var root = GetRoot();

        return SyntaxTreeDiagnostics.Enumerate(root.Green, root.Position, GetText());
    }

    /// <summary>Gets the diagnostics at or below <paramref name="node"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public virtual IEnumerable<Diagnostic> GetDiagnostics(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return SyntaxTreeDiagnostics.Enumerate(node.Green, node.Position, GetText());
    }

    /// <summary>Gets the diagnostics on <paramref name="token"/> and the trivia around it.</summary>
    public virtual IEnumerable<Diagnostic> GetDiagnostics(SyntaxToken token)
        => SyntaxTreeDiagnostics.Enumerate(token.Node, token.FullSpan.Start, GetText());

    /// <summary>Gets the diagnostics on <paramref name="trivia"/>.</summary>
    public virtual IEnumerable<Diagnostic> GetDiagnostics(SyntaxTrivia trivia)
        => SyntaxTreeDiagnostics.Enumerate(trivia.UnderlyingNode, trivia.FullSpan.Start, GetText());

    /// <summary>Pairs <paramref name="span"/> with the text it indexes into.</summary>
    public virtual Location GetLocation(TextSpan span) => new(span, GetText());

    /// <summary>Converts <paramref name="span"/> to line and character positions.</summary>
    public virtual LinePositionSpan GetLineSpan(TextSpan span) => GetLocation(span).GetLineSpan();

    /// <summary>Describes how <paramref name="oldTree"/> would have to change to become this one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public virtual IReadOnlyList<TextChange> GetChanges(SyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);

        return SyntaxDiffer.GetChanges(oldTree.GetRoot(), GetRoot());
    }

    /// <summary>Gets the ranges of this tree that differ from <paramref name="oldTree"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public virtual IReadOnlyList<TextSpan> GetChangedSpans(SyntaxTree oldTree) => [.. GetChanges(oldTree).Select(change => change.Span)];

    /// <summary>Determines whether the two trees have the same structure and text.</summary>
    public virtual bool IsEquivalentTo([NotNullWhen(true)] SyntaxTree? other) => other is not null && GetRoot().IsEquivalentTo(other.GetRoot());

    public override string ToString() => GetText().Text;
}
