namespace Meziantou.Framework.Language.Css;

/// <summary>What the text given to <see cref="CssSyntaxTree"/> holds.</summary>
public enum CssSourceKind
{
    /// <summary>A style sheet, such as a <c>.css</c> file or the content of a <c>&lt;style&gt;</c> element.</summary>
    StyleSheet = 0,

    /// <summary>
    /// The content of a block, such as the <c>style</c> attribute of an HTML element: declarations, and the rules
    /// nesting allows. It is read as if it were between <c>{</c> and <c>}</c> in a style rule.
    /// </summary>
    DeclarationList = 1,
}
