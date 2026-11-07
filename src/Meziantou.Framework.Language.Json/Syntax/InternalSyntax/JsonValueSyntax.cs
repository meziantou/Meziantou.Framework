using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>Anything that can appear where JSON expects a value.</summary>
internal abstract class JsonValueSyntax : JsonSyntaxNode
{
    protected JsonValueSyntax(SyntaxKind kind)
        : base(kind)
    {
    }

    protected JsonValueSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
