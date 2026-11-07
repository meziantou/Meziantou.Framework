namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE callout, <c>(?C)</c>, <c>(?C1)</c>, or <c>(?C"text")</c>.</summary>
/// <remarks>A callout hands control to the host at that point in the match. It matches nothing itself.</remarks>
public sealed partial class RegexCalloutSyntax : RegexAtomSyntax
{
    /// <summary>The callout's identifier, or an empty string when it has none.</summary>
    public string Value => BodyToken.Text;
}
