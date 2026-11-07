namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an arithmetic expansion, <c>$(( ... ))</c>.</summary>
public sealed partial class PosixArithmeticExpansionSyntax
{
    /// <summary>The expression text as written.</summary>
    public string ExpressionText => Expression.ToFullString();
}
