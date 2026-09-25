using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>The green base type for TOML nodes.</summary>
internal abstract class TomlSyntaxNode : GreenNode
{
    private protected TomlSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public override string KindText => ((SyntaxKind)RawKind).ToString();
}
