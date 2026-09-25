namespace Meziantou.Framework.Language.Toml;

/// <summary>Dispatches on the kind of a TOML node.</summary>
/// <remarks>
/// Visiting a node does not visit its children. Derive from <see cref="TomlSyntaxWalker"/> to walk a whole tree.
/// </remarks>
public abstract class TomlSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(TomlSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(TomlSyntaxNode node)
    {
    }

    public virtual void VisitTomlDocument(TomlDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlTable(TomlTableSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlProperty(TomlPropertySyntax node) => DefaultVisit(node);
    public virtual void VisitTomlKey(TomlKeySyntax node) => DefaultVisit(node);
    public virtual void VisitTomlSkippedText(TomlSkippedTextSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlArray(TomlArraySyntax node) => DefaultVisit(node);
    public virtual void VisitTomlInlineTable(TomlInlineTableSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlString(TomlStringSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlInteger(TomlIntegerSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlFloat(TomlFloatSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlBoolean(TomlBooleanSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlDateTime(TomlDateTimeSyntax node) => DefaultVisit(node);
    public virtual void VisitTomlSkippedValue(TomlSkippedValueSyntax node) => DefaultVisit(node);
}

/// <summary>Dispatches on the kind of a TOML node and returns a result.</summary>
/// <typeparam name="TResult">What visiting a node produces.</typeparam>
public abstract class TomlSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(TomlSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(TomlSyntaxNode node) => default;

    public virtual TResult? VisitTomlDocument(TomlDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlTable(TomlTableSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlProperty(TomlPropertySyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlKey(TomlKeySyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlSkippedText(TomlSkippedTextSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlArray(TomlArraySyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlInlineTable(TomlInlineTableSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlString(TomlStringSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlInteger(TomlIntegerSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlFloat(TomlFloatSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlBoolean(TomlBooleanSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlDateTime(TomlDateTimeSyntax node) => DefaultVisit(node);
    public virtual TResult? VisitTomlSkippedValue(TomlSkippedValueSyntax node) => DefaultVisit(node);
}
