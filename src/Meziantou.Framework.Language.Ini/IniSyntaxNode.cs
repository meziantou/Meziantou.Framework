using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>The base of every node in an INI tree.</summary>
public abstract class IniSyntaxNode : SyntaxNode
{
    private protected IniSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what kind of node this is.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root of its tree.</summary>
    public new IniSyntaxNode? Parent => (IniSyntaxNode?)base.Parent;

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(IniSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor);
}
