using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>The green base type for INI nodes.</summary>
internal abstract class IniSyntaxNode : GreenNode
{
    private protected IniSyntaxNode(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics = null, SyntaxAnnotation[]? annotations = null)
        : base((int)kind, diagnostics, annotations)
    {
    }

    public override string KindText => ((SyntaxKind)RawKind).ToString();
}
