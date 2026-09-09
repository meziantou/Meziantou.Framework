namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>\Q…\E</c> run, in which every character is literal.</summary>
public sealed partial class RegexQuotedLiteralSyntax : RegexAtomSyntax
{
    /// <summary>The literal text between the delimiters.</summary>
    public string Value => TextToken.Text;
}
