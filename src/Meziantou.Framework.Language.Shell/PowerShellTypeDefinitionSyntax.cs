namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>class</c> or <c>enum</c> definition.</summary>
public sealed partial class PowerShellTypeDefinitionSyntax
{
    /// <summary>The type name.</summary>
    public string Name => NameToken.ValueText;
}
