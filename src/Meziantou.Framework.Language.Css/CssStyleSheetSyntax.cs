namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a whole style sheet: its statements, and the end of the text.</summary>
public sealed partial class CssStyleSheetSyntax
{
    /// <summary>Gets the rules at the top level of the style sheet, leaving out what a browser ignores.</summary>
    public IEnumerable<CssRuleSyntax> Rules => Statements.OfType<CssRuleSyntax>();

    /// <summary>Gets the declarations at the top level, which only a declaration list, such as a <c>style</c> attribute, holds.</summary>
    public IEnumerable<CssDeclarationSyntax> Declarations => Statements.OfType<CssDeclarationSyntax>();
}
