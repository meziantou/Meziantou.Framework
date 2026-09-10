namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a type literal, <c>[System.String]</c>.</summary>
public sealed partial class PowerShellTypeLiteralSyntax
{
    /// <summary>The type name between the brackets.</summary>
    public string Name => NameToken.Text;
}
