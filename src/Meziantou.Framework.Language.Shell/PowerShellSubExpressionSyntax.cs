namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a subexpression, <c>$( ... )</c>, or an array subexpression, <c>@( ... )</c>.</summary>
public sealed partial class PowerShellSubExpressionSyntax
{
    /// <summary>Returns <see langword="true"/> for <c>@( ... )</c>, which always produces an array.</summary>
    public bool IsArray => Kind() == SyntaxKind.PowerShellArrayExpression;
}
