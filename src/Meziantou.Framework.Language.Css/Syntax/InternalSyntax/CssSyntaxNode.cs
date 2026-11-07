using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>The base of every immutable CSS node.</summary>
internal abstract class CssSyntaxNode : GreenNode
{
    protected CssSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    public override string KindText => Kind.ToString();

    /// <summary>Selector lists, media query lists, and every other list CSS separates are separated by commas.</summary>
    internal override GreenNode? CreateSeparator() => SyntaxFactory.Token(SyntaxKind.CommaToken);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
}
