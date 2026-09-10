namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a variable reference such as <c>$name</c>, <c>${name}</c>, <c>$env:PATH</c>, or a splat <c>@name</c>.</summary>
public sealed partial class PowerShellVariableExpressionSyntax
{
    /// <summary>The variable name without the sigil or braces.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for a splatted variable, <c>@name</c>.</summary>
    public bool IsSplatted => SigilToken.Text is ['@', ..];
}
