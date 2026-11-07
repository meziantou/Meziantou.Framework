namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an attribute or type constraint, <c>[Parameter(Mandatory)]</c> or <c>[string]</c>.</summary>
public sealed partial class PowerShellAttributeSyntax
{
    /// <summary>The attribute or type name.</summary>
    public string Name => NameToken.Text;

    /// <summary>Returns <see langword="true"/> when the attribute has no argument list, making it a type constraint.</summary>
    public bool IsTypeConstraint => !OpenParenToken.IsPresent();
}
