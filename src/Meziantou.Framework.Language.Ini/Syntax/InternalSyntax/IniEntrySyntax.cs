using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>The green base type for INI document entries.</summary>
internal abstract class IniEntrySyntax : IniSyntaxNode
{
    private protected IniEntrySyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base(kind, diagnostics, annotations)
    {
    }
}
