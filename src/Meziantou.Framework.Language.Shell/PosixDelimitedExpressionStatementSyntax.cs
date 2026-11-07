namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>[[ ... ]]</c> conditional or a <c>(( ... ))</c> arithmetic command.</summary>
public sealed partial class PosixDelimitedExpressionStatementSyntax
{
    /// <summary>Returns <see langword="true"/> for <c>(( ... ))</c>.</summary>
    public bool IsArithmetic => Kind() == SyntaxKind.PosixArithmeticCommand;
}
