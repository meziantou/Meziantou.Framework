using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>The green base type for TOML document entries.</summary>
internal abstract class TomlEntrySyntax : TomlSyntaxNode
{
    private protected TomlEntrySyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base(kind, diagnostics, annotations)
    {
    }
}
