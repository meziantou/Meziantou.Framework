using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>The green base type for anything that can appear where TOML expects a value.</summary>
internal abstract class TomlValueSyntax : TomlSyntaxNode
{
    private protected TomlValueSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base(kind, diagnostics, annotations)
    {
    }
}
