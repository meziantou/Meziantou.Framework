namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a subshell, <c>( ... )</c>, or a brace group, <c>{ ...; }</c>.</summary>
public sealed partial class PosixCompoundStatementSyntax
{
    /// <summary>Returns <see langword="true"/> for <c>( ... )</c>, which runs in a child shell.</summary>
    public bool IsSubshell => Kind() == SyntaxKind.PosixSubshell;
}
