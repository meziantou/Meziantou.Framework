using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>The base of every immutable XML node.</summary>
internal abstract class XmlSyntaxNode : GreenNode
{
    protected XmlSyntaxNode(SyntaxKind kind)
        : base((int)kind)
    {
    }

    protected XmlSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    public override string KindText => Kind.ToString();

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
}
