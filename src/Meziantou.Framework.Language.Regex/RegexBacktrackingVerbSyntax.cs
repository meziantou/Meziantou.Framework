namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE backtracking control verb such as <c>(*SKIP)</c>.</summary>
public sealed partial class RegexBacktrackingVerbSyntax : RegexAtomSyntax
{
    /// <summary>The verb name, without the leading <c>*</c>.</summary>
    public string Name => VerbToken.Text.TrimStart('*');
}
