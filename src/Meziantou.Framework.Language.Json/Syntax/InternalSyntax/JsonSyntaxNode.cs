using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>The base of every immutable JSON node.</summary>
internal abstract class JsonSyntaxNode : GreenNode
{
    protected JsonSyntaxNode(SyntaxKind kind)
        : base((int)kind)
    {
    }

    protected JsonSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    public override string KindText => Kind.ToString();

    /// <summary>JSON separates the elements of a list with a comma.</summary>
    internal override GreenNode? CreateSeparator() => SyntaxFactory.Token(SyntaxKind.CommaToken);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
}
