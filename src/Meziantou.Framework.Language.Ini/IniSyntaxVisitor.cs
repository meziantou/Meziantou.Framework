namespace Meziantou.Framework.Language.Ini;

/// <summary>Dispatches on the kind of an INI node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="IniSyntaxWalker"/> to walk a whole tree.
/// </remarks>
public abstract class IniSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(IniSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(IniSyntaxNode node)
    {
    }

    public virtual void VisitIniDocument(IniDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitIniSection(IniSectionSyntax node) => DefaultVisit(node);
    public virtual void VisitIniProperty(IniPropertySyntax node) => DefaultVisit(node);
    public virtual void VisitIniSkippedText(IniSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Dispatches on the kind of an INI node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract class IniSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(IniSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(IniSyntaxNode node) => default;

    public virtual TResult? VisitIniDocument(IniDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitIniSection(IniSectionSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitIniProperty(IniPropertySyntax node) => DefaultVisit(node);
    public virtual TResult? VisitIniSkippedText(IniSkippedTextSyntax node) => DefaultVisit(node);
}
