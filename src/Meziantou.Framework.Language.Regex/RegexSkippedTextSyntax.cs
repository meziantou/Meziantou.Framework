namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents text the parser could not recognize, kept so the pattern still round-trips.</summary>
public sealed partial class RegexSkippedTextSyntax : RegexAtomSyntax
{
    public string Text => ToFullString();
}
