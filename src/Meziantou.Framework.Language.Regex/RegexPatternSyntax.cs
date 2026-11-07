namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a whole pattern: its body, and the delimiters around it when it was written as a literal.</summary>
public sealed partial class RegexPatternSyntax : RegexSyntaxNode
{
    /// <summary>Gets a value indicating whether the pattern was written as a JavaScript literal, <c>/…/flags</c>.</summary>
    public bool IsJavaScriptLiteral => OpenSlashToken.RawKind != (int)SyntaxKind.None;
}
