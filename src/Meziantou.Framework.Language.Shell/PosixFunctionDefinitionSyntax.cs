namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a function definition, in either the <c>name() { }</c> or <c>function name { }</c> form.</summary>
public sealed partial class PosixFunctionDefinitionSyntax
{
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for a zsh anonymous function, which has no name.</summary>
    public bool IsAnonymous => NameToken.IsMissing;
}
