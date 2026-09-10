namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents member access, <c>$x.Name</c> or <c>[Type]::Member</c>.</summary>
public sealed partial class PowerShellMemberAccessExpressionSyntax
{
    /// <summary>Returns <see langword="true"/> for the static member operator, <c>::</c>.</summary>
    public bool IsStatic => OperatorToken.Kind() == SyntaxKind.ColonColonToken;
}
