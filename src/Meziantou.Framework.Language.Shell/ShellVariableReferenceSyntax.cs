namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parameter expansion such as <c>$name</c>, <c>${name}</c>, or <c>%NAME%</c>.</summary>
public sealed partial class ShellVariableReferenceSyntax
{
    /// <summary>The name of the referenced variable, without the introducer or braces.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> when the reference uses the braced form.</summary>
    public bool IsBraced => OpenBraceToken.IsPresent();
}
