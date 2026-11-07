using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>The green base type for TOML nodes.</summary>
internal abstract class TomlSyntaxNode : GreenNode
{
    private protected TomlSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    public override string KindText => Kind.ToString();

    /// <summary>TOML separates the elements of arrays and inline tables with a comma.</summary>
    internal override GreenNode? CreateSeparator() => SyntaxFactory.Token(SyntaxKind.CommaToken);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
}
