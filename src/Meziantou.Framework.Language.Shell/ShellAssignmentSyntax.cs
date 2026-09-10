namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a variable assignment such as <c>NAME=value</c>.</summary>
public sealed partial class ShellAssignmentSyntax
{
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for the bash and zsh append form, <c>name+=value</c>.</summary>
    public bool IsAppend => EqualsToken.Kind() == SyntaxKind.PlusEqualsToken;
}
