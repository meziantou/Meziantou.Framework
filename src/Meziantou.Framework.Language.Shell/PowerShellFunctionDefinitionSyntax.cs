namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>function</c>, <c>filter</c>, or <c>workflow</c> definition.</summary>
public sealed partial class PowerShellFunctionDefinitionSyntax
{
    /// <summary>The definition name.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for a <c>filter</c>, which runs its body once per pipeline item.</summary>
    public bool IsFilter => Kind() == SyntaxKind.PowerShellFilterDefinition;
}
