using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>The base of every immutable shell node.</summary>
internal abstract class ShellSyntaxNode : GreenNode
{
    protected ShellSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    public override string KindText => Kind.ToString();

    /// <summary>A shell separates the statements of a list with a semicolon, which every dialect accepts.</summary>
    internal override GreenNode? CreateSeparator() => SyntaxFactory.Token(SyntaxKind.SemicolonToken);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
}
