namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a POSIX collating element, <c>[.ch.]</c>, or an equivalence class, <c>[=a=]</c>.</summary>
public sealed partial class RegexCollatingElementSyntax : RegexSyntaxNode
{
    /// <summary>The text between the delimiters.</summary>
    public string Value => TextToken.Text;
}
