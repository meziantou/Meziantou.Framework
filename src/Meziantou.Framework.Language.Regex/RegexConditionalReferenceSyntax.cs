namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents the group reference that a conditional tests, the <c>(1)</c> or <c>(name)</c> of <c>(?(1)yes|no)</c>.</summary>
public sealed partial class RegexConditionalReferenceSyntax : RegexSyntaxNode
{
    /// <summary>The group name or number being tested.</summary>
    public string Name => NameToken.Text;
}
